# 가독성·손맛 개선 (D4.1) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 카드 무늬색 강조를 폭탄 보유 카드 글로우로 재배치하고, 라운드 결과를 팀 합산으로 단순화하고, 티츄 콜을 좌석 배지 + 순간 플래시로 가시화한다.

**Architecture:** D4의 시임을 그대로 확장. 폭탄 탐지는 신규 Core 순수 헬퍼 `BombScanner`, 카드 글로우는 `CardView` 색 우선순위, 티츄 콜은 `TableViewModel`의 R3 투영 + `IPlayAnimator` 연출 훅(`TichuDeclared`). 진실은 `ApplySnapshot`(R3 즉시 갱신), 연출은 렌더 반응 — 오라클 비트동일성 불변.

**Tech Stack:** Unity 6000.3.17f1 · URP3D · R3 · UniTask · DoTween(코어 Transform 숏컷) · NUnit EditMode.

**스펙:** `docs/superpowers/specs/2026-06-19-card-readability-and-tichu-signals-design.md`

## Global Constraints

- **오라클 비트동일성:** 모든 신규 상태/연출은 `ApplySnapshot`/렌더 반응 경로에만 붙는다. `onApply=null`이면 `ApplySnapshot` 미호출 → 새 ReactiveProperty 불변·렌더 훅 미발화 → 비트동일성 무영향.
- **새 asmdef 0개.** 신규 파일은 `Tichu.Core`(BombScanner) / `Tichu.Presentation`(그 외) 안.
- **DoTween 격리:** `DoTweenPlayAnimator`만 `DG.Tweening` 참조. EditMode 테스트는 DoTween 비의존(NoOp/Recording/순수 로직만). 모든 트윈 `SetAutoKill(true)`, 재사용 대상 `DOKill()` 선행, **localScale만** 트윈.
- **풀링 호환:** 풀 재사용 새 플래그(`_bombMember`)는 `CardView.Set`에서 중립화.
- **테스트 실행(D3 교훈):** `run_tests`는 항상 **단일 어셈블리**씩. Presentation 태스크 → `Tichu.Presentation.Tests`, Core 태스크(R2) → `Tichu.Core.Tests`. 두 어셈블리 동시 실행 금지(에디터 stuck).
- **신규 .cs는 .meta 동반 커밋.** 신규 파일 추가 후 `refresh_unity(scope="all", mode="force")`(scope="scripts"는 신규파일 누락 가능).
- **커밋 컨벤션:** `feat(p1d): ...` 한국어 + `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>` 트레일러.
- **현재 브랜치:** `feat/p1d-d4`. 전환/리베이스 금지.

**Unity 테스트 레시피:** 파일 작성 → `refresh_unity` → `read_console(types=["error"])`(0 확인, "Saving results to..." 정보성 무시) → `run_tests(assembly_names=[<단일>], mode="EditMode")` → `get_test_job(job_id, wait_timeout=60)`.

**현 베이스라인:** `Tichu.Presentation.Tests` 88/88 그린(D4 완료, HEAD f4a01a9 시점 스펙 커밋 포함). `Tichu.Core.Tests`는 실행해 베이스라인 확인 후 비교.

---

## File Structure

| 파일 | 역할 | 신규/수정 | 태스크 |
|---|---|---|---|
| `Core/Combinations/BombScanner.cs` | 손패→폭탄 카드 집합(순수, 문맥무관) | 신규 | R2 |
| `Presentation/Visuals/CardArtFactory.cs` | FrameStyle {Normal,Special} 축소 | 수정 | R1 |
| `Presentation/Views/CardView.cs` | `SetBombMember`/`IsBombMember` + 색 우선순위 | 수정 | R3 |
| `Presentation/Views/RuntimeTableView.cs` | 폭탄 스캔/적용·결과 문자열·티츄 배지/전이 | 수정 | R4·R5·R7 |
| `Presentation/ViewModel/TableViewModel.cs` | `SeatCall(seat)` 투영 | 수정 | R6 |
| `Presentation/Views/IPlayAnimator.cs` | `TichuDeclared` + NoOp 구현 | 수정 | R7 |
| `Presentation/Visuals/DoTweenPlayAnimator.cs` | `TichuDeclared` DoTween 구현 | 수정 | R7 |
| (테스트들) | 아래 각 태스크 | 신규/수정 | 전 태스크 |

---

## Task R1: CardArtFactory — FrameStyle {Normal, Special} 축소

**Files:**
- Modify: `Assets/_Project/Presentation/Visuals/CardArtFactory.cs`
- Test: `Assets/_Project/Presentation/Tests/CardArtFactoryTests.cs`

**Interfaces:**
- Produces: `enum CardArtFactory.FrameStyle { Normal, Special }`; `StyleFor(Card)` → 특수=Special, 그외=Normal; `Frame(FrameStyle)` non-null·캐시.

- [ ] **Step 1: 테스트 갱신(RED)** — `CardArtFactoryTests.cs`의 3개 테스트를 아래로 교체.

