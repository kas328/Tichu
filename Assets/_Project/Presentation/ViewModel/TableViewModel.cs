using System.Collections.Generic;
using R3;
using Tichu.Core.Cards;
using Tichu.Core.Combinations;
using Tichu.Core.Game;
using Tichu.GameFlow;
using Tichu.GameFlow.Agents;
using Tichu.Net;

namespace Tichu.Presentation.ViewModel
{
    /// <summary>
    /// 서버가 보낸 <see cref="PlayerView"/> 를 R3 ReactiveProperty 로 투영하고, 사람의
    /// 입력을 <see cref="ClientAction"/> 으로 내보낸다. 권위 상태(GameState)를 들고 있지
    /// 않다 — 여기 있는 값은 전부 서버가 내 좌석 시야로 마스킹해 보낸 것이다.
    ///
    /// 입력은 "보내고 잊는다". 수락·거부는 서버가 ActionRejected 로 알려 주며, 여기서
    /// TCS 로 기다리지 않는다 — 그 대기·취소 기계가 서버의 RemoteAgent 우편함으로
    /// 옮겨 갔기 때문이다. Submit* 의 bool 은 "지금 Awaiting 이 이 입력을 허용하는가"
    /// 만 뜻한다(로컬 선검사). false 면 아무것도 내보내지 않는다.
    /// </summary>
    public sealed class TableViewModel
    {
        private const int FullHand = 14;

        private readonly int _mySeat;
        private long _actionSeq;

        // ── 서버 시야 투영 ───────────────────────────────────────────────────

        /// <summary>현재 라운드 페이즈.</summary>
        public ReactiveProperty<RoundPhase> Phase { get; } = new ReactiveProperty<RoundPhase>();

        /// <summary>현재 차례 좌석(Play 페이즈에서만 유효, 그 외 -1).</summary>
        public ReactiveProperty<int> CurrentTurn { get; } = new ReactiveProperty<int>(-1);

        /// <summary>내 손패.</summary>
        public ReactiveProperty<IReadOnlyList<Card>> MyHand { get; }
            = new ReactiveProperty<IReadOnlyList<Card>>(new List<Card>());

        /// <summary>진행 중 트릭의 공개 상태.</summary>
        public ReactiveProperty<TrickView> CurrentTrick { get; } = new ReactiveProperty<TrickView>();

        /// <summary>마지막으로 완료된 라운드 결산. 라운드 시작 전 null.</summary>
        public ReactiveProperty<RoundResult> RoundResult { get; } = new ReactiveProperty<RoundResult>();

        /// <summary>서버가 지금 <b>나에게</b> 묻고 있는 것. 내 차례가 아니면 null.</summary>
        public ReactiveProperty<Awaiting> Prompt { get; } = new ReactiveProperty<Awaiting>();

        /// <summary>현재 활성 마작 소원 랭크(없으면 null).</summary>
        public ReactiveProperty<int?> Wish { get; } = new ReactiveProperty<int?>();

        /// <summary>매치 누적 팀 점수. RoundScored 를 더해 만든다.</summary>
        public ReactiveProperty<int> CumulativeA { get; } = new ReactiveProperty<int>(0);
        public ReactiveProperty<int> CumulativeB { get; } = new ReactiveProperty<int>(0);

        /// <summary>작은 티츄 선언 가능 여부(Play·내 손패 14장·미콜·미전송).</summary>
        public ReactiveProperty<bool> TichuAvailable { get; } = new ReactiveProperty<bool>(false);

        private readonly ReactiveProperty<int>[] _handCounts = new ReactiveProperty<int>[4];
        private readonly ReactiveProperty<TichuCall>[] _calls = new ReactiveProperty<TichuCall>[4];

        /// <summary>좌석 i 의 손패 수.</summary>
        public ReactiveProperty<int> HandCount(int seat) => _handCounts[seat];

        /// <summary>좌석 i 의 티츄 콜 상태.</summary>
        public ReactiveProperty<TichuCall> SeatCall(int seat) => _calls[seat];

        // ── 스트림 ───────────────────────────────────────────────────────────

        private readonly Subject<AppliedAction> _played = new Subject<AppliedAction>();
        /// <summary>서버가 적용한 액션(로그·효과음·연출이 먹는다).</summary>
        public Observable<AppliedAction> Played => _played;

        private readonly Subject<R3.Unit> _cleared = new Subject<R3.Unit>();
        /// <summary>새 라운드 시작(로그 비우기).</summary>
        public Observable<R3.Unit> PlaysCleared => _cleared;

        private readonly Subject<ClientAction> _submitted = new Subject<ClientAction>();
        /// <summary>내가 서버로 올릴 액션. TichuClient 가 전송에 밀어 넣는다.</summary>
        public Observable<ClientAction> Submitted => _submitted;

