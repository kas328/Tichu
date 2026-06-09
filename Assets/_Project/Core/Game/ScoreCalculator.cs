#nullable enable
namespace Tichu.Core.Game
{
    /// <summary>
    /// 라운드 종료 후 점수 정산. 기획서 §4.4 / §11.6.
    /// Phase==Scoring 일 때 ScoreRound 호출 → 카드 점수/티츄 보너스 산출,
    /// ScoreBoard 갱신, Phase=RoundEnd 전환.
    /// </summary>
    public static class ScoreCalculator
    {
        private const int SeatCount = 4;
        private const int TichuPoints = 100;
        private const int GrandTichuPoints = 200;
        private const int OneTwoBonus = 200;

        /// <summary>라운드 점수를 계산해 RoundResult를 만들고 ScoreBoard에 반영한다.</summary>
        public static RoundResult ScoreRound(GameState s)
        {
            int firstOutSeat = FindFirstOut(s);
            bool isOneTwo = IsOneTwo(s, firstOutSeat);

            int teamACard, teamBCard;
            if (isOneTwo)
            {
                // CASE A — 원-투: 1,2번째 아웃이 파트너. 그 팀 200, 상대 0. 실제 카드 점수 무시.
                int firstTeam = Seating.TeamOf(firstOutSeat);
                teamACard = firstTeam == 0 ? OneTwoBonus : 0;
                teamBCard = firstTeam == 1 ? OneTwoBonus : 0;
            }
            else
            {
                // CASE B — 일반 종료(3아웃, 1명 잔류).
                // 불변식: 실제 한 판이면 teamACard + teamBCard == 100 (덱 전체 카드 점수).
                // 단위 테스트는 부분 상태를 쓸 수 있으므로 여기서 강제하지 않고 테스트로 검증한다.
                ComputeNormalCardPoints(s, firstOutSeat, out teamACard, out teamBCard);
            }

            // 티츄 / 큰 티츄 보너스 (두 케이스 공통).
            int teamATichu = 0, teamBTichu = 0;
            for (int seat = 0; seat < SeatCount; seat++)
            {
                int delta = TichuDelta(s.Seats[seat]);
                if (delta == 0) continue;
                if (Seating.TeamOf(seat) == 0) teamATichu += delta;
                else teamBTichu += delta;
            }

            var result = new RoundResult
            {
                TeamACardPoints = teamACard,
                TeamBCardPoints = teamBCard,
                TeamATichuDelta = teamATichu,
                TeamBTichuDelta = teamBTichu,
                TeamATotal = teamACard + teamATichu,
                TeamBTotal = teamBCard + teamBTichu
            };

            s.Scores.TeamA += result.TeamATotal;
            s.Scores.TeamB += result.TeamBTotal;
            s.Scores.Rounds.Add(result);
            s.Phase = RoundPhase.RoundEnd;

            return result;
        }

        /// <summary>CASE B 카드 점수: 트릭 귀속 + 마지막 손패/트릭 양도.</summary>
        private static void ComputeNormalCardPoints(GameState s, int firstOutSeat, out int teamACard, out int teamBCard)
        {
            // 좌석별 트릭 점수 귀속.
            var perSeat = new int[SeatCount];
            foreach (var t in s.CompletedTricks)
            {
                // 용 트릭: (좌석+1)은 항상 홀짝이 바뀌어 TeamOf 기준 상대팀이 된다.
                // 실제 티츄는 패배 측이 수혜 상대를 지정하지만, GiveToOpponent 액션 도입 전까지 +1 고정(두 상대 중 한 좌석에 귀속).
                int credited = t.WonByDragon
                    ? (t.TopOwnerSeat + 1) % SeatCount
                    : t.TopOwnerSeat;
                perSeat[credited] += t.AccumulatedPoints;
            }

            int lastSeat = FindLastPlayer(s);

            // 양도 1: 마지막 플레이어가 딴 트릭 점수 → first-out 좌석으로.
            perSeat[firstOutSeat] += perSeat[lastSeat];
            perSeat[lastSeat] = 0;

            // 양도 2: 마지막 플레이어의 남은 손패 점수 → 상대팀(마지막 좌석의 반대팀)으로.
            int lastHandPoints = HandPoints(s.Seats[lastSeat]);

            teamACard = perSeat[0] + perSeat[2];
            teamBCard = perSeat[1] + perSeat[3];
            if (Seating.TeamOf(lastSeat) == 0) teamBCard += lastHandPoints;
            else teamACard += lastHandPoints;
        }

        private static int FindFirstOut(GameState s)
        {
            for (int i = 0; i < SeatCount; i++)
                if (s.Seats[i].FinishOrder == 1) return i;
            throw new System.InvalidOperationException("no first-out seat (FinishOrder==1)");
        }

        /// <summary>1,2번째 아웃이 파트너인가(원-투).</summary>
        private static bool IsOneTwo(GameState s, int firstOutSeat)
        {
            int partner = Seating.Partner(firstOutSeat);
            return s.Seats[partner].FinishOrder == 2;
        }

        /// <summary>일반 종료에서 유일하게 아웃이 아닌 좌석.</summary>
        private static int FindLastPlayer(GameState s)
        {
            for (int i = 0; i < SeatCount; i++)
                if (!s.Seats[i].IsOut) return i;
            throw new System.InvalidOperationException("no remaining (last) player in normal end");
        }

        private static int HandPoints(PlayerSeat ps)
        {
            int sum = 0;
            foreach (var c in ps.Hand) sum += c.Points;
            return sum;
        }

        /// <summary>해당 좌석의 티츄/큰 티츄 점수 변동. 성공 조건: 그 좌석이 first-out(FinishOrder==1).</summary>
        private static int TichuDelta(PlayerSeat ps)
        {
            if (ps.Call == TichuCall.None) return 0;
            bool success = ps.FinishOrder == 1;
            int magnitude = ps.Call == TichuCall.GrandTichu ? GrandTichuPoints : TichuPoints;
            return success ? magnitude : -magnitude;
        }
    }
}
