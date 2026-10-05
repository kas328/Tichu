using System;
using System.Threading;
using Tichu.Core;
using Tichu.Core.Game;
using Tichu.GameFlow;
using Tichu.GameFlow.Agents;

namespace Tichu.Net
{
    /// <summary>
    /// 한 매치의 권위 세션. 전용 스레드 하나에서 기존 동기 GameDriver 를 돌리고,
    /// 모든 엔진 변이는 그 스레드에서만 일어난다. 원격 좌석은 RemoteAgent 로,
    /// 나머지는 주입된 AI 에이전트로 채워진다 — 드라이버는 둘을 구분하지 않는다.
    ///
    /// 매 액션 적용 직후 좌석별로 마스킹된 뷰를 방송한다. "지금 누구에게 묻는지"는
    /// RemoteAgent 가 대기에 들어갈 때 알려 주며(드라이버 내부 카운터를 볼 수 없다),
    /// 대기 좌석에게만 합법수를 함께 싣는다.
    /// </summary>
    public sealed class MatchSession
    {
        private const int SeatCount = 4;

        private readonly ITichuTransport _transport;
        private readonly ulong _masterSeed;
        private readonly int _targetScore;
        private readonly Action<RoundOutcome>? _onRoundCompleted;
        private readonly TimeSpan _interRoundPause;
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();

        private readonly IAgent[] _agents = new IAgent[SeatCount];
        private readonly RemoteAgent?[] _remote = new RemoteAgent?[SeatCount];

        private Thread? _thread;
        private int _started;
        private long _arrival;
        private int _teamA;
        private int _teamB;

        /// <param name="aiSeats">좌석별 AI 에이전트. null 인 좌석은 원격 플레이어다.</param>
        /// <param name="turnTimeout">원격 좌석 1회 대기 상한. 기본 무한(만료 정책은 M5).</param>
        /// <param name="onRoundCompleted">라운드 완료 훅. 오라클 검증과 이후 리플레이·로깅이 쓴다.</param>
        public MatchSession(
            IAgent?[] aiSeats,
            ITichuTransport transport,
            ulong masterSeed,
            int targetScore = 1000,
            TimeSpan? turnTimeout = null,
            Action<RoundOutcome>? onRoundCompleted = null,
            TimeSpan? interRoundPause = null)
        {
            if (aiSeats == null || aiSeats.Length != SeatCount)
                throw new ArgumentException("exactly 4 seats required", nameof(aiSeats));

            _transport = transport;
            _masterSeed = masterSeed;
            _targetScore = targetScore;
            _onRoundCompleted = onRoundCompleted;
            _interRoundPause = interRoundPause ?? TimeSpan.Zero;

            var timeout = turnTimeout ?? Timeout.InfiniteTimeSpan;
            for (int seat = 0; seat < SeatCount; seat++)
            {
                if (aiSeats[seat] != null) { _agents[seat] = aiSeats[seat]!; continue; }

                var remote = new RemoteAgent(
                    seat,
                    (s, ev) => _transport.SendTo(s, ev),
                    OnAwait,
                    NextArrival,
                    timeout,
                    _cts.Token);
                _remote[seat] = remote;
                _agents[seat] = remote;
            }

            _transport.OnClientAction(Submit);
        }

        /// <summary>세션 스레드를 띄운다. 두 번 호출하면 두 스레드가 같은 상태를 경합하므로 거부한다.</summary>
        public void Start()
        {
            if (Interlocked.Exchange(ref _started, 1) == 1)
                throw new InvalidOperationException("session already started");

            _thread = new Thread(Run) { IsBackground = true, Name = "TichuMatchSession" };
            _thread.Start();
        }

        /// <summary>세션을 중단한다. 대기 중인 원격 좌석의 블로킹도 풀린다.</summary>
        public void Stop() => _cts.Cancel();