        // ── 로컬 전용 상태 ───────────────────────────────────────────────────

        // 빠른 진행(스킵). 세션 스레드의 AI 가 읽으므로 volatile 로 둔다.
        private volatile bool _fastForward;

        /// <summary>true 면 AI 관전 지연을 건너뛴다. 내가 아웃된 뒤 사용, 새 라운드에 해제.</summary>
        public bool FastForward
        {
            get { return _fastForward; }
            set { _fastForward = value; }
        }

        // 작은 티츄를 보냈다(서버 적용 전이라도 버튼을 숨긴다).
        private bool _tichuSent;
        // 차례밖 폭탄을 보냈다(뷰가 "발동!" 표시). 내 Play 가 적용되면 해제.
        private bool _bombSent;

        /// <summary>차례밖 폭탄 전송 상태(뷰 표시용).</summary>
        public bool HasPendingBomb { get { return _bombSent; } }

        public TableViewModel(int mySeat)
        {
            _mySeat = mySeat;
            for (int i = 0; i < 4; i++)
            {
                _handCounts[i] = new ReactiveProperty<int>(0);
                _calls[i] = new ReactiveProperty<TichuCall>(TichuCall.None);
            }
        }

        // ── 수신 ─────────────────────────────────────────────────────────────

        /// <summary>서버 StateUpdate 의 뷰를 투영한다.</summary>
        public void Apply(PlayerView v)
        {
            Phase.Value = v.Phase;
            Wish.Value = v.Wish;

            // 트릭은 매번 새 객체라 참조 비교로 통지된다(강제 재통지 불필요 —
            // 제자리 변경되는 GameState 를 직접 보던 시절의 우회가 사라졌다).
            CurrentTrick.Value = v.CurrentTrick;

            for (int i = 0; i < 4; i++)
            {
                _handCounts[i].Value = v.HandCounts[i];
                _calls[i].Value = v.Calls[i];
            }

            TichuAvailable.Value = v.Phase == RoundPhase.Play
                && v.Calls[_mySeat] == TichuCall.None
                && v.HandCounts[_mySeat] == FullHand
                && !_tichuSent;

            CurrentTurn.Value = v.Phase == RoundPhase.Play ? v.Turn : -1;

            // 내게 온 질문만 프롬프트다. 남의 차례 알림은 축약판이라 합법수가 없다.
            Prompt.Value = (v.Awaiting != null && v.Awaiting.Seat == _mySeat) ? v.Awaiting : null;

            // MyHand 는 마지막 — 손패 렌더가 최신 트릭/차례를 참조한다.
            MyHand.Value = new List<Card>(v.MyHand);
        }

        /// <summary>서버가 적용한 액션을 로그·사운드로 흘린다.</summary>
        public void Record(AppliedAction a)
        {
            if (a.Kind == GameActionKind.Play && a.Seat == _mySeat) _bombSent = false;

            if (a.Kind == GameActionKind.Play
                || a.Kind == GameActionKind.Pass
                || a.Kind == GameActionKind.GiveDragon)
                _played.OnNext(a);
        }

        /// <summary>라운드 정산 수신 — 누적에 더하고 결과를 띄운다.</summary>
        public void OnRoundScored(RoundResult r)
        {
            CumulativeA.Value += r.TeamATotal;
            CumulativeB.Value += r.TeamBTotal;
            RoundResult.Value = r;
        }

        /// <summary>새 라운드 시작: 로그·로컬 예약 상태를 비운다.</summary>
        public void ClearPlays()
        {
            _tichuSent = false;
            _bombSent = false;
            RoundResult.Value = null;
            FastForward = false;
            _cleared.OnNext(default);
        }

        // ── 송신 (로컬 선검사 후 발행) ───────────────────────────────────────

        /// <summary>큰 티츄 선언/패스. 큰 티츄 질문을 받은 상태여야 한다.</summary>
        public bool SubmitGrandTichu(bool call)
        {
            if (!Asking(StepKind.GrandTichu)) return false;
            Send(call
                ? ClientAction.CallGrandTichu(_mySeat, NextSeq())
                : ClientAction.DeclineGrandTichu(_mySeat, NextSeq()));
            return true;
        }

        /// <summary>교환 3장. 서로 다르고 모두 내 손패에 있어야 한다.</summary>
        public bool SubmitExchange(ExchangeChoice choice)
        {
            if (!Asking(StepKind.Exchange)) return false;
            if (!IsDistinctAndInHand(choice.ToLeft, choice.ToPartner, choice.ToRight, MyHand.CurrentValue))
                return false;
            Send(ClientAction.Exchange(_mySeat, NextSeq(), choice.ToLeft, choice.ToPartner, choice.ToRight));
            return true;
        }

