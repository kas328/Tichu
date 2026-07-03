using NUnit.Framework;
using Tichu.Presentation.Shell;

namespace Tichu.Presentation.Tests
{
    /// <summary>
    /// 순수 <see cref="AppFlowReducer"/> 전이 표 테스트. UnityEngine·DI·씬 무관(헤드리스 EditMode).
    /// 화면 흐름 회귀를 디바이스 없이 잡는다.
    /// </summary>
    public sealed class AppFlowReducerTests
    {
        // ── 유효 전이(해피패스) ──────────────────────────────────────────────
        [TestCase(ScreenState.Intro,      AppFlowEvent.IntroFinished,  ScreenState.MainHub)]
        [TestCase(ScreenState.MainHub,    AppFlowEvent.OpenModeSelect, ScreenState.ModeSelect)]
        [TestCase(ScreenState.MainHub,    AppFlowEvent.OpenHowTo,      ScreenState.HowTo)]
        [TestCase(ScreenState.MainHub,    AppFlowEvent.OpenSettings,   ScreenState.Settings)]
        [TestCase(ScreenState.ModeSelect, AppFlowEvent.OpenDifficultySelect, ScreenState.DifficultySelect)]
        [TestCase(ScreenState.DifficultySelect, AppFlowEvent.StartAiMatch, ScreenState.InGame)]
        [TestCase(ScreenState.DifficultySelect, AppFlowEvent.Back,     ScreenState.ModeSelect)]
        [TestCase(ScreenState.ModeSelect, AppFlowEvent.Back,           ScreenState.MainHub)]
        [TestCase(ScreenState.HowTo,      AppFlowEvent.Back,           ScreenState.MainHub)]
        [TestCase(ScreenState.Settings,   AppFlowEvent.Back,           ScreenState.MainHub)]
        [TestCase(ScreenState.InGame,     AppFlowEvent.MatchEnded,     ScreenState.Result)]
        [TestCase(ScreenState.InGame,     AppFlowEvent.ReturnToHub,    ScreenState.MainHub)]
        [TestCase(ScreenState.Result,     AppFlowEvent.ReturnToHub,    ScreenState.MainHub)]
        public void Valid_transitions(ScreenState from, AppFlowEvent evt, ScreenState expected)
        {
            Assert.That(AppFlowReducer.Reduce(from, evt), Is.EqualTo(expected));
        }

        // ── Phase3 스텁: 랭킹·친구방은 화면을 바꾸지 않는다(머문다) ───────────
        [TestCase(AppFlowEvent.SelectRankingStub)]
        [TestCase(AppFlowEvent.SelectFriendRoomStub)]
        public void Ranking_and_friendroom_are_stubs_no_navigation(AppFlowEvent stub)
        {
            Assert.That(AppFlowReducer.Reduce(ScreenState.ModeSelect, stub), Is.EqualTo(ScreenState.ModeSelect));
        }

        // ── 부적합 이벤트는 현재 상태를 그대로 유지한다(무시) ─────────────────
        [TestCase(ScreenState.MainHub, AppFlowEvent.MatchEnded)]
        [TestCase(ScreenState.InGame,  AppFlowEvent.OpenModeSelect)]
        [TestCase(ScreenState.Intro,   AppFlowEvent.Back)]
        public void Inapplicable_events_keep_current_state(ScreenState from, AppFlowEvent evt)
        {
            Assert.That(AppFlowReducer.Reduce(from, evt), Is.EqualTo(from));
        }
    }
}
