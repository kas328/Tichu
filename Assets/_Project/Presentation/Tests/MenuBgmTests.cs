using NUnit.Framework;
using Tichu.Presentation.Shell;

namespace Tichu.Presentation.Tests
{
    /// <summary>메뉴 BGM 재생 정책(순수)의 EditMode 검증 — 인게임에서만 무음.</summary>
    public class MenuBgmTests
    {
        [Test]
        public void Plays_in_all_menu_and_result_states()
        {
            Assert.IsTrue(MenuBgm.PlaysIn(ScreenState.Intro));
            Assert.IsTrue(MenuBgm.PlaysIn(ScreenState.MainHub));
            Assert.IsTrue(MenuBgm.PlaysIn(ScreenState.ModeSelect));
            Assert.IsTrue(MenuBgm.PlaysIn(ScreenState.HowTo));
            Assert.IsTrue(MenuBgm.PlaysIn(ScreenState.Settings));
            Assert.IsTrue(MenuBgm.PlaysIn(ScreenState.Result));
        }

        [Test]
        public void Silent_during_ingame()
        {
            Assert.IsFalse(MenuBgm.PlaysIn(ScreenState.InGame));
        }
    }
}
