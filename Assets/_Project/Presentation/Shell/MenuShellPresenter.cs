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
        CanvasGroup _active;

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

        /// <summary>현재 화면 패널을 페이드인하고 이전 패널을 페이드아웃한다. 메뉴 밖 상태면 전부 숨김.</summary>
        void Show(ScreenState s)
        {
            Debug.Log($"[Shell] {s}");
            var next = _view.Panels.TryGetValue(s, out var cg) ? cg : null;
            if (next == _active) return;

            if (_active != null) Fade(_active, 0f, deactivate: true);
            _active = next;
            if (next != null)
            {
                next.gameObject.SetActive(true);
                Fade(next, 1f, deactivate: false);
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

        static void Fade(CanvasGroup cg, float to, bool deactivate)
        {
            // DOTween 코어(DOTween.dll)만 사용 — CanvasGroup.DOFade는 asmdef 없는 모듈 소스라
            // 별도 asmdef에서 안 보임. SetTarget(cg)로 Kill(cg)이 이 트윈을 끊게 한다.
            DOTween.Kill(cg);
            cg.interactable = to > 0.5f;
            cg.blocksRaycasts = to > 0.5f;
            var tween = DOTween.To(() => cg.alpha, a => cg.alpha = a, to, FadeSeconds).SetTarget(cg);
            if (deactivate)
                tween.OnComplete(() => { if (cg.alpha <= 0.01f) cg.gameObject.SetActive(false); });
        }

        public void Dispose() => _sub?.Dispose();
    }
}