        /// <summary>세션 스레드가 끝날 때까지 기다린다. 끝났으면 true(테스트·종료 처리용).</summary>
        public bool Join(TimeSpan timeout) => _thread?.Join(timeout) ?? true;

        /// <summary>클라이언트 액션을 해당 좌석의 원격 에이전트로 보낸다.</summary>
        public void Submit(ClientAction a)
        {
            if (a.Seat < 0 || a.Seat >= SeatCount)
            {
                // 보낼 곳이 없다. ActionRejected 는 발신자에게만 가야 하는데(기획서 7.7) 이
                // 분기에서는 발신자를 식별할 수 없다 — 식별 가능하면 범위 검사가 걸리지 않았다.
                // 무고한 좌석에 "누군가 잘못된 액션을 보냈다" 를 알리는 대신 버린다.
                // 신뢰 못 할 좌석 번호를 거르는 것은 실제 전송이 붙는 M2 의 몫이다.
                return;
            }

            var remote = _remote[a.Seat];
            if (remote == null)
            {
                _transport.SendTo(a.Seat, ServerEvent.ActionRejected(a.ActionSeq, "seat is not remote"));
                return;
            }

            remote.Submit(a);
        }

        private long NextArrival() => Interlocked.Increment(ref _arrival);

        private void Run()
        {
            var master = new Rng(_masterSeed);
            var driver = new GameDriver(_agents);

            try
            {
                int roundIndex = 0;
                while (!_cts.IsCancellationRequested)
                {
                    // 첫 라운드(roundIndex == 0)는 기존 선행 버퍼링을 그대로 둬야 한다 — 그래야
                    // 아직 아무도 묻지 않은 시점에 네 클라이언트가 동시에 교환을 제출해도 받아
                    // 진다. 둘째 라운드부터는 각 원격 좌석의 미소비 우편함을 비운다 — 거기 남은
                    // 액션은 라운드당 한 번뿐인 질문(큰 티츄·교환·용 양도)을 향한 것이라, 비우지
                    // 않으면 다음 라운드의 같은 질문에 조용히 답으로 쓰인다.
                    if (roundIndex > 0)
                        for (int seat = 0; seat < SeatCount; seat++)
                            _remote[seat]?.ResetForNewRound();

                    ulong roundSeed = master.NextULong();
                    var state = GameEngine.NewRound(roundSeed);

                    Broadcast(state, null);
                    var outcome = driver.RunRound(state, OnApply, TakePendingTichu, TakePendingBomb);

                    _teamA += outcome.Result.TeamATotal;
                    _teamB += outcome.Result.TeamBTotal;

                    for (int seat = 0; seat < SeatCount; seat++)
                        _transport.SendTo(seat, ServerEvent.RoundScored(outcome.Result));

                    _onRoundCompleted?.Invoke(outcome);

                    var decision = MatchRunner.Decide(_teamA, _teamB, _targetScore);
                    if (decision == MatchDecision.Continue)
                    {
                        // 네 클라이언트가 라운드 결과를 읽을 시간. 서버가 쥐는 것이 맞다 —
                        // 다음 라운드가 이미 시작된 뒤엔 클라이언트가 멈출 수 없다.
                        // Stop() 으로 깨울 수 있어야 하므로 Sleep 이 아니라 토큰 대기를 쓴다.
                        if (_interRoundPause > TimeSpan.Zero)
                            _cts.Token.WaitHandle.WaitOne(_interRoundPause);
                        roundIndex++;
                        continue;
                    }

                    int winner = decision == MatchDecision.TeamAWins ? 0 : 1;
                    for (int seat = 0; seat < SeatCount; seat++)
                        _transport.SendTo(seat, ServerEvent.MatchEnded(winner, _teamA, _teamB));
                    return;
                }
            }
            catch (OperationCanceledException)
            {
                // Stop() — 정상 종료.
            }
            catch (Exception e)
            {
                for (int seat = 0; seat < SeatCount; seat++)
                    _transport.SendTo(seat, ServerEvent.SessionFaulted(e.Message));
            }
        }