`Frame_is_nonnull_and_cached_per_style` 교체:
```csharp
        [Test]
        public void Frame_is_nonnull_and_cached_per_style()
        {
            var f = new CardArtFactory();
            foreach (var s in new[] { CardArtFactory.FrameStyle.Normal, CardArtFactory.FrameStyle.Special })
            {
                var sp = f.Frame(s);
                Assert.IsNotNull(sp, $"{s} 프레임 non-null");
                Assert.AreSame(sp, f.Frame(s), $"{s} 프레임 캐시");
            }
            Assert.AreNotSame(f.Frame(CardArtFactory.FrameStyle.Normal), f.Frame(CardArtFactory.FrameStyle.Special),
                "스타일이 다르면 다른 스프라이트");
        }
```

`Frame_has_transparent_rounded_corner`의 `FrameStyle.Black` → `FrameStyle.Normal`:
```csharp
            var tex = f.Frame(CardArtFactory.FrameStyle.Normal).texture;
```

`StyleFor_maps_color_and_special` 교체:
```csharp
        [Test]
        public void StyleFor_maps_color_and_special()
        {
            Assert.AreEqual(CardArtFactory.FrameStyle.Normal, CardArtFactory.StyleFor(Card.Normal(14, Suit.Star)));
            Assert.AreEqual(CardArtFactory.FrameStyle.Normal, CardArtFactory.StyleFor(Card.Normal(7, Suit.Pagoda)));
            Assert.AreEqual(CardArtFactory.FrameStyle.Normal, CardArtFactory.StyleFor(Card.Normal(13, Suit.Sword)));
            Assert.AreEqual(CardArtFactory.FrameStyle.Normal, CardArtFactory.StyleFor(Card.Normal(2, Suit.Jade)));
            Assert.AreEqual(CardArtFactory.FrameStyle.Special, CardArtFactory.StyleFor(Card.Dragon));
            Assert.AreEqual(CardArtFactory.FrameStyle.Special, CardArtFactory.StyleFor(Card.Mahjong));
        }
```

- [ ] **Step 2: 실패 확인** — refresh → Run `Tichu.Presentation.Tests`. Expected: 컴파일 에러(`FrameStyle.Black`/`.Red` 없음 — 구현 교체 전).

- [ ] **Step 3: 구현(GREEN)** — `CardArtFactory.cs` 3곳 교체.

(3-1) enum:
```csharp
        public enum FrameStyle { Normal, Special }
```
(3-2) `EdgeRed` 필드 줄 삭제(`private static readonly Color EdgeRed = ...;` 제거). `EdgeBlk`·`EdgeGold` 유지.
(3-3) `StyleFor`·`EdgeFor` 교체:
```csharp
        public static FrameStyle StyleFor(Card card) =>
            card.IsSpecial ? FrameStyle.Special : FrameStyle.Normal;

        private static Color EdgeFor(FrameStyle s) =>
            s == FrameStyle.Special ? EdgeGold : EdgeBlk;
```

- [ ] **Step 4: 통과 확인** — refresh → `read_console`(에러 0) → Run `Tichu.Presentation.Tests`. Expected: 88/88(개수 불변, 단언만 갱신). `CardSpriteAtlasTests`/`CardViewTests`(생성 아틀라스 → Normal 프레임 non-null) 그린 유지.

- [ ] **Step 5: 커밋**
```bash
git add Assets/_Project/Presentation/Visuals/CardArtFactory.cs Assets/_Project/Presentation/Tests/CardArtFactoryTests.cs
git commit -m "feat(p1d): D4.1 CardArtFactory 무늬색 제거 — FrameStyle {Normal,Special}

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task R2: BombScanner — 손패 폭탄 탐지(Core 순수)

**Files:**
- Create: `Assets/_Project/Core/Combinations/BombScanner.cs`
- Test: `Assets/_Project/Tests/EditMode/BombScannerTests.cs`

**Interfaces:**
- Produces: `static HashSet<Card> BombScanner.BombCards(IReadOnlyList<Card> hand)` (네임스페이스 `Tichu.Core.Combinations`).

- [ ] **Step 1: 실패 테스트 작성** — `BombScannerTests.cs`
```csharp
using System.Collections.Generic;
using NUnit.Framework;
using Tichu.Core.Cards;
using Tichu.Core.Combinations;

namespace Tichu.Core.Tests
{
    public class BombScannerTests
    {
        private static List<Card> Hand(params Card[] cards) => new List<Card>(cards);

        [Test]
        public void Four_of_a_kind_marks_all_four()
        {
            var hand = Hand(Card.Normal(7, Suit.Jade), Card.Normal(7, Suit.Sword),
                            Card.Normal(7, Suit.Pagoda), Card.Normal(7, Suit.Star), Card.Normal(2, Suit.Jade));
            var bombs = BombScanner.BombCards(hand);
            Assert.AreEqual(4, bombs.Count);
            Assert.IsTrue(bombs.Contains(Card.Normal(7, Suit.Jade)));
            Assert.IsFalse(bombs.Contains(Card.Normal(2, Suit.Jade)));
        }

