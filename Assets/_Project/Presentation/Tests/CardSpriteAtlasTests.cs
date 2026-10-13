using NUnit.Framework;
using System.Reflection;
using Tichu.Core.Cards;
using Tichu.Presentation.Visuals;
using UnityEngine;

namespace Tichu.Presentation.Tests
{
    public class CardSpriteAtlasTests
    {
        [Test]
        public void Unpopulated_atlas_returns_null_face_and_back()
        {
            var atlas = ScriptableObject.CreateInstance<CardSpriteAtlas>();
            Assert.IsNull(atlas.Face(Card.Normal(14, Suit.Star)));
            Assert.IsNull(atlas.Back);
            Object.DestroyImmediate(atlas);
        }

        private static void SetGenerate(CardSpriteAtlas a, bool v) =>
            typeof(CardSpriteAtlas).GetField("generateArt", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(a, v);

        [Test]
        public void GenerateArt_off_returns_null_frame()
        {
            var atlas = ScriptableObject.CreateInstance<CardSpriteAtlas>();
            Assert.IsNull(atlas.Frame(Card.Normal(14, Suit.Star)), "생성 꺼짐 → 프레임 null");
            Object.DestroyImmediate(atlas);
        }

        [Test]
        public void GenerateArt_on_returns_generated_frame_and_back()
        {
            var atlas = ScriptableObject.CreateInstance<CardSpriteAtlas>();
            SetGenerate(atlas, true);
            Assert.IsNotNull(atlas.Frame(Card.Normal(14, Suit.Star)), "생성 켜짐 → 프레임 non-null");
            Assert.IsNotNull(atlas.Back, "생성 켜짐 → 뒷면 non-null");
            Object.DestroyImmediate(atlas);
        }

        [Test]
        public void Serialized_back_wins_over_generation()
        {
            var atlas = ScriptableObject.CreateInstance<CardSpriteAtlas>();
            SetGenerate(atlas, true);
            var tex = new Texture2D(4, 4);
            var custom = Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f));
            typeof(CardSpriteAtlas).GetField("back", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(atlas, custom);
            Assert.AreSame(custom, atlas.Back, "직렬화 뒷면이 생성보다 우선");
            Object.DestroyImmediate(atlas);
            Object.DestroyImmediate(tex);
        }

        [Test]
        public void Icon_is_looked_up_by_suit_or_special_kind()
        {
            var atlas = ScriptableObject.CreateInstance<CardSpriteAtlas>();
            var tex = new Texture2D(4, 4);
            var star = Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f));
            var dragon = Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f));
            typeof(CardSpriteAtlas).GetField("icons", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(atlas,
                new[] { new CardSpriteAtlas.Entry { key = "Star", sprite = star }, new CardSpriteAtlas.Entry { key = "Dragon", sprite = dragon } });

            Assert.AreSame(star, atlas.Icon(Card.Normal(3, Suit.Star)));
            Assert.AreSame(star, atlas.Icon(Card.Normal(14, Suit.Star)), "같은 무늬는 같은 아이콘");
            Assert.AreSame(dragon, atlas.Icon(Card.Dragon));
            Assert.IsNull(atlas.Icon(Card.Normal(3, Suit.Jade)), "없는 아이콘은 null(텍스트 폴백)");
            Object.DestroyImmediate(atlas);
            Object.DestroyImmediate(tex);
        }

        [Test]
        public void Unpopulated_atlas_returns_null_icon()
        {
            var atlas = ScriptableObject.CreateInstance<CardSpriteAtlas>();
            Assert.IsNull(atlas.Icon(Card.Normal(14, Suit.Star)));
            Object.DestroyImmediate(atlas);
        }
    }
}
