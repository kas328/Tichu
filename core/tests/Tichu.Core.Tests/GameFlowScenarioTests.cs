using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Tichu.Core;
using Tichu.Core.Cards;
using Tichu.Core.Combinations;
using Tichu.Core.Game;
using Tichu.GameFlow;
using Tichu.GameFlow.Agents;

namespace Tichu.Core.Tests
{
    /// <summary>
    /// GameFlow 시나리오 테스트(ScriptedAgent + 손패 직접구성 seam).
    /// 랜덤 딜로는 도달 불가한 특정 국면을 직접 구성해 드라이버/매치 계층의 동작을 고정한다.
    /// 다루는 시나리오: ①용 양도 좌/우 ②차례밖 폭탄 ③소원 강제 ④원-투+티츄 ⑤1000-동점 속행.
    /// </summary>
    [TestFixture]
    public class GameFlowScenarioTests
    {
        private static List<Card> Hand(params Card[] cards) => new List<Card>(cards);

        // ── 시나리오 ①: 용 양도 좌/우 — 수혜자·해시가 선택에 따라 달라진다 ─────────────

        /// <summary>seat0 의 용 트릭을 left(seat1) vs right(seat3) 에 양도하면 같은 플레이라도
        /// 트릭의 DragonGiftRecipient 와 최종 ComputeHash 가 달라야 한다(선택이 끝까지 전파됨).</summary>
        [Test]
        public void Dragon_gift_left_vs_right_differs_in_recipient_and_hash()
        {
            RoundOutcome Drive(int recipient)
            {
                var winner = new ScriptedAgent { ChooseDragonRecipientFn = _ => recipient };
                var driver = new GameDriver(new IAgent[]
                {
                    winner, new ScriptedAgent(), new ScriptedAgent(), new ScriptedAgent()
                });
                return driver.RunRound(DragonEndgame());
            }

            var left  = Drive(1); // 왼쪽 상대(seat1)
            var right = Drive(3); // 오른쪽 상대(seat3)

            // 둘 다 정상 완주.
            Assert.That(left.State.Phase,  Is.EqualTo(RoundPhase.RoundEnd));
            Assert.That(right.State.Phase, Is.EqualTo(RoundPhase.RoundEnd));

            var leftDragon  = left.State.CompletedTricks.FirstOrDefault(t => t.WonByDragon);
            var rightDragon = right.State.CompletedTricks.FirstOrDefault(t => t.WonByDragon);
            Assert.That(leftDragon,  Is.Not.Null, "left: 용 트릭이 회수되어야 함");
            Assert.That(rightDragon, Is.Not.Null, "right: 용 트릭이 회수되어야 함");

            // 수혜자가 선택대로 기록된다.
            Assert.That(leftDragon!.DragonGiftRecipient,  Is.EqualTo(1));
            Assert.That(rightDragon!.DragonGiftRecipient, Is.EqualTo(3));

            // 양도 외 모든 플레이가 동일하므로 차이는 오직 수혜자 → 해시가 달라야 한다.
            Assert.That(left.State.ComputeHash(), Is.Not.EqualTo(right.State.ComputeHash()),
                "좌/우 양도 선택이 최종 해시에 반영되어야 함");
        }

        /// <summary>seat0 가 용+낮은패를 들고, 나머지는 못 이기는 endgame. (driver 테스트와 동일 패턴)</summary>
        private static GameState DragonEndgame() => GameFlowHelpers.PlayState(0,
            Hand(Card.Dragon, Card.Normal(9, Suit.Jade)),
            Hand(Card.Normal(3, Suit.Jade), Card.Normal(3, Suit.Sword)),
            Hand(Card.Normal(4, Suit.Jade), Card.Normal(4, Suit.Sword)),
            Hand(Card.Normal(5, Suit.Jade), Card.Normal(5, Suit.Sword)));

        // ── 시나리오 ②: 차례밖 폭탄이 인-턴 행동을 가로채고 Top 을 갱신한다 ──────────────

