using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Tichu.Presentation.Views
{
    /// <summary>
    /// 플레이 연출 훅(진실/연출 분리). 렌더 시점에 호출되며 절대 블로킹하지 않는다.
    /// 진실은 ApplySnapshot(R3 즉시 갱신)이 담당 — 이 훅은 시각 효과만. onApply=null 경로에선 렌더가
    /// 안 돌아 호출되지 않으므로 오라클 비트동일성에 닿지 않는다.
    /// </summary>
    public interface IPlayAnimator
    {
        /// <summary>트릭 중앙에 새 카드가 깔렸을 때(렌더된 칩들). fastForward면 즉시 스냅.</summary>
        void PlayedIn(IReadOnlyList<CardView> trickChips, bool fastForward);

        /// <summary>현재 차례가 바뀌었을 때(활성 좌석 라벨). null이면 무시.</summary>
        void TurnChanged(Text activeSeatLabel);

        /// <summary>라운드 결과 배너가 표시될 때(배너 RectTransform).</summary>
        void ResultShown(RectTransform banner);

        /// <summary>티츄 콜이 새로 선언됐을 때(좌석 배지). null이면 무시.</summary>
        void TichuDeclared(RectTransform badge);
    }

    /// <summary>연출 없음(테스트·헤드리스 기본). 모든 호출 무시.</summary>
    public sealed class NoOpPlayAnimator : IPlayAnimator
    {
        public void PlayedIn(IReadOnlyList<CardView> trickChips, bool fastForward) { }
        public void TurnChanged(Text activeSeatLabel) { }
        public void ResultShown(RectTransform banner) { }
        public void TichuDeclared(RectTransform badge) { }
    }
}
