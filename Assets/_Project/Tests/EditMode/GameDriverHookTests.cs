using System.Collections.Generic;
using NUnit.Framework;
using Tichu.Core.Cards;
using Tichu.Core.Combinations;
using Tichu.Core.Game;
using Tichu.GameFlow;
using Tichu.GameFlow.Agents;

namespace Tichu.Core.Tests
{
    /// <summary>
    /// GameDriver 선택적 훅 — 비동기 드라이버와 같은 모양의 onApply / 작은 티츄 예약 /
    /// 차례밖 폭탄 예약. 훅을 주지 않으면 기존 동작과 비트 동일해야 한다.
    /// </summary>
    public class GameDriverHookTests
    {
        private static List<Card> Hand(params Card[] cards) => new List<Card>(cards);

        private static IAgent[] FourDefaults() =>
            new IAgent[] { new ScriptedAgent(), new ScriptedAgent(), new ScriptedAgent(), new ScriptedAgent() };

        [Test]
        public void Round_is_deterministic_across_fresh_drivers()
        {
            var a = new GameDriver(FourDefaults()).RunRound(GameEngine.NewRound(42UL));
            var b = new GameDriver(FourDefaults()).RunRound(GameEngine.NewRound(42UL), null, null, null);

            Assert.That(b.State.ComputeHash(), Is.EqualTo(a.State.ComputeHash()), "상태 해시가 달라졌다");
            Assert.That(b.Log.Count, Is.EqualTo(a.Log.Count), "로그 길이가 달라졌다");
            for (int i = 0; i < a.Log.Count; i++)
            {
                Assert.That(b.Log[i].Kind, Is.EqualTo(a.Log[i].Kind), $"로그 {i} 종류");
                Assert.That(b.Log[i].Seat, Is.EqualTo(a.Log[i].Seat), $"로그 {i} 좌석");
            }
        }

        [Test]
        public void OnApply_fires_once_per_logged_action()
        {
            var seen = new List<GameAction>();
            var outcome = new GameDriver(FourDefaults())
                .RunRound(GameEngine.NewRound(42UL), (st, act) => seen.Add(act));

            Assert.That(seen.Count, Is.EqualTo(outcome.Log.Count));
            for (int i = 0; i < seen.Count; i++)
                Assert.That(seen[i], Is.SameAs(outcome.Log[i]), $"로그 {i} 가 onApply 와 다르다");
        }

        [Test]
        public void Pending_tichu_is_applied_as_a_call_before_the_in_turn_action()
        {
            // 작은 티츄는 손패 14장 + 아직 한 장도 내지 않았을 때만 합법이다
            // (GameEngine.ApplyCallTichu: Hand.Count != FullHandCount → reject).
            // 예약을 받을 seat2 에만 14장을 준다.
            var fourteen = new List<Card>();
            for (int rank = 7; rank <= 14; rank++) fourteen.Add(Card.Normal(rank, Suit.Jade));   // 8장
            for (int rank = 7; rank <= 12; rank++) fourteen.Add(Card.Normal(rank, Suit.Sword));  // 6장
            Assert.That(fourteen.Count, Is.EqualTo(14), "테스트 전제: seat2 손패가 14장이어야 함");

            var s = GameFlowHelpers.PlayState(0,
                Hand(Card.Normal(3, Suit.Jade)),
                Hand(Card.Normal(4, Suit.Jade)),
                fourteen,
                Hand(Card.Normal(6, Suit.Jade)));

            bool handed = false;
            var outcome = new GameDriver(FourDefaults()).RunRound(s, null,
                takePendingTichu: _ => { if (handed) return -1; handed = true; return 2; });

            Assert.That(outcome.State.Seats[2].Call, Is.EqualTo(TichuCall.Tichu));
            Assert.That(outcome.Log[0].Kind, Is.EqualTo(GameActionKind.CallTichu), "예약 콜이 먼저 적용돼야 한다");
            Assert.That(outcome.Log[0].Seat, Is.EqualTo(2));
        }