        /// <summary>seat0 가 단독 7 로 리드하면, seat1 의 차례가 오기 전에 seat2 의 폭탄 창이 먼저
        /// 발동해 폭탄이 떨어지고(Top=폭탄, 소유자=seat2), seat1 은 그 트릭에서 행동하지 못한다.</summary>
        [Test]
        public void Off_turn_bomb_preempts_in_turn_action_and_updates_top()
        {
            var s = GameFlowHelpers.PlayState(0,
                Hand(Card.Normal(7, Suit.Sword)),                              // seat0: 리드
                Hand(Card.Normal(3, Suit.Pagoda)),                            // seat1: 못 이김
                Hand(Card.Normal(9, Suit.Jade),  Card.Normal(9, Suit.Sword),  // seat2: 네 장 9 폭탄 + 여분
                     Card.Normal(9, Suit.Pagoda), Card.Normal(9, Suit.Star),
                     Card.Normal(2, Suit.Jade)),
                Hand(Card.Normal(5, Suit.Star)));                             // seat3: 못 이김

            // seat2 는 폭탄 창에서 LegalMoves 의 폭탄을 낸다.
            var bomber = new ScriptedAgent { DecideBombFn = ctx => ctx.LegalMoves.FirstOrDefault(m => m.IsBomb) };
            var driver = new GameDriver(new IAgent[]
            {
                new ScriptedAgent(), new ScriptedAgent(), bomber, new ScriptedAgent()
            });

            var outcome = driver.RunRound(s);

            // 첫 두 액션: seat0 의 단독 7 리드 → seat1 의 차례를 건너뛰고 seat2 의 폭탄.
            Assert.That(outcome.Log[0].Kind, Is.EqualTo(GameActionKind.Play));
            Assert.That(outcome.Log[0].Seat, Is.EqualTo(0), "log[0] = seat0 리드");
            Assert.That(outcome.Log[0].Cards!.Count, Is.EqualTo(1));

            Assert.That(outcome.Log[1].Kind, Is.EqualTo(GameActionKind.Play));
            Assert.That(outcome.Log[1].Seat, Is.EqualTo(2), "log[1] = seat2 의 차례밖 폭탄 (seat1 가로채짐)");
            Assert.That(outcome.Log[1].Cards!.Count, Is.EqualTo(4), "네 장 폭탄");
            Assert.That(outcome.Log[1].Cards!.All(c => c.Rank == 9), Is.True, "모두 랭크 9");

            // Top 갱신: 폭탄 트릭은 폭탄을 낸 seat2 가 가져간다.
            Assert.That(outcome.State.CompletedTricks[0].TopOwnerSeat, Is.EqualTo(2),
                "폭탄 트릭의 Top 소유자는 폭탄을 낸 seat2");
        }

        // ── 시나리오 ③: 마작 소원이 하류 좌석의 패스를 막는다(CanPass=false) ────────────

        /// <summary>seat0 가 마작을 내며 7 을 소원하면, 7 을 가진 seat1 은 패스할 수 없어야 한다.
        /// 소원이 없으면 같은 손패에서 seat1 은 패스 가능 — 소원이 원인임을 분리 검증한다.</summary>
        [Test]
        public void Mahjong_wish_forbids_downstream_pass()
        {
            // 소원 7 → seat1(7 보유)은 패스 불가, 합법수는 소원(7)을 만족.
            var withWish = WishSetup();
            Assert.That(GameEngine.Apply(withWish, GameAction.Play(0,
                new List<Card> { Card.Mahjong }, wish: 7)).Ok, Is.True);

            var ctx = GameFlowHelpers.Context(withWish, 1);
            Assert.That(ctx.CanPass, Is.False, "소원 7 강제 → seat1 은 패스 불가");
            Assert.That(ctx.LegalMoves.Count, Is.GreaterThan(0));
            Assert.That(ctx.LegalMoves.Any(m => m.Cards.Any(c => c.Rank == 7)), Is.True,
                "합법수는 소원(랭크 7)을 만족해야 함");

            // 대조: 소원 없이 마작만 내면 seat1 은 패스 가능.
            var noWish = WishSetup();
            Assert.That(GameEngine.Apply(noWish, GameAction.Play(0,
                new List<Card> { Card.Mahjong })).Ok, Is.True);
            Assert.That(GameFlowHelpers.Context(noWish, 1).CanPass, Is.True,
                "소원이 없으면 seat1 은 패스 가능(소원이 강제의 원인)");
        }

        private static GameState WishSetup() => GameFlowHelpers.PlayState(0,
            Hand(Card.Mahjong, Card.Normal(8, Suit.Sword)),                  // seat0: 마작 리드
            Hand(Card.Normal(7, Suit.Pagoda), Card.Normal(2, Suit.Pagoda)), // seat1: 7 보유
            Hand(Card.Normal(9, Suit.Star),  Card.Normal(10, Suit.Star)),
            Hand(Card.Normal(11, Suit.Jade), Card.Normal(12, Suit.Jade)));

        // ── 시나리오 ④: 원-투(200,0) + 성공 티츄 델타 ───────────────────────────────

