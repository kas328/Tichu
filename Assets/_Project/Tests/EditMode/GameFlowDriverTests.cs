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
    /// GameDriver.RunRound — 1라운드 오케스트레이션 검증.
    /// 고정 순서(용 양도 → 폭탄창 → 작은 티츄 폴스루 → 인-턴), 무작위성 없음,
    /// 모든 Apply 가 거부 시 throw 하는 무-불법-상태 증명, 로그 리플레이 결정성.
    /// </summary>
    public class GameFlowDriverTests
    {
        private static List<Card> Hand(params Card[] cards) => new List<Card>(cards);

        private static GameDriver Driver(params IAgent[] agents) => new GameDriver(agents);

        private static IAgent[] FourDefaults() =>
            new IAgent[] { new ScriptedAgent(), new ScriptedAgent(), new ScriptedAgent(), new ScriptedAgent() };

        // ── 완주 ──────────────────────────────────────────────────────────────────

        [Test]
        public void RunRound_with_default_agents_reaches_round_end()
        {
            var driver = Driver(FourDefaults());
            var outcome = driver.RunRound(GameEngine.NewRound(42UL));

            Assert.That(outcome.State.Phase, Is.EqualTo(RoundPhase.RoundEnd));
            Assert.That(outcome.Result, Is.Not.Null);
            Assert.That(outcome.Log.Count, Is.GreaterThan(0));
        }

        // ── 불법 액션 → throw ──────────────────────────────────────────────────────

        [Test]
        public void Illegal_agent_action_throws()
        {
            // seat0 리드. 손에 없는 페어(9♠9♣)를 내려 한다 → "played cards are not all in hand".
            var s = GameFlowHelpers.PlayState(0,
                Hand(Card.Normal(3, Suit.Jade)),
                Hand(Card.Normal(4, Suit.Jade)),
                Hand(Card.Normal(5, Suit.Jade)),
                Hand(Card.Normal(6, Suit.Jade)));

            var bogus = CombinationRecognizer.Recognize(
                new[] { Card.Normal(9, Suit.Jade), Card.Normal(9, Suit.Sword) }, TrickContext.Lead);
            Assert.That(bogus.Type, Is.EqualTo(CombinationType.Pair), "테스트 전제: 9 페어로 인식되어야 함");

            var rogue = new ScriptedAgent { DecideTurnFn = _ => TurnDecision.Play(bogus) };
            var driver = Driver(rogue, new ScriptedAgent(), new ScriptedAgent(), new ScriptedAgent());

            var ex = Assert.Throws<System.InvalidOperationException>(() => driver.RunRound(s));
            Assert.That(ex!.Message, Does.Contain("played cards are not all in hand"));
        }

        // ── 셋업: 마작 보유자가 선공 ───────────────────────────────────────────────

        [Test]
        public void Setup_seats_mahjong_holder_as_first_turn()
        {
            // NewRound 를 셋업(큰티츄 거절 ×4, 교환 ×4)만 직접 진행해 Play 진입 시점을 확인.
            var s = GameEngine.NewRound(42UL);
            for (int i = 0; i < 4; i++)
                GameEngine.Apply(s, GameAction.DeclineGrandTichu(i));
            for (int seat = 0; seat < 4; seat++)
            {
                var h = s.Seats[seat].Hand;
                GameEngine.Apply(s, GameAction.Exchange(seat,
                    new List<Card> { h[0] }, new List<Card> { h[1] }, new List<Card> { h[2] }));
            }

            Assert.That(FlowQuery.Next(s).Kind, Is.EqualTo(StepKind.Play));
            Assert.That(s.Seats[s.Turn].Hand.Contains(Card.Mahjong), Is.True,
                "Play 진입 시 선공 좌석은 마작 보유자여야 한다");
        }

        // ── 작은 티츄 훅: 콜하고도 같은 턴에 플레이 ────────────────────────────────

        [Test]
        public void Small_tichu_hook_calls_and_still_plays()
        {
            // seat0 선공. 14장 손패(콜 게이트 충족)에서 작은 티츄를 콜하고도 그 턴에 플레이해야 한다.
            // seat0 needs exactly 14 cards to trigger the small-tichu hook (Hand.Count == 14).
            // Seats 1-3 hold distinct minimal hands (no card shared with seat0 or each other).
            // Jade 2..14 = 13 cards; Sword-2 is the unique 14th for seat0.
            // Seats 1-3 get two cards each from their own suits at ranks not used by seat0.
            var seat0Hand = FourteenSingles(Suit.Jade, Card.Normal(2, Suit.Sword));
            var s = GameFlowHelpers.PlayState(0,
                seat0Hand,
                Hand(Card.Normal(3, Suit.Sword), Card.Normal(4, Suit.Sword)),
                Hand(Card.Normal(3, Suit.Pagoda), Card.Normal(4, Suit.Pagoda)),
                Hand(Card.Normal(3, Suit.Star), Card.Normal(4, Suit.Star)));

            var caller = new ScriptedAgent { CallTichuFn = _ => true };
            var driver = Driver(caller, new ScriptedAgent(), new ScriptedAgent(), new ScriptedAgent());
            var outcome = driver.RunRound(s);

            Assert.That(outcome.State.Seats[0].Call, Is.EqualTo(TichuCall.Tichu), "작은 티츄가 콜되어야 한다");
            Assert.That(outcome.State.Seats[0].Hand.Count, Is.LessThan(14),
                "작은 티츄가 턴을 소모하지 않았다 — seat0 은 그 턴에 카드를 냈어야 한다");
            // 로그에 seat0 의 CallTichu 와 Play 가 모두 있어야 한다.
            Assert.That(outcome.Log.Any(a => a.Kind == GameActionKind.CallTichu && a.Seat == 0), Is.True);
            Assert.That(outcome.Log.Any(a => a.Kind == GameActionKind.Play && a.Seat == 0), Is.True);
        }

        // ── 리플레이 결정성 ────────────────────────────────────────────────────────

        [Test]
        public void Replay_log_reproduces_hash()
        {
            var driver = Driver(FourDefaults());
            var outcome = driver.RunRound(GameEngine.NewRound(7UL));

            // 신선한 동일 시드 라운드에 로그를 순서대로 재적용.
            var replay = GameEngine.NewRound(7UL);
            foreach (var a in outcome.Log)
            {
                var r = GameEngine.Apply(replay, a);
                Assert.That(r.Ok, Is.True, $"리플레이 액션 거부됨: {a.Kind} seat {a.Seat}: {r.RejectReason}");
            }
            // 원본은 RunRound 후 ScoreRound 까지 갔으므로 리플레이도 Scoring 에서 ScoreRound 호출.
            Assert.That(replay.Phase, Is.EqualTo(RoundPhase.Scoring));
            ScoreCalculator.ScoreRound(replay);

            Assert.That(replay.ComputeHash(), Is.EqualTo(outcome.State.ComputeHash()));
        }

        // ── 용 양도가 드라이버를 통해 라우팅됨 ─────────────────────────────────────

        [Test]
        public void Dragon_gift_routed_through_driver()
        {
            // seat0 가 용 단독을 들고 리드. 나머지는 못 이겨 패스 → 용 트릭 회수 → 양도 대기.
            // 드라이버가 ChooseDragonRecipient 를 호출해 양도를 처리하고 라운드를 완주해야 한다.
            // 손패를 작게 구성해 라운드가 빠르게 끝나도록 한다.
            var s = GameFlowHelpers.PlayState(0,
                Hand(Card.Dragon, Card.Normal(9, Suit.Jade)),
                Hand(Card.Normal(3, Suit.Jade), Card.Normal(3, Suit.Sword)),
                Hand(Card.Normal(4, Suit.Jade), Card.Normal(4, Suit.Sword)),
                Hand(Card.Normal(5, Suit.Jade), Card.Normal(5, Suit.Sword)));

            // seat0 의 양도 상대를 명시적으로 left(seat1)로 고정.
            var winner = new ScriptedAgent { ChooseDragonRecipientFn = _ => 1 };
            var driver = Driver(winner, new ScriptedAgent(), new ScriptedAgent(), new ScriptedAgent());
            var outcome = driver.RunRound(s);

            // 라운드 완주.
            Assert.That(outcome.State.Phase, Is.EqualTo(RoundPhase.RoundEnd));
            // 용 트릭이 완료되었고 수혜자가 기록됨.
            var dragonTrick = outcome.State.CompletedTricks.FirstOrDefault(t => t.WonByDragon);
            Assert.That(dragonTrick, Is.Not.Null, "용 트릭이 회수되어야 한다");
            Assert.That(dragonTrick!.DragonGiftRecipient, Is.EqualTo(1), "드라이버가 양도 상대를 기록해야 한다");
            // 로그에 GiveDragon 이 있어야 한다.
            Assert.That(outcome.Log.Any(a => a.Kind == GameActionKind.GiveDragon && a.Seat == 0), Is.True);
        }

        // ── 보조 ──────────────────────────────────────────────────────────────────

        /// <summary>한 슈트의 rank 2..14(13장) + 호출자가 지정한 fill 카드 1장 = 총 14장.</summary>
        private static List<Card> FourteenSingles(Suit suit, Card fill)
        {
            var list = new List<Card>();
            for (int r = 2; r <= 14; r++)
                list.Add(Card.Normal(r, suit));
            list.Add(fill);
            return list;
        }
    }
}
