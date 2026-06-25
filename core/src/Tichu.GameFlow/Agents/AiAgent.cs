using System.Collections.Generic;
using Tichu.Core;
using Tichu.Core.Cards;
using Tichu.Core.Combinations;
using Tichu.Core.Game;

namespace Tichu.GameFlow.Agents
{
    /// <summary>
    /// Normal 난이도 휴리스틱 에이전트.
    /// 공개 정보 + ctx.LegalMoves/CanPass + CurrentTrick 만 읽는다. Clone 룩어헤드는 쓰지 않는다.
    /// 모든 결정은 합법이며, RNG 는 동점 처리에만 쓰여 결정성을 해치지 않는다.
    /// 정렬/선택의 단일 출처는 MoveOrder 이다.
    /// </summary>
    public sealed class AiAgent : IAgent
    {
        // ── 임계값(휴리스틱 게이트) ──────────────────────────────────────────────────
        private const int GrandThreshold = 10;   // 큰 티츄: 보수적(실패 −200).
        private const int FinishHandSize = 5;     // 손패 ≤ 이 값이면 끝내기 모드(강한 수로 리드).
        private const int RichTrickPoints = 15;   // 이 점수 이상이면 "점수 많은 트릭".
        private const int BombMinPoints = 15;     // 폭탄 인터럽트 최소 누적 점수.
        private const int PartnerLowTopScaled = 20; // 파트너 Top 랭크 ≤ 10(스케일 ×2) → "낮은 카드".
        private const int GoOutThreatCards = 2;   // 상대 손패 ≤ 이 값이면 아웃 임박(블로킹 위협).

        private Rng _rng;
        private readonly int _seat;

        public AiAgent(ulong roundSeed, int seat)
        {
            _rng = new Rng(roundSeed ^ 0xA1A1_0000_0000_0001UL ^ (ulong)seat);
            _seat = seat;
        }

        // ── 큰 티츄 ─────────────────────────────────────────────────────────────────

        /// <summary>8장 초기패 시점에 호출; HandPower 가 보수적 임계값 이상이면 큰 티츄 선언. RNG 미사용(순수 게이트).</summary>
        public bool CallGrandTichu(in DecisionContext ctx)
        {
            return HandPower(ctx.MyHand) >= GrandThreshold;
        }

        /// <summary>손패의 대략적인 강함 점수(장수 무관; 큰 티츄는 8장, 작은 티츄는 14장 시점에 호출).</summary>
        private static int HandPower(IReadOnlyList<Card> hand)
        {
            int score = 0;
            for (int i = 0; i < hand.Count; i++)
            {
                var c = hand[i];
                switch (c.Special)
                {
                    case SpecialKind.Dragon: score += 4; break;
                    case SpecialKind.Phoenix: score += 3; break;
                    case SpecialKind.Dog: score += 1; break;
                    case SpecialKind.None:
                        if (c.Rank == 14) score += 2;       // A
                        else if (c.Rank == 13) score += 1;  // K
                        break;
                }
            }
            return score;
        }

        // ── 교환 ───────────────────────────────────────────────────────────────────

        /// <summary>
        /// 가장 낮은 비특수 카드 3장을 교환한다. 특수카드(용/봉황/개/마작)는 절대 주지 않으며
        /// 가능하면 에이스도 피한다. 가장 낮은 두 장을 상대(Left/Right)에게, 중간 한 장을 파트너에게.
        /// </summary>
        public ExchangeChoice ChooseExchange(in DecisionContext ctx)
        {
            var hand = ctx.MyHand;

            // 비특수 후보를 (Rank, Suit) 오름차순으로 정렬한 인덱스 목록.
            var candidates = new List<Card>();
            for (int i = 0; i < hand.Count; i++)
                if (!hand[i].IsSpecial) candidates.Add(hand[i]);

            // 비특수가 3장 미만이면(이론상 거의 없음) 특수 포함 폴백.
            if (candidates.Count < 3)
            {
                candidates.Clear();
                for (int i = 0; i < hand.Count; i++) candidates.Add(hand[i]);
            }

            candidates.Sort(CompareLow);

            // 가능하면 에이스를 뒤로 미뤄 보존: 비-에이스가 충분하면 에이스 제외.
            int nonAce = 0;
            for (int i = 0; i < candidates.Count; i++)
                if (candidates[i].Rank != 14 || candidates[i].IsSpecial) nonAce++;
            if (nonAce >= 3)
                candidates.RemoveAll(c => !c.IsSpecial && c.Rank == 14);

            // 가장 낮은 3장: [0],[1] → 상대(Left/Right), [2] → 파트너(약간 더 높은 카드 보냄).
            var low0 = candidates[0];
            var low1 = candidates[1];
            var mid = candidates[2];
            return new ExchangeChoice(low0, mid, low1); // ToLeft, ToPartner, ToRight
        }

