using System.Collections.Generic;
using DG.Tweening;
using Tichu.Presentation.Views;
using UnityEngine;
using UnityEngine.UI;

namespace Tichu.Presentation.Visuals
{
    /// <summary>
    /// DoTween 기반 플레이 연출. 모든 트윈 SetAutoKill, 재사용 대상엔 DOKill 선행.
    /// 풀/레이아웃 호환: localScale만 트윈(position/size는 LayoutGroup이 제어). FastForward면 duration 0.
    /// </summary>
    public sealed class DoTweenPlayAnimator : IPlayAnimator
    {
        public void PlayedIn(IReadOnlyList<CardView> trickChips, bool fastForward)
        {
            float d = AnimTiming.Scale(AnimTiming.PlayPop, fastForward);
            for (int i = 0; i < trickChips.Count; i++)
            {
                var rt = (RectTransform)trickChips[i].transform;
                rt.DOKill();
                rt.localScale = new Vector3(0.7f, 0.7f, 1f);
                rt.DOScale(1f, d).SetEase(Ease.OutBack).SetAutoKill(true);
            }
        }

        public void TurnChanged(Text activeSeatLabel)
        {
            if (activeSeatLabel == null) return;
            var rt = (RectTransform)activeSeatLabel.transform;
            rt.DOKill();
            rt.localScale = Vector3.one;
            rt.DOPunchScale(new Vector3(0.15f, 0.15f, 0f), AnimTiming.TurnPulse, 1, 0.5f).SetAutoKill(true);
        }

        public void ResultShown(RectTransform banner)
        {
            if (banner == null) return;
            banner.DOKill();
            banner.localScale = new Vector3(0.85f, 0.85f, 1f);
            banner.DOScale(1f, AnimTiming.BannerPop).SetEase(Ease.OutBack).SetAutoKill(true);
        }

        public void TichuDeclared(RectTransform badge)
        {
            if (badge == null) return;
            badge.DOKill();
            badge.localScale = Vector3.one;
            badge.DOPunchScale(new Vector3(0.4f, 0.4f, 0f), AnimTiming.TurnPulse, 1, 0.5f).SetAutoKill(true);
        }
    }
}
