using System.Collections.Generic;
using Tichu.Core.Cards;
using Tichu.Presentation.Views;
using UnityEngine;

namespace Tichu.Presentation.Visuals
{
    /// <summary>
    /// 카드 면 프레임/뒷면 스프라이트를 코드로 생성(외부 에셋·폰트 불필요). 생성 시 1회 그리고 캐시.
    /// 면 = 둥근 흰 카드 + 무늬색 테두리(랭크/무늬는 CardView 라벨이 위에 얹는다). 뒷면 = 대각 격자 패턴.
    /// </summary>
    public sealed class CardArtFactory
    {
        public enum FrameStyle { Black, Red, Special }

        private const int W = 132;   // 칩 66×100 의 2배 해상도(0.66 비율 유지)
        private const int H = 200;
        private const int Radius = 16;
        private const int Border = 6;

        private static readonly Color Paper   = new Color(0.97f, 0.98f, 0.99f);
        private static readonly Color EdgeBlk  = new Color(0.16f, 0.18f, 0.22f);
        private static readonly Color EdgeRed  = new Color(0.78f, 0.12f, 0.14f);
        private static readonly Color EdgeGold = new Color(0.85f, 0.66f, 0.22f);
        private static readonly Color BackBg   = new Color(0.13f, 0.20f, 0.42f);
        private static readonly Color BackInk  = new Color(0.30f, 0.42f, 0.72f);

        private readonly Dictionary<FrameStyle, Sprite> _frames = new Dictionary<FrameStyle, Sprite>();
        private Sprite _back;

        public Sprite Frame(FrameStyle style)
        {
            if (_frames.TryGetValue(style, out var s)) return s;
            s = BuildFrame(EdgeFor(style));
            _frames[style] = s;
            return s;
        }

        public Sprite Back => _back != null ? _back : (_back = BuildBack());

        public static FrameStyle StyleFor(Card card)
        {
            if (card.IsSpecial) return FrameStyle.Special;
            return CardFormat.IsRed(card) ? FrameStyle.Red : FrameStyle.Black;
        }

        private static Color EdgeFor(FrameStyle s) =>
            s == FrameStyle.Red ? EdgeRed : s == FrameStyle.Special ? EdgeGold : EdgeBlk;

        private static Sprite BuildFrame(Color edge)
        {
            var px = new Color[W * H];
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    Color c;
                    if (!RoundedInside(x, y, 0)) c = Color.clear;
                    else if (!RoundedInside(x, y, Border)) c = edge;  // 테두리 띠
                    else c = Paper;                                    // 카드 면
                    px[y * W + x] = c;
                }
            return MakeSprite(px);
        }

        private static Sprite BuildBack()
        {
            var px = new Color[W * H];
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    if (!RoundedInside(x, y, 0)) { px[y * W + x] = Color.clear; continue; }
                    if (!RoundedInside(x, y, Border)) { px[y * W + x] = BackInk; continue; }
                    bool hatch = ((x + y) / 10) % 2 == 0; // 대각 격자
                    px[y * W + x] = hatch ? BackBg : BackInk;
                }
            return MakeSprite(px);
        }

        // 둥근 사각형 내부 판정(inset 만큼 안쪽). 네 모서리는 사분원.
        private static bool RoundedInside(int x, int y, int inset)
        {
            int left = inset, right = W - 1 - inset, bottom = inset, top = H - 1 - inset;
            if (x < left || x > right || y < bottom || y > top) return false;
            int r = Radius - inset; if (r < 0) r = 0;
            int cx = -1, cy = -1;
            if (x < left + r && y < bottom + r) { cx = left + r; cy = bottom + r; }
            else if (x < left + r && y > top - r) { cx = left + r; cy = top - r; }
            else if (x > right - r && y < bottom + r) { cx = right - r; cy = bottom + r; }
            else if (x > right - r && y > top - r) { cx = right - r; cy = top - r; }
            if (cx < 0) return true; // 모서리 영역 밖 = 직선 변 안쪽
            int dx = x - cx, dy = y - cy;
            return dx * dx + dy * dy <= r * r;
        }

        private static Sprite MakeSprite(Color[] px)
        {
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
            tex.SetPixels(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(0.5f, 0.5f), 100f);
        }
    }
}
