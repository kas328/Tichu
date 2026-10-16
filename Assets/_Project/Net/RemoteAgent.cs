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
    ///
    /// M5: 대리 AI 를 받으면 사람이 없을 때 결정을 넘긴다. 연결이 끊기거나(SetConnected(false))
    /// 연속 maxTimeouts 번 시간 초과면 "자리 비움" — 묻자마자 대리 AI 가 둔다. 그 좌석의 아무
    /// 액션·Resume·재접속이 복귀다(다음 질문부터). 방에 연결된 원격 좌석이 하나도 없으면(anyConnected
    /// == false) 대리 AI 로 넘기지 않고 무기한 기다린다 — 판을 멈추는 쪽은 방이고 종료도 방이 한다.
    /// 질문마다 AskId 를 붙이고, 지난 질문을 향한 늦은 답은 거부한다. 대리 AI 는 큰 티츄·작은 티츄를
    /// 대신 부르지 않는다. 대리 AI 가 없으면 M4 이전과 동작이 같다.
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
        private bool _tichuCalledThisRound;   // 이번 라운드에 CallTichu 를 이미 받았다(이후 것은 무시)

        // 작은 티츄 예약을 받았을 때 부른다(잠금 밖). 세션은 차례 대기 중인 좌석을 깨우도록
        // 잇는다 — 콜한 좌석이 아니라 지금 Play 를 기다리는 좌석이 Yield 해야 드라이버가 돈다.
        private readonly Action _onTichuReserved;
        // Play 대기에 들어간 직후 부른다. 어느 좌석이든 소비되지 않은 예약이 있으면 스스로 깨운다
        // — 0단계와 _awaiting 설정 사이에 도착한 콜을 놓치지 않기 위함. null 이면 확인하지 않는다.
        private readonly Func<bool>? _anyTichuPending;

        private readonly IAgent? _standIn;
        private readonly Func<bool>? _anyConnected;
        private readonly Action<string>? _log;
        private readonly int _maxTimeouts;
        private readonly Func<DateTime?>? _phaseStartedUtc;
        private readonly Func<DateTime> _utcNow;
        private bool _connected = true;   // _gate — 방이 SetConnected 로 바꾼다
        private bool _afk;                // _gate — 연속 시간 초과로 자리 비움
        private int _timeouts;            // _gate — 연속 시간 초과 횟수
        private long _askCounter;         // _gate
        private long _currentAsk;         // _gate — 지금 사람에게 묻는 질문(0 = 없음)
        private bool _paused;             // _gate — 아무도 연결돼 있지 않아 무기한 대기 중
        private bool _grandDecided;       // _gate — 이번 라운드 결정 끝
        private bool _exchangeDecided;    // _gate
        private DateTime? _decisionDeadlineUtc; // _gate — 진행 중인 차례·용 양도 결정의 마감(재질문해도 유지, M5.1)
        private (StepKind Kind, int Tricks, int Plays, int Turn) _decisionKey;   // _gate — 그 마감이 속한 결정

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
            CancellationToken ct,
            Action? onTichuReserved = null,
            Func<bool>? anyTichuPending = null,
            IAgent? standIn = null,
            Func<bool>? anyConnected = null,
            Action<string>? log = null,
            int maxTimeouts = 1,
            Func<DateTime?>? phaseStartedUtc = null,
            Func<DateTime>? utcNow = null)
        {
            _seat = seat;
            _send = send;
            _onAwait = onAwait;
            _nextArrival = nextArrival;
            _timeout = timeout;
            _ct = ct;
            _onTichuReserved = onTichuReserved ?? WakeTurnForTichu;
            _anyTichuPending = anyTichuPending;
            _standIn = standIn;
            _anyConnected = anyConnected;
            _log = log;
            _maxTimeouts = maxTimeouts;
            _phaseStartedUtc = phaseStartedUtc;
            _utcNow = utcNow ?? (() => DateTime.UtcNow);
        }

        // ── 클라이언트 입구 ──────────────────────────────────────────────────────

        /// <summary>클라이언트가 액션을 제출한다. 전송 스레드에서 호출된다.</summary>
        public void Submit(ClientAction a)
        {
            if (a.Seat != _seat) { Reject(a, "action seat does not match this seat"); return; }

            // 거부는 잠금 안에서 결정만 하고 해제한 뒤에 보낸다 — _gate 를 쥔 채 콜백을
            // 부르면 재진입 틈이 생긴다. 이러면 좌석 불일치 거부와 위치가 같아진다.
            string? rejectReason = null;
            bool tichuReserved = false;
            lock (_gate)
            {
                if (a.ActionSeq <= _lastSeq)
                {
                    rejectReason = "duplicate or out-of-order actionSeq";
                }
                else
                {
                    _lastSeq = a.ActionSeq;

                    // 그 좌석이 무언가 보냈다 = 자리에 있다. 자리 비움이었으면 대기를 깨워 다시 보게 한다
                    // (멈춤 대기 중이면 사람에게 다시 묻는다).
                    bool wasAfk = _afk;
                    _afk = false;
                    _timeouts = 0;
                    if (wasAfk) WakeLocked();

                    if (a.Kind == ClientActionKind.Resume)
                    {
                        // 복귀 표시만 — 적용할 것이 없다.
                    }
                    else if (_awaiting.HasValue && Matches(_awaiting.Value, a.Kind))
                    {
                        if (a.AskId != 0 && a.AskId != _currentAsk) rejectReason = "stale answer";
                        else if (DecidedLocked(a.Kind)) rejectReason = "already decided this round";
                        else
                        {
                            // 큐로 받는다. 거부 뒤 재제출처럼 한 결정에 여러 액션이 연달아 올 수 있고,
                            // 단일 슬롯이면 먼저 온 것이 덮어써져 거부 통보가 사라진다.
                            _matched.Enqueue(a);
                            _signal.Release();
                            return;
                        }
                    }
                    else if (a.Kind == ClientActionKind.CallTichu)
                    {
                        // 라운드당 예약은 한 번만 받는다. 작은 티츄의 합법성은 라운드 안에서 한
                        // 방향으로만 변한다(적용됐거나 불법이 되면 이후 것도 불법) — 그래서 두 번째
                        // 부터는 무시해도 정당한 콜을 잃지 않는다. 받아 주면 연타 한 번마다 드라이버
                        // 0단계가 한 스텝씩 돌아(엔진 거부여도 return) MaxSteps 를 소모시킬 수 있다.
                        if (!_tichuCalledThisRound)
                        {
                            _tichuCalledThisRound = true;
                            _tichuReserved = true;
                            tichuReserved = true;   // 대기 좌석 깨우기는 잠금을 푼 뒤에
                        }
                    }
                    else if (a.AskId != 0 && a.AskId != _currentAsk)
                    {
                        // 지금 묻지 않는 질문을 향한 번호 붙은 답 = 시간 초과로 대리 AI 가 이미 둔 질문의 늦은 답.
                        // 차례 밖 큐에 넣으면 다음 차례에 엉뚱하게 쓰인다. (현재 질문 번호면 대기가 막 공개되기
                        // 직전에 온 정당한 답이라 아래에서 보관한다.)
                        rejectReason = "stale answer";
                    }
                    else if (DecidedLocked(a.Kind))
                    {
                        rejectReason = "already decided this round";
                    }
                    else if (a.Kind == ClientActionKind.Play)
                    {
                        // 차례밖 폭탄 후보. 자기 차례가 되면 Await 가 여기서 꺼내 쓴다.
                        // 도착 순번은 제출 시점에 확정해 항목마다 들고 간다.
                        _offTurnPlays.Enqueue(new OffTurnPlay(a, _nextArrival()));
                    }
                    else
                    {
                        _early.Add(a);
                    }
                }
            }
            if (rejectReason != null) Reject(a, rejectReason);
            // 잠금 밖에서 부른다 — 콜백은 다른 좌석의 _gate 를 잡으므로, 쥔 채 부르면 잠금이 중첩된다.
            if (tichuReserved) _onTichuReserved();
        }

        /// <summary>
        /// 이 좌석이 차례(Play)를 기다리는 중이면 깨워 DecideTurn 이 Yield 를 돌려주게 한다.
        /// 드라이버는 0단계에서 예약된 작은 티츄를 모두 적용·방송한 뒤 이 좌석에 다시 묻는다.
        /// CallTichu 액션을 깨움 표식으로 _matched 에 넣고 Release 해 (퍼밋 수 == _matched.Count)
        /// 불변식을 지킨다. 표식은 예약을 들고 있지 않다 — 예약 소비는 TakeReservedTichu 만 한다.
        /// 표식이 여러 개 쌓여도 무해하다: 남은 것은 다음 Await 시작의 배수가 퍼밋과 함께 버린다.
        /// </summary>
        public void WakeTurnForTichu()
        {
            lock (_gate)
            {
                if (_awaiting != StepKind.Play) return;
                _matched.Enqueue(ClientAction.CallTichu(_seat, 0L));
                _signal.Release();
            }
        }

        /// <summary>방이 연결해 둔 사람이 있는가(끊기면 false).</summary>
        public bool IsConnected { get { lock (_gate) return _connected; } }

        /// <summary>자리 비움 — 끊겼거나 연속 시간 초과. 뷰의 Away 로 다른 좌석에 보인다.</summary>
        public bool IsAway { get { lock (_gate) return !_connected || _afk; } }

        /// <summary>방이 연결/끊김을 알린다. 대기 중이면 깨워 다시 판단하게 한다(끊김 → 즉시 대리 AI).</summary>
        public void SetConnected(bool connected)
        {
            lock (_gate)
            {
                _connected = connected;
                if (connected) { _afk = false; _timeouts = 0; }
                WakeLocked();
            }
        }

        /// <summary>아무도 없어 멈춰 기다리는 중이면 깨운다 — 다른 좌석의 사람이 돌아왔다.</summary>
        public void WakeIfPaused()
        {
            lock (_gate) { if (_paused) WakeLocked(); }
        }

        /// <summary>재접속: 새 앱은 ActionSeq 를 1 부터 센다.</summary>
        public void ResetActionSeq()
        {
            lock (_gate) _lastSeq = 0;
        }

        /// <summary>대기 중이면 Resume 표식으로 깨운다 — Await 가 상태를 다시 보고 다시 묻는다. _gate 안에서.</summary>
        private void WakeLocked()
        {
            if (!_awaiting.HasValue) return;
            _matched.Enqueue(ClientAction.Resume(_seat, 0L));
            _signal.Release();
        }

        private bool DecidedLocked(ClientActionKind k) =>
            (_grandDecided && (k == ClientActionKind.CallGrandTichu || k == ClientActionKind.DeclineGrandTichu))
            || (_exchangeDecided && k == ClientActionKind.Exchange);

        /// <summary>소비되지 않은 작은 티츄 예약이 있는가.</summary>
        public bool HasReservedTichu
        {
            get { lock (_gate) return _tichuReserved; }
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
                while (_matched.Count > 0)
                {
                    var m = _matched.Dequeue();
                    if (m.Kind != ClientActionKind.CallTichu && m.Kind != ClientActionKind.Resume) discarded.Add(m);   // 깨움 표식은 거부 대상이 아니다
                }
                discarded.AddRange(_early);
                _early.Clear();
                while (_offTurnPlays.Count > 0) discarded.Add(_offTurnPlays.Dequeue().Action);
                _tichuReserved = false;
                _tichuCalledThisRound = false;
                _grandDecided = false;
                _exchangeDecided = false;
                _paused = false;
                _awaiting = null;
                _decisionDeadlineUtc = null;   // 끝나지 않은 결정의 마감이 새 라운드로 새지 않게
                while (_signal.Wait(0)) { }   // 남은 퍼밋 소각 — 퍼밋 수와 큐 길이를 함께 0으로
            }
            foreach (var a in discarded) Reject(a, "stale action from a previous round");
        }

        // ── IAgent 구현 ─────────────────────────────────────────────────────────

        /// <inheritdoc/>
        public bool CallGrandTichu(in DecisionContext ctx)
        {
            var a = Await(StepKind.GrandTichu, ctx.State, ctx.Seat);
            lock (_gate) _grandDecided = true;
            // 사람이 없으면 패스 — 대리 AI 가 큰 티츄를 대신 부르지 않는다(계획 Global Constraints).
            return a != null && a.Kind == ClientActionKind.CallGrandTichu;
        }

        /// <inheritdoc/>
        public ExchangeChoice ChooseExchange(in DecisionContext ctx)
        {
            var state = ctx.State;
            int seat = ctx.Seat;
            while (true)
            {
                var a = Await(StepKind.Exchange, state, seat);
                if (a == null)
                {
                    lock (_gate) _exchangeDecided = true;
                    return _standIn!.ChooseExchange(ctx);
                }
                if (!IsLegalExchange(state, seat, a)) { Reject(a, "exchange cards must be three distinct cards from hand"); continue; }
                lock (_gate) _exchangeDecided = true;
                return new ExchangeChoice(a.ToLeft, a.ToPartner, a.ToRight);
            }
        }

        /// <inheritdoc/>
        public bool CallTichu(in DecisionContext ctx) => TakeReservedTichu();

        /// <inheritdoc/>
        public TurnDecision DecideTurn(in DecisionContext ctx)
        {
            var d = DecideTurnCore(ctx);
            if (!d.IsYield) ClearDecisionDeadline();   // Yield 는 같은 결정을 다시 묻는다 — 마감 유지
            return d;
        }

        private TurnDecision DecideTurnCore(in DecisionContext ctx)
        {
            var state = ctx.State;
            int seat = ctx.Seat;
            while (true)
            {
                var a = Await(StepKind.Play, state, seat);
                if (a == null) return _standIn!.DecideTurn(ctx);

                // CallTichu 로 깨어났다 — 드라이버가 예약을 먼저 적용하고 다시 묻게 한다.
                if (a.Kind == ClientActionKind.CallTichu) return TurnDecision.Yield;

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
            int r = ChooseDragonRecipientCore(ctx);
            ClearDecisionDeadline();
            return r;
        }

        private int ChooseDragonRecipientCore(in DecisionContext ctx)
        {
            var state = ctx.State;
            int seat = ctx.Seat;
            while (true)
            {
                var a = Await(StepKind.DragonGift, state, seat);
                if (a == null) return _standIn!.ChooseDragonRecipient(ctx);
                int r = a.RecipientSeat;
                bool isOpponent = r >= 0 && r < 4 && (r % 2) != (seat % 2);
                if (!isOpponent) { Reject(a, "dragon recipient must be an opponent"); continue; }
                return r;
            }
        }

        // ── 내부 ────────────────────────────────────────────────────────────────

        /// <summary>
        /// 해당 종류의 액션이 올 때까지 기다린다. null 이면 사람이 없다 — 호출자가 대리 AI 에게 넘긴다.
        /// 사람 있음: 질문 번호·남은 초를 붙여 묻고 기다린다(초과 → null, 연속 _maxTimeouts 번이면 자리 비움).
        /// 대기 상한은 마감까지 남은 시간이다. 차례·용 양도는 그 결정을 처음 물을 때 마감을 정하고 다시 물어도 유지하며
        /// (재접속·티츄 Yield·거부 뒤 재질문 — 시간을 되돌리지 않는다), 큰 티츄·교환은 단계 시작부터 전원이 같은 마감을 공유한다.
        /// 멈춤에서 돌아와도 예외 없이 남은 시간만 준다(M5.1).
        /// 자리 비움: 묻지 않고 바로 null. 단 방에 연결된 사람이 아무도 없으면 멈춰 무기한 기다린다.
        /// Resume 표식(연결·복귀 변화)으로 깨면 상태를 다시 보고 처음부터 다시 묻는다.
        /// </summary>
        private ClientAction? Await(StepKind kind, GameState state, int seat)
        {
            while (true)
            {
                // 다른 좌석의 잠금을 잡는 콜백이라 _gate 밖에서 부른다(잠금 중첩 금지).
                bool anyConnected = _standIn == null || (_anyConnected?.Invoke() ?? true);
                bool human, paused;
                long ask;
                lock (_gate)
                {
                    human = _standIn == null || (_connected && !_afk);
                    paused = !human && !anyConnected;
                    ask = ++_askCounter;
                    _currentAsk = human ? ask : 0;
                }
                if (!human && !paused)
                {
                    // 큰 티츄·교환은 미리 낸 답이 있으면 대리 AI 에게 넘기기 전에 먼저 쓴다 — 답을 내고 곧
                    // 끊긴 사람의 결정이 사라지면 안 된다(재접속 뒤 다시 뜬 질문은 "already decided" 로 거부된다).
                    ClientAction? early = null;
                    List<ClientAction>? staleEarly = null;
                    if (kind == StepKind.GrandTichu || kind == StepKind.Exchange)
                        lock (_gate) early = TakeEarlyLocked(kind, ask, ref staleEarly);
                    if (staleEarly != null) foreach (var st in staleEarly) Reject(st, "stale answer");
                    return early;
                }

                // 사람에게 물을 때만 시계가 돈다(멈춤 대기는 무기한). 큰 티츄·교환은 단계 시작부터 전원이 같은
                // 마감을 공유하고, 차례·용 양도는 결정마다 마감 하나 — 다시 물어도 남은 시간만 준다.
                // 콜백은 잠금 밖에서 부른다(세션의 읽기 전용 값이라 잠금은 없다).
                TimeSpan waitFor = _timeout;
                if (human && _timeout != Timeout.InfiniteTimeSpan)
                {
                    var now = _utcNow();
                    if (kind == StepKind.GrandTichu || kind == StepKind.Exchange)
                    {
                        var phaseStart = _phaseStartedUtc?.Invoke();
                        if (phaseStart.HasValue) waitFor = Remaining(phaseStart.Value + _timeout, now);
                    }
                    else
                    {
                        // 판이 한 수라도 진행됐으면 다른 결정이다 — Yield 뒤 다른 좌석의 폭탄으로 차례가 넘어가면
                        // 드라이버가 이 결정을 다시 묻지 않아 마감이 지워지지 않는다(트릭·수·차례로 구분).
                        var key = (kind, state.CompletedTricks.Count, state.CurrentTrick?.History.Count ?? 0, state.Turn);
                        DateTime deadline;
                        lock (_gate)
                        {
                            if (_decisionDeadlineUtc == null || _decisionKey != key)
                            {
                                _decisionDeadlineUtc = now + _timeout;
                                _decisionKey = key;
                            }
                            deadline = _decisionDeadlineUtc.Value;
                        }
                        waitFor = Remaining(deadline, now);
                    }
                }
                int? secondsLeft = human && _timeout != Timeout.InfiniteTimeSpan
                    ? (int)Math.Ceiling(waitFor.TotalSeconds) : (int?)null;
                // 버퍼에 답이 이미 있든 없든 **항상 먼저 알린다.** 이 방송이 "지금 누구에게 무엇을 묻는지"의
                // 유일한 신호라, 건너뛰면 아무도 차례를 알 수 없게 된다. (예외: 자리 비움 좌석은 묻지 않고
                // 대리 AI 가 바로 둔다 — 위에서 이미 반환했다.)
                _onAwait(_seat, BuildAwaiting(kind, state, seat, ask, secondsLeft));

                // 이전 질문에서 남은 매칭 큐 항목은 이 질문의 답이 아니다. 선행 큐로 되돌리고 그 항목의
                // 퍼밋을 함께 소각해 (퍼밋 수 == _matched.Count) 불변식을 복원한다. 배수를 _early 스캔보다
                // **먼저** 두어야 같은 종류의 잔여물이 스캔 대상에 들어온다. 이 배수 덕분에 대기 시작 시
                // _matched 가 비고, _awaiting 이 설정된 동안 Submit 은 매칭 종류만 넣으므로 대기 후 Dequeue 에는
                // 종류 필터가 필요 없다. 깨움 표식(CallTichu·Resume)은 예약과 별개라 되돌리지 않고 버린다 —
                // 선행 큐에 넣으면 어떤 질문과도 매칭되지 않은 채 남아 라운드 경계에서 엉뚱한 거부 통보가 된다.
                // 번호 붙은 답은 지난 질문의 늦은 답이라 거부한다.
                // _awaiting 이 null 인 동안 Submit 은 _matched 에 넣지 않으므로 별도 잠금이어도 안전하다.
                List<ClientAction>? stale = null;
                lock (_gate)
                {
                    while (_matched.Count > 0)
                    {
                        var leftover = _matched.Dequeue();
                        _signal.Wait(0);
                        if (leftover.Kind == ClientActionKind.CallTichu || leftover.Kind == ClientActionKind.Resume) continue;
                        if (leftover.AskId != 0) (stale ??= new List<ClientAction>()).Add(leftover);
                        else _early.Add(leftover);
                    }
                }
                if (stale != null) foreach (var st in stale) Reject(st, "stale answer");

                // 버퍼에서 꺼내는 시점에도 번호를 다시 본다 — 지난 질문의 번호 붙은 답은 쓰지 않고 거부한다.
                ClientAction? ready = null;
                List<ClientAction>? stale2 = null;
                lock (_gate)
                {
                    // 위에서 상태를 읽은 뒤 방송하는 사이에 연결·복귀가 바뀌었을 수 있다 — 그 깨움은
                    // _awaiting 이 없어 유실됐으니 다시 묻는다.
                    if (_standIn != null && human != (_connected && !_afk)) continue;

                    // 멈춤 중에도 큰 티츄·교환의 미리 낸 답은 쓴다(위 비사람 경로와 같은 이유).
                    if (human || kind == StepKind.GrandTichu || kind == StepKind.Exchange)
                        ready = TakeEarlyLocked(kind, ask, ref stale2);

                    if (human)
                    {
                        if (ready == null && kind == StepKind.Play)
                        {
                            while (_offTurnPlays.Count > 0)
                            {
                                var o = _offTurnPlays.Dequeue().Action;
                                if (o.AskId != 0 && o.AskId != ask) { (stale2 ??= new List<ClientAction>()).Add(o); continue; }
                                ready = o;
                                break;
                            }
                        }
                    }

                    if (ready != null)
                    {
                        _timeouts = 0;
                        _currentAsk = 0;   // 질문이 끝났다 — 같은 번호의 중복·늦은 답은 거부된다
                    }
                    else
                    {
                        _awaiting = kind;
                        _paused = paused;
                    }
                }
                if (stale2 != null) foreach (var st in stale2) Reject(st, "stale answer");
                if (ready != null) return ready;

                bool got = false;
                try
                {
                    // _awaiting 을 세운 **뒤에** 예약을 확인한다. 콜 제출 쪽은 예약을 세운 뒤에 _awaiting 을
                    // 보므로, 둘 중 적어도 한쪽은 상대의 쓰기를 본다 — 0단계와 여기 사이에 도착한 콜도
                    // 결정적으로 깨운다. 양쪽 다 깨워도 남는 표식은 버려진다.
                    if (human && kind == StepKind.Play && _anyTichuPending != null && _anyTichuPending())
                        WakeTurnForTichu();

                    // 멈춤 공개 뒤에 다른 좌석이 돌아왔을 수 있다(WakeIfPaused 가 _paused 를 못 봄) — 스스로 깨운다.
                    if (paused && (_anyConnected?.Invoke() ?? true))
                        lock (_gate) WakeLocked();

                    // 마감이 이미 지났으면(waitFor == 0) 기다리지 않고 시간 초과로 간다 — 미리 낸 답은 위 스캔에서 이미 썼다.
                    // Wait(0) 이어도 취소는 확인된다.
                    got = _signal.Wait(human ? waitFor : Timeout.InfiniteTimeSpan, _ct);
                }
                finally
                {
                    lock (_gate)
                    {
                        // 취소는 두 lock 블록 사이에서 throw 되므로 여기서 정리해야 _awaiting 이 남지 않는다.
                        _awaiting = null;
                        _paused = false;
                        _currentAsk = 0;   // 질문이 끝났다
                        // 시간 초과 경계에 끼어든 답·표식은 이 대기가 소비한다(다음 질문으로 새지 않게).
                        if (!got && _matched.Count > 0 && _signal.Wait(0)) got = true;
                    }
                }

                if (!got)
                {
                    if (_standIn == null) throw new TimeoutException($"seat {_seat} did not act within {_timeout}");
                    bool nowAfk;
                    lock (_gate)
                    {
                        _timeouts++;
                        nowAfk = _timeouts >= _maxTimeouts;
                        if (nowAfk) _afk = true;
                        _currentAsk = 0;
                    }
                    _log?.Invoke(nowAfk
                        ? $"좌석 {_seat} 시간 초과 {_maxTimeouts}회 — 자리 비움, 대리 AI"
                        : $"좌석 {_seat} 시간 초과 — 대리 AI 가 한 수");
                    return null;
                }

                ClientAction a;
                lock (_gate) a = _matched.Dequeue();
                if (a.Kind == ClientActionKind.Resume) continue;   // 연결·복귀 변화 — 다시 판단
                lock (_gate) _timeouts = 0;
                return a;
            }
        }

        /// <summary>마감까지 남은 시간 — 0 아래면 0, 벽시계가 뒤로 가도 전체 시간을 넘지 않는다.</summary>
        private TimeSpan Remaining(DateTime deadlineUtc, DateTime now)
        {
            var left = deadlineUtc - now;
            if (left < TimeSpan.Zero) return TimeSpan.Zero;
            return left > _timeout ? _timeout : left;
        }

        /// <summary>차례·용 양도 결정이 끝났다 — 다음 결정은 새 마감을 받는다.</summary>
        private void ClearDecisionDeadline()
        {
            lock (_gate) _decisionDeadlineUtc = null;
        }

        /// <summary>선행 큐에서 이 질문의 답을 꺼낸다(_gate 안에서). 지난 질문의 번호 붙은 답은 stale 로 모은다.</summary>
        private ClientAction? TakeEarlyLocked(StepKind kind, long ask, ref List<ClientAction>? stale)
        {
            for (int i = 0; i < _early.Count; i++)
            {
                if (!Matches(kind, _early[i].Kind)) continue;
                var buffered = _early[i];
                _early.RemoveAt(i);
                i--;
                if (buffered.AskId != 0 && buffered.AskId != ask) { (stale ??= new List<ClientAction>()).Add(buffered); continue; }
                return buffered;
            }
            return null;
        }

        private Awaiting BuildAwaiting(StepKind kind, GameState state, int seat, long askId, int? secondsLeft)
        {
            // Seat 은 _seat, LegalMoves/CanPass 는 seat 로 계산한다. 둘이 어긋나면 한 좌석의 신분에 다른 좌석의
            // 합법수를 실어 보내는 은닉 정보 누출이 된다 — 이 마일스톤의 핵심 불변식이라 배선 오류를 조용히
            // 넘기지 않고 크게 터뜨린다(클라이언트 입력이 아니라 프로그래머 오류이므로 세션을 죽이는 쪽이 맞다).
            if (seat != _seat)
                throw new InvalidOperationException(
                    $"seat {_seat} 에이전트가 seat {seat} 으로 질의됐다 — 배선 오류");

            var aw = new Awaiting { Kind = kind, Seat = _seat, AskId = askId, SecondsLeft = secondsLeft };
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
