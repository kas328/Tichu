using System;
using System.Collections.Generic;
using System.Threading;
using Tichu.Core.Cards;
using Tichu.Core.Combinations;
using Tichu.Core.Game;
using Tichu.GameFlow;
using Tichu.GameFlow.Agents;

namespace Tichu.Net
{
    /// <summary>
    /// 원격 플레이어 좌석. 드라이버가 물으면 해당 종류의 액션이 도착할 때까지 블로킹한다.
    ///
    /// 세 가지 우편함을 쓴다.
    ///  · 대기 슬롯  — 지금 묻고 있는 종류의 액션. 도착하면 즉시 대기를 깨운다.
    ///  · 선행 큐    — 아직 묻지 않은 종류의 액션. 보관해 두면 교환 4인 동시 제출이 자연히 동작한다.
    ///  · 예약       — 작은 티츄 콜과 차례밖 폭탄. 드라이버의 예약 훅으로 빠져나간다.
    ///
    /// 폭탄을 예약으로 두는 이유는 드라이버의 폭탄 창이 좌석을 순회하며 각자에게 묻기
    /// 때문이다 — 사람에게 그걸 동기로 물으면 매 트릭마다 전원이 대기한다.
    ///
    /// 불법 입력은 상태를 바꾸지 않고 발신자에게만 ActionRejected 를 보낸 뒤 계속 기다린다
    /// (기획서 7.7). 즉 이 클래스가 서버측 검증 관문이다.
    ///
    /// 단, 그 보장은 블로킹 메서드 네 개(큰 티츄·교환·차례·용 양도)에만 적용된다. 예약
    /// 우편함 둘은 관문을 우회한다 — 드라이버는 티츄·폭탄 예약 훅에서 엔진이 거부한 액션을
    /// ActionRejected 없이 조용히 버린다. 그래서 작은 티츄 콜과 차례밖 폭탄은 실패해도
    /// 클라이언트가 통보를 받지 못한다. 이걸 표면화하는 일은 M1 범위가 아니다(M2/M5).
    /// </summary>
    public sealed class RemoteAgent : IAgent
    {
        private readonly int _seat;
        private readonly Action<int, ServerEvent> _send;
        private readonly Action<int, Awaiting> _onAwait;
        private readonly Func<long> _nextArrival;
        private readonly TimeSpan _timeout;
        private readonly CancellationToken _ct;

        private readonly object _gate = new object();
        private readonly SemaphoreSlim _signal = new SemaphoreSlim(0);
        private readonly List<ClientAction> _early = new List<ClientAction>();

        private StepKind? _awaiting;
        private readonly Queue<ClientAction> _matched = new Queue<ClientAction>();
        private long _lastSeq;
        private bool _tichuReserved;

        /// <summary>차례밖에 도착한 Play 한 건과 그 도착 순번.</summary>
        private readonly struct OffTurnPlay
        {
            public readonly ClientAction Action;
            public readonly long Arrival;

            public OffTurnPlay(ClientAction action, long arrival)
            {
                Action = action;
                Arrival = arrival;
            }
        }

        // 단일 슬롯이 아니라 큐인 이유: 클라이언트가 기다리지 않고 Play 를 두 번 연달아
        // 보내면 제출된 모든 액션이 각자 검증돼야 한다. 슬롯이면 먼저 온 것이 조용히
        // 덮어써져 거부 통보 없이 사라지는데, 그건 Task 4 에서 티츄 훅에서 걷어낸
        // '통보 없는 유실' 과 같은 부류다 — 이 클래스가 검증 관문이라는 전제와 정면으로
        // 어긋난다. 단, 순서는 이 큐 **안에서만** 보장된다 — 종류별로 다른 우편함으로
        // 갈라지므로(오프턴 Pass 는 _early, Play 는 여기) 전역 FIFO 는 아니다. Await 가
        // _early 를 먼저 스캔하므로 나중에 제출된 Pass 가 먼저 제출된 Play 보다 먼저
        // 서빙될 수 있다.
        private readonly Queue<OffTurnPlay> _offTurnPlays = new Queue<OffTurnPlay>();