        [Test]
        public void No_bomb_returns_empty()
        {
            var hand = Hand(Card.Normal(7, Suit.Jade), Card.Normal(8, Suit.Sword), Card.Normal(9, Suit.Pagoda));
            Assert.AreEqual(0, BombScanner.BombCards(hand).Count);
        }

        [Test]
        public void Straight_flush_five_marks_all_five()
        {
            var hand = Hand(Card.Normal(3, Suit.Star), Card.Normal(4, Suit.Star), Card.Normal(5, Suit.Star),
                            Card.Normal(6, Suit.Star), Card.Normal(7, Suit.Star), Card.Normal(9, Suit.Jade));
            var bombs = BombScanner.BombCards(hand);
            Assert.AreEqual(5, bombs.Count);
            Assert.IsFalse(bombs.Contains(Card.Normal(9, Suit.Jade)));
        }

        [Test]
        public void Straight_flush_six_marks_all_six()
        {
            var hand = Hand(Card.Normal(3, Suit.Star), Card.Normal(4, Suit.Star), Card.Normal(5, Suit.Star),
                            Card.Normal(6, Suit.Star), Card.Normal(7, Suit.Star), Card.Normal(8, Suit.Star));
            Assert.AreEqual(6, BombScanner.BombCards(hand).Count);
        }

        [Test]
        public void Four_consecutive_same_suit_is_not_bomb()
        {
            var hand = Hand(Card.Normal(3, Suit.Star), Card.Normal(4, Suit.Star),
                            Card.Normal(5, Suit.Star), Card.Normal(6, Suit.Star));
            Assert.AreEqual(0, BombScanner.BombCards(hand).Count);
        }

        [Test]
        public void Phoenix_never_in_bomb()
        {
            var hand = Hand(Card.Normal(7, Suit.Jade), Card.Normal(7, Suit.Sword),
                            Card.Normal(7, Suit.Pagoda), Card.Phoenix);
            var bombs = BombScanner.BombCards(hand);
            Assert.IsFalse(bombs.Contains(Card.Phoenix));
            Assert.AreEqual(0, bombs.Count);
        }

        [Test]
        public void Card_in_both_four_and_straightflush_counted_once()
        {
            var hand = Hand(
                Card.Normal(5, Suit.Star), Card.Normal(5, Suit.Jade), Card.Normal(5, Suit.Sword), Card.Normal(5, Suit.Pagoda),
                Card.Normal(3, Suit.Star), Card.Normal(4, Suit.Star), Card.Normal(6, Suit.Star), Card.Normal(7, Suit.Star));
            var bombs = BombScanner.BombCards(hand);
            Assert.AreEqual(8, bombs.Count);
            Assert.IsTrue(bombs.Contains(Card.Normal(5, Suit.Star)));
        }

        [Test]
        public void Mahjong_not_in_straight_flush()
        {
            var hand = Hand(Card.Mahjong, Card.Normal(2, Suit.Star), Card.Normal(3, Suit.Star),
                            Card.Normal(4, Suit.Star), Card.Normal(5, Suit.Star));
            Assert.AreEqual(0, BombScanner.BombCards(hand).Count);
        }
    }
}
```

- [ ] **Step 2: 실패 확인** — refresh(force, 신규파일) → Run `Tichu.Core.Tests`. Expected: 컴파일 에러(`BombScanner` 없음).

- [ ] **Step 3: 구현** — `BombScanner.cs`
```csharp
using System.Collections.Generic;
using Tichu.Core.Cards;

namespace Tichu.Core.Combinations
{
    /// <summary>
    /// 손패에서 폭탄(4카드 동값 / 같은 무늬 5장 이상 연속)에 속한 카드들의 집합을 찾는다.
    /// 봉황 제외, 턴·트릭 문맥 무관(들고만 있으면 폭탄). UI 강조용.
    /// </summary>
    public static class BombScanner
    {
        public static HashSet<Card> BombCards(IReadOnlyList<Card> hand)
        {
            var result = new HashSet<Card>();
            if (hand == null || hand.Count == 0) return result;

            // 1) 4카드 폭탄: 같은 일반 랭크 4장 이상.
            var byRank = new List<Card>[15]; // index 1..14
            for (int r = 0; r < 15; r++) byRank[r] = new List<Card>();
            foreach (var c in hand)
                if (!c.IsSpecial) byRank[c.Rank].Add(c);
            for (int r = 2; r <= 14; r++)
                if (byRank[r].Count >= 4)
                    foreach (var c in byRank[r]) result.Add(c);

            // 2) 스트레이트플러시 폭탄: 같은 무늬 연속 길이 >=5인 극대 구간 전체.
            foreach (Suit suit in new[] { Suit.Jade, Suit.Sword, Suit.Pagoda, Suit.Star })
            {
                var suited = new Card[15];
                var has = new bool[15];
                foreach (var c in hand)
                    if (!c.IsSpecial && c.Suit == suit) { suited[c.Rank] = c; has[c.Rank] = true; }

                int run = 0;
                for (int r = 2; r <= 15; r++) // r=15 = sentinel: 끝 구간 flush
                {
                    bool present = r <= 14 && has[r];
                    if (present) { run++; }
                    else
                    {
                        if (run >= 5)
                            for (int k = r - run; k <= r - 1; k++) result.Add(suited[k]);
                        run = 0;
                    }
                }
            }
            return result;
        }
    }
}
```

- [ ] **Step 4: 통과 확인** — refresh → `read_console`(에러 0) → Run `Tichu.Core.Tests`. Expected: 베이스라인+8 그린, 0 실패.

- [ ] **Step 5: 커밋**
```bash
git add Assets/_Project/Core/Combinations/BombScanner.cs Assets/_Project/Core/Combinations/BombScanner.cs.meta Assets/_Project/Tests/EditMode/BombScannerTests.cs Assets/_Project/Tests/EditMode/BombScannerTests.cs.meta
git commit -m "feat(p1d): D4.1 BombScanner — 손패 폭탄 카드 탐지(Core 순수)

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task R3: CardView — 폭탄 멤버 글로우

