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
        /// <summary>손패 강조: 일반 / 선택(노랑+lift) / 교환배정(초록).</summary>
        public enum Highlight { Normal, Selected, Assigned }

        private static readonly Color CardBg  = new Color(0.96f, 0.97f, 0.98f);
        private static readonly Color CardSel = new Color(1.00f, 0.86f, 0.32f);
        private static readonly Color CardUse = new Color(0.55f, 0.80f, 0.62f); // 교환 배정됨
        private static readonly Color BombGlowGold = new Color(1.00f, 0.82f, 0.20f); // 폭탄 보유 카드 금색 오오라
        private static readonly Color CardInk = new Color(0.10f, 0.12f, 0.16f);
        private static readonly Color CardRed = new Color(0.78f, 0.10f, 0.12f);
        private static readonly Color Back    = new Color(0.16f, 0.24f, 0.45f);
        private const float LiftHeight = 12f;

        private RectTransform _rt;
        private Image _bg, _face;
        private Text _label;
        private LayoutElement _le;
        private Button _button;
        private Image _bombGlow;
        private bool _built;
        private float _baseH;

        private Card _card;
        private CardSpriteAtlas _atlas;
        private bool _faceUp = true;
        private Highlight _highlight = Highlight.Normal;
        private bool _bombMember;

        /// <summary>이 카드가 내 손패의 폭탄 조합에 속하는지(금색 오오라).</summary>
        public bool IsBombMember => _bombMember;

        /// <summary>폭탄 멤버 표시 토글(카드 금색 오오라 글로우. 선택 채움과 공존).</summary>
        public void SetBombMember(bool on)
        {
            EnsureBuilt();
            _bombMember = on;
            _bombGlow.enabled = on;
        }

        /// <summary>현재 활성 onClick 리스너가 걸려 있는지(풀 재사용 안전 단언용).</summary>
        public bool HasClickListener { get; private set; }

        public Card Card => _card;

        /// <summary>카드 내용을 설정한다(앞면/뒷면). 스프라이트 없으면 텍스트 폴백. 강조는 Normal로 초기화.</summary>
        public void Set(Card card, CardSpriteAtlas atlas, bool faceUp)
        {
            EnsureBuilt();
            _card = card; _atlas = atlas; _faceUp = faceUp; _highlight = Highlight.Normal; _bombMember = false;
            _bombGlow.enabled = false;
            Refresh();
            ApplyHeight();
            // 풀 재사용 안전: Set+SetSize 만 부르는 소비자(트릭/뒷면)에서도 옛 리스너/상호작용을 비운다.
            if (_button != null) { _button.onClick.RemoveAllListeners(); _button.interactable = false; }
            HasClickListener = false;
        }

        /// <summary>카드 칩 크기(레이아웃 그룹에서 사용). 선택 lift의 기준 높이.</summary>
        public void SetSize(float w, float h)
        {
            EnsureBuilt();
            _baseH = h;
            _le.preferredWidth = w; _le.minWidth = w;
            ApplyHeight();
        }

        /// <summary>손패 강조 상태. Selected=노랑+lift, Assigned=초록, Normal=기본.</summary>
        public void SetHighlight(Highlight h)
        {
            EnsureBuilt();
            _highlight = h;
            Refresh();
            ApplyHeight();
        }

        /// <summary>클릭 가능 토글. 리스너를 항상 비우고 다시 건다(중립화는 Set 도 수행).</summary>
        public void SetInteractable(bool on, Action onClick)
        {
            EnsureBuilt();
            if (_button == null) _button = gameObject.GetComponent<Button>() ?? gameObject.AddComponent<Button>();
            _button.onClick.RemoveAllListeners();
            _button.interactable = on;
            HasClickListener = on && onClick != null;
            if (HasClickListener) _button.onClick.AddListener(() => onClick());
        }

        private void ApplyHeight()
        {
            if (_le == null || _baseH <= 0f) return;
            float h = (_faceUp && _highlight == Highlight.Selected) ? _baseH + LiftHeight : _baseH;
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

            _bg.sprite = _atlas != null ? _atlas.Frame(_card) : null; // 생성 프레임(없으면 납작 사각형=기존)
            _bg.color = HighlightColor();
            var sprite = _atlas != null ? _atlas.Face(_card) : null;   // PNG 풀아트 우선
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

        private Color HighlightColor()
        {
            if (_highlight == Highlight.Selected) return CardSel;
            if (_highlight == Highlight.Assigned) return CardUse;
            return CardBg;
        }

        private void EnsureBuilt()
        {
            if (_built) return;
            _rt = (RectTransform)transform;
            _bg = GetComponent<Image>(); if (_bg == null) _bg = gameObject.AddComponent<Image>();
            _bg.color = CardBg;
            _le = GetComponent<LayoutElement>(); if (_le == null) _le = gameObject.AddComponent<LayoutElement>();

            // 폭탄 오오라: 카드보다 살짝 큰 금색 halo(면/라벨 뒤·기본 꺼짐). 첫 자식이라 _bg 위·나머지 뒤에 그려진다.
            _bombGlow = NewChildImage("BombGlow");
            _bombGlow.sprite = GlowSprite();
            _bombGlow.color = BombGlowGold;
            _bombGlow.raycastTarget = false;
            _bombGlow.enabled = false;
            var grt = (RectTransform)_bombGlow.transform;
            grt.offsetMin = new Vector2(-14f, -14f);
            grt.offsetMax = new Vector2(14f, 14f);

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

        // 폭탄 오오라용 부드러운 스프라이트(흰색+가장자리 페더 알파). Image.color=금색으로 틴트. 1회 생성·캐시.
        private static Sprite _glowSprite;
        private static Sprite GlowSprite()
        {
            if (_glowSprite != null) return _glowSprite;
            const int w = 132, h = 200, feather = 34;
            var px = new Color[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int edge = Mathf.Min(Mathf.Min(x, w - 1 - x), Mathf.Min(y, h - 1 - y));
                    float a = Mathf.Clamp01(edge / (float)feather); // 가장자리 0 → 안쪽 1(부드러운 번짐)
                    px[y * w + x] = new Color(1f, 1f, 1f, a * 0.5f);
                }
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
            tex.SetPixels(px);
            tex.Apply();
            _glowSprite = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f);
            return _glowSprite;
        }
    }
}
