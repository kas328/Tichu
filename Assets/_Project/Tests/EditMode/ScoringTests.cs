using System.Collections.Generic;
using NUnit.Framework;
using Tichu.Core.Cards;
using Tichu.Core.Combinations;
using Tichu.Core.Game;

namespace Tichu.Core.Tests
{
    /// <summary>정산(ScoreCalculator) 검증: 원-투, 일반 종료 양도, 용 양도, 티츄 보너스.</summary>
    public class ScoringTests
    {
        // ── 헬퍼 ────────────────────────────────────────────────────────────────

        /// <summary>Scoring 페이즈 상태를 직접 구성한다.</summary>
        private static GameState ScoringState()
        {
            var s = new GameState { Phase = RoundPhase.Scoring, CurrentTrick = null };
            for (int i = 0; i < 4; i++)
                s.Seats[i] = new PlayerSeat { SeatIndex = i };
            return s;
        }

        /// <summary>지정한 소유자/점수/용여부로 완료된 트릭을 만든다.</summary>
        private static Trick MakeTrick(int ownerSeat, int points, bool wonByDragon = false)
        {
            return new Trick
            {
                TopOwnerSeat = ownerSeat,
                AccumulatedPoints = points,
                WonByDragon = wonByDragon
            };
        }

        private static List<Card> Hand(params Card[] cards) => new List<Card>(cards);

        // ── CASE A: 원-투 ─────────────────────────────────────────────────────────

        [Test]
        public void OneTwo_scores_200_0_ignoring_card_points()
        {
            // seats 0,2 (team A) 가 1,2번째 아웃. 트릭 점수가 상대편에 쌓여 있어도 무시.
            var s = ScoringState();
            s.Seats[0].IsOut = true; s.Seats[0].FinishOrder = 1;
            s.Seats[2].IsOut = true; s.Seats[2].FinishOrder = 2;
            s.Seats[1].IsOut = false;
            s.Seats[3].IsOut = false;
            // 상대(team B)가 많은 트릭 점수를 가지고 있어도 무시되어야.
            s.CompletedTricks.Add(MakeTrick(1, 60));
            s.CompletedTricks.Add(MakeTrick(3, 40));

            var r = ScoreCalculator.ScoreRound(s);

            Assert.That(r.TeamACardPoints, Is.EqualTo(200));
            Assert.That(r.TeamBCardPoints, Is.EqualTo(0));
            Assert.That(r.TeamATotal, Is.EqualTo(200));
            Assert.That(r.TeamBTotal, Is.EqualTo(0));
            Assert.That(s.Phase, Is.EqualTo(RoundPhase.RoundEnd));
        }

        // ── CASE B: 일반 종료 ──────────────────────────────────────────────────────

        [Test]
        public void Normal_end_card_points_sum_to_100()
        {
            // 일반 종료. 트릭 + 마지막 손패가 덱 전체 100점이 되도록 구성.
            var s = ScoringState();
            s.Seats[0].IsOut = true; s.Seats[0].FinishOrder = 1;
            s.Seats[1].IsOut = true; s.Seats[1].FinishOrder = 2;
            s.Seats[2].IsOut = true; s.Seats[2].FinishOrder = 3;
            s.Seats[3].IsOut = false; // 마지막 플레이어
            // 트릭 점수 합 = 90, 마지막 손패 10 → 총 100.
            s.CompletedTricks.Add(MakeTrick(0, 50));
            s.CompletedTricks.Add(MakeTrick(1, 25));
            s.CompletedTricks.Add(MakeTrick(2, 15));
            s.Seats[3].Hand.Add(Card.Normal(13, Suit.Jade)); // K = 10점, 마지막 손패

            var r = ScoreCalculator.ScoreRound(s);

            Assert.That(r.TeamACardPoints + r.TeamBCardPoints, Is.EqualTo(100),
                "card points must sum to 100");
        }

        [Test]
        public void Last_hand_goes_to_opponent_and_tricks_to_first_out()
        {
            // seat0 first-out (team A). 마지막 플레이어 = seat3 (team B).
            // seat3 가 트릭으로 딴 점수 → seat0(first out) 로 양도.
            // seat3 의 남은 손패 점수 → 상대팀(team A) 으로.
            var s = ScoringState();
            s.Seats[0].IsOut = true; s.Seats[0].FinishOrder = 1;
            s.Seats[1].IsOut = true; s.Seats[1].FinishOrder = 2;
            s.Seats[2].IsOut = true; s.Seats[2].FinishOrder = 3;
            s.Seats[3].IsOut = false; // 마지막 플레이어 (team B)
            // 트릭: seat3가 40점 트릭을 이김; seat1이 30점; seat0이 20점.
            s.CompletedTricks.Add(MakeTrick(3, 40)); // 마지막 플레이어가 딴 점수 → first-out(seat0)로
            s.CompletedTricks.Add(MakeTrick(1, 30)); // team B
            s.CompletedTricks.Add(MakeTrick(0, 20)); // team A
            // 마지막 손패: 10점 → 상대팀(team A)로.
            s.Seats[3].Hand.Add(Card.Normal(10, Suit.Jade)); // 10점

            var r = ScoreCalculator.ScoreRound(s);

            // 점수 분배 후:
            //  seat0: 20(자기 트릭) + 40(seat3 양도) = 60
            //  seat3: 0 (양도됨)
            //  seat1: 30
            //  마지막 손패 10 → team A
            // team A (seat0,2) = 60 + 10(hand) = 70
            // team B (seat1,3) = 30 + 0 = 30
            Assert.That(r.TeamACardPoints, Is.EqualTo(70));
            Assert.That(r.TeamBCardPoints, Is.EqualTo(30));
            Assert.That(r.TeamACardPoints + r.TeamBCardPoints, Is.EqualTo(100));
        }

