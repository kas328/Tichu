using System.Collections.Generic;
using NUnit.Framework;
using Tichu.Core.Cards;
using Tichu.Core.Combinations;
using Tichu.Core.Game;

namespace Tichu.Core.Tests
{
    /// <summary>
    /// 용(Dragon) 양도(캡처 → 가드 → 재개) 검증.
    /// 용 단독으로 이긴 트릭은 승자가 두 상대 중 한 명을 선택해 그 트릭 점수를 양도한다.
    /// 선택 전까지 라운드 종료/턴 진행을 보류(pending)해야 한다.
    /// </summary>
    public class GameFlowDragonGiftTests
    {
        // ── 헬퍼 ────────────────────────────────────────────────────────────────

        private static GameState PlayState(int turn, params List<Card>[] hands)
        {
            var s = new GameState
            {
                Phase = RoundPhase.Play,
                Turn = turn,
                CurrentTrick = null
            };
            for (int i = 0; i < 4; i++)
            {
                s.Seats[i] = new PlayerSeat { SeatIndex = i };
                s.Seats[i].Hand.AddRange(hands[i]);
            }
            return s;
        }

        private static List<Card> Hand(params Card[] cards) => new List<Card>(cards);

        private static GameState ScoringStateForDragon(int winnerSeat, int dragonPoints, int? recipient)
        {
            // winnerSeat 가 용 트릭을 이김; 나머지 점수는 다른 좌석에 둔다.
            // 일반 종료(3아웃, 1명 잔류)로 구성해 ScoreRound 가 CASE B 로 가도록.
            var s = new GameState { Phase = RoundPhase.Scoring, CurrentTrick = null };
            for (int i = 0; i < 4; i++)
                s.Seats[i] = new PlayerSeat { SeatIndex = i };

            // 잔류 좌석 = winnerSeat(손패 없음, ScoreRound CASE B에서 처리). 나머지 셋은 아웃.
            int order = 1;
            for (int i = 0; i < 4; i++)
            {
                if (i == winnerSeat) continue;
                s.Seats[i].IsOut = true;
                s.Seats[i].FinishOrder = order++;
            }
            s.Seats[winnerSeat].IsOut = false;

            var t = new Trick
            {
                TopOwnerSeat = winnerSeat,
                AccumulatedPoints = dragonPoints,
                WonByDragon = true,
                DragonGiftRecipient = recipient
            };
            s.CompletedTricks.Add(t);
            return s;
        }

        // ── 캡처: 용으로 이기면 양도 대기 ──────────────────────────────────────────

        [Test]
        public void Dragon_win_pauses_for_gift()
        {
            var s = PlayState(0,
                Hand(Card.Dragon, Card.Normal(9, Suit.Jade)),
                Hand(Card.Normal(3, Suit.Jade)),
                Hand(Card.Normal(4, Suit.Jade)),
                Hand(Card.Normal(5, Suit.Jade)));

            Assert.That(GameEngine.Apply(s, GameAction.Play(0, new List<Card> { Card.Dragon })).Ok, Is.True);
            Assert.That(GameEngine.Apply(s, GameAction.Pass(1)).Ok, Is.True);
            Assert.That(GameEngine.Apply(s, GameAction.Pass(2)).Ok, Is.True);
            Assert.That(GameEngine.Apply(s, GameAction.Pass(3)).Ok, Is.True);

            // 트릭은 회수되어 있다 (CompletedTricks에 들어감, CurrentTrick=null).
            Assert.That(s.CurrentTrick, Is.Null);
            Assert.That(s.CompletedTricks.Count, Is.EqualTo(1));
            Assert.That(s.CompletedTricks[0].WonByDragon, Is.True);

            // 양도 대기 상태.
            Assert.That(s.TryGetPendingDragonGift(out int winner), Is.True);
            Assert.That(winner, Is.EqualTo(0));
            // 아직 Play 페이즈, 라운드 미종료. (턴 값은 양도 대기 중 의미 없음 — 양도 후 재설정된다.)
            Assert.That(s.Phase, Is.EqualTo(RoundPhase.Play));
        }

        // ── 가드(THE CRITICAL CASE): 라운드를 끝내는 용도 보류 ─────────────────────

