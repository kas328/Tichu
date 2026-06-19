# P1-D D4 — 카드 아트(코드 생성) + DoTween 애니(진실/연출 분리) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 평면 텍스트 카드를 코드로 생성한 실제 스프라이트(둥근 프레임·뒷면 패턴)로 바꾸고, 플레이/차례/결과에 DoTween 연출을 입혀 "손맛"을 만든다 — 오라클 비트동일성은 100% 보존.

**Architecture:** 두 묶음. **(A) 카드 아트** = `CardArtFactory`가 Texture2D로 둥근 프레임·뒷면을 1회 그려 캐시 → `CardSpriteAtlas`가 `generateArt` 플래그로 폴백 제공 → `CardView`는 프레임을 배경에 깔고 랭크/무늬 라벨을 그 위에 유지(폰트 래스터화 불필요). **(B) 애니** = §6.1 "진실/연출 분리" — 논리 진실은 `ApplySnapshot`(R3 즉시 갱신) 그대로, 시각 연출은 렌더 시점 훅 `IPlayAnimator`(기본 `NoOpPlayAnimator`)로 분리. DoTween 구현체(`DoTweenPlayAnimator`)만 DoTween에 닿고, EditMode 테스트는 순수 로직/배선만 검증(DoTween 비의존).

**Tech Stack:** Unity 6000.3.17f1 · URP3D · R3 · UniTask · VContainer · DoTween 1.2.825(코어 Transform 숏컷만) · NUnit EditMode.

## Global Constraints

- **오라클 비트동일성 불변:** `onApply=null` 경로(테스트/오라클/헤드리스)는 동기 `GameDriver`와 비트 동일해야 한다. 애니/아트는 이 경로에 닿지 않는다 — `onApply=null`이면 `RecordPlay`가 안 불려 `vm.Played`가 발화하지 않고, ReactiveProperty도 안 바뀌어 렌더 훅이 안 돈다(`AsyncGameDriver.cs:173`, `RoundBootstrap.cs:85`).
- **새 asmdef 0개:** 모든 신규 파일은 기존 `Tichu.Presentation` 어셈블리 안. DoTween은 자동 참조(`DOTween.dll` Auto Reference ON, `Tichu.Presentation.asmdef` overrideReferences:false) — asmdef 수정 금지.
- **테스트 asmdef는 DoTween 비참조:** `Tichu.Presentation.Tests.asmdef`는 `overrideReferences:true`. 어떤 테스트도 `DG.Tweening`/`DoTweenPlayAnimator`를 참조하면 안 된다 — DoTween 의존이 테스트로 새면 컴파일 실패. 테스트는 `NoOpPlayAnimator`/`RecordingAnimator`/`AnimTiming` 순수 로직만 사용.
- **DoTween 안전 규칙:** 모든 트윈은 `SetAutoKill(true)`. 재사용 칩/라벨에 트윈 걸기 전 `DOKill()`로 이전 트윈 정리. `FastForward`면 duration 0(즉시 스냅) — `AnimTiming.Scale`로 강제.
- **풀링 호환:** 카드 칩은 `CardChipPool`이 `SetActive` 재사용 + `Horizontal/VerticalLayoutGroup`이 위치/크기 제어. 트윈은 **localScale만** 건드린다(레이아웃이 sizeDelta를 덮어쓰므로 position/size 트윈 금지).
- **커밋 컨벤션:** `feat(p1d): ...` / `docs(p1d): ...` (한국어), 커밋 메시지 끝에 `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>`.

---

## File Structure