        public RemoteAgent(
            int seat,
            Action<int, ServerEvent> send,
            Action<int, Awaiting> onAwait,
            Func<long> nextArrival,
            TimeSpan timeout,
            CancellationToken ct)
        {
            _seat = seat;
            _send = send;
            _onAwait = onAwait;
            _nextArrival = nextArrival;
            _timeout = timeout;
            _ct = ct;
        }

        // ── 클라이언트 입구 ──────────────────────────────────────────────────────

        /// <summary>클라이언트가 액션을 제출한다. 전송 스레드에서 호출된다.</summary>
        public void Submit(ClientAction a)
        {
            if (a.Seat != _seat) { Reject(a, "action seat does not match this seat"); return; }

            // 거부는 잠금 안에서 결정만 하고 해제한 뒤에 보낸다 — _gate 를 쥔 채 콜백을
            // 부르면 재진입 틈이 생긴다. 이러면 좌석 불일치 거부와 위치가 같아진다.
            string? rejectReason = null;
            lock (_gate)
            {
                if (a.ActionSeq <= _lastSeq)
                {
                    rejectReason = "duplicate or out-of-order actionSeq";
                }
                else
                {
                    _lastSeq = a.ActionSeq;

                    if (_awaiting.HasValue && Matches(_awaiting.Value, a.Kind))
                    {
                        // 큐로 받는다. 거부 뒤 재제출처럼 한 결정에 여러 액션이 연달아 올 수 있고,
                        // 단일 슬롯이면 먼저 온 것이 덮어써져 거부 통보가 사라진다.
                        _matched.Enqueue(a);
                        _signal.Release();
                        return;
                    }

                    if (a.Kind == ClientActionKind.CallTichu) { _tichuReserved = true; return; }

                    if (a.Kind == ClientActionKind.Play)
                    {
                        // 차례밖 폭탄 후보. 자기 차례가 되면 Await 가 여기서 꺼내 쓴다.
                        // 도착 순번은 제출 시점에 확정해 항목마다 들고 간다.
                        _offTurnPlays.Enqueue(new OffTurnPlay(a, _nextArrival()));
                        return;
                    }

                    _early.Add(a);
                }
            }
            if (rejectReason != null) Reject(a, rejectReason);
        }

        // ── 세션이 예약을 꺼내가는 입구 ─────────────────────────────────────────

        /// <summary>예약된 차례밖 폭탄 후보(큐의 머리). 없으면 null.</summary>
        public ClientAction? PeekReservedBomb()
        {
            lock (_gate) return _offTurnPlays.Count > 0 ? _offTurnPlays.Peek().Action : null;
        }

        /// <summary>예약된 폭탄(큐의 머리)의 도착 순번. 좌석 간 직렬화에 쓴다.</summary>
        public long ReservedBombArrival()
        {
            lock (_gate) return _offTurnPlays.Count > 0 ? _offTurnPlays.Peek().Arrival : 0;
        }

        /// <summary>예약된 폭탄(큐의 머리)을 소비한다.</summary>
        public void ConsumeReservedBomb()
        {
            lock (_gate) { if (_offTurnPlays.Count > 0) _offTurnPlays.Dequeue(); }
        }

        /// <summary>예약된 작은 티츄 콜을 한 번만 꺼낸다.</summary>
        public bool TakeReservedTichu()
        {
            lock (_gate)
            {
                if (!_tichuReserved) return false;
                _tichuReserved = false;
                return true;
            }
        }

