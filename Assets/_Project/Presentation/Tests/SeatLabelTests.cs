using NUnit.Framework;
using Tichu.Presentation.Views;

namespace Tichu.Presentation.Tests
{
    /// <summary>좌석 이름표 조립 — 이름, 자리 비움이면 (AI), 남은 초가 있으면 · N초(M5.1).</summary>
    [TestFixture]
    public class SeatLabelTests
    {
        [Test]
        public void Name_only_when_nothing_else_applies() =>
            Assert.That(RuntimeTableView.SeatLabel("왼쪽", false, null), Is.EqualTo("왼쪽"));

        [Test]
        public void Away_seat_is_marked_AI() =>
            Assert.That(RuntimeTableView.SeatLabel("왼쪽", true, null), Is.EqualTo("왼쪽(AI)"));

        [Test]
        public void Seconds_follow_the_name() =>
            Assert.That(RuntimeTableView.SeatLabel("오른쪽", false, 12), Is.EqualTo("오른쪽 · 12초"));

        [Test]
        public void Zero_seconds_are_still_shown() =>
            Assert.That(RuntimeTableView.SeatLabel("파트너", false, 0), Is.EqualTo("파트너 · 0초"));
    }
}