**Files:**
- Modify: `Assets/_Project/Presentation/Views/CardView.cs`
- Test: `Assets/_Project/Presentation/Tests/CardViewTests.cs`

**Interfaces:**
- Consumes: 없음.
- Produces: `void CardView.SetBombMember(bool on)`; `bool CardView.IsBombMember { get; }`. 색 우선순위 선택 > 폭탄 > 배정 > 일반.

- [ ] **Step 1: 실패 테스트 추가** — `CardViewTests.cs`에 추가(`using`은 기존 `Tichu.Core.Cards`/`UnityEngine.UI` 있음).
```csharp
        [Test]
        public void BombMember_tints_background_red_when_not_selected()
        {
            var cv = New(out var go);
            cv.Set(Card.Normal(7, Suit.Jade), null, faceUp: true);
            cv.SetBombMember(true);
            var bg = go.GetComponent<Image>();
            Assert.AreEqual(new Color(0.96f, 0.55f, 0.52f), bg.color);
            Assert.IsTrue(cv.IsBombMember);
            Object.DestroyImmediate(go);
        }

        [Test]
        public void Selected_wins_over_bomb_member()
        {
            var cv = New(out var go);
            cv.Set(Card.Normal(7, Suit.Jade), null, faceUp: true);
            cv.SetBombMember(true);
            cv.SetHighlight(CardView.Highlight.Selected);
            var bg = go.GetComponent<Image>();
            Assert.AreEqual(new Color(1.00f, 0.86f, 0.32f), bg.color); // CardSel
            Object.DestroyImmediate(go);
        }

        [Test]
        public void Set_neutralizes_bomb_member_for_pool_reuse()
        {
            var cv = New(out var go);
            cv.Set(Card.Normal(7, Suit.Jade), null, faceUp: true);
            cv.SetBombMember(true);
            Assert.IsTrue(cv.IsBombMember);
            cv.Set(Card.Normal(8, Suit.Jade), null, faceUp: true);
            Assert.IsFalse(cv.IsBombMember, "Set 은 풀 재사용 위해 폭탄 멤버를 중립화");
            Object.DestroyImmediate(go);
        }
```

- [ ] **Step 2: 실패 확인** — refresh → Run `Tichu.Presentation.Tests`. Expected: 컴파일 에러(`SetBombMember`/`IsBombMember` 없음).

- [ ] **Step 3: 구현** — `CardView.cs` 4곳.

(3-1) 색 상수 추가(`CardUse` 줄 아래):
```csharp
        private static readonly Color CardBomb = new Color(0.96f, 0.55f, 0.52f); // 폭탄 보유 글로우
```
(3-2) 필드 + 접근자 추가(`_highlight` 필드 근처):
```csharp
        private bool _bombMember;

        /// <summary>이 카드가 내 손패의 폭탄 조합에 속하는지(빨강 글로우).</summary>
        public bool IsBombMember => _bombMember;

        /// <summary>폭탄 멤버 표시 토글(선택보다 낮은 우선순위 빨강 글로우).</summary>
        public void SetBombMember(bool on)
        {
            EnsureBuilt();
            _bombMember = on;
            Refresh();
        }
```
(3-3) `Set()`에서 `_highlight = Highlight.Normal;` 옆에 중립화 추가:
```csharp
            _card = card; _atlas = atlas; _faceUp = faceUp; _highlight = Highlight.Normal; _bombMember = false;
```
(3-4) `HighlightColor()` 교체(switch → 우선순위):
```csharp
        private Color HighlightColor()
        {
            if (_highlight == Highlight.Selected) return CardSel;
            if (_bombMember) return CardBomb;
            if (_highlight == Highlight.Assigned) return CardUse;
            return CardBg;
        }
```