| 파일 | 역할 | 신규/수정 |
|---|---|---|
| `Assets/_Project/Presentation/Visuals/CardArtFactory.cs` | Texture2D로 둥근 프레임(3종)·뒷면 스프라이트 생성+캐시. 순수(EditMode 가능). | **신규** |
| `Assets/_Project/Presentation/Visuals/CardSpriteAtlas.cs` | `generateArt` 폴백 + `Frame(Card)` + `Back` 폴백 추가. PNG 면(Face)은 여전히 우선. | 수정 |
| `Assets/_Project/Presentation/Views/CardView.cs` | 면 배경에 프레임 스프라이트, 랭크/무늬 라벨은 위에 유지. null 아틀라스 동작 보존. | 수정 |
| `Assets/_Project/Presentation/Resources/CardSpriteAtlas.asset` | `generateArt: 1` 설정(직렬화). | 수정(에셋) |
| `Assets/_Project/Presentation/Visuals/AnimTiming.cs` | duration 상수 + `Scale(dur, fastForward)`. 순수. | **신규** |
| `Assets/_Project/Presentation/Views/IPlayAnimator.cs` | 연출 훅 인터페이스 + `NoOpPlayAnimator`. | **신규** |
| `Assets/_Project/Presentation/Visuals/DoTweenPlayAnimator.cs` | DoTween 구현(플레이 팝·차례 펄스·결과 팝). 유일한 DoTween 의존 파일. | **신규** |
| `Assets/_Project/Presentation/Views/RuntimeTableView.cs` | 생성자 `IPlayAnimator` 주입(기본 NoOp) + 렌더 시점 훅 호출. | 수정 |
| `Assets/_Project/Presentation/RoundBootstrap.cs` | `new RuntimeTableView(new DoTweenPlayAnimator())` 한 줄. | 수정 |
| `Assets/_Project/Presentation/Tests/CardArtFactoryTests.cs` | A1 테스트. | **신규** |
| `Assets/_Project/Presentation/Tests/CardSpriteAtlasTests.cs` | A2 테스트 추가. | 수정 |
| `Assets/_Project/Presentation/Tests/CardViewTests.cs` | A3 테스트 추가. | 수정 |
| `Assets/_Project/Presentation/Tests/AnimTimingTests.cs` | B1 테스트. | **신규** |
| `Assets/_Project/Presentation/Tests/PlayAnimatorWiringTests.cs` | B2 배선 테스트. | **신규** |

**테스트 실행:** Unity Test Runner(EditMode) 또는 MCP `run_tests`(mode=EditMode, 필요 시 `filter`=테스트 클래스명). 각 단계 "Run" = 해당 클래스만 필터해 실행. 스크립트 수정 후엔 항상 `read_console`로 컴파일 에러 0 확인 후 진행.

> **Task 0 (착수 전):** 이 계획 문서를 먼저 커밋한다 — `git add docs/superpowers/plans/2026-06-19-p1d-d4-card-art-and-dotween-animations.md && git commit -m "docs(p1d): D4 카드 아트+DoTween 애니 구현 계획(TDD 7태스크)"`.

---

## Group A — 카드 아트 (코드 생성 스프라이트)

### Task A1: CardArtFactory — 둥근 프레임·뒷면 스프라이트 생성

**Files:**
- Create: `Assets/_Project/Presentation/Visuals/CardArtFactory.cs`
- Test: `Assets/_Project/Presentation/Tests/CardArtFactoryTests.cs`

**Interfaces:**
- Consumes: `Tichu.Core.Cards.Card`, `Tichu.Presentation.Views.CardFormat.IsRed`.
- Produces:
  - `enum CardArtFactory.FrameStyle { Black, Red, Special }`
  - `Sprite CardArtFactory.Frame(FrameStyle style)` — 캐시된 스프라이트(스타일당 1개)
  - `Sprite CardArtFactory.Back` — 캐시된 뒷면 스프라이트
  - `static FrameStyle CardArtFactory.StyleFor(Card card)`

- [ ] **Step 1: 실패 테스트 작성** — `CardArtFactoryTests.cs`

```csharp
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
```

- [ ] **Step 2: 실패 확인** — Run: EditMode, filter `CardArtFactoryTests`. Expected: FAIL/컴파일 에러(`CardArtFactory` 없음).

- [ ] **Step 3: 구현** — `CardArtFactory.cs`

```csharp
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
```

- [ ] **Step 4: 통과 확인** — Run: EditMode, filter `CardArtFactoryTests`. Expected: PASS(5/5). `read_console` 에러 0.

- [ ] **Step 5: 커밋**

```bash
git add Assets/_Project/Presentation/Visuals/CardArtFactory.cs Assets/_Project/Presentation/Tests/CardArtFactoryTests.cs
git commit -m "feat(p1d): D4 CardArtFactory — 둥근 프레임·뒷면 스프라이트 코드 생성(캐시)

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task A2: CardSpriteAtlas — generateArt 폴백 + Frame(Card)

**Files:**
- Modify: `Assets/_Project/Presentation/Visuals/CardSpriteAtlas.cs`
- Test: `Assets/_Project/Presentation/Tests/CardSpriteAtlasTests.cs`

**Interfaces:**
- Consumes: `CardArtFactory.Frame`, `CardArtFactory.Back`, `CardArtFactory.StyleFor`.
- Produces:
  - `Sprite CardSpriteAtlas.Frame(Card card)` — `generateArt`이면 생성 프레임, 아니면 null
  - `Sprite CardSpriteAtlas.Back` — 직렬화 `back` 우선, 없고 `generateArt`이면 생성 뒷면, 둘 다 없으면 null
  - 직렬화 필드 `bool generateArt`

- [ ] **Step 1: 실패 테스트 추가** — `CardSpriteAtlasTests.cs` 에 추가(기존 테스트 유지). 상단 using 에 `using System.Reflection;` 추가.

```csharp
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
```

- [ ] **Step 2: 실패 확인** — Run: EditMode, filter `CardSpriteAtlasTests`. Expected: FAIL(컴파일 에러 — `Frame`/`generateArt` 없음).

- [ ] **Step 3: 구현** — `CardSpriteAtlas.cs`. 필드/프로퍼티 블록을 아래로 교체.

기존:
```csharp
        [SerializeField] private Sprite back;
        [SerializeField] private Entry[] faces = new Entry[0];

        private Dictionary<string, Sprite> _map;

        public Sprite Back => back;
