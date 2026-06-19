using NUnit.Framework;
using Tichu.Core.Cards;
using Tichu.Presentation.Visuals;
using UnityEngine;

namespace Tichu.Presentation.Tests
{
    public class CardArtFactoryTests
    {
        [Test]
        public void Back_is_nonnull_sized_and_cached()
        {
            var f = new CardArtFactory();
            var back = f.Back;
            Assert.IsNotNull(back, "뒷면 스프라이트는 non-null");
            Assert.Greater(back.texture.width, 0);
            Assert.Greater(back.texture.height, 0);
            Assert.AreSame(back, f.Back, "Back 은 캐시(같은 인스턴스 반환)");
        }

        [Test]
        public void Back_has_transparent_corner_and_opaque_center()
        {
            var f = new CardArtFactory();
            var tex = f.Back.texture;
            Assert.AreEqual(0f, tex.GetPixel(0, 0).a, 0.01f, "모서리는 둥글어 투명");
            Assert.AreEqual(1f, tex.GetPixel(tex.width / 2, tex.height / 2).a, 0.01f, "중앙은 불투명");
        }

        [Test]
        public void Frame_is_nonnull_and_cached_per_style()
        {
            var f = new CardArtFactory();
            foreach (var s in new[] { CardArtFactory.FrameStyle.Black, CardArtFactory.FrameStyle.Red, CardArtFactory.FrameStyle.Special })
            {
                var sp = f.Frame(s);
                Assert.IsNotNull(sp, $"{s} 프레임 non-null");
                Assert.AreSame(sp, f.Frame(s), $"{s} 프레임 캐시");
            }
            Assert.AreNotSame(f.Frame(CardArtFactory.FrameStyle.Black), f.Frame(CardArtFactory.FrameStyle.Red),
                "스타일이 다르면 다른 스프라이트");
        }

        [Test]
        public void Frame_has_transparent_rounded_corner()
        {
            var f = new CardArtFactory();
            var tex = f.Frame(CardArtFactory.FrameStyle.Black).texture;
            Assert.AreEqual(0f, tex.GetPixel(0, 0).a, 0.01f, "프레임 모서리도 둥글어 투명");
        }

        [Test]
        public void StyleFor_maps_color_and_special()
        {
            Assert.AreEqual(CardArtFactory.FrameStyle.Red, CardArtFactory.StyleFor(Card.Normal(14, Suit.Star)));   // 하트=빨강
            Assert.AreEqual(CardArtFactory.FrameStyle.Red, CardArtFactory.StyleFor(Card.Normal(7, Suit.Pagoda)));  // 다이아=빨강
            Assert.AreEqual(CardArtFactory.FrameStyle.Black, CardArtFactory.StyleFor(Card.Normal(13, Suit.Sword))); // 스페이드=검정
            Assert.AreEqual(CardArtFactory.FrameStyle.Black, CardArtFactory.StyleFor(Card.Normal(2, Suit.Jade)));   // 클럽=검정
            Assert.AreEqual(CardArtFactory.FrameStyle.Special, CardArtFactory.StyleFor(Card.Dragon));
            Assert.AreEqual(CardArtFactory.FrameStyle.Special, CardArtFactory.StyleFor(Card.Mahjong));
        }
    }
}