- [ ] **Step 4: 통과 확인** — refresh → `read_console`(에러 0) → Run `Tichu.Presentation.Tests`. Expected: 91/91(88+3). 기존 CardView/선택 테스트 회귀 0.

- [ ] **Step 5: 커밋**
```bash
git add Assets/_Project/Presentation/Views/CardView.cs Assets/_Project/Presentation/Tests/CardViewTests.cs
git commit -m "feat(p1d): D4.1 CardView 폭탄 멤버 글로우(선택>폭탄>배정>일반)

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task R4: RuntimeTableView — 내 손패 폭탄 반영

**Files:**
- Modify: `Assets/_Project/Presentation/Views/RuntimeTableView.cs`
- Test: `Assets/_Project/Presentation/Tests/RuntimeTableViewPoolingTests.cs`

**Interfaces:**
- Consumes: `BombScanner.BombCards` (R2), `CardView.SetBombMember`/`IsBombMember` (R3).

- [ ] **Step 1: 실패 테스트 추가** — `RuntimeTableViewPoolingTests.cs`. 상단 using에 `using Tichu.Core.Cards;` 추가. 테스트 추가:
```csharp
        [Test]
        public void Hand_marks_bomb_cards()
        {
            var canvasGo = new GameObject("TestCanvas", typeof(Canvas));
            try
            {
                var vm = new TableViewModel(0);
                ITableView view = new RuntimeTableView();
                view.Bind(vm, canvasGo.GetComponent<Canvas>(), CancellationToken.None);

                vm.MyHand.Value = new System.Collections.Generic.List<Card>
                {
                    Card.Normal(7, Suit.Jade), Card.Normal(7, Suit.Sword),
                    Card.Normal(7, Suit.Pagoda), Card.Normal(7, Suit.Star), Card.Normal(2, Suit.Jade),
                };

                var hand = FindByName(canvasGo, "Hand");
                int total = 0, bombMarked = 0;
                foreach (var cv in hand.GetComponentsInChildren<CardView>(false))
                {
                    total++;
                    if (cv.IsBombMember) bombMarked++;
                }
                Assert.AreEqual(5, total, "손패 5장 렌더");
                Assert.AreEqual(4, bombMarked, "네 장의 7 = 폭탄 멤버");
            }
            finally { Object.DestroyImmediate(canvasGo); }
        }
```

- [ ] **Step 2: 실패 확인** — refresh → Run `Tichu.Presentation.Tests`. Expected: FAIL(`bombMarked`=0 — 아직 미적용).

- [ ] **Step 3: 구현** — `RuntimeTableView.cs` 3곳.

(3-1) 필드 추가(`_hand` 필드 근처):
```csharp
        private HashSet<Card> _bombCards = new HashSet<Card>();
```
(3-2) `MyHand` 구독에 스캔 추가. 기존:
```csharp
            _vm.MyHand.Subscribe(h => { _hand = h ?? new List<Card>(); _selection.RemoveAll(c => !_hand.Contains(c)); RenderHand(); }).AddTo(_subs);
```
교체:
```csharp
            _vm.MyHand.Subscribe(h => { _hand = h ?? new List<Card>(); _selection.RemoveAll(c => !_hand.Contains(c)); _bombCards = BombScanner.BombCards(_hand); RenderHand(); }).AddTo(_subs);
```
(3-3) `RenderHand`의 카드 루프에 1줄 추가. 기존:
```csharp
                var cv = _handPool.Next();
                cv.Set(card, _atlas, faceUp: true);
                cv.SetSize(66, 100);
```
교체:
```csharp
                var cv = _handPool.Next();
                cv.Set(card, _atlas, faceUp: true);
                cv.SetSize(66, 100);
                cv.SetBombMember(_bombCards.Contains(card));
```
(`using Tichu.Core.Combinations;`는 기존 존재 — `CombinationRecognizer` 사용 중.)

- [ ] **Step 4: 통과 확인** — refresh → `read_console`(에러 0) → Run `Tichu.Presentation.Tests`. Expected: 92/92(91+1). 풀링 회귀 4개 그린.

- [ ] **Step 5: 커밋**
```bash
git add Assets/_Project/Presentation/Views/RuntimeTableView.cs Assets/_Project/Presentation/Tests/RuntimeTableViewPoolingTests.cs
git commit -m "feat(p1d): D4.1 RuntimeTableView 내 손패 폭탄 글로우 반영

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task R5: 결과 배너 합산만

**Files:**
- Modify: `Assets/_Project/Presentation/Views/RuntimeTableView.cs` (`RenderResult` 문자열 1줄)

**Interfaces:** 없음(문자열 변경).

- [ ] **Step 1: 구현** — `RenderResult`의 `_resultText.text` 문자열 교체. 기존:
```csharp
            _resultText.text = r == null ? "" :
                $"라운드 종료 — 우리 {r.TeamATotal} : 상대 {r.TeamBTotal}  (카드 {r.TeamACardPoints}/{r.TeamBCardPoints}, 티츄 {r.TeamATichuDelta}/{r.TeamBTichuDelta})";
```
교체:
```csharp
            _resultText.text = r == null ? "" :
                $"라운드 종료 — 우리 {r.TeamATotal} : 상대 {r.TeamBTotal}";
```
(`_anim.ResultShown(...)` 줄은 그대로 유지.)

