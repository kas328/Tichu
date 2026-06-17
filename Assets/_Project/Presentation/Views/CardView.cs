using System;
using Tichu.Core.Cards;
using Tichu.Presentation.Visuals;
using UnityEngine;
using UnityEngine.UI;

namespace Tichu.Presentation.Views
{
    /// <summary>
    /// 카드 1장 뷰(D2). 자식(face Image / label Text)을 지연 자가빌드한다.
    /// 스프라이트가 있으면 face, 없으면 텍스트 라벨 폴백(CardFormat). D3 풀링의 단위.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public sealed class CardView : MonoBehaviour
    {
        private static readonly Color CardBg  = new Color(0.96f, 0.97f, 0.98f);
        private static readonly Color CardSel = new Color(1.00f, 0.86f, 0.32f);
        private static readonly Color CardInk = new Color(0.10f, 0.12f, 0.16f);
        private static readonly Color CardRed = new Color(0.78f, 0.10f, 0.12f);
        private static readonly Color Back    = new Color(0.16f, 0.24f, 0.45f);
        private const float LiftHeight = 12f;

        private RectTransform _rt;
        private Image _bg, _face;
        private Text _label;
        private LayoutElement _le;
        private Button _button;
        private bool _built;
        private float _baseH;

        private Card _card;
        private CardSpriteAtlas _atlas;
        private bool _faceUp = true;
        private bool _selected;

        public Card Card => _card;

        /// <summary>카드 내용을 설정한다(앞면/뒷면). 스프라이트 없으면 텍스트 폴백.</summary>
        public void Set(Card card, CardSpriteAtlas atlas, bool faceUp)
        {
            EnsureBuilt();
            _card = card; _atlas = atlas; _faceUp = faceUp; _selected = false;
            Refresh();
        }

        /// <summary>카드 칩 크기(레이아웃 그룹에서 사용). 선택 lift의 기준 높이.</summary>
        public void SetSize(float w, float h)
        {
            EnsureBuilt();
            _baseH = h;
            _le.preferredWidth = w; _le.minWidth = w;
            ApplyHeight();
        }

        /// <summary>선택 강조(노란 배경 + 위로 lift).</summary>
        public void SetSelected(bool selected)
        {
            EnsureBuilt();
            _selected = selected;
            _bg.color = _faceUp ? (_selected ? CardSel : CardBg) : Back;
            ApplyHeight();
        }

        /// <summary>클릭 가능 토글. 풀 재사용 안전을 위해 리스너를 항상 비우고 다시 건다.</summary>
        public void SetInteractable(bool on, Action onClick)
        {
            EnsureBuilt();
            if (_button == null) _button = gameObject.GetComponent<Button>() ?? gameObject.AddComponent<Button>();
            _button.onClick.RemoveAllListeners();
            _button.interactable = on;
            if (on && onClick != null) _button.onClick.AddListener(() => onClick());
        }

        private void ApplyHeight()
        {
            if (_le == null || _baseH <= 0f) return;
            float h = _selected ? _baseH + LiftHeight : _baseH;
            _le.preferredHeight = h; _le.minHeight = h;
        }

        private void Refresh()
        {
            if (!_faceUp)
            {
                var back = _atlas != null ? _atlas.Back : null;
                _bg.sprite = back;
                _bg.color = back != null ? Color.white : Back;
                _face.enabled = false;
                _label.enabled = false;
                return;
            }

            _bg.sprite = null;
            _bg.color = _selected ? CardSel : CardBg;
            var sprite = _atlas != null ? _atlas.Face(_card) : null;
            if (sprite != null)
            {
                _face.enabled = true; _face.sprite = sprite;
                _label.enabled = false;
            }
            else
            {
                _face.enabled = false;
                _label.enabled = true;
                _label.text = CardFormat.Label(_card);
                _label.color = CardFormat.IsRed(_card) ? CardRed : CardInk;
            }
        }

        private void EnsureBuilt()
        {
            if (_built) return;
            _rt = (RectTransform)transform;
            _bg = GetComponent<Image>(); if (_bg == null) _bg = gameObject.AddComponent<Image>();
            _bg.color = CardBg;
            _le = GetComponent<LayoutElement>(); if (_le == null) _le = gameObject.AddComponent<LayoutElement>();

            _face = NewChildImage("Face");
            _face.enabled = false;
            _face.preserveAspect = true;

            _label = NewChildText("Label");
            _label.alignment = TextAnchor.MiddleCenter;
            _label.horizontalOverflow = HorizontalWrapMode.Overflow;
            _label.verticalOverflow = VerticalWrapMode.Overflow;
            _label.fontSize = 22;

            _built = true;
        }

        private Image NewChildImage(string name)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(transform, false);
            Stretch((RectTransform)go.transform);
            return go.GetComponent<Image>();
        }

        private Text NewChildText(string name)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(transform, false);
            Stretch((RectTransform)go.transform);
            var t = go.GetComponent<Text>();
            t.font = DefaultFont();
            t.color = CardInk;
            return t;
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        }

        private static Font DefaultFont()
        {
            var f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (f == null) f = Resources.GetBuiltinResource<Font>("Arial.ttf");
            return f;
        }
    }
}
