using System.Collections.Generic;
using NUnit.Framework;
using Tichu.Core.Cards;
using Tichu.Core.Combinations;
using Tichu.Core.Game;
using Tichu.Net;

namespace Tichu.Core.Tests
{
    /// <summary>
    /// MoveResolver — 클라이언트가 보낸 카드 집합을 서버의 합법수 중 하나로 확정한다.
    /// Recognize 는 카드 다중집합의 순수 함수라 두 합법수가 같은 카드 집합을 가질 수 없고,
    /// 그래서 카드 집합만으로 확정된다. Rank 는 모호성 해소가 아니라 클라이언트 의도 검증용이다.
    /// </summary>
    public class MoveResolverTests
    {
        private static List<Card> Hand(params Card[] cards) => new List<Card>(cards);

        private static GameState LeadState(params IReadOnlyList<Card>[] hands) =>
            GameFlowHelpers.PlayState(0, hands);

        [Test]
        public void Unique_card_set_resolves_without_rank()
        {
            var s = LeadState(
                Hand(Card.Normal(5, Suit.Jade), Card.Normal(9, Suit.Sword)),
                Hand(Card.Normal(4, Suit.Jade)),
                Hand(Card.Normal(6, Suit.Jade)),
                Hand(Card.Normal(7, Suit.Jade)));

            var a = ClientAction.Play(0, 1L, new List<Card> { Card.Normal(5, Suit.Jade) }, rank: 0, wish: null);
            var move = MoveResolver.ResolveMove(s, 0, a);

            Assert.That(move, Is.Not.Null);
            Assert.That(move!.Type, Is.EqualTo(CombinationType.Single));
            Assert.That(move.Cards, Is.EquivalentTo(new[] { Card.Normal(5, Suit.Jade) }));
        }

        [Test]
        public void Cards_not_in_hand_resolve_to_null()
        {
            var s = LeadState(
                Hand(Card.Normal(5, Suit.Jade)),
                Hand(Card.Normal(4, Suit.Jade)),
                Hand(Card.Normal(6, Suit.Jade)),
                Hand(Card.Normal(7, Suit.Jade)));

            var a = ClientAction.Play(0, 1L, new List<Card> { Card.Normal(13, Suit.Star) }, rank: 0, wish: null);

            Assert.That(MoveResolver.ResolveMove(s, 0, a), Is.Null);
        }

        [Test]
        public void Phoenix_straight_has_a_single_deterministic_reading()
        {
            // 엔진은 끝 확장 시 상단을 우선한다(CombinationRecognizer.cs:229-233) —
            // 3,4,5,6,봉황 은 7-high 하나뿐이고 봉황을 2로 쓰는 변형은 생성되지 않는다.
            var phoenixHand = Hand(
                Card.Normal(3, Suit.Jade), Card.Normal(4, Suit.Jade),
                Card.Normal(5, Suit.Jade), Card.Normal(6, Suit.Jade), Card.Phoenix);

            var s = LeadState(phoenixHand,
                Hand(Card.Normal(8, Suit.Jade)),
                Hand(Card.Normal(9, Suit.Jade)),
                Hand(Card.Normal(10, Suit.Jade)));

            var straights = new List<Combination>();
            foreach (var m in LegalMoveGenerator.LegalMoves(s, 0))
                if (m.Type == CombinationType.Straight && m.Cards.Count == 5) straights.Add(m);

            Assert.That(straights.Count, Is.EqualTo(1), "같은 5장으로 스트레이트가 둘 생성되면 안 된다");
            Assert.That(straights[0].Rank, Is.EqualTo(14), "봉황은 상단(7)으로 확장되어야 한다 — Rank 는 ×2 스케일");

            var a = ClientAction.Play(0, 1L, new List<Card>(phoenixHand), rank: 0, wish: null);
            var resolved = MoveResolver.ResolveMove(s, 0, a);
            Assert.That(resolved, Is.Not.Null);
            Assert.That(resolved!.Rank, Is.EqualTo(straights[0].Rank));
        }

        [Test]
        public void Mismatched_rank_is_not_applied()
        {
            var s = LeadState(
                Hand(Card.Normal(5, Suit.Jade), Card.Normal(9, Suit.Sword)),
                Hand(Card.Normal(4, Suit.Jade)),
                Hand(Card.Normal(6, Suit.Jade)),
                Hand(Card.Normal(7, Suit.Jade)));

            var cards = new List<Card> { Card.Normal(5, Suit.Jade) };
            var right = MoveResolver.ResolveMove(s, 0, ClientAction.Play(0, 1L, cards, rank: 0, wish: null));
            Assert.That(right, Is.Not.Null, "테스트 전제: 카드 집합만으로 해소되어야 함");

            var wrong = ClientAction.Play(0, 2L, cards, rank: right!.Rank + 1, wish: null);
            Assert.That(MoveResolver.ResolveMove(s, 0, wrong), Is.Null,
                "클라이언트가 다른 조합을 의도했다면 적용하지 않는다");
        }