        [Test]
        public void Dragon_trick_points_go_to_opponent_team()
        {
            // seat0(team A)가 용 단독으로 25점 트릭을 이김 → 점수는 상대팀(team B)로.
            var s = ScoringState();
            s.Seats[0].IsOut = true; s.Seats[0].FinishOrder = 1;
            s.Seats[1].IsOut = true; s.Seats[1].FinishOrder = 2;
            s.Seats[2].IsOut = true; s.Seats[2].FinishOrder = 3;
            s.Seats[3].IsOut = false; // 마지막 플레이어
            s.CompletedTricks.Add(MakeTrick(0, 25, wonByDragon: true)); // 용 트릭 → 상대팀으로
            s.CompletedTricks.Add(MakeTrick(1, 75)); // 나머지 점수
            // 마지막 손패 없음(0점). 합계 100.

            var r = ScoreCalculator.ScoreRound(s);

            // 용 트릭을 이긴 seat0(team A)의 점수는 상대팀(team B)으로 귀속.
            // seat1(team B) 본래 트릭 75 + 용 귀속 25 = team B = 100, team A = 0.
            Assert.That(r.TeamBCardPoints, Is.EqualTo(100));
            Assert.That(r.TeamACardPoints, Is.EqualTo(0));
        }

        // ── 티츄 / 큰 티츄 보너스 ───────────────────────────────────────────────────

        [Test]
        public void Grand_tichu_success_plus_200_and_failure_minus_200()
        {
            // seat0: 큰 티츄 & first-out → +200 (team A).
            // seat1: 큰 티츄 & first-out 아님 → -200 (team B).
            var s = ScoringState();
            s.Seats[0].IsOut = true; s.Seats[0].FinishOrder = 1; s.Seats[0].Call = TichuCall.GrandTichu;
            s.Seats[1].IsOut = true; s.Seats[1].FinishOrder = 2; s.Seats[1].Call = TichuCall.GrandTichu;
            s.Seats[2].IsOut = true; s.Seats[2].FinishOrder = 3;
            s.Seats[3].IsOut = false;
            // 카드 점수는 단순화: 모두 0(트릭 없음, 손패 없음).

            var r = ScoreCalculator.ScoreRound(s);

            Assert.That(r.TeamATichuDelta, Is.EqualTo(200), "seat0 grand tichu success");
            Assert.That(r.TeamBTichuDelta, Is.EqualTo(-200), "seat1 grand tichu failure");
        }

        [Test]
        public void Tichu_fails_if_partner_out_first()
        {
            // seat0 작은 티츄. 하지만 first-out은 파트너 seat2 → seat0 본인은 first가 아님 → 실패 -100.
            var s = ScoringState();
            s.Seats[2].IsOut = true; s.Seats[2].FinishOrder = 1; // 파트너가 먼저 아웃
            s.Seats[0].IsOut = true; s.Seats[0].FinishOrder = 2; s.Seats[0].Call = TichuCall.Tichu;
            // 원-투(0,2 파트너)지만 티츄 판정만 확인. (카드 200/0)

            var r = ScoreCalculator.ScoreRound(s);

            Assert.That(r.TeamATichuDelta, Is.EqualTo(-100),
                "tichu fails: caller (seat0) is not first-out even though partner is");
        }

        // ── 골든 케이스 (기획서 4.4 예시) ───────────────────────────────────────────

        [Test]
        public void Golden_normal_end_25_75_with_successful_tichu_yields_125_75()
        {
            // 일반 종료. 우리팀(A) 카드 25, 상대(B) 75. 우리팀이 성공한 작은 티츄 → 최종 125 / 75.
            var s = ScoringState();
            // seat0(team A) first-out & 작은 티츄 성공.
            s.Seats[0].IsOut = true; s.Seats[0].FinishOrder = 1; s.Seats[0].Call = TichuCall.Tichu;
            s.Seats[1].IsOut = true; s.Seats[1].FinishOrder = 2;
            s.Seats[3].IsOut = true; s.Seats[3].FinishOrder = 3;
            s.Seats[2].IsOut = false; // 마지막 플레이어 (team A)
            //  seat0(A) 트릭 10, seat2(A,마지막) 트릭 15 → 양도되면 seat0이 가짐.
            //  seat1(B) 트릭 75.
            s.CompletedTricks.Add(MakeTrick(0, 10));
            s.CompletedTricks.Add(MakeTrick(2, 15)); // 마지막 → seat0로 양도
            s.CompletedTricks.Add(MakeTrick(1, 75));
            // 마지막 손패 없음(0점). 트릭 합 100.
            // 양도 후: seat0 = 10 + 15 = 25, seat2 = 0 → team A = 25; team B = 75.

            var r = ScoreCalculator.ScoreRound(s);

            Assert.That(r.TeamACardPoints, Is.EqualTo(25));
            Assert.That(r.TeamBCardPoints, Is.EqualTo(75));
            Assert.That(r.TeamATichuDelta, Is.EqualTo(100), "successful small tichu");
            Assert.That(r.TeamATotal, Is.EqualTo(125));
            Assert.That(r.TeamBTotal, Is.EqualTo(75));
            // ScoreBoard 반영 확인.
            Assert.That(s.Scores.TeamA, Is.EqualTo(125));
            Assert.That(s.Scores.TeamB, Is.EqualTo(75));
            Assert.That(s.Scores.Rounds.Count, Is.EqualTo(1));
        }
    }
}
