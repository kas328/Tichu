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

        // ── GuiTopLeftInside: IMGUI(FpsOverlay)용 좌상단 안전 지점 ────────────────────
        // Screen.safeArea 는 좌하단 원점, IMGUI 는 좌상단 원점 → y 반전이 필요하다.
        // margin 은 Android safeArea 가 인셋해 주지 않는 "둥근 모서리" 여유분.

        [Test]
        public void GuiTopLeftInside_without_cutout_is_just_the_margin()
        {
            var p = SafeAreaMath.GuiTopLeftInside(new Rect(0, 0, 1920, 1080), new Vector2(1920, 1080), 40f);
            Assert.AreEqual(40f, p.x, 1e-4f);
            Assert.AreEqual(40f, p.y, 1e-4f);
        }

        [Test]
        public void GuiTopLeftInside_shifts_right_past_a_left_cutout()
        {
            // 가로 모드에서 좌측 80px 컷아웃 — S23 펀치홀이 왼쪽 가장자리에 오는 경우.
            var p = SafeAreaMath.GuiTopLeftInside(new Rect(80, 0, 1840, 1080), new Vector2(1920, 1080), 40f);
            Assert.AreEqual(120f, p.x, 1e-4f, "컷아웃(80) 바깥 + 여백(40)");
            Assert.AreEqual(40f, p.y, 1e-4f);
        }

        [Test]
        public void GuiTopLeftInside_flips_y_for_a_top_inset()
        {
            // safeArea.yMax 가 화면 높이보다 50 작다 = 상단 50px 이 안전하지 않다.
            // 좌하단 원점 → 좌상단 원점 변환이 되어야 y 가 50+40 이 된다(반전 누락 시 40).
            var p = SafeAreaMath.GuiTopLeftInside(new Rect(0, 0, 1920, 1030), new Vector2(1920, 1080), 40f);
            Assert.AreEqual(90f, p.y, 1e-4f, "(screen.y - safeArea.yMax) + margin");
        }
    }
}
