using NUnit.Framework;
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
    }
}