        [Test]
        public void No_two_legal_moves_share_a_card_multiset()
        {
            // ResolveMove 가 매치 개수를 세지 않는 근거가 되는 불변식.
            // 엔진이 나중에 봉황 하단 확장을 허용하면 이 테스트가 먼저 터져 알려 준다.
            var hands = new[]
            {
                Hand(Card.Normal(3, Suit.Jade), Card.Normal(4, Suit.Jade), Card.Normal(5, Suit.Jade),
                     Card.Normal(6, Suit.Jade), Card.Phoenix, Card.Normal(6, Suit.Sword),
                     Card.Normal(6, Suit.Pagoda), Card.Normal(6, Suit.Star)),
                Hand(Card.Normal(7, Suit.Jade), Card.Normal(7, Suit.Sword), Card.Normal(8, Suit.Jade),
                     Card.Normal(8, Suit.Sword), Card.Normal(9, Suit.Jade), Card.Normal(9, Suit.Sword),
                     Card.Phoenix, Card.Dragon),
            };

            foreach (var hand in hands)
            {
                var s = LeadState(hand,
                    Hand(Card.Normal(13, Suit.Star)),
                    Hand(Card.Normal(12, Suit.Star)),
                    Hand(Card.Normal(11, Suit.Star)));

                var moves = LegalMoveGenerator.LegalMoves(s, 0);
                for (int i = 0; i < moves.Count; i++)
                    for (int j = i + 1; j < moves.Count; j++)
                        Assert.That(SameSet(moves[i], moves[j]), Is.False,
                            $"합법수 {i} 와 {j} 가 카드 집합을 공유한다 ({moves[i].Type}/{moves[i].Rank} vs {moves[j].Type}/{moves[j].Rank})");
            }
        }

        /// <summary>두 조합이 같은 카드 다중집합인가(테스트 전용 비교).</summary>
        private static bool SameSet(Combination x, Combination y)
        {
            if (x.Cards.Count != y.Cards.Count) return false;
            var used = new bool[y.Cards.Count];
            for (int i = 0; i < x.Cards.Count; i++)
            {
                bool found = false;
                for (int j = 0; j < y.Cards.Count; j++)
                {
                    if (used[j] || !y.Cards[j].Equals(x.Cards[i])) continue;
                    used[j] = true; found = true; break;
                }
                if (!found) return false;
            }
            return true;
        }

        [Test]
        public void Null_cards_resolve_to_null()
        {
            var s = LeadState(
                Hand(Card.Normal(5, Suit.Jade)),
                Hand(Card.Normal(4, Suit.Jade)),
                Hand(Card.Normal(6, Suit.Jade)),
                Hand(Card.Normal(7, Suit.Jade)));

            var a = new ClientAction { Kind = ClientActionKind.Play, Seat = 0, ActionSeq = 1L, Cards = null };

            Assert.That(MoveResolver.ResolveMove(s, 0, a), Is.Null);
        }

        // ── Task 4.5: 랭크 등가 ─────────────────────────────────────────────────

        [Test]
        public void Single_of_a_suit_the_generator_did_not_pick_still_resolves_to_the_card_sent()
        {
            // bag.Cards[9] 는 손패 추가 순서로 채워지므로(HandBag.Build) 생성기 대표는
            // 먼저 추가된 Jade 다(GenerateSingles 는 랭크당 Cards[r][0] 하나만 내보낸다).
            var s = LeadState(
                Hand(Card.Normal(9, Suit.Jade), Card.Normal(9, Suit.Sword)),
                Hand(Card.Normal(4, Suit.Jade)),
                Hand(Card.Normal(6, Suit.Jade)),
                Hand(Card.Normal(7, Suit.Jade)));

            Combination? representative = null;
            foreach (var m in LegalMoveGenerator.LegalMoves(s, 0))
                if (m.Type == CombinationType.Single && m.Cards[0].Rank == 9) representative = m;
            Assert.That(representative, Is.Not.Null, "테스트 전제: 랭크 9 싱글이 합법수로 생성돼야 함");
            Assert.That(representative!.Cards[0].Suit, Is.EqualTo(Suit.Jade),
                "테스트 전제: 생성기 대표는 먼저 추가된 Jade 다");

            var a = ClientAction.Play(0, 1L, new List<Card> { Card.Normal(9, Suit.Sword) }, rank: 0, wish: null);
            var move = MoveResolver.ResolveMove(s, 0, a);

            Assert.That(move, Is.Not.Null, "생성기 대표가 아닌 무늬라고 거부되면 안 된다");
            Assert.That(move!.Cards, Is.EquivalentTo(new[] { Card.Normal(9, Suit.Sword) }),
                "돌려주는 카드는 생성기 대표(Jade)가 아니라 클라이언트가 보낸 카드(Sword)여야 한다");
        }

