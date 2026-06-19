using System.Reflection;
using NUnit.Framework;
using Tichu.Core.Cards;
using Tichu.Presentation.Views;
using Tichu.Presentation.Visuals;
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

        [Test]
        public void Set_neutralizes_button_for_pool_reuse()
        {
            var cv = New(out var go);
            bool fired = false;
            cv.SetInteractable(true, () => fired = true);
            Assert.IsTrue(cv.HasClickListener);

            // 풀에서 다른 카드로 재사용 — Set 만 호출.
            cv.Set(Card.Normal(7, Suit.Jade), null, faceUp: true);

            Assert.IsFalse(cv.HasClickListener, "Set 은 풀 재사용 위해 버튼을 중립화해야 한다");
            var btn = go.GetComponent<Button>();
            Assert.IsFalse(btn.interactable);
            btn.onClick.Invoke();
            Assert.IsFalse(fired, "옛 리스너가 Set 이후 발화하면 안 된다");
            Object.DestroyImmediate(go);
        }

        [Test]
        public void SetInteractable_tracks_HasClickListener()
        {
            var cv = New(out var go);
            Assert.IsFalse(cv.HasClickListener);
            cv.SetInteractable(true, () => { });
            Assert.IsTrue(cv.HasClickListener);
            cv.SetInteractable(false, null);
            Assert.IsFalse(cv.HasClickListener);
            Object.DestroyImmediate(go);
        }

        [Test]
        public void BombMember_enables_red_outline()
        {
            var cv = New(out var go);
            cv.Set(Card.Normal(7, Suit.Jade), null, faceUp: true);
            var outline = go.GetComponent<Outline>();
            Assert.IsNotNull(outline, "폭탄 표시는 Outline 컴포넌트");
            Assert.IsFalse(outline.enabled, "기본은 아웃라인 꺼짐");
            cv.SetBombMember(true);
            Assert.IsTrue(outline.enabled, "폭탄 멤버 → 빨강 아웃라인 켜짐");
            Assert.AreEqual(new Color(0.92f, 0.20f, 0.18f), outline.effectColor);
            Assert.IsTrue(cv.IsBombMember);
            Object.DestroyImmediate(go);
        }

        [Test]
        public void Selected_fill_and_bomb_outline_coexist()
        {
            var cv = New(out var go);
            cv.Set(Card.Normal(7, Suit.Jade), null, faceUp: true);
            cv.SetBombMember(true);
            cv.SetHighlight(CardView.Highlight.Selected);
            var bg = go.GetComponent<Image>();
            var outline = go.GetComponent<Outline>();
            Assert.AreEqual(new Color(1.00f, 0.86f, 0.32f), bg.color, "선택은 노랑 채움(CardSel)");
            Assert.IsTrue(outline.enabled, "폭탄 아웃라인은 선택과 무관하게 유지");
            Object.DestroyImmediate(go);
        }

        [Test]
        public void Set_neutralizes_bomb_member_for_pool_reuse()
        {
            var cv = New(out var go);
            cv.Set(Card.Normal(7, Suit.Jade), null, faceUp: true);
            cv.SetBombMember(true);
            Assert.IsTrue(cv.IsBombMember);
            Assert.IsTrue(go.GetComponent<Outline>().enabled);
            cv.Set(Card.Normal(8, Suit.Jade), null, faceUp: true);
            Assert.IsFalse(cv.IsBombMember, "Set 은 풀 재사용 위해 폭탄 멤버를 중립화");
            Assert.IsFalse(go.GetComponent<Outline>().enabled, "Set 은 아웃라인도 끈다");
            Object.DestroyImmediate(go);
        }

        private static CardSpriteAtlas GeneratingAtlas()
        {
            var a = ScriptableObject.CreateInstance<CardSpriteAtlas>();
            typeof(CardSpriteAtlas).GetField("generateArt", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(a, true);
            return a;
        }

        [Test]
        public void FaceUp_generating_atlas_uses_frame_background_and_keeps_label()
        {
            var cv = New(out var go);
            var atlas = GeneratingAtlas();
            cv.Set(Card.Normal(14, Suit.Star), atlas, faceUp: true);

            var bg = go.GetComponent<Image>();
            var label = go.transform.Find("Label").GetComponent<Text>();
            Assert.IsNotNull(bg.sprite, "면 배경에 생성 프레임 스프라이트");
            Assert.IsTrue(label.enabled, "PNG 풀아트가 없으면 랭크/무늬 라벨은 위에 유지");
            Assert.AreEqual("A\n♥", label.text);

            Object.DestroyImmediate(go);
            Object.DestroyImmediate(atlas);
        }

        [Test]
        public void FaceDown_generating_atlas_uses_back_sprite()
        {
            var cv = New(out var go);
            var atlas = GeneratingAtlas();
            cv.Set(Card.Normal(14, Suit.Star), atlas, faceUp: false);

            var bg = go.GetComponent<Image>();
            Assert.IsNotNull(bg.sprite, "뒷면 스프라이트가 배경에 설정");

            Object.DestroyImmediate(go);
            Object.DestroyImmediate(atlas);
        }
    }
}