```

교체:
```csharp
        [SerializeField] private Sprite back;
        [SerializeField] private Entry[] faces = new Entry[0];
        [SerializeField] private bool generateArt; // true면 미할당 면/뒷면을 CardArtFactory로 생성

        private Dictionary<string, Sprite> _map;
        private CardArtFactory _factory;
        private CardArtFactory Factory => _factory ?? (_factory = new CardArtFactory());

        public Sprite Back => back != null ? back : (generateArt ? Factory.Back : null);

        /// <summary>면 배경 프레임(생성 아트). PNG 면(Face)이 있으면 CardView가 그쪽을 우선한다.</summary>
        public Sprite Frame(Card card) =>
            generateArt ? Factory.Frame(CardArtFactory.StyleFor(card)) : null;
```

(`Face(Card)` 메서드는 그대로 둔다 — PNG 풀아트 override 경로.)

- [ ] **Step 4: 통과 확인** — Run: EditMode, filter `CardSpriteAtlasTests`. Expected: PASS(기존 1 + 신규 3 = 4/4). `read_console` 에러 0.

- [ ] **Step 5: 커밋**

```bash
git add Assets/_Project/Presentation/Visuals/CardSpriteAtlas.cs Assets/_Project/Presentation/Tests/CardSpriteAtlasTests.cs
git commit -m "feat(p1d): D4 CardSpriteAtlas generateArt 폴백 + Frame(Card)

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task A3: CardView — 프레임 배경 + 라벨 유지

**Files:**
- Modify: `Assets/_Project/Presentation/Views/CardView.cs` (`Refresh()` faceUp 분기)
- Test: `Assets/_Project/Presentation/Tests/CardViewTests.cs`

**Interfaces:**
- Consumes: `CardSpriteAtlas.Frame(Card)`, `CardSpriteAtlas.Face(Card)`, `CardSpriteAtlas.Back`.
- Produces: 동작 변화 — faceUp이고 생성 아틀라스면 루트 `Image.sprite`=프레임, 라벨 enabled. PNG `Face`가 있으면 `_face` 사용+라벨 숨김(기존). 아틀라스 null이면 프레임 null+라벨(기존 보존).

- [ ] **Step 1: 실패 테스트 추가** — `CardViewTests.cs` 에 추가. 상단 using 에 `using System.Reflection;`, `using Tichu.Presentation.Visuals;` 추가.

```csharp
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
```

- [ ] **Step 2: 실패 확인** — Run: EditMode, filter `CardViewTests`. Expected: FAIL(`bg.sprite` null — 현재 faceUp 분기가 `_bg.sprite = null`).

- [ ] **Step 3: 구현** — `CardView.cs` `Refresh()` 의 faceUp 분기(뒷면 분기 아래) 첫 두 줄 교체.

기존:
```csharp
            _bg.sprite = null;
            _bg.color = HighlightColor();
            var sprite = _atlas != null ? _atlas.Face(_card) : null;
```

교체:
```csharp
            _bg.sprite = _atlas != null ? _atlas.Frame(_card) : null; // 생성 프레임(없으면 납작 사각형=기존)
            _bg.color = HighlightColor();
            var sprite = _atlas != null ? _atlas.Face(_card) : null;   // PNG 풀아트 우선
```

(나머지 분기 — sprite!=null이면 `_face`+라벨숨김, else 라벨 표시 — 변경 없음. 뒷면 분기 `_bg.sprite = back` 도 변경 없음.)

- [ ] **Step 4: 통과 확인** — Run: EditMode, filter `CardViewTests`. Expected: PASS(기존 6 + 신규 2 = 8/8). 특히 기존 `FaceUp_no_atlas_shows_label_hides_face`(아틀라스 null → 라벨 유지)가 여전히 그린이어야 한다. `read_console` 에러 0.

