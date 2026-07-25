using UnityEngine;

namespace Tichu.Presentation.Shell
{
    /// <summary>
    /// 앱 백그라운드/포커스 아웃 시 전역 오디오 정지(AudioListener.pause), 복귀 시 복원.
    /// App-수명 GO(MenuShellPresenter가 부트 시 생성). 순수 판정은 AudioLifecycle.
    /// </summary>
    public sealed class AppLifecycleAudio : MonoBehaviour
    {
        void OnApplicationPause(bool paused) => AudioListener.pause = paused;
        void OnApplicationFocus(bool focus) => AudioListener.pause = AudioLifecycle.ShouldPause(focus);
    }
}
