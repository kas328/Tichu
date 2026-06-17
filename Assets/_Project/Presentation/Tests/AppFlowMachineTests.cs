using System.Collections.Generic;
using NUnit.Framework;
using R3;
using Tichu.Presentation.Shell;

namespace Tichu.Presentation.Tests
{
    /// <summary>
    /// 비순수 <see cref="AppFlowMachine"/>(R3 상태 스트림 + 순수 reducer 위임) 단위 테스트.
    /// UnityEngine·DI·씬 무관(헤드리스 EditMode) — R3 발화 의미까지 못박는다.
    /// </summary>
    public sealed class AppFlowMachineTests
    {
        [Test]
        public void Starts_at_intro_by_default()
        {
            using var m = new AppFlowMachine();
            Assert.That(m.Current, Is.EqualTo(ScreenState.Intro));
        }

        [Test]
        public void Starts_at_given_initial_state()
        {
            using var m = new AppFlowMachine(ScreenState.MainHub);
            Assert.That(m.Current, Is.EqualTo(ScreenState.MainHub));
        }

        [Test]
        public void Send_applies_reducer_transition()
        {
            using var m = new AppFlowMachine(ScreenState.Intro);
            m.Send(AppFlowEvent.IntroFinished);
            Assert.That(m.Current, Is.EqualTo(ScreenState.MainHub));
        }

        [Test]
        public void State_stream_emits_current_then_each_transition()
        {
            using var m = new AppFlowMachine(ScreenState.Intro);
            var seen = new List<ScreenState>();
            using (m.State.Subscribe(seen.Add))
            {
                m.Send(AppFlowEvent.IntroFinished);   // Intro -> MainHub
                m.Send(AppFlowEvent.OpenModeSelect);  // MainHub -> ModeSelect
            }
            Assert.That(seen, Is.EqualTo(new[]
            {
                ScreenState.Intro, ScreenState.MainHub, ScreenState.ModeSelect
            }));
        }

        [Test]
        public void Inapplicable_event_does_not_reemit()
        {
            using var m = new AppFlowMachine(ScreenState.MainHub);
            var seen = new List<ScreenState>();
            using (m.State.Subscribe(seen.Add))
            {
                m.Send(AppFlowEvent.MatchEnded);      // MainHub에서 정의 안 됨 → 머문다(상태 불변)
            }
            Assert.That(seen, Is.EqualTo(new[] { ScreenState.MainHub }));  // 초기 1회만, 재발화 없음
        }

        [Test]
        public void Dispose_completes_state_stream()
        {
            var m = new AppFlowMachine();
            var completed = false;
            m.State.Subscribe(_ => { }, (Result _) => completed = true);
            m.Dispose();
            Assert.That(completed, Is.True);
        }
    }
}
