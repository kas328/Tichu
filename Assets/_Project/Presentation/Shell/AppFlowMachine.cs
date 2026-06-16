using System;
using R3;

namespace Tichu.Presentation.Shell
{
    /// <summary>
    /// 순수 <see cref="AppFlowReducer"/>를 R3 상태 스트림으로 감싼 비순수 흐름기.
    /// 현재 화면을 <see cref="ReactiveProperty{T}"/>로 들고, <see cref="Send"/>로 reducer를 적용해 갱신한다.
    /// 부수효과(패널 토글·씬 로드·BGM)는 <see cref="State"/> 구독자(엔트리포인트/패널)가 담당한다.
    /// </summary>
    public sealed class AppFlowMachine : IDisposable
    {
        readonly ReactiveProperty<ScreenState> _state;

        public AppFlowMachine() : this(ScreenState.Intro) { }

        public AppFlowMachine(ScreenState initial)
        {
            _state = new ReactiveProperty<ScreenState>(initial);
        }

        /// <summary>현재 화면(즉시값).</summary>
        public ScreenState Current => _state.Value;

        /// <summary>화면 상태 스트림(구독 시 현재값 즉시 발화, 값이 바뀔 때만 재발화).</summary>
        public ReadOnlyReactiveProperty<ScreenState> State => _state;

        /// <summary>
        /// 이벤트를 reducer로 적용해 화면을 전이한다. 결과가 현재 상태와 같으면
        /// <see cref="ReactiveProperty{T}"/>가 재발화하지 않는다(부적합 이벤트는 화면을 흔들지 않음).
        /// </summary>
        public void Send(AppFlowEvent evt) => _state.Value = AppFlowReducer.Reduce(_state.Value, evt);

        public void Dispose() => _state.Dispose();
    }
}
