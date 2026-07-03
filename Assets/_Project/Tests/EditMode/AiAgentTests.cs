using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Tichu.Core.Cards;
using Tichu.Core.Combinations;
using Tichu.Core.Game;
using Tichu.GameFlow;
using Tichu.GameFlow.Agents;

namespace Tichu.Core.Tests
{
    /// <summary>
    /// AiAgent(Normal 휴리스틱) + RandomAgent 검증.
    /// 통제된 DecisionContext 를 GameFlowHelpers 로 구성해 각 휴리스틱을 단위 테스트한다.
    /// </summary>
    public class AiAgentTests
    {
        private static List<Card> Hand(params Card[] cards) => new List<Card>(cards);

        private static Card N(int rank, Suit suit) => Card.Normal(rank, suit);

        // 트릭(팔로우 상황)을 통제 구성: Top 조합 + 소유 좌석 + 누적 점수.
        private static GameState FollowState(
            int turn, Combination top, int topOwner, int accumulatedPoints,
            params IReadOnlyList<Card>[] hands)
        {
            var s = GameFlowHelpers.PlayState(turn, hands);
            s.CurrentTrick = new Trick
            {
                LeadType = top.Type,
                LeadLength = top.Length,
                Top = top,
                TopOwnerSeat = topOwner,
                AccumulatedPoints = accumulatedPoints
            };
            return s;
        }

        private static Combination Pair(int rank) =>
            CombinationRecognizer.Recognize(new[] { N(rank, Suit.Jade), N(rank, Suit.Sword) }, TrickContext.Lead);

        private static Combination Single(int rank) =>
            CombinationRecognizer.Recognize(new[] { N(rank, Suit.Jade) }, TrickContext.Lead);

        private static Combination Triple(int rank) =>
            CombinationRecognizer.Recognize(
                new[] { N(rank, Suit.Jade), N(rank, Suit.Sword), N(rank, Suit.Pagoda) }, TrickContext.Lead);

        private static Combination FullHouse(int tripleRank, int pairRank) =>
            CombinationRecognizer.Recognize(new[]
            {
                N(tripleRank, Suit.Jade), N(tripleRank, Suit.Sword), N(tripleRank, Suit.Pagoda),
                N(pairRank, Suit.Jade), N(pairRank, Suit.Sword)
            }, TrickContext.Lead);

        private static List<Card> PairCards(int rank) =>
            new List<Card> { N(rank, Suit.Jade), N(rank, Suit.Sword) };

        private static List<Card> BombCards(int rank) =>
            new List<Card> { N(rank, Suit.Jade), N(rank, Suit.Sword), N(rank, Suit.Pagoda), N(rank, Suit.Star) };

        // ── 결정성 ──────────────────────────────────────────────────────────────────

        [Test]
        public void AiAgent_is_deterministic_across_all_methods()
        {
            var hands = new IReadOnlyList<Card>[]
            {
                Hand(N(3, Suit.Jade), N(5, Suit.Sword), N(9, Suit.Pagoda), N(11, Suit.Star)),
                Hand(N(4, Suit.Jade), N(6, Suit.Sword)),
                Hand(N(7, Suit.Jade), N(8, Suit.Sword)),
                Hand(N(2, Suit.Jade), N(10, Suit.Sword))
            };
            var s1 = GameFlowHelpers.PlayState(0, hands);
            var s2 = GameFlowHelpers.PlayState(0, hands);

            var a1 = new AiAgent(12345UL, 0);
            var a2 = new AiAgent(12345UL, 0);

            var d1 = a1.DecideTurn(GameFlowHelpers.Context(s1, 0));
            var d2 = a2.DecideTurn(GameFlowHelpers.Context(s2, 0));

            Assert.That(d1.IsPass, Is.EqualTo(d2.IsPass));
            if (!d1.IsPass)
                Assert.That(d1.Move!.Rank, Is.EqualTo(d2.Move!.Rank));

            // 교환도 결정적이어야 한다 (8장 손패에서).
            var gh = new IReadOnlyList<Card>[]
            {
                Hand(N(3, Suit.Jade), N(5, Suit.Sword), N(9, Suit.Pagoda), N(11, Suit.Star),
                     N(13, Suit.Jade), N(6, Suit.Pagoda), N(8, Suit.Star), N(2, Suit.Sword)),
                Hand(N(4, Suit.Jade)), Hand(N(7, Suit.Jade)), Hand(N(2, Suit.Pagoda))
            };
            var e1 = new AiAgent(99UL, 0).ChooseExchange(GameFlowHelpers.Context(GameFlowHelpers.PlayState(0, gh), 0));
            var e2 = new AiAgent(99UL, 0).ChooseExchange(GameFlowHelpers.Context(GameFlowHelpers.PlayState(0, gh), 0));
            Assert.That(e1.ToLeft, Is.EqualTo(e2.ToLeft));
            Assert.That(e1.ToPartner, Is.EqualTo(e2.ToPartner));
            Assert.That(e1.ToRight, Is.EqualTo(e2.ToRight));
        }

        // ── CallGrandTichu ───────────────────────────────────────────────────────────

        [Test]
        public void CallGrandTichu_true_for_strong_hand()
        {
            // 용 + 봉황 + 에이스 2장 + 잡카드 4장 = 강한 8장.
            var hand = Hand(Card.Dragon, Card.Phoenix, N(14, Suit.Jade), N(14, Suit.Sword),
                            N(13, Suit.Pagoda), N(13, Suit.Star), N(3, Suit.Jade), N(4, Suit.Sword));
            var s = GameFlowHelpers.PlayState(0, hand, Hand(N(2, Suit.Jade)), Hand(N(2, Suit.Sword)), Hand(N(2, Suit.Pagoda)));
            var agent = new AiAgent(1UL, 0);
            Assert.That(agent.CallGrandTichu(GameFlowHelpers.Context(s, 0)), Is.True);
        }

