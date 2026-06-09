#nullable enable
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Tichu.Core.Cards;
using Tichu.Core.Combinations;
using Tichu.Core.Game;

namespace Tichu.Core.Tests
{
    /// <summary>합법수 생성/검증(LegalMoveGenerator) 검증.</summary>
    public class LegalMovesTests
    {
        // ── 헬퍼 ────────────────────────────────────────────────────────────────

        private static List<Card> Hand(params Card[] cards) => new List<Card>(cards);

        /// <summary>리드 상황(CurrentTrick==null) Play 페이즈 상태.</summary>
        private static GameState LeadState(int turn, params List<Card>[] hands)
        {
            var s = new GameState { Phase = RoundPhase.Play, Turn = turn, CurrentTrick = null };
            for (int i = 0; i < 4; i++)
            {
                s.Seats[i] = new PlayerSeat { SeatIndex = i };
                s.Seats[i].Hand.AddRange(hands[i]);
            }
            return s;
        }

        /// <summary>seat0이 leadCards로 리드해 둔 팔로우 상황 상태.</summary>
        private static GameState FollowState(List<Card> leadCards, params List<Card>[] hands)
        {
            // hands[0]에는 leadCards가 포함돼 있어야 한다(seat0이 그것을 냄).
            var s = LeadState(0, hands);
            var r = GameEngine.Apply(s, GameAction.Play(0, leadCards));
            Assert.That(r.Ok, Is.True, "lead setup must succeed");
            return s;
        }

        private static bool ContainsType(IReadOnlyList<Combination> moves, CombinationType type) =>
            moves.Any(m => m.Type == type);

        private static bool ContainsExactRanks(IReadOnlyList<Combination> moves, CombinationType type, params int[] ranksScaled)
        {
            foreach (var m in moves)
            {
                if (m.Type != type) continue;
                if (ranksScaled.Contains(m.Rank)) return true;
            }
            return false;
        }

        // ── 리드 ────────────────────────────────────────────────────────────────

        [Test]
        public void Lead_legal_moves_include_singles_pairs_etc_and_no_pass()
        {
            // 손: 7페어 + 9 + K → 단독 3종(7,7중복은 1개로), 페어 1종.
            var s = LeadState(0,
                Hand(Card.Normal(7, Suit.Jade), Card.Normal(7, Suit.Sword), Card.Normal(9, Suit.Star),
                     Card.Normal(13, Suit.Pagoda)),
                Hand(Card.Normal(3, Suit.Jade)),
                Hand(Card.Normal(4, Suit.Jade)),
                Hand(Card.Normal(5, Suit.Jade)));

            var moves = LegalMoveGenerator.LegalMoves(s, 0);

            Assert.That(ContainsType(moves, CombinationType.Single), Is.True, "singles");
            Assert.That(ContainsType(moves, CombinationType.Pair), Is.True, "pair of 7s");
            Assert.That(LegalMoveGenerator.CanPass(s, 0), Is.False, "no pass on a lead");

            // 단독은 distinct rank 3종(7,9,K).
            int singleCount = moves.Count(m => m.Type == CombinationType.Single);
            Assert.That(singleCount, Is.EqualTo(3), "distinct single ranks 7/9/K");
        }

        [Test]
        public void Lead_includes_special_singles()
        {
            var s = LeadState(0,
                Hand(Card.Mahjong, Card.Dog, Card.Phoenix, Card.Dragon, Card.Normal(9, Suit.Star)),
                Hand(Card.Normal(3, Suit.Jade)),
                Hand(Card.Normal(4, Suit.Jade)),
                Hand(Card.Normal(5, Suit.Jade)));

            var moves = LegalMoveGenerator.LegalMoves(s, 0);

            // Mahjong/Dog/Phoenix/Dragon/9 모두 단독 리드 가능.
            Assert.That(moves.Any(m => m.Type == CombinationType.Single && m.Cards[0].Special == SpecialKind.Mahjong), Is.True);
            Assert.That(moves.Any(m => m.Type == CombinationType.Single && m.Cards[0].Special == SpecialKind.Dog), Is.True);
            Assert.That(moves.Any(m => m.Type == CombinationType.Single && m.Cards[0].Special == SpecialKind.Phoenix), Is.True);
            Assert.That(moves.Any(m => m.Type == CombinationType.Single && m.Cards[0].Special == SpecialKind.Dragon), Is.True);
        }

