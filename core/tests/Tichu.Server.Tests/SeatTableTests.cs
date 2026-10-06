using NUnit.Framework;

namespace Tichu.Server.Tests
{
    public class SeatTableTests
    {
        [Test]
        public void Seats_are_given_in_list_order_until_full()
        {
            var t = new SeatTable(new[] { 0, 2 });
            Assert.That(t.Required, Is.EqualTo(2));

            Assert.That(t.TryTake(out int a), Is.True);
            Assert.That(a, Is.EqualTo(0));
            Assert.That(t.IsFull, Is.False);
            Assert.That(t.TryTake(out int b), Is.True);
            Assert.That(b, Is.EqualTo(2));
            Assert.That(t.IsFull, Is.True);
            Assert.That(t.Connected, Is.EqualTo(2));
            Assert.That(t.TryTake(out _), Is.False, "만석이면 거절");
        }

        [Test]
        public void Released_seat_is_reused_first()
        {
            var t = new SeatTable(new[] { 0, 2 });
            t.TryTake(out _);
            t.TryTake(out _);
            t.Release(0);

            Assert.That(t.Connected, Is.EqualTo(1));
            Assert.That(t.TryTake(out int again), Is.True);
            Assert.That(again, Is.EqualTo(0));
        }

        [Test]
        public void Releasing_a_free_seat_does_nothing()
        {
            var t = new SeatTable(new[] { 1 });
            t.Release(1);
            Assert.That(t.Connected, Is.EqualTo(0));
        }
    }
}