        [Test]
        public void CallGrandTichu_false_for_weak_hand()
        {
            var hand = Hand(N(2, Suit.Jade), N(3, Suit.Sword), N(4, Suit.Pagoda), N(5, Suit.Star),
                            N(6, Suit.Jade), N(7, Suit.Sword), N(8, Suit.Pagoda), N(9, Suit.Star));
            var s = GameFlowHelpers.PlayState(0, hand, Hand(N(2, Suit.Sword)), Hand(N(2, Suit.Pagoda)), Hand(N(2, Suit.Star)));
            var agent = new AiAgent(1UL, 0);
            Assert.That(agent.CallGrandTichu(GameFlowHelpers.Context(s, 0)), Is.False);
        }

        // ── ChooseExchange ───────────────────────────────────────────────────────────

        [Test]
        public void ChooseExchange_never_gives_special_when_non_specials_exist()
        {
            var hand = Hand(Card.Dragon, Card.Phoenix, Card.Dog, Card.Mahjong,
                            N(3, Suit.Jade), N(5, Suit.Sword), N(9, Suit.Pagoda), N(11, Suit.Star));
            var s = GameFlowHelpers.PlayState(0, hand, Hand(N(2, Suit.Jade)), Hand(N(2, Suit.Sword)), Hand(N(2, Suit.Pagoda)));
            var ex = new AiAgent(7UL, 0).ChooseExchange(GameFlowHelpers.Context(s, 0));

            var given = new[] { ex.ToLeft, ex.ToPartner, ex.ToRight };
            foreach (var c in given)
                Assert.That(c.IsSpecial, Is.False, $"특수카드 {c} 를 교환에 내면 안 된다");
        }

        [Test]
        public void ChooseExchange_returns_three_distinct_in_hand_cards()
        {
            var hand = Hand(N(3, Suit.Jade), N(5, Suit.Sword), N(9, Suit.Pagoda), N(11, Suit.Star),
                            N(13, Suit.Jade), N(6, Suit.Pagoda), N(8, Suit.Star), N(2, Suit.Sword));
            var s = GameFlowHelpers.PlayState(0, hand, Hand(N(4, Suit.Jade)), Hand(N(7, Suit.Jade)), Hand(N(2, Suit.Pagoda)));
            var ex = new AiAgent(7UL, 0).ChooseExchange(GameFlowHelpers.Context(s, 0));

            var given = new List<Card> { ex.ToLeft, ex.ToPartner, ex.ToRight };
            Assert.That(given.Distinct().Count(), Is.EqualTo(3), "세 장 모두 달라야 한다");
            foreach (var c in given)
                Assert.That(hand.Contains(c), Is.True, $"{c} 는 손패에 있어야 한다");
        }

        [Test]
        public void ChooseExchange_weak_hand_gives_highest_to_partner()
        {
            // 약한 패(고카드 없음, 티츄 힘듦) → 파트너에게 가장 높은 카드(9), 상대에게 가장 낮은 둘.
            var hand = Hand(N(2, Suit.Jade), N(3, Suit.Sword), N(4, Suit.Pagoda), N(5, Suit.Star),
                            N(6, Suit.Jade), N(7, Suit.Sword), N(8, Suit.Pagoda), N(9, Suit.Star));
            var s = GameFlowHelpers.PlayState(0, hand,
                Hand(N(2, Suit.Sword)), Hand(N(2, Suit.Pagoda)), Hand(N(2, Suit.Star)));
            var ex = new AiAgent(1UL, 0).ChooseExchange(GameFlowHelpers.Context(s, 0));
            Assert.That(ex.ToPartner.Rank, Is.EqualTo(9), "약한 패면 파트너에게 최고 카드(팀 강화)");
        }

        [Test]
        public void ChooseExchange_tichu_intent_hand_keeps_top_gives_low_to_partner()
        {
            // 티츄 의향(용 보유) → 고카드 보존, 파트너에게 낮은(중간) 카드.
            var hand = Hand(Card.Dragon, N(14, Suit.Jade), N(3, Suit.Pagoda), N(4, Suit.Star),
                            N(5, Suit.Jade), N(6, Suit.Sword), N(7, Suit.Pagoda), N(8, Suit.Star));
            var s = GameFlowHelpers.PlayState(0, hand,
                Hand(N(2, Suit.Sword)), Hand(N(2, Suit.Pagoda)), Hand(N(2, Suit.Star)));
            var ex = new AiAgent(1UL, 0).ChooseExchange(GameFlowHelpers.Context(s, 0));
            Assert.That(ex.ToPartner.Rank, Is.LessThan(13), "티츄 의향이면 고카드 보존(파트너에 낮은 카드)");
        }

        [Test]
        public void ChooseExchange_moderate_hand_not_tichu_gives_top_to_partner()
        {
            // A·K 보유하나 티츄 의향 아님(HandPower<10, 용/봉황 없음) → 파트너에게 최고 카드(A) 줘 팀 강화.
            var hand = Hand(N(14, Suit.Jade), N(13, Suit.Sword), N(3, Suit.Pagoda), N(4, Suit.Star),
                            N(5, Suit.Jade), N(6, Suit.Sword), N(7, Suit.Pagoda), N(8, Suit.Star));
            var s = GameFlowHelpers.PlayState(0, hand,
                Hand(N(2, Suit.Sword)), Hand(N(2, Suit.Pagoda)), Hand(N(2, Suit.Star)));
            var ex = new AiAgent(1UL, 0).ChooseExchange(GameFlowHelpers.Context(s, 0));
            Assert.That(ex.ToPartner.Rank, Is.EqualTo(14), "티츄 의향 아니면 파트너에게 최고 카드(A)");
        }

        // ── DecideTurn (follow) ──────────────────────────────────────────────────────

        [Test]
        public void DecideTurn_passes_when_partner_owns_top_and_safe()
        {
            // seat0 팔로우, Top 소유 파트너(seat2)=Pair(J). seat0 는 Pair(Q)로 밟을 수 있으나
            // 티츄 미선언·나가기 아님·파트너가 낮지 않음 → 여유로우니 패스(카드 보존, 팀 주도권 유지).
            var s = FollowState(0, Pair(11), topOwner: 2, accumulatedPoints: 0,
                Hand(N(12, Suit.Jade), N(12, Suit.Sword), N(2, Suit.Pagoda), N(3, Suit.Star)),
                Hand(N(2, Suit.Jade)),
                Hand(N(2, Suit.Sword)),
                Hand(N(2, Suit.Star)));
            var d = new AiAgent(1UL, 0).DecideTurn(GameFlowHelpers.Context(s, 0));
            Assert.That(d.IsPass, Is.True);
        }

