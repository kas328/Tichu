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
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();

        private readonly IAgent[] _agents = new IAgent[SeatCount];
        private readonly RemoteAgent?[] _remote = new RemoteAgent?[SeatCount];

        private Thread? _thread;
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
            Action<RoundOutcome>? onRoundCompleted = null)
        {
            if (aiSeats == null || aiSeats.Length != SeatCount)
                throw new ArgumentException("exactly 4 seats required", nameof(aiSeats));

            _transport = transport;
            _masterSeed = masterSeed;
            _targetScore = targetScore;
            _onRoundCompleted = onRoundCompleted;

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

        /// <summary>세션 스레드를 띄운다.</summary>
        public void Start()
        {
            _thread = new Thread(Run) { IsBackground = true, Name = "TichuMatchSession" };
            _thread.Start();
        }

        /// <summary>세션을 중단한다. 대기 중인 원격 좌석의 블로킹도 풀린다.</summary>
        public void Stop() => _cts.Cancel();

        /// <summary>세션 스레드가 끝날 때까지 기다린다(테스트·종료 처리용).</summary>
        public void Join(TimeSpan timeout) => _thread?.Join(timeout);

        /// <summary>클라이언트 액션을 해당 좌석의 원격 에이전트로 보낸다.</summary>
        public void Submit(ClientAction a)
        {
            if (a.Seat < 0 || a.Seat >= SeatCount)
            {
                _transport.SendTo(0, ServerEvent.ActionRejected(a.ActionSeq, "seat out of range"));
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
                while (!_cts.IsCancellationRequested)
                {
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
                    if (decision == MatchDecision.Continue) continue;

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

        private void OnApply(GameState s, GameAction a) => Broadcast(s, null);

        /// <summary>RemoteAgent 가 대기에 들어갔다. 대기 좌석엔 합법수까지, 나머지엔 축약판을 보낸다.</summary>
        private void OnAwait(int seat, Awaiting awaiting)
        {
            var redacted = awaiting.Redacted();
            for (int i = 0; i < SeatCount; i++)
                _transport.SendTo(i, ServerEvent.StateUpdate(
                    ViewProjector.ProjectFor(CurrentState!, i), i == seat ? awaiting : redacted));
        }

        private void Broadcast(GameState s, Awaiting? awaiting)
        {
            CurrentState = s;
            for (int seat = 0; seat < SeatCount; seat++)
                _transport.SendTo(seat, ServerEvent.StateUpdate(ViewProjector.ProjectFor(s, seat), awaiting));
        }

        /// <summary>세션 스레드 전용. OnAwait 가 투영에 쓴다.</summary>
        private GameState? CurrentState { get; set; }

        private int TakePendingTichu(GameState s)
        {
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