        // (Rank, Suit, Special) 오름차순 — 두 카드가 Rank·Suit·Special 모두 같은 경우는 없으므로 항상 결정적.
        private static int CompareLow(Card a, Card b)
        {
            int ra = SortRank(a), rb = SortRank(b);
            if (ra != rb) return ra - rb;
            int sa = (int)a.Suit, sb = (int)b.Suit;
            if (sa != sb) return sa - sb;
            int ka = (int)a.Special, kb = (int)b.Special;
            if (ka != kb) return ka - kb;
            return 0;
        }

        // 정렬용 랭크: 개=0, 마작=1, 봉황=2, 일반=Rank, 용=15(가장 높음).
        private static int SortRank(Card c)
        {
            switch (c.Special)
            {
                case SpecialKind.Dog: return 0;
                case SpecialKind.Mahjong: return 1;
                case SpecialKind.Phoenix: return 2;
                case SpecialKind.Dragon: return 15;
                default: return c.Rank;
            }
        }

        // ── 작은 티츄 ────────────────────────────────────────────────────────────────

        /// <summary>
        /// 손이 강하고(용/봉황 또는 폭탄 보유) 아무 상대도 아직 아웃되지 않았고
        /// 파트너가 콜하지 않았을 때만 선언. 보수적.
        /// </summary>
        public bool CallTichu(in DecisionContext ctx)
        {
            // 리드 시점(트릭 없음)에만 선언 — 팔로우 중 외치고 곧장 패스하는 어색함 방지.
            if (ctx.State.CurrentTrick != null) return false;
            var seats = ctx.State.Seats;
            // 상대가 이미 아웃이면 위험 → 콜 안 함.
            if (seats[ctx.LeftSeat].IsOut || seats[ctx.RightSeat].IsOut) return false;
            // 파트너가 이미 콜했으면 중복 회피.
            if (seats[ctx.PartnerSeat].Call != TichuCall.None) return false;

            // 강한 손: 용/봉황 보유 또는 폭탄 보유.
            bool hasHighSpecial = false;
            var hand = ctx.MyHand;
            for (int i = 0; i < hand.Count; i++)
            {
                var sp = hand[i].Special;
                if (sp == SpecialKind.Dragon || sp == SpecialKind.Phoenix) { hasHighSpecial = true; break; }
            }
            if (hasHighSpecial) return true;

            // 폭탄 보유 여부는 리드(=CurrentTrick null) 시점의 LegalMoves 로 판단.
            var moves = ctx.LegalMoves;
            for (int i = 0; i < moves.Count; i++)
                if (moves[i].IsBomb) return true;

            return false;
        }

        // ── 인-턴 결정 ───────────────────────────────────────────────────────────────

        public TurnDecision DecideTurn(in DecisionContext ctx)
        {
            var trick = ctx.State.CurrentTrick;
            return trick == null ? DecideLead(ctx) : DecideFollow(ctx, trick);
        }

        // 리드: 폭탄은 아낀다(절대 리드로 내지 않음). 손패가 적으면 강한 수로 끝내기,
        // 아니면 가장 낮은 비점수 수를 낸다. 마작 소원은 그럴듯하게 강제될 때만.
        private TurnDecision DecideLead(in DecisionContext ctx)
        {
            var moves = ctx.LegalMoves;
            // 폭탄 제외 후보.
            var nonBomb = new List<Combination>(moves.Count);
            for (int i = 0; i < moves.Count; i++)
                if (!moves[i].IsBomb) nonBomb.Add(moves[i]);

            // 리드는 반드시 카드를 내야 한다. 비폭탄이 없으면(드묾) 폭탄이라도 가장 작은 걸로.
            var pool = nonBomb.Count > 0 ? nonBomb : new List<Combination>(moves);

            Combination chosen;
            if (ctx.MyHand.Count <= FinishHandSize)
            {
                // 끝내기 모드: 한 방에 많이 털 수 있는 강한 수(가장 높은 Strength).
                chosen = Strongest(pool)!;
            }
            else
            {
                // 평소: 점수 없는 가장 낮은 수를 선호. 없으면 그냥 가장 낮은 수.
                var noPoint = new List<Combination>();
                for (int i = 0; i < pool.Count; i++)
                    if (pool[i].PointsInPlay == 0) noPoint.Add(pool[i]);
                chosen = MoveOrder.Lowest(noPoint.Count > 0 ? noPoint : pool)!;
            }

            int? wish = MaybeWish(ctx, chosen);
            return TurnDecision.Play(chosen, wish);
        }