        [Test]
        public void DecideTurn_overtakes_partner_when_tichu_called()
        {
            // seat0 가 작은 티츄 선언 → 나가야 하니 파트너 Top(Pair5) 위로 싸게(8 페어) 밟는다.
            var s = FollowState(0, Pair(5), topOwner: 2, accumulatedPoints: 0,
                Hand(N(8, Suit.Jade), N(8, Suit.Sword), N(2, Suit.Pagoda), N(3, Suit.Star)),
                Hand(N(2, Suit.Jade)),
                Hand(N(2, Suit.Sword)),
                Hand(N(2, Suit.Star)));
            s.Seats[0].Call = TichuCall.Tichu;
            var d = new AiAgent(1UL, 0).DecideTurn(GameFlowHelpers.Context(s, 0));
            Assert.That(d.IsPass, Is.False, "티츄 선언 시 나가기 위해 파트너 위로라도 밟는다");
            Assert.That(d.Move!.Type, Is.EqualTo(CombinationType.Pair));
            Assert.That(d.Move!.Rank, Is.EqualTo(Pair(8).Rank), "싸게(8 페어)로 밟아야 한다");
        }

        [Test]
        public void DecideTurn_overtakes_partner_low_single_when_partner_is_out()
        {
            // 파트너(seat2)가 마지막 카드(낮은 싱글 2)로 아웃. 패스하면 리드가 상대(NextActive)로 넘어가므로,
            // 싱글로라도 밟아 리드를 우리 팀에 유지한다(엔드게임 템포).
            var s = FollowState(0, Single(2), topOwner: 2, accumulatedPoints: 0,
                Hand(N(5, Suit.Jade), N(3, Suit.Pagoda), N(4, Suit.Star)),  // seat0: 이기는 싱글 보유, 아웃 아님
                Hand(N(7, Suit.Jade), N(8, Suit.Sword)),                     // seat1(상대): 2장
                Hand(),                                                       // seat2(파트너): 아웃
                Hand(N(9, Suit.Jade), N(10, Suit.Sword)));                   // seat3(상대): 2장
            s.Seats[2].IsOut = true;
            var d = new AiAgent(1UL, 0).DecideTurn(GameFlowHelpers.Context(s, 0));
            Assert.That(d.IsPass, Is.False, "파트너 아웃 시 패스하면 리드를 상대에 헌납 → 밟아 유지");
            Assert.That(d.Move!.Type, Is.EqualTo(CombinationType.Single), "싼 싱글로 최소 오버킬");
        }

        [Test]
        public void DecideTurn_overtakes_partner_low_with_cheap_combo_to_reduce_hand()
        {
            // 파트너가 낮은 조합(Pair5)을 냄. seat0 는 점수 없는 콤보(Pair8)로 싸게 밟아 패를 줄인다.
            var s = FollowState(0, Pair(5), topOwner: 2, accumulatedPoints: 0,
                Hand(N(8, Suit.Jade), N(8, Suit.Sword), N(2, Suit.Pagoda), N(3, Suit.Star)),
                Hand(N(2, Suit.Jade)),
                Hand(N(2, Suit.Sword)),
                Hand(N(2, Suit.Star)));
            var d = new AiAgent(1UL, 0).DecideTurn(GameFlowHelpers.Context(s, 0));
            Assert.That(d.IsPass, Is.False, "파트너가 낮은 카드 → 싼 콤보로 밟아 패 줄이기");
            Assert.That(d.Move!.Type, Is.EqualTo(CombinationType.Pair));
            Assert.That(d.Move!.Rank, Is.EqualTo(Pair(8).Rank));
        }

        [Test]
        public void DecideTurn_overtakes_partner_with_point_card_to_go_out()
        {
            // 점수 카드(K 페어)뿐이라도 밟으면 손패가 비어 아웃 → 밟는다(점수카드 밟기 허용).
            var s = FollowState(0, Pair(12), topOwner: 2, accumulatedPoints: 0,
                Hand(N(13, Suit.Jade), N(13, Suit.Sword)),   // K 페어(점수카드) = 남은 전부
                Hand(N(2, Suit.Jade)),
                Hand(N(2, Suit.Sword)),
                Hand(N(2, Suit.Star)));
            var d = new AiAgent(1UL, 0).DecideTurn(GameFlowHelpers.Context(s, 0));
            Assert.That(d.IsPass, Is.False, "점수카드라도 밟으면 아웃이면 밟는다");
            Assert.That(d.Move!.Rank, Is.EqualTo(Pair(13).Rank));
        }

        [Test]
        public void DecideTurn_overtakes_partner_to_go_out()
        {
            // seat0 의 남은 패가 정확히 Pair8(2장) → 밟으면 아웃. 파트너 Top 위라도 나가는 게 이득.
            var s = FollowState(0, Pair(5), topOwner: 2, accumulatedPoints: 0,
                Hand(N(8, Suit.Jade), N(8, Suit.Sword)),
                Hand(N(2, Suit.Jade)),
                Hand(N(2, Suit.Sword)),
                Hand(N(2, Suit.Star)));
            var d = new AiAgent(1UL, 0).DecideTurn(GameFlowHelpers.Context(s, 0));
            Assert.That(d.IsPass, Is.False, "밟으면 손패가 비어 아웃 → 밟는다");
            Assert.That(d.Move!.Rank, Is.EqualTo(Pair(8).Rank));
        }

