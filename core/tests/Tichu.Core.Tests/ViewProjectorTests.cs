using System.Collections.Generic;
using NUnit.Framework;
using Tichu.Core.Cards;
using Tichu.Core.Game;
using Tichu.Net;

namespace Tichu.Core.Tests
{
    /// <summary>
    /// ViewProjector — 좌석별 시야 투영. 핵심 불변식은 "타 좌석 손패 카드가 뷰 어디에도
    /// 없다"이며, 가린 ID조차 넣지 않는다(패킷 스니핑 방지, 기획서 7.5).
    /// </summary>
    public class ViewProjectorTests
    {
        /// <summary>큰 티츄 4연속 패스 + 교환 4연속 제출로 Play 페이즈 상태를 만든다.</summary>
        private static GameState PlayPhaseState(ulong seed)
        {
            var s = GameEngine.NewRound(seed);
            for (int seat = 0; seat < 4; seat++)
                Assert.That(GameEngine.Apply(s, GameAction.DeclineGrandTichu(seat)).Ok, Is.True,
                    $"테스트 전제: seat {seat} 큰 티츄 패스 적용");

            Assert.That(s.Phase, Is.EqualTo(RoundPhase.Exchange), "테스트 전제: 교환 페이즈 진입");
            for (int seat = 0; seat < 4; seat++)
            {
                var h = s.Seats[seat].Hand;
                Assert.That(GameEngine.Apply(s, GameAction.Exchange(seat,
                    new[] { h[0] }, new[] { h[1] }, new[] { h[2] })).Ok, Is.True,
                    $"테스트 전제: seat {seat} 교환 제출 적용");
            }

            Assert.That(s.Phase, Is.EqualTo(RoundPhase.Play), "테스트 전제: 플레이 페이즈 진입");
            return s;
        }

        /// <summary>Play 진입 후 합법수를 적용해 트릭·획득 더미에 실제 카드가 쌓이게 전진시킨다.</summary>
        private static GameState AdvancePlay(GameState s, int actions)
        {
            for (int i = 0; i < actions && s.Phase == RoundPhase.Play; i++)
            {
                if (s.TryGetPendingDragonGift(out int w))
                {
                    Assert.That(GameEngine.Apply(s, GameAction.GiveDragon(w, (w + 1) % 4)).Ok, Is.True,
                        $"테스트 전제: step {i} 용 양도 적용");
                    continue;
                }

                int seat = s.Turn;
                var moves = LegalMoveGenerator.LegalMoves(s, seat);
                var action = moves.Count > 0
                    ? GameAction.Play(seat, moves[0].Cards)
                    : GameAction.Pass(seat);
                Assert.That(GameEngine.Apply(s, action).Ok, Is.True,
                    $"테스트 전제: step {i} seat {seat} 액션 적용");
            }
            return s;
        }

        /// <summary>뷰에 등장하는 모든 카드를 모은다.</summary>
        private static List<Card> AllCardsIn(PlayerView v)
        {
            var all = new List<Card>(v.MyHand);
            for (int i = 0; i < 4; i++) all.AddRange(v.WonCards[i]);
            if (v.CurrentTrick != null)
            {
                all.AddRange(v.CurrentTrick.TopCards);
                foreach (var p in v.CurrentTrick.History) all.AddRange(p.Cards);
            }
            return all;
        }

        [Test]
        public void No_other_seat_hand_card_appears_anywhere_in_the_view()
        {
            var s = AdvancePlay(PlayPhaseState(42UL), 12);
            Assert.That(s.CurrentTrick != null || s.CompletedTricks.Count > 0, Is.True,
                "테스트 전제: 전진 후 트릭이 존재해야 함");

            for (int viewer = 0; viewer < 4; viewer++)
            {
                var v = ViewProjector.ProjectFor(s, viewer);
                var visible = AllCardsIn(v);
                Assert.That(visible.Count, Is.GreaterThan(v.MyHand.Count),
                    $"viewer {viewer} 의 뷰가 자기 손패 외 공개 카드를 전혀 담지 않았다 — 마스킹 검사가 공허해진다");
                for (int other = 0; other < 4; other++)
                {
                    if (other == viewer) continue;
                    foreach (var card in s.Seats[other].Hand)
                        Assert.That(visible, Does.Not.Contain(card),
                            $"viewer {viewer} 가 seat {other} 의 손패 {card} 를 볼 수 있다");
                }
            }
        }