- [ ] **Step 5: 커밋**

```bash
git add Assets/_Project/Presentation/Views/CardView.cs Assets/_Project/Presentation/Tests/CardViewTests.cs
git commit -m "feat(p1d): D4 CardView 프레임 배경 + 라벨 유지(null 아틀라스 폴백 보존)

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task A4: Resources 아틀라스 generateArt 활성 + PlayMode 육안

**Files:**
- Modify: `Assets/_Project/Presentation/Resources/CardSpriteAtlas.asset` (`generateArt: 1`)

**Interfaces:**
- Consumes: A2의 `generateArt` 직렬화 필드. (RuntimeTableView는 이미 `Resources.Load<CardSpriteAtlas>("CardSpriteAtlas")` → 변경 불필요.)
- Produces: 런타임 카드가 생성 프레임/뒷면으로 렌더.

- [ ] **Step 1: 에셋 플래그 설정** — 둘 중 하나(MCP 우선, 실패 시 직접 편집):
  - (a) Unity MCP `manage_asset`/`execute_code`로 `Assets/_Project/Presentation/Resources/CardSpriteAtlas.asset` 로드 → SerializedObject `generateArt`=true → `ApplyModifiedProperties` + `SaveAssets`.
  - (b) 직접 편집: `CardSpriteAtlas.asset` YAML에 `generateArt: 1` 추가(A2 빌드로 필드가 직렬화 가능해진 뒤). `refresh_unity`로 반영.

- [ ] **Step 2: EditMode 회귀** — Run: 전체 EditMode. Expected: 기존 311+신규 그린, 회귀 0. (에셋 변경이 헤드리스 테스트를 깨지 않는지 확인.)

- [ ] **Step 3: PlayMode 육안 검증** — Table 씬 진입(메뉴 셸 → 게임 시작) 또는 `RoundBootstrap` 직접 Play. 스크린샷으로 확인:
  - [ ] 손패 카드 = 둥근 흰 프레임 + 무늬색 테두리(빨강 무늬=빨강 테두리, 검정=어두운 테두리, 특수=금색) + 랭크/무늬 라벨이 또렷.
  - [ ] 상대 뒷면 = 대각 격자 패턴 카드(납작 단색 아님).
  - [ ] 선택(노랑) 하이라이트가 프레임을 노랗게 틴트하며 lift 유지.
  - [ ] 가로 1920×1080·SafeArea 레이아웃 깨짐 없음.

- [ ] **Step 4: 커밋**

```bash
git add Assets/_Project/Presentation/Resources/CardSpriteAtlas.asset
git commit -m "feat(p1d): D4 Resources 아틀라스 generateArt 활성 — 런타임 카드 아트 적용

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Group B — DoTween 애니 (진실/연출 분리)

### Task B1: AnimTiming — FastForward 스냅 규칙(순수)

**Files:**
- Create: `Assets/_Project/Presentation/Visuals/AnimTiming.cs`
- Test: `Assets/_Project/Presentation/Tests/AnimTimingTests.cs`

**Interfaces:**
- Produces:
  - `const float AnimTiming.PlayPop = 0.18f; TurnPulse = 0.25f; BannerPop = 0.30f;`
  - `static float AnimTiming.Scale(float baseDuration, bool fastForward)` — fastForward면 0, 아니면 baseDuration.

- [ ] **Step 1: 실패 테스트 작성** — `AnimTimingTests.cs`

```csharp
using NUnit.Framework;
using Tichu.Presentation.Visuals;

namespace Tichu.Presentation.Tests
{
    public class AnimTimingTests
    {
        [Test]
        public void FastForward_collapses_duration_to_zero()
        {
            Assert.AreEqual(0f, AnimTiming.Scale(AnimTiming.PlayPop, true));
        }

        [Test]
        public void Normal_keeps_base_duration()
        {
            Assert.AreEqual(AnimTiming.PlayPop, AnimTiming.Scale(AnimTiming.PlayPop, false), 1e-6f);
            Assert.Greater(AnimTiming.PlayPop, 0f);
        }
    }
}
```

- [ ] **Step 2: 실패 확인** — Run: EditMode, filter `AnimTimingTests`. Expected: FAIL(`AnimTiming` 없음).

- [ ] **Step 3: 구현** — `AnimTiming.cs`