        [Test]
        public void DecideTurn_plays_cheapest_winning_when_opponent_owns_points_rich_top()
        {
            // seat0 팔로우. Top 소유는 상대(seat1), 누적 점수 높음. 이길 수 있는 페어 8/10/13 중 8을 내야 한다.
            var s = FollowState(0, Pair(7), topOwner: 1, accumulatedPoints: 30,
                Hand(N(8, Suit.Jade), N(8, Suit.Sword), N(10, Suit.Jade), N(10, Suit.Sword),
                     N(13, Suit.Jade), N(13, Suit.Sword)),
                Hand(N(2, Suit.Jade)),
                Hand(N(2, Suit.Sword)),
                Hand(N(2, Suit.Pagoda)));
            var d = new AiAgent(1UL, 0).DecideTurn(GameFlowHelpers.Context(s, 0));

            Assert.That(d.IsPass, Is.False, "점수 많은 상대 Top 은 이겨야 한다");
            Assert.That(d.Move!.Type, Is.EqualTo(CombinationType.Pair));
            Assert.That(d.Move!.Rank, Is.EqualTo(Pair(8).Rank), "최소 오버킬 페어(8)를 내야 한다");
            // 합법수에 속해야 한다.
            Assert.That(GameFlowHelpers.Context(s, 0).LegalMoves.Any(m => m.Rank == d.Move!.Rank && m.Type == d.Move!.Type), Is.True);
        }

        [Test]
        public void DecideTurn_lead_is_legal_and_not_a_bomb()
        {
            // seat0 리드. 폭탄(포카드 9)을 들고 있어도 리드로는 내지 않는다.
            var s = GameFlowHelpers.PlayState(0,
                Hand(N(9, Suit.Jade), N(9, Suit.Sword), N(9, Suit.Pagoda), N(9, Suit.Star),
                     N(3, Suit.Jade), N(4, Suit.Sword)),
                Hand(N(2, Suit.Jade)), Hand(N(2, Suit.Sword)), Hand(N(2, Suit.Pagoda)));
            var ctx = GameFlowHelpers.Context(s, 0);
            var d = new AiAgent(1UL, 0).DecideTurn(ctx);

            Assert.That(d.IsPass, Is.False, "리드는 패스 불가");
            Assert.That(d.Move!.IsBomb, Is.False, "리드로 폭탄을 내면 안 된다");
            Assert.That(ctx.LegalMoves.Any(m => m.Rank == d.Move!.Rank && m.Type == d.Move!.Type), Is.True);
        }

        // ── 엔드게임 락아웃: 상대 1장 봉쇄(#2·#3) ────────────────────────────────────

        [Test]
        public void DecideLead_locks_out_one_card_opponent_with_combo()
        {
            // 상대(seat1)가 1장 남음 → 싱글 말고 콤보(페어3)로 리드해 봉쇄(1장으론 페어를 못 받음).
            var s = GameFlowHelpers.PlayState(0,
                Hand(N(2, Suit.Jade), N(3, Suit.Jade), N(3, Suit.Sword), N(5, Suit.Pagoda),
                     N(7, Suit.Star), N(9, Suit.Jade), N(11, Suit.Sword), N(12, Suit.Pagoda)),
                Hand(N(6, Suit.Sword)),                       // seat1: 1장(아웃 임박)
                Hand(N(2, Suit.Pagoda)),
                Hand(N(2, Suit.Star), N(4, Suit.Star), N(8, Suit.Star)));
            var d = new AiAgent(1UL, 0).DecideTurn(GameFlowHelpers.Context(s, 0));
            Assert.That(d.IsPass, Is.False);
            Assert.That(d.Move!.Type, Is.EqualTo(CombinationType.Pair), "상대 1장 → 콤보로 리드해 봉쇄");
        }

        [Test]
        public void DecideFollow_locks_out_one_card_opponent_over_partner()
        {
            // 파트너(seat2)가 싼 싱글(4)을 냄. 상대(seat3)가 1장 남아 그 싱글을 받아 나갈 수 있음 →
            // 파트너 위라도 가장 높은 싱글(A)로 막아 봉쇄해야 한다(패스하면 안 됨).
            var s = FollowState(0, Single(4), topOwner: 2, accumulatedPoints: 0,
                Hand(N(14, Suit.Jade), N(5, Suit.Sword), N(2, Suit.Pagoda), N(3, Suit.Star)),
                Hand(N(7, Suit.Jade), N(8, Suit.Jade), N(9, Suit.Jade)),
                Hand(N(2, Suit.Jade)),
                Hand(N(6, Suit.Sword)));                      // seat3: 1장(아웃 임박)
            var d = new AiAgent(1UL, 0).DecideTurn(GameFlowHelpers.Context(s, 0));
            Assert.That(d.IsPass, Is.False, "상대 1장 봉쇄 → 패스 금지");
            Assert.That(d.Move!.Rank, Is.EqualTo(Single(14).Rank), "가장 높은 싱글(A)로 봉쇄");
        }

        // ── DecideTurn: 낮은카드 콤보 아웃 / 높은족보 보존(#2) ────────────────────────

        [Test]
        public void DecideTurn_finish_mode_leads_low_combo_to_go_out()
        {
            // 끝내기(손패 4장) 리드. {3,3,3,7} — 트리플 3을 내서 확정 아웃을 추진해야 한다.
            // (3을 싱글로 흘리면 안 됨; 가장 강한 수 = 싱글 7 을 고르던 기존 동작을 교정.)
            var s = GameFlowHelpers.PlayState(0,
                Hand(N(3, Suit.Jade), N(3, Suit.Sword), N(3, Suit.Pagoda), N(7, Suit.Star)),
                Hand(N(2, Suit.Jade), N(2, Suit.Pagoda)),
                Hand(N(2, Suit.Sword)),
                Hand(N(2, Suit.Star)));
            var d = new AiAgent(1UL, 0).DecideTurn(GameFlowHelpers.Context(s, 0));
            Assert.That(d.IsPass, Is.False, "리드는 패스 불가");
            Assert.That(d.Move!.Type, Is.EqualTo(CombinationType.Triple), "낮은 카드는 콤보로 털어 아웃 추진");
            Assert.That(d.Move!.Rank, Is.EqualTo(Triple(3).Rank));
        }