        /// <summary>패스. 서버가 CanPass 라고 알린 상태여야 한다.</summary>
        public bool SubmitPass()
        {
            var aw = Prompt.CurrentValue;
            if (aw == null || aw.Kind != StepKind.Play || !aw.CanPass) return false;
            Send(ClientAction.Pass(_mySeat, NextSeq()));
            return true;
        }

        /// <summary>
        /// 카드를 낸다. 내 차례면 합법수 목록으로 선검사하고, 차례밖이면 선검사 없이
        /// 보낸다 — 차례밖 Play 는 서버가 폭탄 예약으로 받아 합법일 때 적용한다.
        /// </summary>
        public bool SubmitPlay(IReadOnlyList<Card> cards, int? wish = null)
        {
            if (cards == null || cards.Count == 0) return false;

            var aw = Prompt.CurrentValue;
            bool myTurn = aw != null && aw.Kind == StepKind.Play;

            var combo = CombinationRecognizer.Recognize(ToArray(cards), RecognizeContext());
            if (myTurn)
            {
                if (aw.LegalMoves == null || !IsMoveLegal(combo, aw.LegalMoves)) return false;
            }
            else
            {
                if (combo.Type == CombinationType.Invalid || !combo.IsBomb) return false;
                _bombSent = true;
            }

            // Rank 는 의도 확인용이다(모호성 해소가 아니다 — 기획서 11.1).
            Send(ClientAction.Play(_mySeat, NextSeq(), new List<Card>(cards), combo.Rank, wish));
            return true;
        }

        /// <summary>용 양도 대상(상대 좌석).</summary>
        public bool SubmitDragonRecipient(int seat)
        {
            if (!Asking(StepKind.DragonGift)) return false;
            if (seat < 0 || seat > 3 || (seat % 2) == (_mySeat % 2)) return false;
            Send(ClientAction.GiveDragon(_mySeat, NextSeq(), seat));
            return true;
        }

        /// <summary>
        /// 작은 티츄를 선언한다. 서버는 이걸 질문으로 묻지 않고 예약으로 받으므로
        /// 프롬프트 없이 언제든 보낼 수 있다(늦으면 서버가 조용히 버린다 — M5 에서 통보).
        /// </summary>
        public bool DeclareSmallTichu()
        {
            if (_tichuSent) return false;
            _tichuSent = true;
            TichuAvailable.Value = false;
            Send(ClientAction.CallTichu(_mySeat, NextSeq()));
            return true;
        }

        /// <summary>
        /// 지금 깔린 트릭으로 조합 인식 문맥을 만든다. 서버의 합법수 생성이 쓰는
        /// TrickComparer.ContextFor 와 같은 규칙이라야 한다 — 어긋나면 봉황 단독의
        /// Rank 가 서버 것과 달라져 선검사와 서버 검증이 모두 내 수를 거부한다.
        /// 뷰도 선택 카드를 인식할 때 이걸 쓴다(규칙이 한 곳에만 있도록).
        /// </summary>
        public TrickContext RecognizeContext()
        {
            var trick = CurrentTrick.CurrentValue;
            if (trick == null || trick.Top == null) return TrickContext.Lead;
            bool single = trick.Top.Type == CombinationType.Single;
            return new TrickContext(false, single, single ? trick.Top.Rank : 0);
        }

        // ── 내부 ─────────────────────────────────────────────────────────────

        private bool Asking(StepKind kind)
        {
            var aw = Prompt.CurrentValue;
            return aw != null && aw.Kind == kind;
        }

        private long NextSeq() { return ++_actionSeq; }

        private void Send(ClientAction a) { _submitted.OnNext(a); }

        private static Card[] ToArray(IReadOnlyList<Card> cards)
        {
            var arr = new Card[cards.Count];
            for (int i = 0; i < cards.Count; i++) arr[i] = cards[i];
            return arr;
        }

        // 합법성: 카드 정확 일치 대신 (Type, Rank) 등가 — 같은 랭크 다른 무늬도 합법이다
        // (GenerateSingles 가 랭크당 1장만 내므로 정확 일치는 다른 무늬를 거부한다).
        private static bool IsMoveLegal(Combination move, IReadOnlyList<Combination> legalMoves)
        {
            for (int i = 0; i < legalMoves.Count; i++)
                if (legalMoves[i].Type == move.Type && legalMoves[i].Rank == move.Rank)
                    return true;
            return false;
        }

        /// <summary>교환 카드 3장이 손패 안에 있고 서로 다른지 확인한다.</summary>
        private static bool IsDistinctAndInHand(Card l, Card p, Card r, IReadOnlyList<Card> hand)
        {
            if (l.Equals(p) || l.Equals(r) || p.Equals(r)) return false;
            var remaining = new List<Card>(hand.Count);
            for (int i = 0; i < hand.Count; i++) remaining.Add(hand[i]);
            return remaining.Remove(l) && remaining.Remove(p) && remaining.Remove(r);
        }
    }
}