```csharp
namespace Tichu.Presentation.Visuals
{
    /// <summary>연출 duration 상수 + FastForward(스킵) 스냅 규칙. DoTween 비의존(EditMode 테스트 가능).</summary>
    public static class AnimTiming
    {
        public const float PlayPop = 0.18f;   // 트릭 플레이 팝인
        public const float TurnPulse = 0.25f;  // 차례 강조 펄스
        public const float BannerPop = 0.30f;  // 결과 배너 팝

        /// <summary>FastForward면 즉시 스냅(0), 아니면 기본 duration.</summary>
        public static float Scale(float baseDuration, bool fastForward) => fastForward ? 0f : baseDuration;
    }
}
```

- [ ] **Step 4: 통과 확인** — Run: EditMode, filter `AnimTimingTests`. Expected: PASS(2/2).

- [ ] **Step 5: 커밋**

```bash
git add Assets/_Project/Presentation/Visuals/AnimTiming.cs Assets/_Project/Presentation/Tests/AnimTimingTests.cs
git commit -m "feat(p1d): D4 AnimTiming — FastForward 스냅 규칙(순수, EditMode)

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task B2: IPlayAnimator 시임 + RuntimeTableView 배선

**Files:**
- Create: `Assets/_Project/Presentation/Views/IPlayAnimator.cs`
- Modify: `Assets/_Project/Presentation/Views/RuntimeTableView.cs`
- Test: `Assets/_Project/Presentation/Tests/PlayAnimatorWiringTests.cs`

**Interfaces:**
- Produces:
  - `interface IPlayAnimator { void PlayedIn(IReadOnlyList<CardView> trickChips, bool fastForward); void TurnChanged(Text activeSeatLabel); void ResultShown(RectTransform banner); }`
  - `sealed class NoOpPlayAnimator : IPlayAnimator` (모든 호출 무시)
  - `RuntimeTableView(IPlayAnimator anim = null)` 생성자(null→NoOp).
- Consumes: 없음(B3가 DoTween 구현체로 이 시임을 채움).

- [ ] **Step 1: 인터페이스 작성** — `IPlayAnimator.cs`

```csharp
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Tichu.Presentation.Views
{
    /// <summary>
    /// 플레이 연출 훅(진실/연출 분리). 렌더 시점에 호출되며 절대 블로킹하지 않는다.
    /// 진실은 ApplySnapshot(R3 즉시 갱신)이 담당 — 이 훅은 시각 효과만. onApply=null 경로에선 렌더가
    /// 안 돌아 호출되지 않으므로 오라클 비트동일성에 닿지 않는다.
    /// </summary>
    public interface IPlayAnimator
    {
        /// <summary>트릭 중앙에 새 카드가 깔렸을 때(렌더된 칩들). fastForward면 즉시 스냅.</summary>
        void PlayedIn(IReadOnlyList<CardView> trickChips, bool fastForward);

        /// <summary>현재 차례가 바뀌었을 때(활성 좌석 라벨). null이면 무시.</summary>
        void TurnChanged(Text activeSeatLabel);

        /// <summary>라운드 결과 배너가 표시될 때(배너 RectTransform).</summary>
        void ResultShown(RectTransform banner);
    }

    /// <summary>연출 없음(테스트·헤드리스 기본). 모든 호출 무시.</summary>
    public sealed class NoOpPlayAnimator : IPlayAnimator
    {
        public void PlayedIn(IReadOnlyList<CardView> trickChips, bool fastForward) { }
        public void TurnChanged(Text activeSeatLabel) { }
        public void ResultShown(RectTransform banner) { }
    }
}
```

- [ ] **Step 2: 배선 실패 테스트 작성** — `PlayAnimatorWiringTests.cs`

```csharp
using System.Collections.Generic;
using System.Threading;
using NUnit.Framework;
using Tichu.Presentation.ViewModel;
using Tichu.Presentation.Views;
using UnityEngine;
using UnityEngine.UI;

namespace Tichu.Presentation.Tests
{
    public class PlayAnimatorWiringTests
    {
        private sealed class RecordingAnimator : IPlayAnimator
        {
            public int Turn;
            public void PlayedIn(IReadOnlyList<CardView> trickChips, bool fastForward) { }
            public void TurnChanged(Text activeSeatLabel) { Turn++; }
            public void ResultShown(RectTransform banner) { }
        }