- [ ] **Step 2: 회귀 확인** — refresh → `read_console`(에러 0) → Run `Tichu.Presentation.Tests`. Expected: 92/92 그린(개수 불변 — 문자열 변경에 테스트 없음, 회귀만 확인). PlayMode 육안은 최종 세션에서.

- [ ] **Step 3: 커밋**
```bash
git add Assets/_Project/Presentation/Views/RuntimeTableView.cs
git commit -m "feat(p1d): D4.1 결과 배너 팀 합산만 표시(카드/티츄 분해 제거)

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task R6: TableViewModel — 좌석별 티츄 콜 투영

**Files:**
- Modify: `Assets/_Project/Presentation/ViewModel/TableViewModel.cs`
- Test: `Assets/_Project/Presentation/Tests/TableViewModelTests.cs`

**Interfaces:**
- Produces: `ReactiveProperty<TichuCall> TableViewModel.SeatCall(int seat)`.

- [ ] **Step 1: 실패 테스트 추가** — `TableViewModelTests.cs`. 상단 using에 `using Tichu.Core.Game;`가 없으면 추가(GameEngine/TichuCall). 테스트:
```csharp
        [Test]
        public void ApplySnapshot_projects_seat_calls()
        {
            var vm = new TableViewModel(0);
            var state = GameEngine.NewRound(123UL);
            state.Seats[2].Call = TichuCall.GrandTichu;
            state.Seats[1].Call = TichuCall.Tichu;
            vm.ApplySnapshot(state);
            Assert.AreEqual(TichuCall.GrandTichu, vm.SeatCall(2).CurrentValue);
            Assert.AreEqual(TichuCall.Tichu, vm.SeatCall(1).CurrentValue);
            Assert.AreEqual(TichuCall.None, vm.SeatCall(0).CurrentValue);
        }
```

- [ ] **Step 2: 실패 확인** — refresh → Run `Tichu.Presentation.Tests`. Expected: 컴파일 에러(`SeatCall` 없음).

- [ ] **Step 3: 구현** — `TableViewModel.cs` 3곳.

(3-1) 필드 추가(`_handCounts` 배열 근처):
```csharp
        private readonly ReactiveProperty<TichuCall>[] _calls = new ReactiveProperty<TichuCall>[4];

        /// <summary>좌석 i 의 티츄 콜 상태(None/Tichu/GrandTichu).</summary>
        public ReactiveProperty<TichuCall> SeatCall(int seat) => _calls[seat];
```
(3-2) 생성자 루프에 초기화 추가. 기존:
```csharp
            for (int i = 0; i < 4; i++)
                _handCounts[i] = new ReactiveProperty<int>(0);
```
교체:
```csharp
            for (int i = 0; i < 4; i++)
            {
                _handCounts[i] = new ReactiveProperty<int>(0);
                _calls[i] = new ReactiveProperty<TichuCall>(TichuCall.None);
            }
```
(3-3) `ApplySnapshot`의 손패수 루프에 콜 투영 추가. 기존:
```csharp
            for (int i = 0; i < 4; i++)
                _handCounts[i].Value = s.Seats[i].Hand.Count;
```
교체:
```csharp
            for (int i = 0; i < 4; i++)
            {
                _handCounts[i].Value = s.Seats[i].Hand.Count;
                _calls[i].Value = s.Seats[i].Call;
            }
```
(`using Tichu.Core.Game;`는 기존 존재 — RoundPhase/GameState 사용 중.)

- [ ] **Step 4: 통과 확인** — refresh → `read_console`(에러 0) → Run `Tichu.Presentation.Tests`. Expected: 93/93(92+1). 회귀 0.

- [ ] **Step 5: 커밋**
```bash
git add Assets/_Project/Presentation/ViewModel/TableViewModel.cs Assets/_Project/Presentation/Tests/TableViewModelTests.cs
git commit -m "feat(p1d): D4.1 TableViewModel 좌석별 티츄 콜 투영(SeatCall)

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task R7: 티츄 배지 + 콜 플래시 (IPlayAnimator 확장 + 뷰 배선 + DoTween)

**Files:**
- Modify: `Assets/_Project/Presentation/Views/IPlayAnimator.cs` (인터페이스 + NoOp)
- Modify: `Assets/_Project/Presentation/Visuals/DoTweenPlayAnimator.cs`
- Modify: `Assets/_Project/Presentation/Views/RuntimeTableView.cs`
- Test: `Assets/_Project/Presentation/Tests/PlayAnimatorWiringTests.cs`

**Interfaces:**
- Consumes: `TableViewModel.SeatCall` (R6), `AnimTiming.TurnPulse` (D4).
- Produces: `IPlayAnimator.TichuDeclared(RectTransform badge)`.

