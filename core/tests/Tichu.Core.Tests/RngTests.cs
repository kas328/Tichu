using NUnit.Framework;
using Tichu.Core;

namespace Tichu.Core.Tests
{
    public class RngTests
    {
        [Test]
        public void Same_seed_produces_same_sequence()
        {
            var a = new Rng(12345UL);
            var b = new Rng(12345UL);
            for (int i = 0; i < 100; i++)
                Assert.That(a.NextULong(), Is.EqualTo(b.NextULong()));
        }

        [Test]
        public void Different_seeds_diverge()
        {
            var a = new Rng(1UL);
            var b = new Rng(2UL);
            Assert.That(a.NextULong(), Is.Not.EqualTo(b.NextULong()));
        }

        [Test]
        public void NextInt_stays_in_range()
        {
            var r = new Rng(99UL);
            for (int i = 0; i < 1000; i++)
            {
                int v = r.NextInt(10);
                Assert.That(v, Is.InRange(0, 9));
            }
        }
    }
}