        [Test]
        public void TurnChange_routes_to_injected_animator()
        {
            var canvasGo = new GameObject("TestCanvas", typeof(Canvas));
            try
            {
                var rec = new RecordingAnimator();
                var vm = new TableViewModel(0);
                ITableView view = new RuntimeTableView(rec);
                view.Bind(vm, canvasGo.GetComponent<Canvas>(), CancellationToken.None);

                int before = rec.Turn;        // 초기 -1 구독은 호출 안 됨(차례 없음)
                vm.CurrentTurn.Value = 2;      // 파트너 차례
                Assert.AreEqual(before + 1, rec.Turn, "차례 변경이 주입된 애니메이터로 라우팅돼야 한다");
            }
            finally { Object.DestroyImmediate(canvasGo); }
        }

        [Test]
        public void Default_ctor_uses_noop_and_does_not_throw()
        {
            var canvasGo = new GameObject("TestCanvas", typeof(Canvas));
            try
            {
                var vm = new TableViewModel(0);
                ITableView view = new RuntimeTableView(); // 기본 = NoOp
                view.Bind(vm, canvasGo.GetComponent<Canvas>(), CancellationToken.None);
                Assert.DoesNotThrow(() => vm.CurrentTurn.Value = 1);
            }
            finally { Object.DestroyImmediate(canvasGo); }
        }
    }
}
```

- [ ] **Step 3: 실패 확인** — Run: EditMode, filter `PlayAnimatorWiringTests`. Expected: FAIL(컴파일 — `RuntimeTableView(IPlayAnimator)` 생성자 없음).

- [ ] **Step 4: RuntimeTableView 배선** — 4곳 수정.

(4-1) 필드 추가 — `_backPools` 선언 아래(`private readonly CardChipPool[] _backPools ...` 다음 줄):

```csharp
        private readonly IPlayAnimator _anim;
        private readonly List<CardView> _trickChips = new List<CardView>(); // PlayedIn 전달용(GC 회피 재사용)

        public RuntimeTableView(IPlayAnimator anim = null) => _anim = anim ?? new NoOpPlayAnimator();
```

(4-2) `RenderTrick` — 트릭 채움 루프에서 칩을 수집하고 끝에 `PlayedIn` 호출. 기존:

```csharp
            _trickPool.Begin();
            foreach (var card in trick.Top.Cards.OrderBy(SortKey))
            {
                var cv = _trickPool.Next();
                cv.Set(card, _atlas, faceUp: true);
                cv.SetSize(60, 88);
            }
            _trickPool.End();
            _trickOwnerText.text = $"{TypeKo(trick.Top.Type)} · 소유 {SeatNames[trick.TopOwnerSeat]}";
```

교체:

```csharp
            _trickPool.Begin();
            _trickChips.Clear();
            foreach (var card in trick.Top.Cards.OrderBy(SortKey))
            {
                var cv = _trickPool.Next();
                cv.Set(card, _atlas, faceUp: true);
                cv.SetSize(60, 88);
                _trickChips.Add(cv);
            }
            _trickPool.End();
            _trickOwnerText.text = $"{TypeKo(trick.Top.Type)} · 소유 {SeatNames[trick.TopOwnerSeat]}";
            _anim.PlayedIn(_trickChips, _vm.FastForward);
```

(4-3) `UpdateTurnHighlight` — 강조 루프 뒤에 훅 추가. 기존:

```csharp
        private void UpdateTurnHighlight(int turn)
        {
            for (int i = 0; i < 4; i++)
                if (_seatTexts[i] != null)
                    _seatTexts[i].color = (i == turn) ? TurnHi : Ink;
        }
```

교체:

```csharp
        private void UpdateTurnHighlight(int turn)
        {
            for (int i = 0; i < 4; i++)
                if (_seatTexts[i] != null)
                    _seatTexts[i].color = (i == turn) ? TurnHi : Ink;
            if (turn >= 0 && turn < 4 && _seatTexts[turn] != null)
                _anim.TurnChanged(_seatTexts[turn]);
        }
```

(4-4) `RenderResult` — 결과 있을 때 배너 팝 훅. 기존:

```csharp
        private void RenderResult(RoundResult? r)
        {
            _resultPanel.SetActive(r != null);
            _resultText.text = r == null ? "" :
                $"라운드 종료 — 우리 {r.TeamATotal} : 상대 {r.TeamBTotal}  (카드 {r.TeamACardPoints}/{r.TeamBCardPoints}, 티츄 {r.TeamATichuDelta}/{r.TeamBTichuDelta})";
        }
```

교체:

```csharp
        private void RenderResult(RoundResult? r)
        {
            _resultPanel.SetActive(r != null);
            _resultText.text = r == null ? "" :
                $"라운드 종료 — 우리 {r.TeamATotal} : 상대 {r.TeamBTotal}  (카드 {r.TeamACardPoints}/{r.TeamBCardPoints}, 티츄 {r.TeamATichuDelta}/{r.TeamBTichuDelta})";
            if (r != null) _anim.ResultShown((RectTransform)_resultPanel.transform);
        }
