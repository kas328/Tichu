using System;
using R3;
using UnityEngine;
using VContainer.Unity;

namespace Tichu.Presentation.Shell
{
    /// <summary>
    /// 앱 부트 진입점. 영속 화면 상태(<see cref="AppFlowMachine.State"/>)를 구독해 현재 화면을 로그로 노출하고,
    /// 인트로가 끝나면 메뉴로 전이시킨다. C3에서 실제 패널 토글/인트로 연출이 이 지점을 대체·확장한다.
    /// </summary>
    public sealed class AppBootEntryPoint : IStartable, IDisposable
    {
        readonly AppFlowMachine _flow;
        IDisposable _sub;

        public AppBootEntryPoint(AppFlowMachine flow) => _flow = flow;

        public void Start()
        {
            _sub = _flow.State.Subscribe(s => Debug.Log($"[AppFlow] {s}"));
            _flow.Send(AppFlowEvent.IntroFinished);   // 인트로 종료 → 메뉴 (C3가 실제 인트로 패널로 대체)
        }

        public void Dispose() => _sub?.Dispose();
    }
}