        [Test]
        public void Pair_with_one_card_of_a_different_suit_than_the_generator_still_resolves()
        {
            var s = LeadState(
                Hand(Card.Normal(9, Suit.Jade), Card.Normal(9, Suit.Sword), Card.Normal(9, Suit.Star)),
                Hand(Card.Normal(4, Suit.Jade)),
                Hand(Card.Normal(6, Suit.Jade)),
                Hand(Card.Normal(7, Suit.Jade)));

            // 생성기 대표 페어는 [Jade9, Sword9](TakeInto 가 처음 두 장을 집는다).
            // 한 장(Sword→Star)만 다른 무늬로 바꿔 보낸다.
            var sentCards = new List<Card> { Card.Normal(9, Suit.Jade), Card.Normal(9, Suit.Star) };
            var a = ClientAction.Play(0, 1L, sentCards, rank: 0, wish: null);
            var move = MoveResolver.ResolveMove(s, 0, a);

            Assert.That(move, Is.Not.Null);
            Assert.That(move!.Type, Is.EqualTo(CombinationType.Pair));
            Assert.That(move.Cards, Is.EquivalentTo(sentCards));
        }

        [Test]
        public void Exact_generator_cards_still_resolve_unchanged()
        {
            // 정확 일치 경로(AI 가 합법수 목록에서 그대로 집어 보내는 경로)가 여전히 동작함을 고정한다.
            var s = LeadState(
                Hand(Card.Normal(9, Suit.Jade), Card.Normal(9, Suit.Sword)),
                Hand(Card.Normal(4, Suit.Jade)),
                Hand(Card.Normal(6, Suit.Jade)),
                Hand(Card.Normal(7, Suit.Jade)));

            Combination? template = null;
            foreach (var m in LegalMoveGenerator.LegalMoves(s, 0))
                if (m.Type == CombinationType.Pair) template = m;
            Assert.That(template, Is.Not.Null, "테스트 전제: 페어가 합법수로 생성돼야 함");

            var a = ClientAction.Play(0, 1L, new List<Card>(template!.Cards), rank: 0, wish: null);
            var move = MoveResolver.ResolveMove(s, 0, a);

            Assert.That(move, Is.Not.Null);
            Assert.That(move!.Type, Is.EqualTo(CombinationType.Pair));
            Assert.That(move.Cards, Is.EquivalentTo(template.Cards));
        }

        [Test]
        public void Card_not_in_hand_with_matching_rank_is_rejected()
        {
            // 랭크 등가가 여는 구멍의 가드: 7♠ 만 들고 같은 랭크의 다른 무늬(Sword)를 보내면
            // 랭크 다중집합은 일치하지만 그 물리 카드를 손에 쥐고 있지 않다 — HandContainsAll 이 막아야 한다.
            var s = LeadState(
                Hand(Card.Normal(7, Suit.Jade)),
                Hand(Card.Normal(4, Suit.Jade)),
                Hand(Card.Normal(6, Suit.Jade)),
                Hand(Card.Normal(9, Suit.Jade)));

            var a = ClientAction.Play(0, 1L, new List<Card> { Card.Normal(7, Suit.Sword) }, rank: 0, wish: null);

            Assert.That(MoveResolver.ResolveMove(s, 0, a), Is.Null,
                "보유하지 않은 무늬의 같은 랭크 카드는 거부돼야 한다 — HandContainsAll 가드");
        }

        [Test]
        public void Wish_forced_rank_cannot_be_evaded_by_rank_equivalence()
        {
            // 소원이 걸리면 소원 랭크를 포함하지 않는 수는 합법수 목록 자체에서 빠진다
            // (ApplyWishFilter). 랭크 다중집합 비교로 느슨해진 뒤에도 그 필터링이
            // 여전히 적용됨을 고정한다.
            var s = LeadState(
                Hand(Card.Normal(7, Suit.Jade), Card.Normal(9, Suit.Star), Card.Normal(13, Suit.Pagoda)),
                Hand(Card.Normal(3, Suit.Jade)),
                Hand(Card.Normal(4, Suit.Jade)),
                Hand(Card.Normal(5, Suit.Jade)));
            s.Wish = 7;

            var a = ClientAction.Play(0, 1L, new List<Card> { Card.Normal(9, Suit.Star) }, rank: 0, wish: null);

            Assert.That(MoveResolver.ResolveMove(s, 0, a), Is.Null,
                "소원 랭크를 포함하지 않는 수는 등가 비교로도 통과하면 안 된다");
        }
    }
}