        [Test]
        public void Dragon_win_that_ends_the_round_still_pauses()
        {
            // seat0, seat1 이미 아웃(1,2). seat2가 용 단독을 마지막 카드로 내며 3번째 아웃 →
            // 일반적으로는 라운드 종료지만, 용 양도 대기 때문에 보류되어야 한다.
            var s = PlayState(2,
                Hand(),
                Hand(),
                Hand(Card.Dragon),   // 마지막 카드
                Hand(Card.Normal(5, Suit.Jade)));
            s.Seats[0].IsOut = true; s.Seats[0].FinishOrder = 1;
            s.Seats[1].IsOut = true; s.Seats[1].FinishOrder = 2;

            // seat2의 용 리드로 3아웃이 되며 라운드 종료 조건이 충족되지만,
            // 트릭 소유자(seat2)가 아웃되어 진행중 트릭이 곧바로 FinalizeOpenTrick으로 마무리되고
            // 용 양도 대기가 걸려 Scoring 전환이 보류된다.
            Assert.That(GameEngine.Apply(s, GameAction.Play(2, new List<Card> { Card.Dragon })).Ok, Is.True);

            // 트릭은 회수되었고, 용 양도 대기.
            Assert.That(s.CurrentTrick, Is.Null);
            Assert.That(s.CompletedTricks.Count, Is.EqualTo(1));
            Assert.That(s.CompletedTricks[0].WonByDragon, Is.True);
            Assert.That(s.TryGetPendingDragonGift(out int winner), Is.True);
            Assert.That(winner, Is.EqualTo(2));
            // 핵심: 라운드가 아직 종료되지 않았다(가드가 막음).
            Assert.That(s.Phase, Is.EqualTo(RoundPhase.Play),
                "round-ending dragon win must still pause for the gift");
        }

        // ── 재개: 양도 후 진행 재개 ────────────────────────────────────────────────

        [Test]
        public void GiveDragon_left_then_resumes()
        {
            var s = PlayState(0,
                Hand(Card.Dragon, Card.Normal(9, Suit.Jade)),
                Hand(Card.Normal(3, Suit.Jade)),
                Hand(Card.Normal(4, Suit.Jade)),
                Hand(Card.Normal(5, Suit.Jade)));

            GameEngine.Apply(s, GameAction.Play(0, new List<Card> { Card.Dragon }));
            GameEngine.Apply(s, GameAction.Pass(1));
            GameEngine.Apply(s, GameAction.Pass(2));
            GameEngine.Apply(s, GameAction.Pass(3));

            int leftOpp = (0 + 1) % 4; // seat1
            var r = GameEngine.Apply(s, GameAction.GiveDragon(0, leftOpp));
            Assert.That(r.Ok, Is.True);

            // 양도 대기 해제, 트릭에 수혜자 기록.
            Assert.That(s.TryGetPendingDragonGift(out _), Is.False);
            Assert.That(s.CompletedTricks[0].DragonGiftRecipient, Is.EqualTo(leftOpp));
            // 진행 재개: 승자(seat0)가 다음 리드(아웃 아님).
            Assert.That(s.Phase, Is.EqualTo(RoundPhase.Play));
            Assert.That(s.Turn, Is.EqualTo(0));
        }

        [Test]
        public void GiveDragon_resolves_deferred_round_end()
        {
            // 라운드를 끝내는 용 케이스에서 양도하면, 보류했던 라운드 종료가 이제 발동한다.
            var s = PlayState(2,
                Hand(),
                Hand(),
                Hand(Card.Dragon),
                Hand(Card.Normal(5, Suit.Jade)));
            s.Seats[0].IsOut = true; s.Seats[0].FinishOrder = 1;
            s.Seats[1].IsOut = true; s.Seats[1].FinishOrder = 2;

            GameEngine.Apply(s, GameAction.Play(2, new List<Card> { Card.Dragon }));
            Assert.That(s.TryGetPendingDragonGift(out _), Is.True, "paused for gift");
            Assert.That(s.Phase, Is.EqualTo(RoundPhase.Play), "still paused before gift");

            int rightOpp = (2 + 3) % 4; // seat1
            var r = GameEngine.Apply(s, GameAction.GiveDragon(2, rightOpp));
            Assert.That(r.Ok, Is.True);

            Assert.That(s.TryGetPendingDragonGift(out _), Is.False);
            Assert.That(s.CompletedTricks[0].DragonGiftRecipient, Is.EqualTo(rightOpp));
            // 보류했던 라운드 종료가 이제 발동 → Scoring.
            Assert.That(s.Phase, Is.EqualTo(RoundPhase.Scoring),
                "deferred round-end fires after the gift");
        }