```

- [ ] **Step 5: 통과 확인** — Run: EditMode, filter `PlayAnimatorWiringTests`. Expected: PASS(2/2). 이어서 `RuntimeTableViewPoolingTests`도 그린(기존 4/4 회귀 0). `read_console` 에러 0.

- [ ] **Step 6: 커밋**

```bash
git add Assets/_Project/Presentation/Views/IPlayAnimator.cs Assets/_Project/Presentation/Views/RuntimeTableView.cs Assets/_Project/Presentation/Tests/PlayAnimatorWiringTests.cs
git commit -m "feat(p1d): D4 IPlayAnimator 시임 + RuntimeTableView 렌더훅 배선(기본 NoOp)

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task B3: DoTweenPlayAnimator 구현 + RoundBootstrap 주입

**Files:**
- Create: `Assets/_Project/Presentation/Visuals/DoTweenPlayAnimator.cs`
- Modify: `Assets/_Project/Presentation/RoundBootstrap.cs` (생성자 주입 1줄 + using 1줄)

**Interfaces:**
- Consumes: `IPlayAnimator`, `AnimTiming`, `DG.Tweening`(코어 Transform 숏컷).
- Produces: 런타임 연출(플레이 팝인·차례 펄스·결과 팝). EditMode 테스트 없음(DoTween+시각) → PlayMode 육안.

- [ ] **Step 1: 구현** — `DoTweenPlayAnimator.cs`

```csharp
using System.Collections.Generic;
using DG.Tweening;
using Tichu.Presentation.Views;
using UnityEngine;
using UnityEngine.UI;

namespace Tichu.Presentation.Visuals
{
    /// <summary>
    /// DoTween 기반 플레이 연출. 모든 트윈 SetAutoKill, 재사용 대상엔 DOKill 선행.
    /// 풀/레이아웃 호환: localScale만 트윈(position/size는 LayoutGroup이 제어). FastForward면 duration 0.
    /// </summary>
    public sealed class DoTweenPlayAnimator : IPlayAnimator
    {
        public void PlayedIn(IReadOnlyList<CardView> trickChips, bool fastForward)
        {
            float d = AnimTiming.Scale(AnimTiming.PlayPop, fastForward);
            for (int i = 0; i < trickChips.Count; i++)
            {
                var rt = (RectTransform)trickChips[i].transform;
                rt.DOKill();
                rt.localScale = new Vector3(0.7f, 0.7f, 1f);
                rt.DOScale(1f, d).SetEase(Ease.OutBack).SetAutoKill(true);
            }
        }

        public void TurnChanged(Text activeSeatLabel)
        {
            if (activeSeatLabel == null) return;
            var rt = (RectTransform)activeSeatLabel.transform;
            rt.DOKill();
            rt.localScale = Vector3.one;
            rt.DOPunchScale(new Vector3(0.15f, 0.15f, 0f), AnimTiming.TurnPulse, 1, 0.5f).SetAutoKill(true);
        }

        public void ResultShown(RectTransform banner)
        {
            if (banner == null) return;
            banner.DOKill();
            banner.localScale = new Vector3(0.85f, 0.85f, 1f);
            banner.DOScale(1f, AnimTiming.BannerPop).SetEase(Ease.OutBack).SetAutoKill(true);
        }
    }
}
```

- [ ] **Step 2: 컴파일 확인** — `read_console`. Expected: 에러 0. **만약 `DG.Tweening` 미해결이면**(Auto Reference 비활성 환경): Unity에서 `Assets/Plugins/Demigiant/DOTween/DOTween.dll` 인스펙터의 "Auto Reference" 체크 → Apply, 재컴파일. (현재 메타 기준 Auto Reference ON이라 보통 불필요.)

- [ ] **Step 3: RoundBootstrap 주입** — 2곳.

(3-1) using 추가 — 상단 `using Tichu.Presentation.Views;` 아래:

```csharp
using Tichu.Presentation.Visuals;
```

(3-2) 뷰 생성 — 기존:

```csharp
            ITableView view = new RuntimeTableView();
```

교체:

```csharp
            ITableView view = new RuntimeTableView(new DoTweenPlayAnimator());
```

- [ ] **Step 4: EditMode 회귀** — Run: 전체 EditMode. Expected: 전체 그린, 회귀 0. (Tests asmdef는 DoTween 비참조 — `DoTweenPlayAnimator`를 참조하지 않으므로 컴파일 영향 없음.)