        // 마작을 포함해 리드하고, 내 손에 없는 랭크를 소원으로 걸어 상대를 압박한다.
        private int? MaybeWish(in DecisionContext ctx, Combination chosen)
        {
            bool hasMahjong = false;
            for (int i = 0; i < chosen.Cards.Count; i++)
                if (chosen.Cards[i].Special == SpecialKind.Mahjong) { hasMahjong = true; break; }
            if (!hasMahjong) return null;

            // 내 손에 없는 랭크 중 하나를 소원으로(상대가 강제될 가능성이 높음).
            // 높은 랭크(A→2 순)부터 찾아 강한 카드를 강제로 끌어내려 시도.
            var present = new bool[15];
            var hand = ctx.MyHand;
            for (int i = 0; i < hand.Count; i++)
            {
                var c = hand[i];
                if (!c.IsSpecial && c.Rank >= 2 && c.Rank <= 14) present[c.Rank] = true;
            }
            for (int r = 14; r >= 2; r--)
                if (!present[r]) return r;
            return null;
        }

        // 팔로우: 파트너가 Top 이면 기본 패스(나가기/티츄/낮은카드 싼-밟기 이득이면 밟기).
        // 점수 많은 상대 Top 이면 최소 오버킬 비폭탄으로 회수. 가치 없는 트릭을 비싼 수로만
        // 이길 수 있으면 패스. 그 외엔 가장 낮은 이기는 수.
        private TurnDecision DecideFollow(in DecisionContext ctx, Trick trick)
        {
            int topOwner = trick.TopOwnerSeat;
            bool partnerOwns = Seating.Partner(_seat) == topOwner;
            // 팔로우 상황에서는 자기 자신이 Top 일 수 없으므로 opponentOwns == !partnerOwns.
            bool opponentOwns = Seating.TeamOf(topOwner) != Seating.TeamOf(_seat);

            // 비폭탄 합법수(팔로우에서는 모두 Top 을 이기는 수). 폭탄은 DecideBomb 담당.
            var moves = ctx.LegalMoves;
            var nonBomb = new List<Combination>(moves.Count);
            for (int i = 0; i < moves.Count; i++)
                if (!moves[i].IsBomb) nonBomb.Add(moves[i]);

            // 파트너가 Top → 기본은 패스(팀에 점수·주도권 유지). 단 나가기/티츄/낮은카드 싼-밟기가
            // 이득이면 점수 없는 최소 오버킬로 밟는다(A·용 같은 비싼 카드 낭비 금지).
            if (partnerOwns)
            {
                var over = PartnerOvertakeMove(ctx, _seat, trick, nonBomb);
                if (over != null) return TurnDecision.Play(over);
                if (ctx.CanPass) return TurnDecision.Pass;
                // 패스 불가(소원 강제 등): 가능한 가장 낮은 수로.
                if (nonBomb.Count > 0) return TurnDecision.Play(MoveOrder.Lowest(nonBomb)!);
                return TurnDecision.Play(MoveOrder.Smallest(moves)!);
            }

            // 이길 수 있는 비폭탄이 없으면 패스(가능하면), 아니면(소원 강제 등) 가능한 수.
            if (nonBomb.Count == 0)
            {
                if (ctx.CanPass) return TurnDecision.Pass;
                // 패스 불가 + 비폭탄 없음 → 폭탄이라도 내야 한다(소원 강제 + 폭탄만 이김).
                var anyBomb = MoveOrder.Smallest(moves);
                return TurnDecision.Play(anyBomb!);
            }

            bool richOpponentTop = opponentOwns && trick.AccumulatedPoints >= RichTrickPoints;

            if (richOpponentTop)
            {
                // 점수 많은 상대 Top → 최소 오버킬로 반드시 회수.
                var cheap = MoveOrder.CheapestThatBeats(nonBomb, trick.Top!) ?? MoveOrder.Lowest(nonBomb);
                return TurnDecision.Play(cheap!);
            }

            // 상대 위협(티츄 콜/아웃 임박) → 원투를 저지하기 위해 막는다. 단 막을 수 있는 수가
            // 스트레이트를 깨는 것뿐이면(예: 6-7-8-9-10서 싱글 10) 비용 극심 → 보내준다(패스).
            if (opponentOwns && OpponentThreat(ctx))
            {
                var block = CheapestNonStructural(ctx.MyHand, nonBomb);
                if (block != null) return TurnDecision.Play(block);
                if (ctx.CanPass) return TurnDecision.Pass;
                // 패스 불가(소원 강제 등) → 아래 폴백.
            }

            // 가치 없는(점수 적은) 트릭: 비싼(점수 카드/높은 카드) 수밖에 없으면 패스가 낫다.
            // 가장 낮은 이기는 수가 점수 카드를 포함하면 굳이 안 이기고 패스.
            var lowestWin = MoveOrder.Lowest(nonBomb)!;
            if (ctx.CanPass && lowestWin.PointsInPlay > 0 && trick.AccumulatedPoints < RichTrickPoints)
                return TurnDecision.Pass;

            return TurnDecision.Play(lowestWin);
        }