        /// <summary>
        /// 라운드 경계에서 호출한다. 소비되지 않은 액션은 모두 지난 라운드의 질문을 향한 것이라
        /// 새 라운드의 답이 될 수 없다 — 남겨 두면 라운드당 한 번뿐인 질문(큰 티츄·교환·용 양도)의
        /// 답으로 조용히 쓰인다. 버리면서 발신자에게 거부를 통보한다(조용한 유실 금지).
        /// ActionSeq 는 매치 전체에서 단조 증가해야 하므로 초기화하지 않는다(리플레이 방지).
        /// </summary>
        public void ResetForNewRound()
        {
            var discarded = new List<ClientAction>();
            lock (_gate)
            {
                while (_matched.Count > 0) discarded.Add(_matched.Dequeue());
                discarded.AddRange(_early);
                _early.Clear();
                while (_offTurnPlays.Count > 0) discarded.Add(_offTurnPlays.Dequeue().Action);
                _tichuReserved = false;
                _awaiting = null;
                while (_signal.Wait(0)) { }   // 남은 퍼밋 소각 — 퍼밋 수와 큐 길이를 함께 0으로
            }
            foreach (var a in discarded) Reject(a, "stale action from a previous round");
        }

        // ── IAgent 구현 ─────────────────────────────────────────────────────────

        /// <inheritdoc/>
        public bool CallGrandTichu(in DecisionContext ctx)
        {
            var a = Await(StepKind.GrandTichu, ctx.State, ctx.Seat);
            return a.Kind == ClientActionKind.CallGrandTichu;
        }

        /// <inheritdoc/>
        public ExchangeChoice ChooseExchange(in DecisionContext ctx)
        {
            var state = ctx.State;
            int seat = ctx.Seat;
            while (true)
            {
                var a = Await(StepKind.Exchange, state, seat);
                if (!IsLegalExchange(state, seat, a)) { Reject(a, "exchange cards must be three distinct cards from hand"); continue; }
                return new ExchangeChoice(a.ToLeft, a.ToPartner, a.ToRight);
            }
        }

        /// <inheritdoc/>
        public bool CallTichu(in DecisionContext ctx) => TakeReservedTichu();

        /// <inheritdoc/>
        public TurnDecision DecideTurn(in DecisionContext ctx)
        {
            var state = ctx.State;
            int seat = ctx.Seat;
            while (true)
            {
                var a = Await(StepKind.Play, state, seat);

                if (a.Kind == ClientActionKind.Pass)
                {
                    if (LegalMoveGenerator.CanPass(state, seat)) return TurnDecision.Pass;
                    Reject(a, "cannot pass now");
                    continue;
                }

                var move = MoveResolver.ResolveMove(state, seat, a);
                if (move == null) { Reject(a, "illegal or ambiguous move"); continue; }
                return TurnDecision.Play(move, a.Wish);
            }
        }

        /// <inheritdoc/>
        /// <remarks>항상 null. 폭탄은 예약 경로로만 들어온다 — 그래야 매 트릭 전원 대기가 없다.</remarks>
        public Combination? DecideBomb(in DecisionContext ctx) => null;

        /// <inheritdoc/>
        public int ChooseDragonRecipient(in DecisionContext ctx)
        {
            var state = ctx.State;
            int seat = ctx.Seat;
            while (true)
            {
                var a = Await(StepKind.DragonGift, state, seat);
                int r = a.RecipientSeat;
                bool isOpponent = r >= 0 && r < 4 && (r % 2) != (seat % 2);
                if (!isOpponent) { Reject(a, "dragon recipient must be an opponent"); continue; }
                return r;
            }
        }

        // ── 내부 ────────────────────────────────────────────────────────────────