> ⚠️ **원자성:** `IPlayAnimator`에 메서드를 추가하면 모든 구현체(NoOp·DoTween·테스트 Recording)가 동시에 구현해야 컴파일된다. 이 태스크에서 한꺼번에 처리한다.

- [ ] **Step 1: 실패 테스트 추가** — `PlayAnimatorWiringTests.cs`. 상단 using에 `using Tichu.Core.Game;` 추가. `RecordingAnimator`에 `Tichu` 카운터 + `TichuDeclared` 추가, 테스트 1개 추가.

`RecordingAnimator` 교체:
```csharp
        private sealed class RecordingAnimator : IPlayAnimator
        {
            public int Turn, Tichu;
            public void PlayedIn(IReadOnlyList<CardView> trickChips, bool fastForward) { }
            public void TurnChanged(Text activeSeatLabel) { Turn++; }
            public void ResultShown(RectTransform banner) { }
            public void TichuDeclared(RectTransform badge) { Tichu++; }
        }
```
테스트 추가:
```csharp
        [Test]
        public void TichuCall_transition_routes_to_animator_once()
        {
            var canvasGo = new GameObject("TestCanvas", typeof(Canvas));
            try
            {
                var rec = new RecordingAnimator();
                var vm = new TableViewModel(0);
                ITableView view = new RuntimeTableView(rec);
                view.Bind(vm, canvasGo.GetComponent<Canvas>(), CancellationToken.None);

                Assert.AreEqual(0, rec.Tichu, "초기 None 은 발화 안 함");
                vm.SeatCall(2).Value = TichuCall.GrandTichu;
                Assert.AreEqual(1, rec.Tichu, "None→콜 전이 1회 발화");
                vm.SeatCall(2).Value = TichuCall.GrandTichu;
                Assert.AreEqual(1, rec.Tichu, "동일 콜 재투영은 추가 발화 없음");
            }
            finally { Object.DestroyImmediate(canvasGo); }
        }
```

- [ ] **Step 2: 실패 확인** — refresh → Run `Tichu.Presentation.Tests`. Expected: 컴파일 에러(`TichuDeclared` 인터페이스 미정의 → NoOp/DoTween 미구현).

- [ ] **Step 3: 인터페이스 + NoOp** — `IPlayAnimator.cs`. 인터페이스에 메서드 추가:
```csharp
        /// <summary>티츄 콜이 새로 선언됐을 때(좌석 배지). null이면 무시.</summary>
        void TichuDeclared(RectTransform badge);
```
`NoOpPlayAnimator`에 구현 추가:
```csharp
        public void TichuDeclared(RectTransform badge) { }
```

- [ ] **Step 4: DoTween 구현** — `DoTweenPlayAnimator.cs`에 메서드 추가:
```csharp
        public void TichuDeclared(RectTransform badge)
        {
            if (badge == null) return;
            badge.DOKill();
            badge.localScale = Vector3.one;
            badge.DOPunchScale(new Vector3(0.4f, 0.4f, 0f), AnimTiming.TurnPulse, 1, 0.5f).SetAutoKill(true);
        }
```

- [ ] **Step 5: RuntimeTableView 배지 + 전이** — 5곳.

(5-1) 색 상수 + 상태 필드 추가(색 상수 블록 / `_seatTexts` 근처):
```csharp
        private static readonly Color TichuPurple = new Color(0.55f, 0.35f, 0.78f);
        private static readonly Color GrandGold   = new Color(0.85f, 0.66f, 0.22f);

        private readonly Image[] _callBadgeBg = new Image[4];
        private readonly Text[] _callBadgeText = new Text[4];
        private readonly TichuCall[] _prevCalls = new TichuCall[4]; // 초기 None
```
(5-2) 배지 빌더 메서드 추가(헬퍼 영역):
```csharp
        private void BuildCallBadge(int seat, RectTransform parent, Vector2 anchor, Vector2 pos)
        {
            var go = new GameObject($"CallBadge{seat}", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = anchor;
            rt.sizeDelta = new Vector2(96, 34); rt.anchoredPosition = pos;
            _callBadgeBg[seat] = go.GetComponent<Image>();
            var t = NewText($"CallBadgeTxt{seat}", go.transform, "", 22);
            t.alignment = TextAnchor.MiddleCenter; t.color = Ink;
            StretchFull(t.rectTransform);
            _callBadgeText[seat] = t;
            go.SetActive(false);
        }

        private void BuildCallBadges(RectTransform rt)
        {
            BuildCallBadge(0, rt, new Vector2(0.5f, 0), new Vector2(250, 320)); // 나(남쪽 라벨 옆)
            BuildCallBadge(2, rt, new Vector2(0.5f, 1), new Vector2(110, -40));  // 파트너(상)
            BuildCallBadge(3, rt, new Vector2(0, 0.5f), new Vector2(150, 70));   // 왼쪽
            BuildCallBadge(1, rt, new Vector2(1, 0.5f), new Vector2(-150, 70));  // 오른쪽
        }
```
(5-3) `BuildLayout` 끝(메서드 마지막, `_skipButton = skip;` 다음 줄)에서 호출:
```csharp
            BuildCallBadges(rt);
```
(5-4) `UpdateCallBadge` 메서드 추가(구독 영역 근처):
```csharp
        private void UpdateCallBadge(int seat, TichuCall call)
        {
            var prev = _prevCalls[seat];
            _prevCalls[seat] = call; // 항상 갱신(라운드 리셋 None 반영)

            var bg = _callBadgeBg[seat];
            if (bg == null) return;
            bool active = call != TichuCall.None;
            bg.gameObject.SetActive(active);
            if (active)
            {
                bool grand = call == TichuCall.GrandTichu;
                bg.color = grand ? GrandGold : TichuPurple;
                _callBadgeText[seat].text = grand ? "大 티츄" : "티츄";
            }
            if (call != TichuCall.None && call != prev)
                _anim.TichuDeclared((RectTransform)bg.transform);
        }
```
(5-5) `Subscribe()`에 구독 추가(예: `PendingDecision` 구독 줄 근처):
```csharp
            for (int i = 0; i < 4; i++)
            {
                int seat = i;
                _vm.SeatCall(seat).Subscribe(call => UpdateCallBadge(seat, call)).AddTo(_subs);
            }
```

