using NUnit.Framework;
using Tichu.Presentation.Visuals;
using UnityEngine;

namespace Tichu.Presentation.Tests
{
    public class SafeAreaMathTests
    {
        [Test]
        public void FullScreen_safeArea_maps_to_unit_anchors()
        {
            var (min, max) = SafeAreaMath.ComputeAnchors(new Rect(0, 0, 1920, 1080), new Vector2(1920, 1080));
            Assert.AreEqual(Vector2.zero, min);
            Assert.AreEqual(Vector2.one, max);
        }

        [Test]
        public void Notch_inset_maps_to_fractional_anchors()
        {
            // 왼쪽 96px 노치 인셋(가로)
            var (min, max) = SafeAreaMath.ComputeAnchors(new Rect(96, 0, 1824, 1080), new Vector2(1920, 1080));
            Assert.AreEqual(0.05f, min.x, 1e-4f);
            Assert.AreEqual(1.0f, max.x, 1e-4f);
            Assert.AreEqual(0f, min.y, 1e-4f);
        }

        [Test]
        public void Zero_screen_falls_back_to_full()
        {
            var (min, max) = SafeAreaMath.ComputeAnchors(new Rect(0, 0, 0, 0), Vector2.zero);
            Assert.AreEqual(Vector2.zero, min);
            Assert.AreEqual(Vector2.one, max);
        }
    }
}
