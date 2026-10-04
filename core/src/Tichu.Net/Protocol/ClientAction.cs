using System.Collections.Generic;
using Tichu.Core.Cards;

namespace Tichu.Net
{
    /// <summary>클라이언트 → 서버 액션 종류. Tichu.Core.Game.GameActionKind 와 1:1 대응한다.</summary>
    public enum ClientActionKind
    {
        CallGrandTichu,
        DeclineGrandTichu,
        Exchange,
        CallTichu,
        Play,
        Pass,
        GiveDragon
    }

    /// <summary>
    /// 클라이언트가 서버에 보내는 요청. 서버는 이걸 그대로 적용하지 않고 합법성을
    /// 재검증한 뒤 Tichu.Core.Game.GameAction 으로 변환해 적용한다.
    ///
    /// ActionSeq 는 좌석별 단조 증가 번호다. 중복·역행은 상태를 바꾸지 않고 거부한다
    /// (리플레이 방지). 중복 수신 시 현재 상태를 재전송하는 처리는 M2 몫이다.
    /// </summary>
    public sealed class ClientAction
    {
        public ClientActionKind Kind { get; set; }
        public int Seat { get; set; }
        public long ActionSeq { get; set; }

        /// <summary>Play: 제출 카드.</summary>
        public List<Card>? Cards { get; set; }

        /// <summary>
        /// Play: 클라이언트가 의도한 조합의 ×2 스케일 Rank. 0 = 미지정.
        /// 봉황이 스트레이트의 어느 칸을 메우는지는 카드 집합만으로 결정되지 않으므로
        /// (3,4,5,6,봉황 → 봉황을 2로 또는 7로) 이 값으로 해소한다.
        /// </summary>
        public int Rank { get; set; }

        /// <summary>Play: 마작 리드 시 지정하는 소원 랭크.</summary>
        public int? Wish { get; set; }

        /// <summary>Exchange: 왼쪽/파트너/오른쪽에 보낼 카드.</summary>
        public Card ToLeft { get; set; }
        public Card ToPartner { get; set; }
        public Card ToRight { get; set; }

        /// <summary>GiveDragon: 트릭 점수를 양도받을 상대 좌석.</summary>
        public int RecipientSeat { get; set; }

        public static ClientAction CallGrandTichu(int seat, long actionSeq) =>
            new ClientAction { Kind = ClientActionKind.CallGrandTichu, Seat = seat, ActionSeq = actionSeq };

        public static ClientAction DeclineGrandTichu(int seat, long actionSeq) =>
            new ClientAction { Kind = ClientActionKind.DeclineGrandTichu, Seat = seat, ActionSeq = actionSeq };

        public static ClientAction Exchange(int seat, long actionSeq, Card toLeft, Card toPartner, Card toRight) =>
            new ClientAction
            {
                Kind = ClientActionKind.Exchange, Seat = seat, ActionSeq = actionSeq,
                ToLeft = toLeft, ToPartner = toPartner, ToRight = toRight
            };

        public static ClientAction CallTichu(int seat, long actionSeq) =>
            new ClientAction { Kind = ClientActionKind.CallTichu, Seat = seat, ActionSeq = actionSeq };

        public static ClientAction Play(int seat, long actionSeq, List<Card> cards, int rank, int? wish) =>
            new ClientAction
            {
                Kind = ClientActionKind.Play, Seat = seat, ActionSeq = actionSeq,
                Cards = cards, Rank = rank, Wish = wish
            };

        public static ClientAction Pass(int seat, long actionSeq) =>
            new ClientAction { Kind = ClientActionKind.Pass, Seat = seat, ActionSeq = actionSeq };

        public static ClientAction GiveDragon(int seat, long actionSeq, int recipientSeat) =>
            new ClientAction
            {
                Kind = ClientActionKind.GiveDragon, Seat = seat, ActionSeq = actionSeq,
                RecipientSeat = recipientSeat
            };
    }
}
