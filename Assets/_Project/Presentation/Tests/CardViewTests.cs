using System.Reflection;
using NUnit.Framework;
using Tichu.Core.Cards;
using Tichu.Core.Combinations;
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
            Assert.AreEqual("A\n별", label.text);
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
        public void BombMember_enables_gold_glow()
        {
            var cv = New(out var go);
            cv.Set(Card.Normal(7, Suit.Jade), null, faceUp: true);
            var glow = go.transform.Find("BombGlow")?.GetComponent<Image>();
            Assert.IsNotNull(glow, "폭탄 표시는 BombGlow 자식 이미지");
            Assert.IsFalse(glow.enabled, "기본은 글로우 꺼짐");
            cv.SetBombMember(true);
            Assert.IsTrue(glow.enabled, "폭탄 멤버 → 금색 글로우 켜짐");
            Assert.AreEqual(new Color(1.00f, 0.82f, 0.20f), glow.color);
            Assert.IsTrue(cv.IsBombMember);
            Object.DestroyImmediate(go);
        }

        [Test]
        public void Selected_fill_and_bomb_glow_coexist()
        {
            var cv = New(out var go);
            cv.Set(Card.Normal(7, Suit.Jade), null, faceUp: true);
            cv.SetBombMember(true);
            cv.SetHighlight(CardView.Highlight.Selected);
            var bg = go.GetComponent<Image>();
            var glow = go.transform.Find("BombGlow").GetComponent<Image>();
            Assert.AreEqual(new Color(1.00f, 0.86f, 0.32f), bg.color, "선택은 노랑 채움(CardSel)");
            Assert.IsTrue(glow.enabled, "폭탄 글로우는 선택과 무관하게 유지");
            Object.DestroyImmediate(go);
        }

        [Test]
        public void Set_neutralizes_bomb_member_for_pool_reuse()
        {
            var cv = New(out var go);
            cv.Set(Card.Normal(7, Suit.Jade), null, faceUp: true);
            cv.SetBombMember(true);
            Assert.IsTrue(cv.IsBombMember);
            Assert.IsTrue(go.transform.Find("BombGlow").GetComponent<Image>().enabled);
            cv.Set(Card.Normal(8, Suit.Jade), null, faceUp: true);
            Assert.IsFalse(cv.IsBombMember, "Set 은 풀 재사용 위해 폭탄 멤버를 중립화");
            Assert.IsFalse(go.transform.Find("BombGlow").GetComponent<Image>().enabled, "Set 은 글로우도 끈다");
            Object.DestroyImmediate(go);
        }

        [Test]
        public void PhoenixRepRank_places_phoenix_at_substituted_rank()
        {
            // 스트레이트 봉+10 J Q K A (봉=9 대체) → 봉황이 맨 앞에 정렬돼야.
            var straight = new Combination(CombinationType.Straight,
                new[] { Card.Phoenix, Card.Normal(10, Suit.Jade), Card.Normal(11, Suit.Sword),
                        Card.Normal(12, Suit.Pagoda), Card.Normal(13, Suit.Star), Card.Normal(14, Suit.Jade) },
                6, 14 * 2, 0);
            Assert.AreEqual(9, CardFormat.PhoenixRepRank(straight));
            Assert.Less(CardFormat.TrickSortKey(straight, Card.Phoenix),
                        CardFormat.TrickSortKey(straight, Card.Normal(10, Suit.Jade)), "봉황(9)이 10보다 앞");

            // 연속페어 10 봉 J J (봉=10 대체) → 봉황이 10과 J 사이.
            var tractor = new Combination(CombinationType.ConsecutivePairs,
                new[] { Card.Normal(10, Suit.Jade), Card.Phoenix, Card.Normal(11, Suit.Sword), Card.Normal(11, Suit.Pagoda) },
                4, 11 * 2, 0);
            Assert.AreEqual(10, CardFormat.PhoenixRepRank(tractor));
            double kTen = CardFormat.TrickSortKey(tractor, Card.Normal(10, Suit.Jade));
            double kPhx = CardFormat.TrickSortKey(tractor, Card.Phoenix);
            double kJack = CardFormat.TrickSortKey(tractor, Card.Normal(11, Suit.Sword));
            Assert.Less(kTen, kPhx, "10 < 봉");
            Assert.Less(kPhx, kJack, "봉 < J");
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
            Assert.AreEqual("A\n별", label.text);

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

        private static CardSpriteAtlas IconAtlas(params string[] keys)
        {
            var a = ScriptableObject.CreateInstance<CardSpriteAtlas>();
            var tex = new Texture2D(4, 4);
            var entries = new CardSpriteAtlas.Entry[keys.Length];
            for (int i = 0; i < keys.Length; i++)
                entries[i] = new CardSpriteAtlas.Entry { key = keys[i], sprite = Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f)) };
            typeof(CardSpriteAtlas).GetField("icons", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(a, entries);
            return a;
        }

        [Test]
        public void Icon_present_shows_rank_only_label_and_suit_icon()
        {
            var cv = New(out var go);
            var atlas = IconAtlas("Star");
            cv.Set(Card.Normal(14, Suit.Star), atlas, faceUp: true);

            var label = go.transform.Find("Label").GetComponent<Text>();
            var icon = go.transform.Find("Icon").GetComponent<Image>();
            Assert.AreEqual("A", label.text, "무늬는 아이콘이 맡는다");
            Assert.IsTrue(icon.enabled);
            Assert.AreSame(atlas.Icon(Card.Normal(14, Suit.Star)), icon.sprite);

            Object.DestroyImmediate(go);
            Object.DestroyImmediate(atlas);
        }

        [Test]
        public void Icon_missing_falls_back_to_full_text_label()
        {
            var cv = New(out var go);
            var atlas = IconAtlas("Star");
            cv.Set(Card.Normal(9, Suit.Jade), atlas, faceUp: true);

            Assert.AreEqual("9\n옥", go.transform.Find("Label").GetComponent<Text>().text);
            Assert.IsFalse(go.transform.Find("Icon").GetComponent<Image>().enabled);

            Object.DestroyImmediate(go);
            Object.DestroyImmediate(atlas);
        }

        [Test]
        public void Special_with_icon_keeps_its_name_label()
        {
            var cv = New(out var go);
            var atlas = IconAtlas("Phoenix");
            cv.Set(Card.Phoenix, atlas, faceUp: true);

            Assert.AreEqual("봉황", go.transform.Find("Label").GetComponent<Text>().text);
            Assert.IsTrue(go.transform.Find("Icon").GetComponent<Image>().enabled);

            Object.DestroyImmediate(go);
            Object.DestroyImmediate(atlas);
        }

        [Test]
        public void Each_suit_has_its_own_ink_colour()
        {
            var colours = new System.Collections.Generic.HashSet<Color>();
            foreach (var suit in new[] { Suit.Jade, Suit.Sword, Suit.Pagoda, Suit.Star })
            {
                var cv = New(out var go);
                cv.Set(Card.Normal(5, suit), null, faceUp: true);
                colours.Add(go.transform.Find("Label").GetComponent<Text>().color);
                Object.DestroyImmediate(go);
            }
            Assert.AreEqual(4, colours.Count, "옥·검·탑·별은 서로 다른 색");
        }

        [Test]
        public void FaceDown_hides_icon()
        {
            var cv = New(out var go);
            var atlas = IconAtlas("Star");
            cv.Set(Card.Normal(14, Suit.Star), atlas, faceUp: false);

            Assert.IsFalse(go.transform.Find("Icon").GetComponent<Image>().enabled);

            Object.DestroyImmediate(go);
            Object.DestroyImmediate(atlas);
        }
    }
}