- [ ] **Step 5: PlayMode 육안 검증** — Table Play. 스크린샷/관찰:
  - [ ] 누가 카드를 내면 중앙 트릭 카드가 살짝 작게 떴다가(0.7→1) OutBack으로 톡 튀어 안착(플레이 "손맛").
  - [ ] 차례가 넘어갈 때 현재 좌석 이름이 펀치 스케일로 깜빡(차례 인지).
  - [ ] 라운드 종료 시 결과 배너가 0.85→1 팝으로 등장.
  - [ ] 스킵(▶▶) 눌러 FastForward면 연출이 즉시 스냅(딜레이 없이 진행).
  - [ ] 빠른 연속 플레이에서 칩 깜빡임/잔상/예외 없음(DOKill 선행 확인). `read_console` 런타임 에러 0.

- [ ] **Step 6: 커밋**

```bash
git add Assets/_Project/Presentation/Visuals/DoTweenPlayAnimator.cs Assets/_Project/Presentation/RoundBootstrap.cs
git commit -m "feat(p1d): D4 DoTweenPlayAnimator(플레이 팝·차례 펄스·결과 팝) + RoundBootstrap 주입

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## 최종 검증 게이트 (머지 전)

- [ ] **전체 EditMode 그린** — Run: 전체 EditMode. Expected: 기존 311 + 신규(A1:5, A2:3, A3:2, B1:2, B2:2 = 14) ≈ 325 그린, 회귀 0. 오라클 포함 Presentation 스위트 그린.
- [ ] **오라클 비트동일성 확인** — `AsyncContractTests`/오라클 테스트 그린(애니/아트가 `onApply=null` 경로에 닿지 않음 재확인).
- [ ] **PlayMode 통합 육안** — 한 라운드 완주: 카드 아트(프레임/뒷면)·플레이 팝·차례 펄스·결과 팝·FastForward 스냅 모두 정상, 가로 레이아웃 무결.
- [ ] **opus 전체 리뷰** — `/code-review` 또는 적대적 검증(풀 재사용 중 트윈 누수, DOKill 누락, FastForward 경로, null 가드).
- [ ] **머지** — `git checkout main && git merge --no-ff feat/p1d-d4 -m "Merge: P1-D D4 — 카드 아트(코드 생성) + DoTween 애니"` → origin 푸시 → feat 브랜치 삭제(로컬+origin) → 메모리 업데이트.

---

## Self-Review

**Spec coverage (아키텍처 §6.1·§6.5 + 사용자 결정):**
- §6.1 진실/연출 분리 → IPlayAnimator(렌더훅, NoOp 기본), 진실은 ApplySnapshot 유지 → B2 ✅
- §6.1 onApply=null 오라클 보존 → 렌더 미발화 → 최종 게이트에서 재확인 ✅
- §6.1 FastForward 스냅 → AnimTiming.Scale → B1, B3 ✅
- §6.1 대상 모션(플레이·하이라이트) → PlayedIn·TurnChanged·ResultShown → B3 ✅ (딜/회수 애니는 풀/레이아웃 충돌 위험으로 MVP 제외 — 아래 범위 메모)
- 카드 아트(프로그래매틱, 사용자 결정) → CardArtFactory + 아틀라스 폴백 + CardView 프레임 → A1~A4 ✅
- 새 asmdef 0·DoTween 자동참조·테스트 DoTween 비의존(Global Constraints) ✅

**범위 메모(의도적 제외, YAGNI):** 딜 스태거·트릭 회수 슬라이드 애니는 `CardChipPool`의 즉시 SetActive 비활성화 + LayoutGroup 위치제어와 충돌(파괴 후 트윈 불가)하여 MVP에서 제외. 필요 시 후속(D4.1)에서 별도 회수 레이어로 추가. PlayedIn은 매 트릭 변경 시 현재 top 칩을 재팝(누적 파일 아님) — 설계상 "새 플레이 슬랩" 의도와 일치.

**Placeholder scan:** 모든 스텝에 실제 코드/명령/기대출력 포함. TBD/"적절히 처리" 없음. ✅

**Type consistency:** `Frame(Card)`(아틀라스)·`Frame(FrameStyle)`(팩토리)·`StyleFor(Card)`·`Scale(float,bool)`·`PlayedIn/TurnChanged/ResultShown` 시그니처가 A1→A2→A3, B1→B2→B3 사이 일관. `IPlayAnimator`는 Views, `CardArtFactory`/`AnimTiming`/`DoTweenPlayAnimator`는 Visuals(동일 asmdef). ✅
