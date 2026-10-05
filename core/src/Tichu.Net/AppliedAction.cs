using System.Collections.Generic;
using Tichu.Core.Cards;
using Tichu.Core.Game;

namespace Tichu.Net
{
    /// <summary>
    /// StateUpdate 를 유발한 액션의 공개 요약. 뷰 두 장을 비교해 역산할 수 없는
    /// "무슨 일이 일어났는지" 를 싣는다(패스는 상태를 거의 바꾸지 않고, 같은 카드
    /// 집합이 여러 경로로 나올 수 있다). 로그·효과음·연출이 이걸 먹는다.
    ///
    /// 교환은 카드를 싣지 않는다 — 좌석별 마스킹(기획서 7.5)을 뷰에서 지키면서
    /// 액션 요약으로 우회해 뚫으면 안 된다. 교환이 일어났다는 사실과 좌석만 알린다.
    /// </summary>
    public sealed class AppliedAction
    {
        public GameActionKind Kind { get; set; }
        public int Seat { get; set; }

        /// <summary>Play: 제출된 카드. 그 외 null.</summary>
        public List<Card>? Cards { get; set; }

        /// <summary>GiveDragon: 트릭을 양도받은 좌석. 그 외 -1.</summary>
        public int RecipientSeat { get; set; } = -1;

        /// <summary>권위 액션에서 공개 가능한 부분만 복사한다.</summary>
        public static AppliedAction From(GameAction a)
        {
            var r = new AppliedAction { Kind = a.Kind, Seat = a.Seat, RecipientSeat = -1 };
            switch (a.Kind)
            {
                case GameActionKind.Play:
                    // 값 사본 — 이후 엔진 변경이 이미 보낸 요약에 새지 않는다.
                    r.Cards = a.Cards == null ? null : new List<Card>(a.Cards);
                    break;
                case GameActionKind.GiveDragon:
                    r.RecipientSeat = a.RecipientSeat;
                    break;
            }
            return r;
        }
    }
}