        /// <summary>해당 종류의 액션이 올 때까지 기다린다. 선행 큐와 폭탄 예약을 먼저 확인한다.</summary>
        private ClientAction Await(StepKind kind, GameState state, int seat)
        {
            // 버퍼에 답이 이미 있든 없든 **항상 먼저 알린다.** 이 방송이 "지금 누구에게
            // 무엇을 묻는지"의 유일한 신호라, 건너뛰면 아무도 차례를 알 수 없게 된다.
            _onAwait(_seat, BuildAwaiting(kind, state, seat));

            lock (_gate)
            {
                // 이전 질문에서 남은 매칭 큐 항목은 이 질문의 답이 아니다. 선행 큐로 되돌리고
                // 그 항목의 퍼밋을 함께 소각해 (퍼밋 수 == _matched.Count) 불변식을 복원한다.
                // 배수를 _early 스캔보다 **먼저** 두어야 같은 종류의 잔여물이 스캔 대상에 들어온다.
                // 이 배수 덕분에 대기 시작 시 _matched 가 비고, _awaiting 이 설정된 동안
                // Submit 은 매칭 종류만 넣으므로 대기 후 Dequeue 에는 종류 필터가 필요 없다.
                while (_matched.Count > 0)
                {
                    _early.Add(_matched.Dequeue());
                    _signal.Wait(0);
                }

                for (int i = 0; i < _early.Count; i++)
                {
                    if (!Matches(kind, _early[i].Kind)) continue;
                    var buffered = _early[i];
                    _early.RemoveAt(i);
                    return buffered;
                }

                if (kind == StepKind.Play && _offTurnPlays.Count > 0)
                    return _offTurnPlays.Dequeue().Action;

                _awaiting = kind;
            }

            bool got = false;
            try
            {
                got = _signal.Wait(_timeout, _ct);
            }
            finally
            {
                // 취소는 두 lock 블록 사이에서 throw 되므로 여기서 정리해야 _awaiting 이 남지 않는다.
                lock (_gate) _awaiting = null;
            }

            if (!got) throw new TimeoutException($"seat {_seat} did not act within {_timeout}");

            // _matched 는 이 대기가 시작되기 전에 이미 비워졌고(위의 배수), _awaiting 이
            // 설정된 동안 Submit 은 매칭하는 종류의 액션만 여기에 넣는다. 그래서 종류
            // 필터 없이 그대로 꺼내도 안전하다.
            lock (_gate) return _matched.Dequeue();
        }

        private Awaiting BuildAwaiting(StepKind kind, GameState state, int seat)
        {
            // Seat 은 _seat, LegalMoves/CanPass 는 seat 로 계산한다. 둘이 어긋나면 한 좌석의
            // 신분에 다른 좌석의 합법수를 실어 보내는 은닉 정보 누출이 된다 — 이 마일스톤의
            // 핵심 불변식이라 배선 오류를 조용히 넘기지 않고 크게 터뜨린다(클라이언트 입력이
            // 아니라 프로그래머 오류이므로 세션을 죽이는 쪽이 맞다).
            if (seat != _seat)
                throw new InvalidOperationException(
                    $"seat {_seat} 에이전트가 seat {seat} 으로 질의됐다 — 배선 오류");

            var aw = new Awaiting { Kind = kind, Seat = _seat };
            if (kind == StepKind.Play)
            {
                aw.LegalMoves = LegalMoveGenerator.LegalMoves(state, seat);
                aw.CanPass = LegalMoveGenerator.CanPass(state, seat);
            }
            return aw;
        }

        private static bool Matches(StepKind kind, ClientActionKind action) => kind switch
        {
            StepKind.GrandTichu => action == ClientActionKind.CallGrandTichu
                                || action == ClientActionKind.DeclineGrandTichu,
            StepKind.Exchange   => action == ClientActionKind.Exchange,
            StepKind.Play       => action == ClientActionKind.Play || action == ClientActionKind.Pass,
            StepKind.DragonGift => action == ClientActionKind.GiveDragon,
            _ => false,
        };

        private static bool IsLegalExchange(GameState state, int seat, ClientAction a)
        {
            if (a.ToLeft.Equals(a.ToPartner) || a.ToLeft.Equals(a.ToRight) || a.ToPartner.Equals(a.ToRight))
                return false;

            var hand = new List<Card>(state.Seats[seat].Hand);
            return Take(hand, a.ToLeft) && Take(hand, a.ToPartner) && Take(hand, a.ToRight);
        }

        private static bool Take(List<Card> hand, Card c)
        {
            int i = hand.IndexOf(c);
            if (i < 0) return false;
            hand.RemoveAt(i);
            return true;
        }

        private void Reject(ClientAction a, string reason) =>
            _send(_seat, ServerEvent.ActionRejected(a.ActionSeq, reason));
    }
}