        [Test]
        public void DecideTurn_does_not_waste_high_fullhouse_on_cheap_trick()
        {
            // 상대(seat1)가 3 풀하우스(싼 트릭)를 냄. seat0 의 이기는 수는 A 풀하우스뿐.
            // 비싼 족보를 싼 트릭에 낭비하지 않는다 → 패스(아웃도 아니고 위협도 아님).
            var s = FollowState(0, FullHouse(3, 4), topOwner: 1, accumulatedPoints: 0,
                Hand(N(14, Suit.Jade), N(14, Suit.Sword), N(14, Suit.Pagoda),
                     N(2, Suit.Jade), N(2, Suit.Sword), N(13, Suit.Star), N(12, Suit.Star)),
                Hand(N(6, Suit.Jade), N(7, Suit.Jade), N(8, Suit.Jade)),
                Hand(N(2, Suit.Pagoda)),
                Hand(N(6, Suit.Star), N(7, Suit.Star), N(9, Suit.Star)));
            var ctx = GameFlowHelpers.Context(s, 0);
            Assert.That(ctx.CanPass, Is.True);
            var d = new AiAgent(1UL, 0).DecideTurn(ctx);
            Assert.That(d.IsPass, Is.True, "A 풀하우스로 3 풀하우스 막는 건 낭비 → 패스");
        }

        // ── DecideBomb ───────────────────────────────────────────────────────────────

        [Test]
        public void DecideBomb_null_when_points_below_threshold()
        {
            // 누적 점수 10 < 15. 폭탄 있어도 거절.
            var top = Pair(13);
            var s = FollowState(1, top, topOwner: 1, accumulatedPoints: 10,
                Hand(N(2, Suit.Jade)),
                Hand(N(2, Suit.Sword)),
                Hand(BombCards(9).ToArray()),   // seat2 가 폭탄 보유
                Hand(N(2, Suit.Pagoda)));
            // seat2 의 폭탄 창.
            var d = new AiAgent(1UL, 2).DecideBomb(GameFlowHelpers.Context(s, 2));
            Assert.That(d, Is.Null);
        }

        [Test]
        public void DecideBomb_returns_smallest_beating_bomb_for_rich_opponent_top()
        {
            // 누적 20 ≥ 15, Top 소유 상대(seat1; 팀1). seat2(팀0)가 폭탄 4/8/11 보유 → 가장 작은 4 폭탄.
            var top = Pair(13);
            var seat2Hand = new List<Card>();
            seat2Hand.AddRange(BombCards(4));
            seat2Hand.AddRange(BombCards(8));
            seat2Hand.AddRange(BombCards(11));
            var s = FollowState(1, top, topOwner: 1, accumulatedPoints: 20,
                Hand(N(2, Suit.Jade)),
                Hand(N(2, Suit.Sword)),
                seat2Hand,
                Hand(N(2, Suit.Pagoda)));
            var d = new AiAgent(1UL, 2).DecideBomb(GameFlowHelpers.Context(s, 2));

            Assert.That(d, Is.Not.Null, "점수 많은 상대 Top 은 폭탄으로 회수");
            Assert.That(d!.IsBomb, Is.True);
            var fourBomb = CombinationRecognizer.Recognize(BombCards(4).ToArray(), TrickContext.Lead);
            Assert.That(d.Rank, Is.EqualTo(fourBomb.Rank), "가장 작은 폭탄(4)을 내야 한다");
        }

        [Test]
        public void DecideBomb_null_when_partner_owns_top()
        {
            // 점수 충분(40)하지만 Top 소유가 파트너(seat0 의 파트너 = seat2). seat0 가 폭탄 보유하나 거절.
            var top = Pair(13);
            var s = FollowState(1, top, topOwner: 2, accumulatedPoints: 40,
                Hand(BombCards(9).ToArray()),  // seat0(팀0) 폭탄
                Hand(N(2, Suit.Sword)),
                Hand(N(2, Suit.Pagoda)),       // seat2(팀0) = seat0 의 파트너이며 Top 소유
                Hand(N(2, Suit.Star)));
            var d = new AiAgent(1UL, 0).DecideBomb(GameFlowHelpers.Context(s, 0));
            Assert.That(d, Is.Null, "파트너가 Top 을 소유하면 폭탄을 쓰지 않는다");
        }

        // ── ChooseDragonRecipient ─────────────────────────────────────────────────────

        [Test]
        public void ChooseDragonRecipient_is_an_opponent_and_picks_more_cards()
        {
            // seat0 가 용으로 이김. 상대 = seat1(left), seat3(right). seat1 이 더 많은 카드 보유.
            var s = GameFlowHelpers.PlayState(0,
                Hand(N(2, Suit.Jade)),
                Hand(N(3, Suit.Jade), N(4, Suit.Jade), N(5, Suit.Jade)), // seat1: 3장
                Hand(N(6, Suit.Jade)),
                Hand(N(7, Suit.Jade)));                                   // seat3: 1장
            var ctx = GameFlowHelpers.Context(s, 0);
            int r = new AiAgent(1UL, 0).ChooseDragonRecipient(ctx);

            Assert.That(r, Is.EqualTo(ctx.LeftSeat).Or.EqualTo(ctx.RightSeat), "상대 중 하나여야 한다");
            Assert.That(r, Is.Not.EqualTo(ctx.PartnerSeat), "파트너에게 줄 수 없다");
            Assert.That(r, Is.EqualTo(1), "카드가 더 많은 상대(seat1)에게 줘야 한다");
        }

        // ── RandomAgent: 완주 ─────────────────────────────────────────────────────────

        [Test]
        public void RandomAgents_complete_a_round_without_throwing()
        {
            ulong seed = 2024UL;
            var agents = new IAgent[]
            {
                new RandomAgent(seed, 0), new RandomAgent(seed, 1),
                new RandomAgent(seed, 2), new RandomAgent(seed, 3)
            };
            var driver = new GameDriver(agents);
            var outcome = driver.RunRound(GameEngine.NewRound(seed));

            Assert.That(outcome.State.Phase, Is.EqualTo(RoundPhase.RoundEnd));
            Assert.That(outcome.Result, Is.Not.Null);
        }

        // ── AiAgent: 완주 ─────────────────────────────────────────────────────────────

        [Test]
        public void AiAgents_complete_a_round_without_throwing()
        {
            ulong seed = 555UL;
            var agents = new IAgent[]
            {
                new AiAgent(seed, 0), new AiAgent(seed, 1),
                new AiAgent(seed, 2), new AiAgent(seed, 3)
            };
            var driver = new GameDriver(agents);
            var outcome = driver.RunRound(GameEngine.NewRound(seed));

            Assert.That(outcome.State.Phase, Is.EqualTo(RoundPhase.RoundEnd));
            Assert.That(outcome.Result, Is.Not.Null);
        }