        [Test]
        public void Pending_bomb_preempts_the_in_turn_action()
        {
            // seat0 이 단독 3 을 리드한 뒤, seat2 가 9 포카드를 예약한다.
            var s = GameFlowHelpers.PlayState(0,
                Hand(Card.Normal(3, Suit.Jade), Card.Normal(2, Suit.Jade)),
                Hand(Card.Normal(4, Suit.Jade)),
                Hand(Card.Normal(9, Suit.Jade), Card.Normal(9, Suit.Sword),
                     Card.Normal(9, Suit.Pagoda), Card.Normal(9, Suit.Star)),
                Hand(Card.Normal(6, Suit.Jade)));

            Assert.That(GameEngine.Apply(s, GameAction.Play(0, new[] { Card.Normal(3, Suit.Jade) })).Ok,
                Is.True, "테스트 전제: seat0 단독 3 리드 적용");

            var bombCards = new[]
            {
                Card.Normal(9, Suit.Jade), Card.Normal(9, Suit.Sword),
                Card.Normal(9, Suit.Pagoda), Card.Normal(9, Suit.Star)
            };
            var bomb = CombinationRecognizer.Recognize(bombCards, TrickContext.Lead);
            Assert.That(bomb.Type, Is.EqualTo(CombinationType.FourBomb), "테스트 전제: 9 포카드로 인식");

            bool handed = false;
            var outcome = new GameDriver(FourDefaults()).RunRound(s, null, null,
                takePendingBomb: _ =>
                {
                    if (handed) return null;
                    handed = true;
                    return GameAction.Play(2, bombCards);
                });

            Assert.That(outcome.Log[0].Kind, Is.EqualTo(GameActionKind.Play));
            Assert.That(outcome.Log[0].Seat, Is.EqualTo(2), "예약 폭탄이 차례를 선점해야 한다");
        }

        [Test]
        public void Rejected_pending_bomb_is_neither_logged_nor_broadcast()
        {
            var s = GameFlowHelpers.PlayState(0,
                Hand(Card.Normal(3, Suit.Jade)),
                Hand(Card.Normal(4, Suit.Jade)),
                Hand(Card.Normal(5, Suit.Jade)),
                Hand(Card.Normal(6, Suit.Jade)));

            // seat2 는 손에 없는 카드를 폭탄으로 예약한다 → 엔진이 거부 → 조용히 버려야 한다.
            var bogus = new[]
            {
                Card.Normal(9, Suit.Jade), Card.Normal(9, Suit.Sword),
                Card.Normal(9, Suit.Pagoda), Card.Normal(9, Suit.Star)
            };

            var notified = new List<GameAction>();
            bool handed = false;
            RoundOutcome outcome = null!;

            Assert.DoesNotThrow(() =>
                outcome = new GameDriver(FourDefaults()).RunRound(s,
                    (st, act) => notified.Add(act),
                    null,
                    _ =>
                    {
                        if (handed) return null;
                        handed = true;
                        return GameAction.Play(2, bogus);
                    }));

            Assert.That(handed, Is.True, "테스트 전제: 폭탄 예약 훅이 한 번은 호출돼야 함");
            foreach (var a in outcome.Log)
                Assert.That(a.Cards, Is.Not.EqualTo(bogus), "거부된 폭탄이 로그에 들어갔다");
            foreach (var a in notified)
                Assert.That(a.Cards, Is.Not.EqualTo(bogus), "거부된 폭탄이 방송됐다");
            Assert.That(notified.Count, Is.EqualTo(outcome.Log.Count), "방송 수와 로그 수가 어긋났다");
        }

        [Test]
        public void Rejected_pending_tichu_is_dropped_without_throwing()
        {
            // 손패가 14장이 아니면 GameEngine.ApplyCallTichu 가 거부한다. 원격 입력이므로
            // 드라이버는 throw 하지 않고 버려야 한다.
            var s = GameFlowHelpers.PlayState(0,
                Hand(Card.Normal(3, Suit.Jade)),
                Hand(Card.Normal(4, Suit.Jade)),
                Hand(Card.Normal(5, Suit.Jade)),
                Hand(Card.Normal(6, Suit.Jade)));

            bool handed = false;
            RoundOutcome outcome = null!;

            Assert.DoesNotThrow(() =>
                outcome = new GameDriver(FourDefaults()).RunRound(s, null,
                    takePendingTichu: _ => { if (handed) return -1; handed = true; return 2; }));

            Assert.That(handed, Is.True, "테스트 전제: 티츄 예약 훅이 한 번은 호출돼야 함");
            Assert.That(outcome.State.Seats[2].Call, Is.EqualTo(TichuCall.None), "거부된 콜이 적용됐다");
            foreach (var a in outcome.Log)
                Assert.That(a.Kind, Is.Not.EqualTo(GameActionKind.CallTichu), "거부된 콜이 로그에 들어갔다");
        }
    }
}