        // ── 폭탄 인터럽트 ────────────────────────────────────────────────────────────

        /// <summary>
        /// 누적 점수 ≥ 임계값이고 Top 을 상대가 소유할 때만, Top 을 이기는 가장 작은 폭탄을 낸다.
        /// 파트너가 Top 이면 절대 폭탄 쓰지 않는다.
        /// </summary>
        public Combination? DecideBomb(in DecisionContext ctx)
        {
            var trick = ctx.State.CurrentTrick;
            if (trick == null) return null;

            int topOwner = trick.TopOwnerSeat;
            bool opponentOwns = Seating.TeamOf(topOwner) != Seating.TeamOf(_seat);
            if (!opponentOwns) return null;                    // 파트너/자기 Top → 폭탄 안 함.
            if (trick.AccumulatedPoints < BombMinPoints) return null;

            // LegalMoves 중 Top 을 이기는 폭탄들(폴로우 시 폭탄은 턴 무관으로 포함됨).
            var moves = ctx.LegalMoves;
            var bombs = new List<Combination>();
            for (int i = 0; i < moves.Count; i++)
                if (moves[i].IsBomb) bombs.Add(moves[i]);
            if (bombs.Count == 0) return null;

            return MoveOrder.Smallest(bombs);
        }

        // ── 용 양도 ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// 카드가 가장 많이 남은 상대에게 양도(가장 손해). 동점이면 아웃에 덜 가까운 쪽,
        /// 최종 동점이면 RNG.
        /// </summary>
        public int ChooseDragonRecipient(in DecisionContext ctx)
        {
            int left = ctx.LeftSeat, right = ctx.RightSeat;
            var seats = ctx.State.Seats;
            int lc = seats[left].Hand.Count, rc = seats[right].Hand.Count;

            if (lc != rc) return lc > rc ? left : right;
            // 동점: 아웃에 덜 가까운(=FinishOrder 0, 즉 아직 안 나간) 쪽 선호. 둘 다 같으면 RNG.
            bool leftActive = seats[left].FinishOrder == 0;
            bool rightActive = seats[right].FinishOrder == 0;
            if (leftActive != rightActive) return leftActive ? left : right;
            return _rng.NextInt(2) == 0 ? left : right;
        }

        // ── 보조 ───────────────────────────────────────────────────────────────────

        private static Combination? Strongest(IReadOnlyList<Combination> moves)
        {
            Combination? best = null;
            int bestKey = int.MinValue;
            for (int i = 0; i < moves.Count; i++)
            {
                int key = MoveOrder.Strength(moves[i]);
                if (key > bestKey) { bestKey = key; best = moves[i]; }
            }
            return best;
        }