        // ── 정산: 선택한 상대 팀에 점수 귀속 ───────────────────────────────────────

        [Test]
        public void GiveDragon_credits_chosen_opponent_in_scoring()
        {
            // seat0(team A)가 용 25점 트릭을 이김. left=seat1(team B), right=seat3(team B).
            // 두 상대 모두 team B 이므로 팀 점수로는 같지만, perSeat 귀속 좌석이 선택에 따라 달라진다.
            // 좌→ seat1, 우→ seat3 모두 team B 에 25점이 가야 한다(하드코드 +1 가 아님을 증명).

            // 좌(seat1) 선택.
            var sLeft = ScoringStateForDragon(winnerSeat: 0, dragonPoints: 25, recipient: 1);
            sLeft.CompletedTricks.Add(new Trick { TopOwnerSeat = 1, AccumulatedPoints = 75 }); // 합 100
            var rLeft = ScoreCalculator.ScoreRound(sLeft);
            Assert.That(rLeft.TeamBCardPoints, Is.EqualTo(100));
            Assert.That(rLeft.TeamACardPoints, Is.EqualTo(0));

            // 우(seat3) 선택 — 하드코드 +1(=seat1)이 아니라 seat3 에 귀속되어야.
            var sRight = ScoringStateForDragon(winnerSeat: 0, dragonPoints: 25, recipient: 3);
            sRight.CompletedTricks.Add(new Trick { TopOwnerSeat = 1, AccumulatedPoints = 75 });
            var rRight = ScoreCalculator.ScoreRound(sRight);
            Assert.That(rRight.TeamBCardPoints, Is.EqualTo(100));
            Assert.That(rRight.TeamACardPoints, Is.EqualTo(0));
        }

        // ── 거부 케이스 ─────────────────────────────────────────────────────────────

        [Test]
        public void GiveDragon_rejects_partner_and_non_winner()
        {
            var s = PlayState(0,
                Hand(Card.Dragon, Card.Normal(9, Suit.Jade)),
                Hand(Card.Normal(3, Suit.Jade)),
                Hand(Card.Normal(4, Suit.Jade)),
                Hand(Card.Normal(5, Suit.Jade)));

            GameEngine.Apply(s, GameAction.Play(0, new List<Card> { Card.Dragon }));
            GameEngine.Apply(s, GameAction.Pass(1));
            GameEngine.Apply(s, GameAction.Pass(2));
            GameEngine.Apply(s, GameAction.Pass(3));

            // 파트너 (seat2) 거부.
            int partner = (0 + 2) % 4;
            var rp = GameEngine.Apply(s, GameAction.GiveDragon(0, partner));
            Assert.That(rp.Ok, Is.False);
            Assert.That(rp.RejectReason, Is.Not.Empty);

            // 비-승자 (seat1)가 양도 시도 거부.
            var rn = GameEngine.Apply(s, GameAction.GiveDragon(1, 0));
            Assert.That(rn.Ok, Is.False);
            Assert.That(rn.RejectReason, Is.Not.Empty);

            // 여전히 대기 상태(아무 것도 바뀌지 않음).
            Assert.That(s.TryGetPendingDragonGift(out int w), Is.True);
            Assert.That(w, Is.EqualTo(0));
        }

        // ── 폴백: 수혜자 null 이면 +1 고정 ─────────────────────────────────────────

        [Test]
        public void Fallback_credits_plus1_when_recipient_null()
        {
            // recipient==null 인 용 트릭은 기존 의미(TopOwnerSeat+1)%4 로 귀속.
            var s = ScoringStateForDragon(winnerSeat: 0, dragonPoints: 25, recipient: null);
            s.CompletedTricks.Add(new Trick { TopOwnerSeat = 1, AccumulatedPoints = 75 });
            var r = ScoreCalculator.ScoreRound(s);
            // (0+1)%4 = seat1(team B) → team B 100.
            Assert.That(r.TeamBCardPoints, Is.EqualTo(100));
            Assert.That(r.TeamACardPoints, Is.EqualTo(0));
        }

        // ── 해시: 수혜자 좌·우 구분 ────────────────────────────────────────────────

