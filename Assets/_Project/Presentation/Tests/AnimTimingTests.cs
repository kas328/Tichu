using NUnit.Framework;
using Tichu.Presentation.Visuals;

namespace Tichu.Presentation.Tests
{
    public class AnimTimingTests
    {
        [Test]
        public void FastForward_collapses_duration_to_zero()
        {
            Assert.AreEqual(0f, AnimTiming.Scale(AnimTiming.PlayPop, true));
        }

        [Test]
        public void Normal_keeps_base_duration()
        {
            Assert.AreEqual(AnimTiming.PlayPop, AnimTiming.Scale(AnimTiming.PlayPop, false), 1e-6f);
            Assert.Greater(AnimTiming.PlayPop, 0f);
        }
    }
}
