using System.Collections.Generic;
using Tichu.Core.Cards;
using Tichu.Core.Combinations;
using Tichu.Core.Game;

namespace Tichu.GameFlow.Agents
{
    /// <summary>에이전트(인간/AI) 공통 계약. 드라이버는 이 인터페이스만 본다.</summary>
    public interface IAgent
    {
        /// <summary>큰 티츄 선언 여부. true=선언, false=패스.</summary>
        bool CallGrandTichu(in DecisionContext ctx);

        /// <summary>교환 카드 선택. 방향별 1장씩, 서로 달라야 한다.</summary>
        ExchangeChoice ChooseExchange(in DecisionContext ctx);

        /// <summary>작은 티츄 선언 여부(첫 패 내기 전). true=선언, false=패스.</summary>
        bool CallTichu(in DecisionContext ctx);

        /// <summary>자기 턴의 행동: 리드/팔로우/패스 + 마작 소원.</summary>
        TurnDecision DecideTurn(in DecisionContext ctx);

        /// <summary>오프턴 폭탄 인터럽트 여부. null=폭탄 거절.</summary>
        Combination? DecideBomb(in DecisionContext ctx);

        /// <summary>용으로 트릭을 먹었을 때 양도할 상대 좌석 번호.</summary>
        int ChooseDragonRecipient(in DecisionContext ctx);
    }

    /// <summary>에이전트에게 전달되는 읽기 전용 좌석 뷰.</summary>
    public readonly struct DecisionContext
    {
        public readonly GameState State;
        public readonly int Seat;

        public DecisionContext(GameState state, int seat)
        {
            State = state;
            Seat = seat;
        }

        /// <summary>이 좌석의 현재 손패.</summary>
        public IReadOnlyList<Card> MyHand => State.Seats[Seat].Hand;

        /// <summary>지금 낼 수 있는 모든 합법 조합.</summary>
        public IReadOnlyList<Combination> LegalMoves => LegalMoveGenerator.LegalMoves(State, Seat);

        /// <summary>현재 패스 가능 여부.</summary>
        public bool CanPass => LegalMoveGenerator.CanPass(State, Seat);

        /// <summary>왼쪽 좌석 번호.</summary>
        public int LeftSeat => (Seat + 1) % 4;

        /// <summary>파트너 좌석 번호.</summary>
        public int PartnerSeat => Seating.Partner(Seat);

        /// <summary>오른쪽 좌석 번호.</summary>
        public int RightSeat => (Seat + 3) % 4;
    }

    /// <summary>교환 단계에서 세 방향에 낼 카드.</summary>
    public readonly struct ExchangeChoice
    {
        public readonly Card ToLeft;
        public readonly Card ToPartner;
        public readonly Card ToRight;

        public ExchangeChoice(Card toLeft, Card toPartner, Card toRight)
        {
            ToLeft = toLeft;
            ToPartner = toPartner;
            ToRight = toRight;
        }
    }

    /// <summary>자기 턴 결정: 패스, 패 내기(소원 선택 포함).</summary>
    public readonly struct TurnDecision
    {
        public readonly bool IsPass;
        public readonly Combination? Move;
        public readonly int? Wish;

        private TurnDecision(bool isPass, Combination? move, int? wish)
        {
            IsPass = isPass;
            Move = move;
            Wish = wish;
        }

        /// <summary>패스 결정.</summary>
        public static TurnDecision Pass => new TurnDecision(true, null, null);

        /// <summary>패 내기 결정. wish는 마작 리드 시 선택한 소원 랭크(없으면 null).</summary>
        public static TurnDecision Play(Combination move, int? wish = null)
            => new TurnDecision(false, move, wish);
    }
}