        /// <summary>
        /// 파트너가 Top 을 소유한 팔로우 상황에서 "밟을 수"를 돌려준다(밟지 말아야 하면 null).
        /// 기본은 패스(null). 다음 중 하나면 최소 오버킬(beat, 점수카드 허용)로 밟는다:
        /// ①(작은/큰) 티츄 선언 → 나가기 추진, ②밟으면 손패가 비어 아웃(예: K 페어가 마지막),
        /// ③파트너가 낮은 카드(랭크 ≤ 10)를 냈고 콤보(≥2장)로 패를 줄이는 경우.
        /// "이유 없이 비싼 카드로 파트너를 밟는" 낭비는 ①~③ 조건이 막는다(이유 없으면 패스).
        /// 카드 선택은 점수 무관 최소 오버킬이라 더 싼 수가 있으면 그쪽을 쓴다.
        /// PimcAgent 도 파트너-Top 가드로 이 규칙을 공유한다.
        /// </summary>
        internal static Combination? PartnerOvertakeMove(
            in DecisionContext ctx, int seat, Trick trick, IReadOnlyList<Combination> nonBombWins)
        {
            var cheap = MoveOrder.Lowest(nonBombWins);   // 최소 오버킬(점수카드 허용)
            if (cheap == null) return null;

            bool calledTichu = ctx.State.Seats[seat].Call != TichuCall.None;
            bool goesOut = ctx.MyHand.Count == cheap.Cards.Count;     // 밟으면 손패 소진
            bool partnerLow = trick.Top!.Rank <= PartnerLowTopScaled;
            bool reducesHand = cheap.Cards.Count >= 2;                // 콤보 = 패 ≥2장 감소

            return (calledTichu || goesOut || (partnerLow && reducesHand)) ? cheap : null;
        }

        // ── 블로킹(#3) ─────────────────────────────────────────────────────────────

        /// <summary>상대팀이 티츄/큰티츄를 선언했거나 상대가 아웃 임박이면 위협(원투 저지 동기).</summary>
        private static bool OpponentThreat(in DecisionContext ctx)
        {
            var seats = ctx.State.Seats;
            var l = seats[ctx.LeftSeat];
            var r = seats[ctx.RightSeat];
            if (l.Call != TichuCall.None || r.Call != TichuCall.None) return true;
            return l.Hand.Count <= GoOutThreatCards || r.Hand.Count <= GoOutThreatCards;
        }

        /// <summary>
        /// 위협을 막을 수 있는 가장 싼 수(점수 무관). 단 "스트레이트를 깨는 싱글"은 제외(비용 극심).
        /// 깰 수밖에 없으면 null → 호출부가 보내준다(패스).
        /// </summary>
        private static Combination? CheapestNonStructural(IReadOnlyList<Card> hand, IReadOnlyList<Combination> wins)
        {
            var inRun = StraightRanks(hand);
            Combination? best = null;
            int bestK = int.MaxValue;
            for (int i = 0; i < wins.Count; i++)
            {
                var m = wins[i];
                if (BreaksStraight(m, inRun)) continue;
                int k = MoveOrder.Strength(m);
                if (k < bestK) { bestK = k; best = m; }
            }
            return best;
        }

        /// <summary>손패에서 길이 ≥5 연속(스트레이트)에 속하는 랭크 표시(마작=1 포함).</summary>
        private static bool[] StraightRanks(IReadOnlyList<Card> hand)
        {
            var present = new bool[15]; // 1..14
            for (int i = 0; i < hand.Count; i++)
            {
                var c = hand[i];
                if (c.Special == SpecialKind.Mahjong) present[1] = true;
                else if (!c.IsSpecial && c.Rank >= 1 && c.Rank <= 14) present[c.Rank] = true;
            }
            var inRun = new bool[15];
            int rr = 1;
            while (rr <= 14)
            {
                if (!present[rr]) { rr++; continue; }
                int lo = rr;
                while (rr <= 14 && present[rr]) rr++;
                int hi = rr - 1;
                if (hi - lo + 1 >= 5)
                    for (int k = lo; k <= hi; k++) inRun[k] = true;
            }
            return inRun;
        }

        // 싱글이고 그 랭크가 ≥5 스트레이트의 일부면 그 싱글을 내면 스트레이트가 깨진다(v1: 싱글만 검사).
        private static bool BreaksStraight(Combination m, bool[] inRun)
        {
            if (m.Type != CombinationType.Single || m.Cards.Count != 1) return false;
            var c = m.Cards[0];
            if (c.IsSpecial) return false;
            return c.Rank >= 1 && c.Rank <= 14 && inRun[c.Rank];
        }
    }
}
