using System.Collections.Generic;
using Tichu.Core.Game;

namespace Tichu.GameFlow
{
    /// <summary>다음 필요한 액션 종류.</summary>
    public enum StepKind { GrandTichu, Exchange, Play, DragonGift, Scoring }

    /// <summary>드라이버가 처리해야 할 다음 단계.</summary>
    public readonly struct NextStep
    {
        /// <summary>단계 종류.</summary>
        public readonly StepKind Kind;

        /// <summary>대상 좌석. 페이즈 전체 처리일 때 -1.</summary>
        public readonly int Seat;

        public NextStep(StepKind kind, int seat)
        {
            Kind = kind;
            Seat = seat;
        }
    }

    /// <summary>GameState 를 읽어 드라이버가 다음에 할 일을 알려주는 순수 조회 클래스.</summary>
    public static class FlowQuery
    {
        /// <summary>현재 상태에서 드라이버가 처리해야 할 다음 단계를 반환한다.</summary>
        public static NextStep Next(GameState s)
        {
            switch (s.Phase)
            {
                case Core.Game.RoundPhase.GrandTichuDecision:
                    return new NextStep(StepKind.GrandTichu, -1);

                case Core.Game.RoundPhase.Exchange:
                    return new NextStep(StepKind.Exchange, -1);

                case Core.Game.RoundPhase.Play:
                    if (PendingDragonGift(s, out int winner))
                        return new NextStep(StepKind.DragonGift, winner);
                    return new NextStep(StepKind.Play, s.Turn);

                default:
                    // Scoring, RoundEnd, Deal8, Deal6 등 세팅 과도기
                    return new NextStep(StepKind.Scoring, -1);
            }
        }

        /// <summary>용 양도 대기 중이면 true와 승자 좌석을 반환한다.</summary>
        public static bool PendingDragonGift(GameState s, out int winner)
        {
            return s.TryGetPendingDragonGift(out winner);
        }

        /// <summary>
        /// 현재 트릭에 폭탄을 낼 수 있는 좌석 목록을 시계 방향 순서로 반환한다.
        /// 순서: (Turn+1)%4, (Turn+2)%4, (Turn+3)%4 — Turn 자신 및 아웃 좌석 제외.
        /// CurrentTrick 이 null(리드 상황)이면 빈 목록을 반환한다.
        /// </summary>
        public static IReadOnlyList<int> SeatsWithLegalBomb(GameState s)
        {
            if (s.CurrentTrick == null)
                return new List<int>(0);

            var result = new List<int>();
            for (int offset = 1; offset <= 3; offset++)
            {
                int seat = (s.Turn + offset) % 4;
                if (s.Seats[seat].IsOut) continue;

                var moves = LegalMoveGenerator.LegalMoves(s, seat);
                for (int i = 0; i < moves.Count; i++)
                {
                    if (moves[i].IsBomb)
                    {
                        result.Add(seat);
                        break;
                    }
                }
            }
            return result;
        }
    }
}