        [Test]
        public void Lead_straight_generation()
        {
            // 5,6,7,8,9 스트레이트 한 종.
            var s = LeadState(0,
                Hand(Card.Normal(5, Suit.Jade), Card.Normal(6, Suit.Sword), Card.Normal(7, Suit.Pagoda),
                     Card.Normal(8, Suit.Star), Card.Normal(9, Suit.Jade)),
                Hand(Card.Normal(3, Suit.Jade)),
                Hand(Card.Normal(4, Suit.Jade)),
                Hand(Card.Normal(5, Suit.Jade)));

            var moves = LegalMoveGenerator.LegalMoves(s, 0);
            Assert.That(ContainsType(moves, CombinationType.Straight), Is.True);
            var straight = moves.First(m => m.Type == CombinationType.Straight);
            Assert.That(straight.Length, Is.EqualTo(5));
        }

        [Test]
        public void Lead_phoenix_straight_generation()
        {
            // 5,6,_,8,9 + Phoenix → 봉황이 7 메움 → 길이5 스트레이트.
            var s = LeadState(0,
                Hand(Card.Normal(5, Suit.Jade), Card.Normal(6, Suit.Sword),
                     Card.Normal(8, Suit.Star), Card.Normal(9, Suit.Jade), Card.Phoenix),
                Hand(Card.Normal(3, Suit.Jade)),
                Hand(Card.Normal(4, Suit.Jade)),
                Hand(Card.Normal(5, Suit.Jade)));

            var moves = LegalMoveGenerator.LegalMoves(s, 0);
            Assert.That(moves.Any(m => m.Type == CombinationType.Straight && m.Length == 5), Is.True,
                "phoenix fills internal gap to make a straight");
        }

        [Test]
        public void Lead_consecutive_pairs_generation()
        {
            // 5,5,6,6 → 연속페어 길이4.
            var s = LeadState(0,
                Hand(Card.Normal(5, Suit.Jade), Card.Normal(5, Suit.Sword),
                     Card.Normal(6, Suit.Pagoda), Card.Normal(6, Suit.Star)),
                Hand(Card.Normal(3, Suit.Jade)),
                Hand(Card.Normal(4, Suit.Jade)),
                Hand(Card.Normal(5, Suit.Jade)));

            var moves = LegalMoveGenerator.LegalMoves(s, 0);
            Assert.That(moves.Any(m => m.Type == CombinationType.ConsecutivePairs && m.Length == 4), Is.True);
        }

        [Test]
        public void Lead_full_house_generation()
        {
            // 7,7,7,9,9 → 풀하우스.
            var s = LeadState(0,
                Hand(Card.Normal(7, Suit.Jade), Card.Normal(7, Suit.Sword), Card.Normal(7, Suit.Pagoda),
                     Card.Normal(9, Suit.Star), Card.Normal(9, Suit.Jade)),
                Hand(Card.Normal(3, Suit.Jade)),
                Hand(Card.Normal(4, Suit.Jade)),
                Hand(Card.Normal(5, Suit.Jade)));

            var moves = LegalMoveGenerator.LegalMoves(s, 0);
            Assert.That(moves.Any(m => m.Type == CombinationType.FullHouse), Is.True);
        }

        [Test]
        public void Lead_straight_flush_bomb_generation()
        {
            // 같은 문양 5,6,7,8,9 → 스트레이트플러시 폭탄.
            var s = LeadState(0,
                Hand(Card.Normal(5, Suit.Jade), Card.Normal(6, Suit.Jade), Card.Normal(7, Suit.Jade),
                     Card.Normal(8, Suit.Jade), Card.Normal(9, Suit.Jade)),
                Hand(Card.Normal(3, Suit.Sword)),
                Hand(Card.Normal(4, Suit.Jade)),
                Hand(Card.Normal(5, Suit.Sword)));

            var moves = LegalMoveGenerator.LegalMoves(s, 0);
            Assert.That(moves.Any(m => m.Type == CombinationType.StraightFlushBomb), Is.True);
        }

        // ── 팔로우 ──────────────────────────────────────────────────────────────