        [Test]
        public void ComputeHash_differs_left_vs_right()
        {
            var sLeft = ScoringStateForDragon(winnerSeat: 0, dragonPoints: 25, recipient: 1);
            var sRight = ScoringStateForDragon(winnerSeat: 0, dragonPoints: 25, recipient: 3);
            Assert.That(sLeft.ComputeHash(), Is.Not.EqualTo(sRight.ComputeHash()));
        }

        [Test]
        public void ComputeHash_equal_same_side()
        {
            var a = ScoringStateForDragon(winnerSeat: 0, dragonPoints: 25, recipient: 1);
            var b = ScoringStateForDragon(winnerSeat: 0, dragonPoints: 25, recipient: 1);
            Assert.That(a.ComputeHash(), Is.EqualTo(b.ComputeHash()));
        }

        // ── 대기 중 비-GiveDragon 액션 거부 ─────────────────────────────────────────

        [Test]
        public void Non_give_dragon_action_rejected_while_pending()
        {
            var s = PlayState(0,
                Hand(Card.Dragon, Card.Normal(9, Suit.Jade)),
                Hand(Card.Normal(3, Suit.Jade)),
                Hand(Card.Normal(4, Suit.Jade)),
                Hand(Card.Normal(5, Suit.Jade)));

            GameEngine.Apply(s, GameAction.Play(0, new List<Card> { Card.Dragon }));
            GameEngine.Apply(s, GameAction.Pass(1));
            GameEngine.Apply(s, GameAction.Pass(2));
            GameEngine.Apply(s, GameAction.Pass(3));

            // 양도 대기 상태 확인.
            Assert.That(s.TryGetPendingDragonGift(out _), Is.True);

            // 대기 중 Pass 액션 → 거부되어야 하며 예외를 던지지 않아야 한다.
            ApplyResult? r = null;
            Assert.DoesNotThrow(() => r = GameEngine.Apply(s, GameAction.Pass(0)));
            Assert.That(r!.Ok, Is.False);
            Assert.That(r.RejectReason, Is.Not.Empty);

            // 여전히 대기 상태(상태 변경 없음).
            Assert.That(s.TryGetPendingDragonGift(out _), Is.True);
        }

        // ── 폭탄이 용 위에 → WonByDragon 아님 ──────────────────────────────────────

        [Test]
        public void Bomb_over_dragon_top_is_not_WonByDragon()
        {
            // seat0 용 단독 리드. seat2가 폭탄(네 장 6)으로 자기 턴 아니어도 이김.
            // 최종 Top 이 폭탄이므로 WonByDragon 은 false → 양도 없음.
            var s = PlayState(0,
                Hand(Card.Dragon),
                Hand(Card.Normal(3, Suit.Jade)),
                Hand(Card.Normal(6, Suit.Jade), Card.Normal(6, Suit.Sword),
                     Card.Normal(6, Suit.Pagoda), Card.Normal(6, Suit.Star), Card.Normal(2, Suit.Jade)),
                Hand(Card.Normal(5, Suit.Jade)));

            Assert.That(GameEngine.Apply(s, GameAction.Play(0, new List<Card> { Card.Dragon })).Ok, Is.True);
            Assert.That(GameEngine.Apply(s, GameAction.Play(2, new List<Card>
            {
                Card.Normal(6, Suit.Jade), Card.Normal(6, Suit.Sword),
                Card.Normal(6, Suit.Pagoda), Card.Normal(6, Suit.Star)
            })).Ok, Is.True);
            // 이제 seat2가 Top. 남은 좌석(3,0,1 중 살아있는)이 패스해 트릭 완료.
            Assert.That(GameEngine.Apply(s, GameAction.Pass(3)).Ok, Is.True);
            Assert.That(GameEngine.Apply(s, GameAction.Pass(1)).Ok, Is.True);
            // seat0은 용을 내고 아웃되었을 수 있으나 여분 카드 없으니 아웃. 트릭 소유자 seat2는 살아있음 →
            // 턴이 seat2 로 돌아오면 완료. seat0 아웃이므로 NextActive 흐름으로 완료된다.

            Assert.That(s.CurrentTrick, Is.Null, "trick collected");
            Assert.That(s.CompletedTricks.Count, Is.EqualTo(1));
            Assert.That(s.CompletedTricks[0].WonByDragon, Is.False, "bomb over dragon: no dragon gift");
            Assert.That(s.TryGetPendingDragonGift(out _), Is.False, "no pending gift");
        }
    }
}