        // ── DecideTurn: anti-waste pass ───────────────────────────────────────────────

        [Test]
        public void DecideTurn_passes_on_worthless_trick_when_only_costly_win()
        {
            // 팔로우. Top 소유는 상대(seat1). AccumulatedPoints=0 (점수 없는 트릭).
            // seat0 의 유일한 이기는 비폭탄 수 = Pair(10) — 10이 두 장이라 PointsInPlay > 0.
            // → 비싼 수로밖에 못 이기는 가치 없는 트릭 → Pass 해야 한다.
            // 상대(seat1/seat3)는 손패 충분 + 티츄 미콜 → 위협-블로킹 미발동(안티낭비만 검증).
            var s = FollowState(0, Pair(9), topOwner: 1, accumulatedPoints: 0,
                Hand(N(10, Suit.Jade), N(10, Suit.Sword)),  // seat0: 10페어만 보유
                Hand(N(2, Suit.Jade), N(3, Suit.Jade), N(4, Suit.Jade)),
                Hand(N(2, Suit.Sword)),
                Hand(N(2, Suit.Pagoda), N(3, Suit.Pagoda), N(4, Suit.Pagoda)));
            var ctx = GameFlowHelpers.Context(s, 0);

            Assert.That(ctx.CanPass, Is.True, "이 상황에서 패스가 가능해야 한다");
            var d = new AiAgent(1UL, 0).DecideTurn(ctx);
            Assert.That(d.IsPass, Is.True, "가치 없는 트릭을 비싼 수로 이기는 건 낭비 → 패스해야 한다");
        }

        // ── DecideTurn: 봉황 단독 보존(플레이테스트 버그) ─────────────────────────────
        // 봉황 단독은 팔로우 시 스케일 랭크가 반칸 위(예: 5→11)라 MoveOrder.Lowest 가 자연 6/7(12/14)
        // 보다 값싸게 오인 → 귀한 봉황을 싼 트릭에 낭비. 자연 승수 우선(단 봉황뿐이면 헌납 않고 이긴다).

        [Test]
        public void DecideTurn_prefers_natural_single_over_phoenix()
        {
            // 상대(seat1) 낮은 single 5 리드. seat0 = 봉황 + 자연 7. 자연 7 로 이겨 봉황을 아낀다.
            var s = FollowState(0, Single(5), topOwner: 1, accumulatedPoints: 0,
                Hand(Card.Phoenix, N(7, Suit.Jade), N(2, Suit.Pagoda), N(3, Suit.Star)),
                Hand(N(2, Suit.Sword), N(3, Suit.Sword), N(4, Suit.Sword), N(6, Suit.Sword), N(8, Suit.Sword)),
                Hand(N(2, Suit.Jade)),
                Hand(N(9, Suit.Pagoda), N(10, Suit.Pagoda), N(11, Suit.Pagoda), N(12, Suit.Pagoda), N(13, Suit.Pagoda)));
            var d = new AiAgent(1UL, 0).DecideTurn(GameFlowHelpers.Context(s, 0));
            Assert.That(d.IsPass, Is.False, "자연 승수(7)가 있으니 이긴다");
            Assert.That(d.Move!.Cards[0].Special, Is.Not.EqualTo(SpecialKind.Phoenix), "봉황을 낭비하지 않는다");
            Assert.That(d.Move!.Cards[0].Rank, Is.EqualTo(7), "자연 7 로 이긴다");
        }

        [Test]
        public void DecideTurn_uses_phoenix_when_it_is_the_only_winner()
        {
            // 상대(seat1) 낮은 single 5 리드. seat0 = 봉황 + 2·3(5 못 이김). 이기는 수가 봉황뿐 →
            // 트릭을 헌납하지 않고 봉황으로 이긴다(자연 대안 없을 때; 과-패스는 템포 손해라 벤치서 확인).
            var s = FollowState(0, Single(5), topOwner: 1, accumulatedPoints: 0,
                Hand(Card.Phoenix, N(2, Suit.Pagoda), N(3, Suit.Star)),
                Hand(N(2, Suit.Sword), N(3, Suit.Sword), N(4, Suit.Sword), N(6, Suit.Sword), N(8, Suit.Sword)),
                Hand(N(2, Suit.Jade)),
                Hand(N(9, Suit.Pagoda), N(10, Suit.Pagoda), N(11, Suit.Pagoda), N(12, Suit.Pagoda), N(13, Suit.Pagoda)));
            var d = new AiAgent(1UL, 0).DecideTurn(GameFlowHelpers.Context(s, 0));
            Assert.That(d.IsPass, Is.False, "자연 대안이 없으면 봉황으로라도 이긴다(트릭 헌납 안 함)");
            Assert.That(d.Move!.Cards[0].Special, Is.EqualTo(SpecialKind.Phoenix));
        }

        [Test]
        public void DecideTurn_uses_phoenix_single_to_go_out()
        {
            // seat0 마지막 카드가 봉황뿐 → 밟아서 나가는 게 이득이면 봉황이라도 낸다(과-보존 방지).
            var s = FollowState(0, Single(5), topOwner: 1, accumulatedPoints: 0,
                Hand(Card.Phoenix),
                Hand(N(2, Suit.Sword), N(3, Suit.Sword), N(4, Suit.Sword), N(6, Suit.Sword), N(8, Suit.Sword)),
                Hand(N(2, Suit.Jade)),
                Hand(N(9, Suit.Pagoda), N(10, Suit.Pagoda), N(11, Suit.Pagoda), N(12, Suit.Pagoda), N(13, Suit.Pagoda)));
            var d = new AiAgent(1UL, 0).DecideTurn(GameFlowHelpers.Context(s, 0));
            Assert.That(d.IsPass, Is.False, "나가는 수면 봉황이라도 낸다");
            Assert.That(d.Move!.Cards[0].Special, Is.EqualTo(SpecialKind.Phoenix));
        }