        private void OnApply(GameState s, GameAction a) => Broadcast(s, null, AppliedAction.From(a));

        /// <summary>RemoteAgent 가 대기에 들어갔다. 대기 좌석엔 합법수까지, 나머지엔 축약판을 보낸다.</summary>
        private void OnAwait(int seat, Awaiting awaiting)
        {
            var redacted = awaiting.Redacted();
            for (int i = 0; i < SeatCount; i++)
                _transport.SendTo(i, ServerEvent.StateUpdate(
                    ViewProjector.ProjectFor(CurrentState!, i), i == seat ? awaiting : redacted));
        }

        private void Broadcast(GameState s, Awaiting? awaiting, AppliedAction? applied = null)
        {
            CurrentState = s;
            for (int seat = 0; seat < SeatCount; seat++)
                _transport.SendTo(seat, ServerEvent.StateUpdate(ViewProjector.ProjectFor(s, seat), awaiting, applied));
        }

        /// <summary>세션 스레드 전용. OnAwait 가 투영에 쓴다.</summary>
        private GameState? CurrentState { get; set; }

        private int TakePendingTichu(GameState s)
        {
            // 용 양도 대기 중엔 엔진이 GiveDragon 외 모든 액션을 거부한다(GameEngine.Apply 의
            // 전역 가드). 일시적 차단이므로 예약을 소비하지 않고 유지한다 — 소비해 버리면
            // 합법적인 콜이 통보도 없이 사라진다. 영구 불법(이미 콜·아웃·카드 냈음)인 경우는
            // 드라이버가 적용을 시도해 거부되고 버려지며, 그것이 의도된 정책이다.
            if (s.TryGetPendingDragonGift(out _)) return -1;

            for (int seat = 0; seat < SeatCount; seat++)
                if (_remote[seat] != null && _remote[seat]!.TakeReservedTichu())
                    return seat;
            return -1;
        }

        /// <summary>
        /// 예약된 차례밖 폭탄 중 가장 먼저 도착한 것을 적용 가능한 형태로 돌려준다.
        /// 도착 순서로 직렬화한다(기획서 7.4). 자기 차례인 좌석의 예약은 건드리지 않는다 —
        /// 그건 드라이버가 그 좌석에게 물을 때 Await 가 직접 꺼낸다.
        ///
        /// 알려진 한계: 각 좌석의 선행 큐 머리만 들여다본다. 폭탄보다 먼저 제출된
        /// 비(非)폭탄 Play 가 머리에 있으면, 그 좌석의 차례가 와서 머리가 빠질 때까지
        /// 폭탄은 지연된다. 머리를 건너뛰어 적용 가능한 폭탄을 찾는 것은 제출 순서와
        /// 다르게 소비하는 것이라 ActionSeq 가 제출 순서 검증을 보장한다는 전제를
        /// 깨뜨린다 — 그래서 의도적으로 하지 않는다.
        /// </summary>
        private GameAction? TakePendingBomb(GameState s)
        {
            int bestSeat = -1;
            long bestArrival = long.MaxValue;

            for (int seat = 0; seat < SeatCount; seat++)
            {
                if (seat == s.Turn) continue;
                var remote = _remote[seat];
                if (remote?.PeekReservedBomb() == null) continue;

                long arrival = remote.ReservedBombArrival();
                if (arrival >= bestArrival) continue;
                bestArrival = arrival;
                bestSeat = seat;
            }

            if (bestSeat < 0) return null;

            var pending = _remote[bestSeat]!.PeekReservedBomb()!;
            var move = MoveResolver.ResolveMove(s, bestSeat, pending);
            if (move == null || !move.IsBomb) return null; // 아직 합법이 아니면 예약을 유지한다

            _remote[bestSeat]!.ConsumeReservedBomb();
            return GameAction.Play(bestSeat, move.Cards);
        }
    }
}