        [Test]
        public void Following_returns_only_beating_moves_plus_pass_plus_bombs()
        {
            // Top = 9 페어. seat1 손: K페어(이김) + 5페어(못이김) + 6포카드(폭탄).
            var s = FollowState(
                Hand(Card.Normal(9, Suit.Jade), Card.Normal(9, Suit.Sword)),       // seat0 리드
                Hand(Card.Normal(9, Suit.Jade), Card.Normal(9, Suit.Sword)),       // seat0 hand (consumed)
                Hand(Card.Normal(13, Suit.Jade), Card.Normal(13, Suit.Sword),      // seat1: K페어
                     Card.Normal(5, Suit.Pagoda), Card.Normal(5, Suit.Star),       // 5페어
                     Card.Normal(6, Suit.Jade), Card.Normal(6, Suit.Sword),
                     Card.Normal(6, Suit.Pagoda), Card.Normal(6, Suit.Star)),      // 6포카드
                Hand(Card.Normal(3, Suit.Jade)),
                Hand(Card.Normal(4, Suit.Jade)));

            Assert.That(s.Turn, Is.EqualTo(1), "seat1 follows");

            var moves = LegalMoveGenerator.LegalMoves(s, 1);

            // K페어 포함.
            Assert.That(moves.Any(m => m.Type == CombinationType.Pair && m.Rank == 13 * 2), Is.True, "K pair beats");
            // 5페어 미포함.
            Assert.That(moves.Any(m => m.Type == CombinationType.Pair && m.Rank == 5 * 2), Is.False, "5 pair does not beat");
            // 폭탄 포함.
            Assert.That(moves.Any(m => m.Type == CombinationType.FourBomb), Is.True, "bomb is a candidate");
            // 패스 가능.
            Assert.That(LegalMoveGenerator.CanPass(s, 1), Is.True);
        }

        [Test]
        public void Out_of_turn_bomb_is_a_legal_move_but_nonbomb_is_not()
        {
            // Top = A 단독(seat0). seat2 차례 아님. seat2는 6포카드 폭탄 + 9단독 보유.
            var s = FollowState(
                Hand(Card.Normal(14, Suit.Jade)),
                Hand(Card.Normal(14, Suit.Jade)),
                Hand(Card.Normal(3, Suit.Jade)),                                   // seat1
                Hand(Card.Normal(6, Suit.Jade), Card.Normal(6, Suit.Sword),       // seat2 폭탄
                     Card.Normal(6, Suit.Pagoda), Card.Normal(6, Suit.Star),
                     Card.Normal(9, Suit.Star)),
                Hand(Card.Normal(4, Suit.Jade)));

            Assert.That(s.Turn, Is.EqualTo(1), "seat1 to move; seat2 is out of turn");

            var moves = LegalMoveGenerator.LegalMoves(s, 2);
            Assert.That(moves.Any(m => m.Type == CombinationType.FourBomb), Is.True, "out-of-turn bomb allowed");
            Assert.That(moves.Any(m => m.Type == CombinationType.Single), Is.False, "no out-of-turn non-bomb");
            Assert.That(LegalMoveGenerator.CanPass(s, 2), Is.False, "cannot pass out of turn");
        }

        // ── IsLegal 일관성 ──────────────────────────────────────────────────────

        [Test]
        public void IsLegal_rejects_card_not_in_hand()
        {
            var s = LeadState(0,
                Hand(Card.Normal(9, Suit.Jade)),
                Hand(Card.Normal(3, Suit.Jade)),
                Hand(Card.Normal(4, Suit.Jade)),
                Hand(Card.Normal(5, Suit.Jade)));

            var fake = CombinationRecognizer.Recognize(
                new[] { Card.Normal(14, Suit.Star) }, TrickContext.Lead);
            Assert.That(LegalMoveGenerator.IsLegal(s, 0, fake, out var reason), Is.False);
            Assert.That(reason, Is.Not.Empty);
        }

        [Test]
        public void Property_all_legal_moves_pass_IsLegal()
        {
            int totalChecks = 0;
            for (int seed = 0; seed < 60; seed++)
            {
                foreach (var (s, actingSeat) in RandomPlayStates(seed))
                {
                    var moves = LegalMoveGenerator.LegalMoves(s, actingSeat);
                    foreach (var m in moves)
                    {
                        totalChecks++;
                        bool legal = LegalMoveGenerator.IsLegal(s, actingSeat, m, out var reason);
                        Assert.That(legal, Is.True,
                            $"seed {seed} seat {actingSeat}: move {Describe(m)} should be legal but: {reason}");
                    }
                }
            }
            Assert.That(totalChecks, Is.GreaterThan(1000), "property test should exercise some moves");
        }

