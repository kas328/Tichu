using NUnit.Framework;
using Tichu.Core.Cards;
using Tichu.Presentation.Views;
using UnityEngine;
using UnityEngine.UI;

namespace Tichu.Presentation.Tests
{
    public class CardViewTests
    {
        private static CardView New(out GameObject go)
        {
            go = new GameObject("cv", typeof(RectTransform));
            return go.AddComponent<CardView>();
        }

        [Test]
        public void FaceUp_no_atlas_shows_label_hides_face()
        {
            var cv = New(out var go);
            cv.Set(Card.Normal(14, Suit.Star), null, faceUp: true);
            var label = go.transform.Find("Label").GetComponent<Text>();
            var face = go.transform.Find("Face").GetComponent<Image>();
            Assert.AreEqual("A\n♥", label.text);
            Assert.IsTrue(label.enabled);
            Assert.IsFalse(face.enabled);
            Object.DestroyImmediate(go);
        }

        [Test]
        public void FaceDown_hides_label()
        {
            var cv = New(out var go);
            cv.Set(Card.Normal(14, Suit.Star), null, faceUp: false);
            var label = go.transform.Find("Label").GetComponent<Text>();
            Assert.IsFalse(label.enabled);
            Object.DestroyImmediate(go);
        }

        [Test]
        public void Set_records_card()
        {
            var cv = New(out var go);
            var card = Card.Normal(9, Suit.Jade);
            cv.Set(card, null, faceUp: true);
            Assert.AreEqual(card, cv.Card);
            Object.DestroyImmediate(go);
        }

        [Test]
        public void Selected_highlight_lifts_card_height()
        {
            var cv = New(out var go);
            cv.Set(Card.Normal(5, Suit.Jade), null, faceUp: true);
            cv.SetSize(66, 100);
            var le = go.GetComponent<LayoutElement>();
            Assert.AreEqual(100f, le.preferredHeight, 0.01f);
            cv.SetHighlight(CardView.Highlight.Selected);
            Assert.AreEqual(112f, le.preferredHeight, 0.01f);
            cv.SetHighlight(CardView.Highlight.Assigned);
            Assert.AreEqual(100f, le.preferredHeight, 0.01f, "교환 배정은 lift 없음");
            Object.DestroyImmediate(go);
        }
    }
}
