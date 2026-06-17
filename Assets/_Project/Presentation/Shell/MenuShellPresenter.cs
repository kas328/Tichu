using System;
using DG.Tweening;
using R3;
using UnityEngine;
using VContainer.Unity;

namespace Tichu.Presentation.Shell
{
    /// <summary>
    /// 메뉴 셸 프레젠터(부트 엔트리포인트). <see cref="MenuShellView"/>를 빌드해 버튼을
    /// <see cref="AppFlowMachine.Send"/>에 배선하고, <see cref="AppFlowMachine.State"/>를 구독해
    /// DoTween CanvasGroup 페이드로 패널을 토글한다. 비-메뉴 상태(InGame/Result)에서는 전 패널을
    /// 숨긴다(Table/Result는 C4가 담당). C2의 AppBootEntryPoint(자동 발화)를 대체한다.
    /// </summary>
    public sealed class MenuShellPresenter : IStartable, IDisposable
    {
        const float FadeSeconds = 0.2f;

        readonly AppFlowMachine _flow;
        MenuShellView _view;
        IDisposable _sub;

        public MenuShellPresenter(AppFlowMachine flow) => _flow = flow;

        public void Start()
        {
            _view = new MenuShellView();
            WireButtons();
            _sub = _flow.State.Subscribe(Show);   // 구독 즉시 현재 화면(Intro) 발화 → 첫 패널 표시
        }

        void WireButtons()
        {
            _view.AddButton(ScreenState.Intro,      "시작하기",  () => _flow.Send(AppFlowEvent.IntroFinished));
            _view.AddButton(ScreenState.MainHub,    "게임 시작", () => _flow.Send(AppFlowEvent.OpenModeSelect));
            _view.AddButton(ScreenState.MainHub,    "게임 방법", () => _flow.Send(AppFlowEvent.OpenHowTo));
            _view.AddButton(ScreenState.MainHub,    "설정",      () => _flow.Send(AppFlowEvent.OpenSettings));
            _view.AddButton(ScreenState.ModeSelect, "AI 대전",   () => _flow.Send(AppFlowEvent.StartAiMatch));   // → InGame
            _view.AddButton(ScreenState.ModeSelect, "랭킹",      () => { _flow.Send(AppFlowEvent.SelectRankingStub);    ShowToast("랭킹은 Phase 3에서 제공됩니다"); });
            _view.AddButton(ScreenState.ModeSelect, "친구방",    () => { _flow.Send(AppFlowEvent.SelectFriendRoomStub); ShowToast("친구방은 Phase 3에서 제공됩니다"); });
            _view.AddButton(ScreenState.ModeSelect, "뒤로",      () => _flow.Send(AppFlowEvent.Back));
            _view.AddButton(ScreenState.HowTo,      "뒤로",      () => _flow.Send(AppFlowEvent.Back));
            _view.AddButton(ScreenState.Settings,   "뒤로",      () => _flow.Send(AppFlowEvent.Back));            // 볼륨 슬라이더는 D5
        }

        /// <summary>
        /// 타깃 화면 패널만 페이드인하고, 그 외 모든 메뉴 패널은 즉시 숨긴다.
        /// 비활성화를 DOTween 완료 콜백에 의존하지 않으므로 프레임/페이드 타이밍과 무관하게
        /// 항상 정확히 한 패널만 보인다(메뉴 밖 InGame/Result면 next=null → 전부 숨김).
        /// </summary>
        void Show(ScreenState s)
        {
            Debug.Log($"[Shell] {s}");
            var next = _view.Panels.TryGetValue(s, out var cg) ? cg : null;

            foreach (var panel in _view.Panels.Values)
            {
                if (panel == next) continue;
                Hide(panel);
            }

            if (next != null)
            {
                next.gameObject.SetActive(true);
                FadeIn(next);
            }
        }

        /// <summary>전 화면 위에 잠깐 뜨는 알림(Phase3 스텁 안내 등). DoTween 시퀀스로 페이드 인→유지→아웃.</summary>
        void ShowToast(string msg)
        {
            var cg = _view.ToastGroup;
            _view.ToastText.text = msg;
            DOTween.Kill(cg);
            cg.alpha = 0f;
            DOTween.Sequence().SetTarget(cg)
                .Append(DOTween.To(() => cg.alpha, a => cg.alpha = a, 1f, 0.2f))
                .AppendInterval(1.5f)
                .Append(DOTween.To(() => cg.alpha, a => cg.alpha = a, 0f, 0.35f));
        }

        // 타깃이 아닌 패널은 즉시 숨긴다(SetActive false) — 페이드아웃 완료 콜백 누락으로 패널이
        // 남아 위에 렌더되는 일을 원천 차단. (DOTween 코어 API만 사용 — 모듈 확장은 asmdef에서 안 보임.)
        static void Hide(CanvasGroup cg)
        {
            DOTween.Kill(cg);
            cg.alpha = 0f;
            cg.interactable = false;
            cg.blocksRaycasts = false;
            cg.gameObject.SetActive(false);
        }

        static void FadeIn(CanvasGroup cg)
        {
            DOTween.Kill(cg);
            cg.interactable = true;
            cg.blocksRaycasts = true;
            DOTween.To(() => cg.alpha, a => cg.alpha = a, 1f, FadeSeconds).SetTarget(cg);
        }

        public void Dispose() => _sub?.Dispose();
    }
}