        /// <summary>team A(seat0,seat2)가 1·2번째로 아웃되면 원-투 → 카드점수 (200,0).
        /// seat0 이 작은 티츄를 부르고 first-out 하면 +100 델타가 합산된다.</summary>
        [Test]
        public void One_two_finish_scores_200_0_plus_tichu_delta()
        {
            var s = GameFlowHelpers.PlayState(0,
                Hand(Card.Normal(10, Suit.Sword)),                          // seat0(A): 리드 후 first-out
                Hand(Card.Normal(2, Suit.Pagoda), Card.Normal(3, Suit.Pagoda)), // seat1(B): 못 이김
                Hand(Card.Normal(14, Suit.Sword)),                          // seat2(A): A 로 second-out
                Hand(Card.Normal(4, Suit.Star), Card.Normal(5, Suit.Star)));    // seat3(B): 못 이김
            s.Seats[0].Call = TichuCall.Tichu; // seat0 작은 티츄 콜(직접 설정 — 콜 페이즈 우회).

            var outcome = new GameDriver(new IAgent[]
            {
                new ScriptedAgent(), new ScriptedAgent(), new ScriptedAgent(), new ScriptedAgent()
            }).RunRound(s);

            // 원-투 확정: seat0=1, seat2=2 (파트너 1·2 아웃).
            Assert.That(outcome.State.Seats[0].FinishOrder, Is.EqualTo(1));
            Assert.That(outcome.State.Seats[2].FinishOrder, Is.EqualTo(2));

            var r = outcome.Result;
            Assert.That(r.TeamACardPoints, Is.EqualTo(200), "원-투 승리 팀 카드점수 200");
            Assert.That(r.TeamBCardPoints, Is.EqualTo(0),   "원-투 패배 팀 카드점수 0");
            Assert.That(r.TeamATichuDelta, Is.EqualTo(100), "seat0 작은 티츄 성공 +100");
            Assert.That(r.TeamBTichuDelta, Is.EqualTo(0));
            Assert.That(r.TeamATotal, Is.EqualTo(300));
            Assert.That(r.TeamBTotal, Is.EqualTo(0));
        }

        // ── 시나리오 ⑤: 양 팀이 target 으로 동점이면 매치가 추가 라운드를 진행한다 ──────

        /// <summary>RunMatch 의 라운드1 이 동점(TeamATotal==TeamBTotal)인 시드를 찾아, target 을 그
        /// 동점값으로 두면 매치는 라운드1 에서 끝나지 않고 동점을 깰 때까지 속행해야 한다.</summary>
        [Test]
        public void Match_continues_past_a_tie_at_target()
        {
            // RandomAgent 는 티츄/폭탄을 쓰지 않으므로 라운드 점수는 순수 카드 분배(50-50 동점이 흔함).
            System.Func<ulong, int, IAgent> factory = (rs, seat) => new RandomAgent(rs, seat);

            for (ulong seed = 0; seed < 3000; seed++)
            {
                var r1 = FirstRoundResult(seed, factory);
                if (r1.TeamATotal != r1.TeamBTotal || r1.TeamATotal <= 0) continue;

                int target = r1.TeamATotal; // 양 팀이 정확히 이 점수로 동시 돌파 → Decide=Continue.
                var match = MatchRunner.RunMatch(seed, factory, target);

                Assert.That(match.Rounds.Count, Is.GreaterThanOrEqualTo(2),
                    "동점-target 라운드에서 매치가 끝나면 안 됨(속행해야 함)");
                Assert.That(match.Rounds[0].TeamATotal, Is.EqualTo(match.Rounds[0].TeamBTotal),
                    "라운드1 이 동점이었음을 확인");
                Assert.That(match.WinningTeam, Is.InRange(0, 1),
                    "결국 동점이 깨져 한 팀이 승리");
                return;
            }

            Assert.Fail("스캔 범위에서 동점 라운드1 을 찾지 못함 — 범위를 넓혀야 함");
        }

        /// <summary>RunMatch 의 라운드1 과 동일한 시딩으로 라운드1 결과를 재현한다.</summary>
        private static RoundResult FirstRoundResult(ulong masterSeed, System.Func<ulong, int, IAgent> factory)
        {
            var master = new Rng(masterSeed);
            ulong roundSeed = master.NextULong();
            var agents = new IAgent[4];
            for (int seat = 0; seat < 4; seat++)
                agents[seat] = factory(roundSeed, seat);
            return new GameDriver(agents).RunRound(GameEngine.NewRound(roundSeed)).Result;
        }
    }
}
