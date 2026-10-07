using NUnit.Framework;

namespace Tichu.Server.Tests
{
    public class TokenBucketTests
    {
        [Test]
        public void Burst_is_allowed_then_refused()
        {
            double now = 0;
            var b = new TokenBucket(capacity: 3, perSecond: 1, () => now);
            Assert.That(b.TryTake(), Is.True);
            Assert.That(b.TryTake(), Is.True);
            Assert.That(b.TryTake(), Is.True);
            Assert.That(b.TryTake(), Is.False);
        }

        [Test]
        public void Tokens_refill_over_time_up_to_capacity()
        {
            double now = 0;
            var b = new TokenBucket(capacity: 2, perSecond: 10, () => now);
            b.TryTake(); b.TryTake();
            Assert.That(b.TryTake(), Is.False);

            now = 0.1;                            // 0.1초 × 10/초 = 1개
            Assert.That(b.TryTake(), Is.True);
            Assert.That(b.TryTake(), Is.False);

            now = 100;                            // 오래 쉬어도 용량을 넘지 않는다
            Assert.That(b.TryTake(), Is.True);
            Assert.That(b.TryTake(), Is.True);
            Assert.That(b.TryTake(), Is.False);
        }
    }
}