        // ── DecideTurn: 위협 블로킹(#3, 비용 인지) ────────────────────────────────────

        [Test]
        public void DecideTurn_blocks_opponent_tichu_even_with_point_card()
        {
            // 상대(seat1)가 티츄 콜 + 싼 트릭 Top(single 8) 소유. seat0 의 이기는 수는 single 10(점수카드)뿐.
            // 평소엔 안티낭비로 패스하지만, 티츄 위협이므로 막는다(콤보 안 깨면 점수카드라도 OK).
            var s = FollowState(0, Single(8), topOwner: 1, accumulatedPoints: 0,
                Hand(N(10, Suit.Jade), N(2, Suit.Sword), N(3, Suit.Pagoda)),
                Hand(N(5, Suit.Jade), N(6, Suit.Sword), N(7, Suit.Pagoda)),
                Hand(N(2, Suit.Jade)),
                Hand(N(4, Suit.Star), N(6, Suit.Star), N(8, Suit.Star)));
            s.Seats[1].Call = TichuCall.Tichu;
            var d = new AiAgent(1UL, 0).DecideTurn(GameFlowHelpers.Context(s, 0));
            Assert.That(d.IsPass, Is.False, "상대 티츄 위협 → 싼 수로 막는다");
            Assert.That(d.Move!.Rank, Is.EqualTo(Single(10).Rank));
        }

        [Test]
        public void DecideTurn_sends_tichu_when_only_block_breaks_straight()
        {
            // 상대(seat1) 티츄 콜, single 7 Top. seat0 손패 = 5-6-7-8-9 스트레이트.
            // 이기는 수(single 8/9)는 전부 스트레이트의 일부 → 깨면서까지 막지 않고 보내준다(패스).
            var s = FollowState(0, Single(7), topOwner: 1, accumulatedPoints: 0,
                Hand(N(5, Suit.Jade), N(6, Suit.Sword), N(7, Suit.Pagoda), N(8, Suit.Star), N(9, Suit.Jade)),
                Hand(N(2, Suit.Jade), N(3, Suit.Jade)),
                Hand(N(2, Suit.Sword)),
                Hand(N(4, Suit.Star), N(6, Suit.Star)));
            s.Seats[1].Call = TichuCall.Tichu;
            var ctx = GameFlowHelpers.Context(s, 0);
            Assert.That(ctx.CanPass, Is.True);
            var d = new AiAgent(1UL, 0).DecideTurn(ctx);
            Assert.That(d.IsPass, Is.True, "막는 비용이 극심(스트레이트 깸) → 티츄 보내준다");
        }

        [Test]
        public void DecideTurn_blocks_near_out_opponent()
        {
            // 상대(seat1)가 손패 2장(아웃 임박) + 싼 트릭 Top(single 8). seat0 single 10(점수)뿐.
            // 티츄 미콜이지만 아웃 임박 위협 → 막는다.
            var s = FollowState(0, Single(8), topOwner: 1, accumulatedPoints: 0,
                Hand(N(10, Suit.Jade), N(2, Suit.Sword), N(3, Suit.Pagoda)),
                Hand(N(5, Suit.Jade), N(6, Suit.Sword)),    // seat1: 2장(아웃 임박)
                Hand(N(2, Suit.Jade)),
                Hand(N(4, Suit.Star), N(6, Suit.Star), N(7, Suit.Star)));
            var d = new AiAgent(1UL, 0).DecideTurn(GameFlowHelpers.Context(s, 0));
            Assert.That(d.IsPass, Is.False, "상대 아웃 임박 → 막는다");
            Assert.That(d.Move!.Rank, Is.EqualTo(Single(10).Rank));
        }

        // ── OpponentThreatBlockMove (D1): PIMC 라이브 블록 가드 ────────────────────────
        // 상대가 Top 소유 + 아웃/티츄 위협이면 EV 전에 저지할 블록 수를 돌려준다(막지 말아야 하면 null).
        // 파트너-밟기 가드(PartnerOvertakeMove)의 구조적 쌍둥이. PimcAgent 가 플래그 ON 일 때 호출한다.

        private static List<Combination> NonBombWins(DecisionContext ctx)
        {
            var list = new List<Combination>();
            foreach (var m in ctx.LegalMoves)
                if (!m.IsBomb) list.Add(m);
            return list;
        }

        [Test]
        public void OpponentThreatBlockMove_locks_out_one_card_opponent_with_highest_single()
        {
            // 상대(seat1)가 낮은 single 4 Top 소유. 다른 상대(seat3)가 1장(아웃 임박).
            // seat0 는 이기는 싱글 5·A 보유 → 가장 높은 싱글(A)로 봉쇄(1장 상대가 받아 나가기 차단).
            var s = FollowState(0, Single(4), topOwner: 1, accumulatedPoints: 0,
                Hand(N(14, Suit.Jade), N(5, Suit.Sword), N(2, Suit.Pagoda), N(3, Suit.Star)),
                Hand(N(7, Suit.Jade), N(8, Suit.Jade), N(9, Suit.Jade)),
                Hand(N(2, Suit.Jade)),
                Hand(N(6, Suit.Sword)));            // seat3: 1장(아웃 임박)
            var ctx = GameFlowHelpers.Context(s, 0);
            var block = AiAgent.OpponentThreatBlockMove(ctx, s.CurrentTrick, NonBombWins(ctx));
            Assert.That(block, Is.Not.Null, "1장 상대 봉쇄 → 블록 수를 내야 한다");
            Assert.That(block!.Rank, Is.EqualTo(Single(14).Rank), "가장 높은 싱글(A)로 봉쇄");
        }

