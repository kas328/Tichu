using System;
using System.Collections.Generic;
using Tichu.Core.Combinations;
using Tichu.GameFlow;

namespace Tichu.Net
{
    /// <summary>
    /// 지금 서버가 누구에게 무엇을 묻고 있는지. 묻는 주체인 RemoteAgent 가 대기에
    /// 들어가는 순간 발행한다 — 드라이버의 내부 카운터와 RoundSetup(internal) 을
    /// 세션이 볼 수 없어 상태에서 역산할 수 없기 때문이다.
    ///
    /// LegalMoves 와 CanPass 는 대기 좌석에게만 보낸다. 둘 다 그 좌석의 손패와 공개
    /// 트릭 상태에서만 유도되므로 은닉 정보가 아니며, 클라이언트가 전체 GameState 를
    /// 복원하지 않아도 수를 고를 수 있게 해 준다.
    /// </summary>
    public sealed class Awaiting
    {
        public StepKind Kind { get; set; }
        public int Seat { get; set; }

        /// <summary>만료 시각. M1 에서는 항상 null(턴 시계는 M5).</summary>
        public DateTime? DeadlineUtc { get; set; }

        /// <summary>대기 좌석이 지금 낼 수 있는 조합 전체. 다른 좌석에게는 null.</summary>
        public IReadOnlyList<Combination>? LegalMoves { get; set; }

        /// <summary>대기 좌석이 지금 패스할 수 있는가. 다른 좌석에게는 false.</summary>
        public bool CanPass { get; set; }

        /// <summary>다른 좌석에게 보낼 축약판 — 차례 표시에 필요한 것만 남긴다.</summary>
        public Awaiting Redacted() =>
            new Awaiting { Kind = Kind, Seat = Seat, DeadlineUtc = DeadlineUtc };
    }
}