        [Test]
        public void Property_all_legal_moves_are_accepted_by_apply()
        {
            int totalMoves = 0;
            for (int seed = 0; seed < 60; seed++)
            {
                foreach (var (s, actingSeat) in RandomPlayStates(seed))
                {
                    var moves = LegalMoveGenerator.LegalMoves(s, actingSeat);
                    foreach (var m in moves)
                    {
                        totalMoves++;
                        var clone = s.Clone();
                        var cards = new List<Card>(m.Cards);
                        var r = GameEngine.Apply(clone, GameAction.Play(actingSeat, cards));
                        Assert.That(r.Ok, Is.True,
                            $"seed {seed} seat {actingSeat}: move {Describe(m)} rejected by Apply: {r.RejectReason}");
                    }
                    // CanPass도 Apply와 일관: 패스 가능하다면 Apply(Pass)도 수락.
                    if (LegalMoveGenerator.CanPass(s, actingSeat))
                    {
                        var clone = s.Clone();
                        var r = GameEngine.Apply(clone, GameAction.Pass(actingSeat));
                        Assert.That(r.Ok, Is.True,
                            $"seed {seed} seat {actingSeat}: CanPass true but Apply(Pass) rejected: {r.RejectReason}");
                    }
                }
            }
            Assert.That(totalMoves, Is.GreaterThan(1000), "property test should exercise some moves");
        }

        // ── 랜덤 상태 빌더 ──────────────────────────────────────────────────────

        /// <summary>
        /// 한 라운드를 Play까지 셋업한 뒤, 무작위 합법수로 여러 수 진행하면서
        /// 각 의사결정 시점의 (상태, 행동좌석)을 yield한다. 리드/팔로우/소원 등 다양한 상황을 만든다.
        /// </summary>
        private static IEnumerable<(GameState s, int seat)> RandomPlayStates(int seed)
        {
            var s = GameEngine.NewRound((ulong)(seed + 1));
            for (int i = 0; i < 4; i++)
                GameEngine.Apply(s, GameAction.DeclineGrandTichu(i));
            for (int seat = 0; seat < 4; seat++)
            {
                var h = s.Seats[seat].Hand;
                GameEngine.Apply(s, GameAction.Exchange(seat,
                    new List<Card> { h[0] }, new List<Card> { h[1] }, new List<Card> { h[2] }));
            }

            var rng = new System.Random(seed);
            int steps = 0, maxSteps = 30;
            // 한 라운드 안에서 종료 전까지 진행하며 매 결정 시점을 snapshot.
            while (s.Phase == RoundPhase.Play && ActiveSeats(s) >= 2 && steps < maxSteps)
            {
                int acting = s.Turn;
                if (s.Seats[acting].IsOut) break; // 안전장치

                // 검사 대상 snapshot 반환.
                yield return (s.Clone(), acting);

                // 무작위로 한 수 진행(패스 또는 합법수 중 택1).
                var moves = LegalMoveGenerator.LegalMoves(s, acting);
                bool canPass = LegalMoveGenerator.CanPass(s, acting);
                if (moves.Count == 0 && !canPass) break;

                bool doPass = canPass && (moves.Count == 0 || rng.Next(3) == 0);
                ApplyResult r;
                if (doPass)
                    r = GameEngine.Apply(s, GameAction.Pass(acting));
                else
                {
                    var pick = moves[rng.Next(moves.Count)];
                    r = GameEngine.Apply(s, GameAction.Play(acting, new List<Card>(pick.Cards)));
                }
                if (!r.Ok) break; // 진행 불가 시 종료(테스트 본체가 별도 검증).
                steps++;
            }
        }

        private static int ActiveSeats(GameState s)
        {
            int n = 0;
            for (int i = 0; i < 4; i++) if (!s.Seats[i].IsOut) n++;
            return n;
        }

        private static string Describe(Combination m) =>
            $"{m.Type}/len{m.Length}/rank{m.Rank}[{string.Join(",", m.Cards.Select(c => c.ToString()))}]";
    }
}
