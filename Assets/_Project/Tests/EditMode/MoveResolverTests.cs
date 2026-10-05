using System.Collections.Generic;
using System.Threading;
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

        // ── Task 4.5 리뷰 라운드 1: 스트레이트플러시 폭탄 오타입 ────────────────────

        [Test]
        public void Genuine_straight_flush_resolves_as_a_bomb_not_a_plain_straight()
        {
            // 생성기는 5-9 구간에 대해 무늬 무관 평범 스트레이트(대표는 bag.Cards[r][0] —
            // 손패 추가 순서상 7 랭크에서 Sword 가 Jade 보다 먼저 들어가 대표가 된다)와
            // 보유한 무늬 그대로의 스트레이트플러시 폭탄을 **따로** 낸다. 리드(자기 턴)라
            // 둘 다 LegalMoves 에 들어간다 — 이 테스트의 전제.
            var s = LeadState(
                Hand(Card.Normal(5, Suit.Jade), Card.Normal(6, Suit.Jade), Card.Normal(7, Suit.Sword),
                     Card.Normal(8, Suit.Jade), Card.Normal(9, Suit.Jade), Card.Normal(7, Suit.Jade)),
                Hand(Card.Normal(3, Suit.Jade)),
                Hand(Card.Normal(4, Suit.Jade)),
                Hand(Card.Normal(10, Suit.Jade)));

            bool hasMixedStraight = false, hasFlushBomb = false;
            foreach (var m in LegalMoveGenerator.LegalMoves(s, 0))
            {
                if (m.Type == CombinationType.Straight && m.Cards.Count == 5) hasMixedStraight = true;
                if (m.Type == CombinationType.StraightFlushBomb) hasFlushBomb = true;
            }
            Assert.That(hasMixedStraight, Is.True, "테스트 전제: 혼동용 평범 스트레이트가 합법수에 있어야 함");
            Assert.That(hasFlushBomb, Is.True, "테스트 전제: 진짜 스트레이트플러시 폭탄도 합법수에 있어야 함");

            var genuineFlush = new List<Card>
            {
                Card.Normal(5, Suit.Jade), Card.Normal(6, Suit.Jade), Card.Normal(7, Suit.Jade),
                Card.Normal(8, Suit.Jade), Card.Normal(9, Suit.Jade)
            };
            var a = ClientAction.Play(0, 1L, genuineFlush, rank: 0, wish: null);
            var move = MoveResolver.ResolveMove(s, 0, a);

            Assert.That(move, Is.Not.Null);
            Assert.That(move!.Type, Is.EqualTo(CombinationType.StraightFlushBomb),
                "보유한 무늬 그대로의 폭탄이 랭크 등가 폴백 때문에 평범 스트레이트로 잘못 타입되면 안 된다");
            Assert.That(move.IsBomb, Is.True);
            Assert.That(move.Cards, Is.EquivalentTo(genuineFlush));
        }

        [Test]
        public void Off_turn_bomb_reservation_for_a_genuine_flush_is_consumed_not_stuck()
        {
            // MatchSession.TakePendingBomb(MatchSession.cs:227-252, private 라 직접 호출 불가)가
            // 소비하는 경로를 그대로 재현한다: RemoteAgent 의 오프턴 폭탄 예약 큐 →
            // MoveResolver.ResolveMove → move.IsBomb 게이트(MatchSession.cs:248) →
            // ConsumeReservedBomb. IsBomb 이 false 로 나오면 그 게이트가 거부해 예약이
            // 큐 머리에 영구히 걸리고(TakePendingBomb 의 문서화된 한계), 이후 모든 폭탄이
            // 막힌다 — Task 4 의 _bombSent 가 막던 것과 같은 부류의 증상이 다른 문으로 재발한다.
            //
            // 참고(실측): 이 구체적 모양은 오프턴에서는 수정 전에도 이미 안전했다 —
            // LegalMoveGenerator.LegalMoves 의 턴 게이트(canPlayNonBomb)가 비(非)폭탄 후보를
            // 오프턴 조회에서 걸러내므로, 오프턴 LegalMoves 에는 진짜 폭탄 후보만 남는다.
            // 이 테스트는 그 보호가 (수정 이후에도) 깨지지 않았다는 것과, 예약→해소→게이트→
            // 소비로 이어지는 전체 경로가 실제로 동작한다는 것을 고정한다.
            var s = GameFlowHelpers.PlayState(1,
                Hand(Card.Normal(5, Suit.Jade), Card.Normal(6, Suit.Jade), Card.Normal(7, Suit.Sword),
                     Card.Normal(8, Suit.Jade), Card.Normal(9, Suit.Jade), Card.Normal(7, Suit.Jade)),
                Hand(Card.Normal(3, Suit.Jade)),
                Hand(Card.Normal(4, Suit.Jade)),
                Hand(Card.Normal(10, Suit.Jade)));
            Assert.That(GameEngine.Apply(s, GameAction.Play(1, new[] { Card.Normal(3, Suit.Jade) })).Ok,
                Is.True, "테스트 전제: seat1 단독 3 리드 적용(seat0 이 오프턴에서 인터럽트할 트릭)");

            long arrival = 0;
            var remote = new RemoteAgent(0,
                (seat, ev) => { }, (seat, aw) => { }, () => ++arrival,
                System.TimeSpan.FromSeconds(5), new CancellationTokenSource().Token);

            var genuineFlush = new List<Card>
            {
                Card.Normal(5, Suit.Jade), Card.Normal(6, Suit.Jade), Card.Normal(7, Suit.Jade),
                Card.Normal(8, Suit.Jade), Card.Normal(9, Suit.Jade)
            };
            remote.Submit(ClientAction.Play(0, 1L, genuineFlush, rank: 0, wish: null)); // 차례밖 → 예약 큐

            var pending = remote.PeekReservedBomb();
            Assert.That(pending, Is.Not.Null, "테스트 전제: 제출이 오프턴 예약 큐에 들어가야 함");

            var move = MoveResolver.ResolveMove(s, 0, pending!); // MatchSession.cs:247 과 동일 호출
            Assert.That(move, Is.Not.Null);
            Assert.That(move!.IsBomb, Is.True,
                "MatchSession.cs:248 의 게이트 — IsBomb 이 false 면 예약이 큐 머리에 영원히 남는다");

            remote.ConsumeReservedBomb();
            Assert.That(remote.PeekReservedBomb(), Is.Null, "소비 후에는 큐가 비어야 한다");
        }

        // ── 최종 리뷰 FIX 2: 랭크 등가 폴백의 재인식 가드 ───────────────────────────

        [Test]
        public void Collapsed_straight_template_rejects_a_mixed_suit_substitution()
        {
            // 손의 5-9 구간에서 매 랭크의 대표 카드(bag.Cards[r][0], 손패 추가 순서상 Jade
            // 가 먼저 들어감)가 전부 Jade 라, GenerateStraights 가 만든 카드 집합 자체가
            // 플러시다 — Recognize 가 그것을 StraightFlushBomb 으로 인식하고, 뒤이은
            // GenerateStraightFlushBombs 의 동일 카드 집합과 ComboKey 가 겹쳐 중복 제거된다.
            // 그 구간엔 StraightFlushBomb 템플릿 하나만 남고, 평범 Straight 템플릿은 따로
            // 생성되지 않는다(이 테스트의 전제).
            var s = LeadState(
                Hand(Card.Normal(5, Suit.Jade), Card.Normal(6, Suit.Jade), Card.Normal(7, Suit.Jade),
                     Card.Normal(8, Suit.Jade), Card.Normal(9, Suit.Jade), Card.Normal(7, Suit.Sword)),
                Hand(Card.Normal(3, Suit.Jade)),
                Hand(Card.Normal(4, Suit.Jade)),
                Hand(Card.Normal(10, Suit.Jade)));

            bool hasPlainStraight = false, hasFlushBombOf5 = false;
            foreach (var m in LegalMoveGenerator.LegalMoves(s, 0))
            {
                if (m.Type == CombinationType.Straight && m.Cards.Count == 5) hasPlainStraight = true;
                if (m.Type == CombinationType.StraightFlushBomb && m.Cards.Count == 5) hasFlushBombOf5 = true;
            }
            Assert.That(hasPlainStraight, Is.False,
                "테스트 전제: 대표 카드가 전부 Jade 라 평범 스트레이트 템플릿이 따로 생기면 안 됨(ComboKey 중복 제거)");
            Assert.That(hasFlushBombOf5, Is.True, "테스트 전제: 그 구간엔 StraightFlushBomb 템플릿 하나만 남아야 함");

            // 클라이언트는 같은 랭크를 혼합 무늬로 제출한다(7 랭크만 Sword) — 진짜 스트레이트일
            // 뿐 플러시가 아니다. 1차(카드 신원)는 실패하고 2차(랭크 등가)가 위 템플릿에 매치된다.
            var mixedSuit = new List<Card>
            {
                Card.Normal(5, Suit.Jade), Card.Normal(6, Suit.Jade), Card.Normal(7, Suit.Sword),
                Card.Normal(8, Suit.Jade), Card.Normal(9, Suit.Jade)
            };
            var a = ClientAction.Play(0, 1L, mixedSuit, rank: 0, wish: null);
            var move = MoveResolver.ResolveMove(s, 0, a);

            Assert.That(move, Is.Null,
                "재인식 없이 템플릿 Type 을 그대로 붙이면 평범 스트레이트가 폭탄으로 둔갑한다 — 재인식 가드가 거부해야 함");
        }
    }
}