        [Test]
        public void Own_hand_is_fully_visible_and_counts_are_public()
        {
            var s = PlayPhaseState(7UL);
            var v = ViewProjector.ProjectFor(s, 1);

            Assert.That(v.Viewer, Is.EqualTo(1));
            Assert.That(v.MyHand, Is.EquivalentTo(s.Seats[1].Hand));
            for (int i = 0; i < 4; i++)
                Assert.That(v.HandCounts[i], Is.EqualTo(s.Seats[i].Hand.Count), $"seat {i} 장수");
        }

        [Test]
        public void Projection_is_a_value_snapshot_not_a_live_reference()
        {
            var s = PlayPhaseState(9UL);
            var v = ViewProjector.ProjectFor(s, 0);

            int handCountBefore = v.MyHand.Count;
            int turnBefore = v.Turn;

            s.Seats[0].Hand.Clear();
            s.Turn = (s.Turn + 1) % 4;

            Assert.That(v.MyHand.Count, Is.EqualTo(handCountBefore), "투영 후 손패 변경이 뷰에 샜다");
            Assert.That(v.Turn, Is.EqualTo(turnBefore), "투영 후 차례 변경이 뷰에 샜다");
        }

        [Test]
        public void Public_facts_are_carried_through()
        {
            var s = PlayPhaseState(11UL);
            s.Wish = 7;
            s.Seats[2].Call = TichuCall.GrandTichu;
            s.Seats[3].IsOut = true;
            s.Seats[3].FinishOrder = 1;
            s.Scores.TeamA = 120;
            s.Scores.TeamB = -20;

            var v = ViewProjector.ProjectFor(s, 0);

            Assert.That(v.Wish, Is.EqualTo(7));
            Assert.That(v.Calls[2], Is.EqualTo(TichuCall.GrandTichu));
            Assert.That(v.IsOut[3], Is.True);
            Assert.That(v.FinishOrder[3], Is.EqualTo(1));
            Assert.That(v.TeamAScore, Is.EqualTo(120));
            Assert.That(v.TeamBScore, Is.EqualTo(-20));
            Assert.That(v.TargetScore, Is.EqualTo(s.Scores.TargetScore));
            Assert.That(v.Phase, Is.EqualTo(RoundPhase.Play));
        }

        [Test]
        public void Trick_view_carries_played_cards_and_marks_passes()
        {
            // seat0 가 A 단독 리드 → seat1 은 3 뿐이라 받을 수 없어 패스한다(결정적).
            var ace = Card.Normal(14, Suit.Jade);
            var s = GameFlowHelpers.PlayState(0,
                new List<Card> { ace },
                new List<Card> { Card.Normal(3, Suit.Jade) },
                new List<Card> { Card.Normal(4, Suit.Jade) },
                new List<Card> { Card.Normal(5, Suit.Jade) });

            Assert.That(GameEngine.Apply(s, GameAction.Play(0, new[] { ace })).Ok, Is.True,
                "테스트 전제: seat0 단독 A 리드 적용");
            Assert.That(LegalMoveGenerator.LegalMoves(s, 1).Count, Is.EqualTo(0),
                "테스트 전제: seat1 은 A 를 받을 수 없어야 함");
            Assert.That(GameEngine.Apply(s, GameAction.Pass(1)).Ok, Is.True,
                "테스트 전제: seat1 패스 적용");

            var v = ViewProjector.ProjectFor(s, 2);

            Assert.That(v.CurrentTrick, Is.Not.Null, "진행 중 트릭이 투영돼야 한다");
            Assert.That(v.CurrentTrick!.TopCards, Is.EquivalentTo(new[] { ace }));
            Assert.That(v.CurrentTrick.TopOwnerSeat, Is.EqualTo(0));
            Assert.That(v.CurrentTrick.History.Count, Is.EqualTo(2));

            var lead = v.CurrentTrick.History[0];
            Assert.That(lead.Seat, Is.EqualTo(0));
            Assert.That(lead.IsPass, Is.False);
            Assert.That(lead.Cards, Is.EquivalentTo(new[] { ace }));

            var passed = v.CurrentTrick.History[1];
            Assert.That(passed.Seat, Is.EqualTo(1));
            Assert.That(passed.IsPass, Is.True, "Combination 이 null 인 엔트리는 패스로 매핑돼야 한다");
            Assert.That(passed.Cards, Is.Empty);
        }
    }
}