- [ ] **Step 6: 통과 확인** — refresh(force) → `read_console`(에러 0 — DoTweenPlayAnimator·NoOp·RuntimeTableView 모두 컴파일) → Run `Tichu.Presentation.Tests`. Expected: 94/94(93+1). 회귀 0(기존 배선 테스트 2개 그린).

- [ ] **Step 7: 커밋**
```bash
git add Assets/_Project/Presentation/Views/IPlayAnimator.cs Assets/_Project/Presentation/Visuals/DoTweenPlayAnimator.cs Assets/_Project/Presentation/Views/RuntimeTableView.cs Assets/_Project/Presentation/Tests/PlayAnimatorWiringTests.cs
git commit -m "feat(p1d): D4.1 티츄 콜 좌석 배지 + 콜 순간 플래시(IPlayAnimator.TichuDeclared)

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## 최종 검증 게이트 (머지 전)

- [ ] **전체 EditMode 그린** — `Tichu.Presentation.Tests` 94/94 + `Tichu.Core.Tests` 베이스라인+8, 회귀 0(어셈블리 1개씩).
- [ ] **오라클 비트동일성** — `AsyncContractTests`/오라클 그린(BombScanner/SeatCall/배지/플래시가 `onApply=null` 경로 미접촉 재확인).
- [ ] **PlayMode 통합 육안(사용자)** — D4 카드아트/애니 + D4.1:
  - [ ] 빨강/검정 카드 테두리 동일(무늬색 글로우 없음), 빨강은 라벨 텍스트로만 식별.
  - [ ] 내가 4카드/스플 폭탄 보유 시 그 카드만 빨강 글로우, 선택 시 노랑 우선.
  - [ ] 라운드 종료 배너 = "우리 N : 상대 M" (분해 없음).
  - [ ] 상대/파트너 티츄 콜 시 좌석 배지(티츄=보라/大티츄=금) 표시 + 콜 순간 펀치. 배지 위치 겹침 없음(필요 시 좌표 미세조정).
  - [ ] 내 티츄 선언도 내 배지 표시.
- [ ] **opus 전체-브랜치 리뷰** — D4 8커밋 + D4.1 7커밋 합산(머지 전).
- [ ] **머지** — `feat/p1d-d4` → main `--no-ff` → origin 푸시 → feat 삭제 → 메모리 갱신.

---

## Self-Review

**Spec coverage:**
- §3.1 무늬색 제거 → R1 ✅
- §3.2 BombScanner → R2 ✅
- §3.3 CardView 글로우 → R3 ✅
- §3.4 RuntimeTableView 폭탄 반영 → R4 ✅
- §4 결과 배너 → R5 ✅
- §5.1 SeatCall 투영 → R6 ✅
- §5.2 배지/전이 + §5.3 TichuDeclared → R7 ✅
- 전역 불변식(오라클/asmdef/DoTween/풀링) → 각 태스크 + 최종 게이트 ✅

**Placeholder scan:** 모든 스텝 실제 코드/명령/기대출력 포함. TBD 없음. ✅ (배지 좌표는 구체값 — PlayMode에서 미세조정 가능하나 placeholder 아님.)

**Type consistency:** `FrameStyle{Normal,Special}`·`StyleFor`(R1) / `BombCards`(R2) / `SetBombMember`·`IsBombMember`·`CardBomb`(R3) / `_bombCards`(R4) / `SeatCall`(R6) / `TichuDeclared(RectTransform)`·`_callBadgeBg/_callBadgeText/_prevCalls`·`UpdateCallBadge`(R7) 일관. R4가 R2·R3 소비, R7이 R6 소비 — 순서 정합. ✅
