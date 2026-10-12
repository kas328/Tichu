using System.Collections.Generic;
using Tichu.Core.Cards;
using Tichu.Core.Combinations;
using Tichu.Core.Game;

namespace Tichu.Net
{
    /// <summary>회수된 트릭의 공개 요약. 카드 데이터는 WonCards 로 이미 공개된다.</summary>
    public sealed class TrickSummary
    {
        public int TopOwnerSeat { get; set; }
        public int AccumulatedPoints { get; set; }
        public bool WonByDragon { get; set; }
        public int? DragonGiftRecipient { get; set; }
    }

    /// <summary>트릭 내 한 번의 제출. IsPass 면 Cards 는 비어 있다.</summary>
    public sealed class PlayView
    {
        public int Seat { get; set; }
        public bool IsPass { get; set; }
        public bool IsBombInterrupt { get; set; }
        public List<Card> Cards { get; set; } = new List<Card>();
    }

    /// <summary>진행 중 트릭의 공개 상태. 전원에게 동일하다.</summary>
    public sealed class TrickView
    {
        public CombinationType LeadType { get; set; }
        public int LeadLength { get; set; }

        /// <summary>
        /// 현재 격파 대상 조합. 깔린 카드가 없으면 null.
        /// 불변 값이라 그대로 싣는다 — 클라이언트가 정렬·비교에 쓰려면 Type·Rank·Cards 가
        /// 한 덩어리로 필요하고, 평탄화해 보내면 되조립 책임이 클라이언트로 번진다.
        /// </summary>
        public Combination? Top { get; set; }

        public int TopOwnerSeat { get; set; }
        public int AccumulatedPoints { get; set; }
        public List<PlayView> History { get; set; } = new List<PlayView>();
    }

    /// <summary>
    /// 한 좌석에게 보내는 게임 상태. 기획서 7.5 시야 마스킹 표에 1:1 대응한다.
    /// 자기 손패만 전체 공개하고 상대는 장수만 싣는다 — 타 좌석 카드 데이터는
    /// 가린 형태로도 포함하지 않는다. GameState 를 참조로 들고 있지 않은 값 스냅샷이다.
    /// </summary>
    public sealed class PlayerView
    {
        /// <summary>이 뷰의 주인 좌석.</summary>
        public int Viewer { get; set; }

        public RoundPhase Phase { get; set; }
        public int Turn { get; set; }
        public int? Wish { get; set; }

        /// <summary>용 양도를 지정해야 하는 좌석. 대기 중이 아니면 null.</summary>
        public int? DragonGiftWinner { get; set; }

        /// <summary>자기 손패 — 전체.</summary>
        public List<Card> MyHand { get; set; } = new List<Card>();

        /// <summary>전 좌석 손패 장수. 상대에 대해 공개되는 유일한 손패 정보다.</summary>
        public int[] HandCounts { get; set; } = new int[4];

        public TichuCall[] Calls { get; set; } = new TichuCall[4];
        public bool[] IsOut { get; set; } = new bool[4];

        /// <summary>좌석별 자리 비움(AI 대행) 여부(M5).</summary>
        public bool[] Away { get; set; } = new bool[4];
        public int[] FinishOrder { get; set; } = new int[4];

        /// <summary>
        /// 큰 티츄·교환 단계의 공유 제한 시간 중 남은 초(단계 시작부터 전원 같은 마감). 그 단계가 아니거나
        /// 시간 제한이 없으면 null.
        /// </summary>
        public int? PhaseSecondsLeft { get; set; }

        /// <summary>전 좌석 획득 더미 — 이미 공개된 카드라 전체를 싣는다(카드 카운팅 근거).</summary>
        public List<Card>[] WonCards { get; set; } = new List<Card>[4];

        public TrickView? CurrentTrick { get; set; }
        public List<TrickSummary> CompletedTricks { get; set; } = new List<TrickSummary>();

        public int TeamAScore { get; set; }
        public int TeamBScore { get; set; }
        public int TargetScore { get; set; }

        /// <summary>서버가 지금 무엇을 기다리는지. 기다리는 중이 아니면 null.</summary>
        public Awaiting? Awaiting { get; set; }
    }
}