        [Test]
        public void OpponentThreatBlockMove_blocks_tichu_threat_with_cheapest_nonstructural()
        {
            // 상대(seat1)가 티츄 콜 + 싼 single 8 Top. seat0 이기는 수 = single 10 뿐.
            // 스트레이트를 깨지 않는 최소 오버킬(single 10)로 저지.
            var s = FollowState(0, Single(8), topOwner: 1, accumulatedPoints: 0,
                Hand(N(10, Suit.Jade), N(2, Suit.Sword), N(3, Suit.Pagoda)),
                Hand(N(5, Suit.Jade), N(6, Suit.Sword), N(7, Suit.Pagoda)),
                Hand(N(2, Suit.Jade)),
                Hand(N(4, Suit.Star), N(6, Suit.Star), N(8, Suit.Star)));
            s.Seats[1].Call = TichuCall.Tichu;
            var ctx = GameFlowHelpers.Context(s, 0);
            var block = AiAgent.OpponentThreatBlockMove(ctx, s.CurrentTrick, NonBombWins(ctx));
            Assert.That(block, Is.Not.Null, "티츄 위협 → 저지");
            Assert.That(block!.Rank, Is.EqualTo(Single(10).Rank), "스트레이트 안 깨는 최소 오버킬(10)");
        }

        [Test]
        public void OpponentThreatBlockMove_returns_null_when_only_block_breaks_straight()
        {
            // 상대(seat1) 티츄 콜, single 7 Top. seat0 = 5-6-7-8-9 스트레이트.
            // 이기는 수(8·9)가 전부 스트레이트의 일부 → 깨면서 막지 않고 null(보내준다).
            var s = FollowState(0, Single(7), topOwner: 1, accumulatedPoints: 0,
                Hand(N(5, Suit.Jade), N(6, Suit.Sword), N(7, Suit.Pagoda), N(8, Suit.Star), N(9, Suit.Jade)),
                Hand(N(2, Suit.Jade), N(3, Suit.Jade)),
                Hand(N(2, Suit.Sword)),
                Hand(N(4, Suit.Star), N(6, Suit.Star)));
            s.Seats[1].Call = TichuCall.Tichu;
            var ctx = GameFlowHelpers.Context(s, 0);
            var block = AiAgent.OpponentThreatBlockMove(ctx, s.CurrentTrick, NonBombWins(ctx));
            Assert.That(block, Is.Null, "막을 수가 스트레이트 깨는 싱글뿐 → null");
        }

        [Test]
        public void OpponentThreatBlockMove_returns_null_when_no_threat()
        {
            // 상대(seat1)가 Top 소유하나 아웃임박 아님·티츄 미콜 → 가드 미개입(null → EV 폴백).
            var s = FollowState(0, Single(8), topOwner: 1, accumulatedPoints: 0,
                Hand(N(10, Suit.Jade), N(2, Suit.Sword), N(3, Suit.Pagoda)),
                Hand(N(5, Suit.Jade), N(6, Suit.Sword), N(7, Suit.Pagoda)),
                Hand(N(2, Suit.Jade)),
                Hand(N(4, Suit.Star), N(6, Suit.Star), N(8, Suit.Star)));
            var ctx = GameFlowHelpers.Context(s, 0);
            var block = AiAgent.OpponentThreatBlockMove(ctx, s.CurrentTrick, NonBombWins(ctx));
            Assert.That(block, Is.Null, "위협 없으면 가드 미개입");
        }

        // ── CallTichu ─────────────────────────────────────────────────────────────────

        [Test]
        public void CallTichu_true_for_strong_hand_no_opp_out_partner_not_called()
        {
            // 14장 손패, 용 보유 → 강한 손. 상대 아웃 없음, 파트너 콜 없음 → true.
            var hand = Hand(
                Card.Dragon,
                N(2, Suit.Jade), N(3, Suit.Sword), N(4, Suit.Pagoda), N(5, Suit.Star),
                N(6, Suit.Jade), N(7, Suit.Sword), N(8, Suit.Pagoda), N(9, Suit.Star),
                N(11, Suit.Jade), N(11, Suit.Sword), N(12, Suit.Pagoda),
                N(13, Suit.Star), N(14, Suit.Jade));
            var s = GameFlowHelpers.PlayState(0, hand,
                Hand(N(2, Suit.Sword)), Hand(N(2, Suit.Pagoda)), Hand(N(2, Suit.Star)));
            // 기본값: IsOut=false, Call=None — 조건 충족.
            var agent = new AiAgent(1UL, 0);
            Assert.That(agent.CallTichu(GameFlowHelpers.Context(s, 0)), Is.True);
        }

        [Test]
        public void CallTichu_false_when_following_not_leading()
        {
            // 강한 손(용)이라도 트릭이 진행 중(팔로우)이면 작은 티츄를 외치지 않는다 —
            // 외치고 곧장 패스하는 어색함 방지(리드 시점에만 선언).
            var s = FollowState(0, Pair(5), topOwner: 1, accumulatedPoints: 0,
                Hand(Card.Dragon, N(2, Suit.Jade), N(3, Suit.Sword), N(4, Suit.Pagoda),
                     N(6, Suit.Star), N(7, Suit.Jade), N(8, Suit.Sword), N(9, Suit.Pagoda),
                     N(11, Suit.Star), N(11, Suit.Jade), N(12, Suit.Sword), N(13, Suit.Pagoda),
                     N(14, Suit.Star), N(2, Suit.Sword)),
                Hand(N(2, Suit.Pagoda)),
                Hand(N(2, Suit.Star)),
                Hand(N(3, Suit.Jade)));
            Assert.That(new AiAgent(1UL, 0).CallTichu(GameFlowHelpers.Context(s, 0)), Is.False);
        }

        [Test]
        public void CallTichu_false_for_weak_hand()
        {
            // 14장 손패, 용/봉황 없음, 폭탄 없음 → false.
            var hand = Hand(
                N(2, Suit.Jade), N(3, Suit.Sword), N(4, Suit.Pagoda), N(5, Suit.Star),
                N(6, Suit.Jade), N(7, Suit.Sword), N(8, Suit.Pagoda), N(9, Suit.Star),
                N(11, Suit.Jade), N(12, Suit.Sword), N(13, Suit.Pagoda),
                N(14, Suit.Star), N(3, Suit.Pagoda), N(4, Suit.Star));
            var s = GameFlowHelpers.PlayState(0, hand,
                Hand(N(2, Suit.Sword)), Hand(N(2, Suit.Pagoda)), Hand(N(2, Suit.Star)));
            var agent = new AiAgent(1UL, 0);
            Assert.That(agent.CallTichu(GameFlowHelpers.Context(s, 0)), Is.False);
        }
    }
}
