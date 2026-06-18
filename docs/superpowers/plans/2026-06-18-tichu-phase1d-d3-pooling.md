# D3 — 카드 풀링 + Canvas 정적/동적 분리 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** `RuntimeTableView`의 손패/트릭/뒷면 렌더가 매 스냅샷마다 카드 뷰를 Destroy+Instantiate하던 핫스팟을, 루트별 `CardChipPool`(SetActive 토글)과 정적/동적 Canvas 분리로 전환한다.

**Architecture:** CardView 전용 작은 풀 클래스를 동적 루트마다 1개(손패1·트릭1·뒷면3=5개) 둔다. 칩은 제 루트에 머물러 `SetParent` 없이 `Begin→Next×N→End`로 재사용되고, 잉여는 `SetActive(false)`로 비활성(파괴 안 함)된다. 동적 루트는 서브 Canvas로 격리하되 클릭이 필요한 손패에만 `GraphicRaycaster`를 추가한다. 뷰는 진실(GameState)의 읽기전용 투영이라 오라클(onApply=null) 경로는 비트동일로 보존된다.

**Tech Stack:** Unity 6000.3.17f1 · uGUI · R3 · UniTask · NUnit EditMode(`Tichu.Presentation.Tests`, Editor-only asmdef).

## Global Constraints

- **오라클 불변(절대):** `AsyncGameDriver`·`onApply` 콜백 배선·`TableViewModel.ApplySnapshot`·`TableViewModel.cs:132`의 방어적 `new List<Card>(...)` 복사를 **건드리지 않는다**. `onApply=null` 경로는 비트동일이어야 한다.
- **풀링 경로에서 `Object.Destroy` 0.** release는 `SetActive(false)`만. **`while(root.childCount > N)` 패턴 금지**(Destroy 프레임 지연 → 과거 OOM 전력). 잉여 정리는 추적 리스트 순회로.
- **루트별 풀 5개:** 손패(`_handRoot`)1 · 트릭(`_trickRoot`)1 · 뒷면(`_backRoots[1·2·3]`)3. 좌석0(=나)은 뒷면 없음.
- **Canvas 분할:** `_handRoot`=`Canvas`+`GraphicRaycaster`; `_trickRoot`·`_backRoots[1..3]`=`Canvas`만; 전부 `overrideSorting=false`.
- **범위 밖(그대로):** `_actionRoot`/`_wishPickRoot`/`_exchangeRoot`/`_playsRoot`의 `ClearChildren`+`Object.Destroy`, 플레이 로그 Text(`_playEntries`). `ClearChildren` 헬퍼는 이들이 계속 쓰므로 삭제하지 않는다.
- **테스트:** EditMode, `Tichu.Presentation.Tests` asmdef. 패턴 준수 — `new GameObject(...) + AddComponent<T>()`, teardown `Object.DestroyImmediate`. childCount로 풀 단언 금지(풀 카운터/active 필터 사용).
- **Unity 검증 절차(매 Run):**
  1. `refresh_unity(scope=all, mode=force)` — **신규 .cs/.asset는 이게 있어야 임포트됨**(scope=scripts만으론 어셈블리 미포함).
  2. `read_console(types=["error"], filter="error CS")` — 컴파일 에러 0 확인.
  3. PlayMode면 **정지**(run_tests 전 필수).
  4. `run_tests(mode=EditMode, assembly_names=["Tichu.Presentation.Tests"])` — **`test_names`에 클래스명만 주면 0건 매칭**, 어셈블리 단위로 실행.
- **베이스라인:** 작업 시작 시 EditMode 311/308 그린(0 실패·3 Explicit skip). 모든 단계 후 이 회귀 0 유지.

---

### Task 1: CardView — `Set()` 버튼 중립화 + `HasClickListener`

**Files:**
- Modify: `Assets/_Project/Presentation/Views/CardView.cs`
- Test: `Assets/_Project/Presentation/Tests/CardViewTests.cs`

**Interfaces:**
- Consumes: 기존 `CardView.Set(Card, CardSpriteAtlas, bool)`, `SetInteractable(bool, Action)`(73줄에서 `_button` 지연 생성).
- Produces: `CardView.HasClickListener` (public bool get) — `SetInteractable(on, cb)`가 `on && cb != null`이면 true, `Set(...)`이 false로. `Set(...)`은 `_button != null`일 때 `onClick.RemoveAllListeners()` + `interactable = false`로 버튼을 중립화한다.

**근거:** 풀 재사용 시 손패 칩이던 인스턴스가 트릭/뒷면(=`Set`+`SetSize`만 호출)으로 재사용되면 옛 onClick 리스너가 살아남는 잠재 버그(검증 dim5). 루트별 토폴로지가 1차 차단하지만, `Set()` 중립화로 소비자 규율과 무관하게 보장한다.

- [ ] **Step 1: 실패하는 테스트 작성** — `CardViewTests.cs`에 두 메서드 추가(클래스 닫는 `}` 앞).

```csharp
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
```

- [ ] **Step 2: 테스트 실패 확인** — Unity 검증 절차(Global) 실행.
Expected: 컴파일 에러 `CS1061: 'CardView' does not contain a definition for 'HasClickListener'` (테스트가 아직 없는 멤버 참조).

- [ ] **Step 3: 최소 구현** — `CardView.cs` 수정.

(a) 필드 영역(38줄 `private Highlight _highlight = Highlight.Normal;` 아래)에 프로퍼티 추가:

```csharp
        /// <summary>현재 활성 onClick 리스너가 걸려 있는지(풀 재사용 안전 단언용).</summary>
        public bool HasClickListener { get; private set; }
```

(b) `Set(...)` 끝(48줄 `ApplyHeight();` 다음 줄)에 버튼 중립화 추가:

```csharp
            // 풀 재사용 안전: Set+SetSize 만 부르는 소비자(트릭/뒷면)에서도 옛 리스너/상호작용을 비운다.
            if (_button != null) { _button.onClick.RemoveAllListeners(); _button.interactable = false; }
            HasClickListener = false;
```

(c) `SetInteractable(...)`(76줄 `if (on && onClick != null) _button.onClick.AddListener(() => onClick());` 줄)을 플래그 갱신 포함으로 교체:

```csharp
            HasClickListener = on && onClick != null;
            if (HasClickListener) _button.onClick.AddListener(() => onClick());
```

(d) 69줄 XML 주석을 정확하게 수정:

```csharp
        /// <summary>클릭 가능 토글. 리스너를 항상 비우고 다시 건다(중립화는 Set 도 수행).</summary>
```

- [ ] **Step 4: 테스트 통과 확인** — Unity 검증 절차 실행.
Expected: `Tichu.Presentation.Tests` PASS(신규 2건 포함), 기존 회귀 0.

- [ ] **Step 5: 커밋**

```bash
git add Assets/_Project/Presentation/Views/CardView.cs Assets/_Project/Presentation/Tests/CardViewTests.cs
git commit -m "feat(p1d): D3 CardView 버튼 중립화(Set)+HasClickListener — 풀 재사용 안전

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 2: `CardChipPool` 클래스

**Files:**
- Create: `Assets/_Project/Presentation/Views/CardChipPool.cs`
- Test: `Assets/_Project/Presentation/Tests/CardChipPoolTests.cs`

**Interfaces:**
- Consumes: `CardView`(풀 단위), `UnityEngine.Object.Instantiate<T>(T, Transform)`.
- Produces: `CardChipPool`(`Views` 네임스페이스). 생성자 `(RectTransform root, CardView prefab)`. 메서드 `void Begin()`, `CardView Next()`, `void End()`. 테스트 시임 `int CreatedCount`, `int ActiveCount`, `int FreeCount`.

- [ ] **Step 1: 실패하는 테스트 작성** — 신규 `CardChipPoolTests.cs`.

```csharp
using NUnit.Framework;
using Tichu.Presentation.Views;
using UnityEngine;

namespace Tichu.Presentation.Tests
{
    public class CardChipPoolTests
    {
        private static CardChipPool MakePool(out GameObject rootGo, out GameObject prefabGo)
        {
            rootGo = new GameObject("root", typeof(RectTransform));
            prefabGo = new GameObject("prefab", typeof(RectTransform));
            var prefab = prefabGo.AddComponent<CardView>();
            return new CardChipPool(rootGo.GetComponent<RectTransform>(), prefab);
        }

        private static void Build(CardChipPool pool, int n)
        {
            pool.Begin();
            for (int i = 0; i < n; i++) pool.Next();
            pool.End();
        }

        [Test]
        public void Reuses_instances_on_shrink_then_regrow_without_new_alloc()
        {
            var pool = MakePool(out var rootGo, out var prefabGo);
            Build(pool, 5);
            Assert.AreEqual(5, pool.CreatedCount);
            Assert.AreEqual(5, pool.ActiveCount);

            Build(pool, 3);
            Assert.AreEqual(5, pool.CreatedCount, "축소는 새 인스턴스를 만들지 않는다");
            Assert.AreEqual(3, pool.ActiveCount);
            Assert.AreEqual(2, pool.FreeCount);

            Build(pool, 5);
            Assert.AreEqual(5, pool.CreatedCount, "재확장은 비활성 인스턴스를 재사용한다");
            Assert.AreEqual(5, pool.ActiveCount);

            Object.DestroyImmediate(rootGo);
            Object.DestroyImmediate(prefabGo);
        }

        [Test]
        public void Released_chips_are_deactivated_under_root()
        {
            var pool = MakePool(out var rootGo, out var prefabGo);
            var root = rootGo.GetComponent<RectTransform>();
            Build(pool, 5);
            Build(pool, 2);

            int active = 0, inactive = 0;
            foreach (Transform c in root)
                if (c.gameObject.activeSelf) active++; else inactive++;

            Assert.AreEqual(2, active, "활성 칩은 현재 패스 개수");
            Assert.AreEqual(3, inactive, "잉여 칩은 비활성(파괴 아님)");
            Assert.AreEqual(2, pool.ActiveCount);
            Assert.AreEqual(3, pool.FreeCount);

            Object.DestroyImmediate(rootGo);
            Object.DestroyImmediate(prefabGo);
        }
    }
}
```

- [ ] **Step 2: 테스트 실패 확인** — Unity 검증 절차 실행.
Expected: 컴파일 에러 `CS0246: 'CardChipPool' could not be found`.

- [ ] **Step 3: 최소 구현** — 신규 `CardChipPool.cs`.

```csharp
using System.Collections.Generic;
using UnityEngine;

namespace Tichu.Presentation.Views
{
    /// <summary>
    /// CardView 전용 루트별 풀(D3). Destroy+Instantiate 대신 SetActive 토글로 재사용한다.
    /// Begin → Next ×N → End: 커서 이후의 칩을 비활성화한다(파괴하지 않음).
    /// 칩은 생성 순서대로 root 의 자식이 되고 그 순서로 재사용되므로 sibling 순서 == 채움 순서.
    /// </summary>
    public sealed class CardChipPool
    {
        private readonly RectTransform _root;
        private readonly CardView _prefab;
        private readonly List<CardView> _items = new List<CardView>();
        private int _cursor;

        public CardChipPool(RectTransform root, CardView prefab)
        {
            _root = root;
            _prefab = prefab;
        }

        public int CreatedCount => _items.Count;
        public int ActiveCount => _cursor;
        public int FreeCount => _items.Count - _cursor;

        public void Begin() => _cursor = 0;

        public CardView Next()
        {
            CardView cv;
            if (_cursor < _items.Count) cv = _items[_cursor];
            else { cv = Object.Instantiate(_prefab, _root); _items.Add(cv); }
            cv.gameObject.SetActive(true);
            _cursor++;
            return cv;
        }

        public void End()
        {
            for (int i = _cursor; i < _items.Count; i++)
                _items[i].gameObject.SetActive(false);
        }
    }
}
```

- [ ] **Step 4: 테스트 통과 확인** — Unity 검증 절차 실행.
Expected: `Tichu.Presentation.Tests` PASS(신규 2건 포함), 회귀 0.

- [ ] **Step 5: 커밋**

```bash
git add Assets/_Project/Presentation/Views/CardChipPool.cs Assets/_Project/Presentation/Views/CardChipPool.cs.meta Assets/_Project/Presentation/Tests/CardChipPoolTests.cs Assets/_Project/Presentation/Tests/CardChipPoolTests.cs.meta
git commit -m "feat(p1d): D3 CardChipPool — 루트별 SetActive 재사용 풀(불변식 테스트)

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 3: `RenderHand` 풀 전환 + `_handRoot` 서브 Canvas + GraphicRaycaster

**Files:**
- Modify: `Assets/_Project/Presentation/Views/RuntimeTableView.cs`
- Test: `Assets/_Project/Presentation/Tests/RuntimeTableViewPoolingTests.cs` (신규)

**Interfaces:**
- Consumes: `CardChipPool`(Task 2), `RuntimeTableView.Bind`, `TableViewModel`, `GameEngine.NewRound`.
- Produces: private `CardChipPool _handPool`; private 헬퍼 `CardChipPool MakeDynamicPool(RectTransform root, bool interactive)` — root 에 `Canvas`(overrideSorting=false) 추가, interactive면 `GraphicRaycaster`도 추가, 새 풀 반환. `RenderHand`가 `_handPool.Begin/Next/End` 사용. `AddHandChip`은 제거(인라인됨).

- [ ] **Step 1: 실패하는 테스트 작성** — 신규 `RuntimeTableViewPoolingTests.cs`.

```csharp
using System.Threading;
using NUnit.Framework;
using Tichu.Core.Game;
using Tichu.Presentation.ViewModel;
using Tichu.Presentation.Views;
using UnityEngine;
using UnityEngine.UI;

namespace Tichu.Presentation.Tests
{
    public class RuntimeTableViewPoolingTests
    {
        private static Transform FindByName(GameObject root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t;
            return null;
        }

        private static int ActiveCards(Transform t) =>
            t.GetComponentsInChildren<CardView>(false).Length;

        [Test]
        public void HandRoot_is_subcanvas_with_raycaster()
        {
            var canvasGo = new GameObject("TestCanvas", typeof(Canvas));
            try
            {
                ITableView view = new RuntimeTableView();
                view.Bind(new TableViewModel(0), canvasGo.GetComponent<Canvas>(), CancellationToken.None);

                var hand = FindByName(canvasGo, "Hand");
                Assert.IsNotNull(hand, "Hand 루트가 있어야 한다");
                Assert.IsNotNull(hand.GetComponent<Canvas>(), "손패는 서브 Canvas");
                Assert.IsNotNull(hand.GetComponent<GraphicRaycaster>(), "손패 클릭에는 GraphicRaycaster 필수");
            }
            finally { Object.DestroyImmediate(canvasGo); }
        }

        [Test]
        public void Hand_renders_pooled_chips_and_reuses_on_rerender()
        {
            var canvasGo = new GameObject("TestCanvas", typeof(Canvas));
            try
            {
                var vm = new TableViewModel(0);
                ITableView view = new RuntimeTableView();
                view.Bind(vm, canvasGo.GetComponent<Canvas>(), CancellationToken.None);

                var state = GameEngine.NewRound(12345UL);
                vm.ApplySnapshot(state);

                var hand = FindByName(canvasGo, "Hand");
                int expected = state.Seats[0].Hand.Count;
                Assert.AreEqual(expected, ActiveCards(hand), "활성 칩 수 == 좌석0 손패 수");
                int totalAfterFirst = hand.GetComponentsInChildren<CardView>(true).Length;

                // 같은 크기로 재렌더 → 새 인스턴스 생성 없이 재사용.
                vm.ApplySnapshot(state);
                Assert.AreEqual(expected, ActiveCards(hand));
                Assert.AreEqual(totalAfterFirst, hand.GetComponentsInChildren<CardView>(true).Length,
                    "동일 크기 재렌더는 새 칩을 만들지 않는다(풀 재사용)");
            }
            finally { Object.DestroyImmediate(canvasGo); }
        }
    }
}
```

- [ ] **Step 2: 테스트 실패 확인** — Unity 검증 절차 실행.
Expected: 두 테스트 FAIL — `HandRoot_is_subcanvas_with_raycaster`는 `Hand`에 `Canvas`/`GraphicRaycaster` 없어 `Null` 단언 실패; `Hand_renders_pooled_chips...`는 재렌더 시 인스턴스 수 증가(현재 Destroy+Instantiate)로 실패.

- [ ] **Step 3: 최소 구현** — `RuntimeTableView.cs` 수정.

(a) 파일 상단 `using`에 UI 보장(이미 13줄에 `using UnityEngine.UI;` 있음 — 변경 없음). 필드 영역(57줄 `private CardView _cardViewPrefab;` 아래)에 풀 필드 추가:

```csharp
        private CardChipPool _handPool;
```

(b) `BuildLayout`에서 `_handRoot` 셋업 직후(124줄 `AnchorBottomStretch(_handRoot, height: 150, bottom: 16, sideInset: 8);` 다음 줄)에 풀 생성:

```csharp
            _handPool = MakeDynamicPool(_handRoot, interactive: true);
```

(c) 생성 헬퍼 영역(`ClearChildren` 정적 헬퍼 근처, 729줄 아래)에 `MakeDynamicPool` 추가:

```csharp
        // 동적 루트를 서브 Canvas 로 격리(리배칭 분리)하고 풀을 만든다. interactive=손패만(버튼 클릭용 레이캐스터).
        private CardChipPool MakeDynamicPool(RectTransform root, bool interactive)
        {
            var canvas = root.gameObject.AddComponent<Canvas>();
            canvas.overrideSorting = false; // 드로 순서 보존(결과배너 > 트릭)
            if (interactive) root.gameObject.AddComponent<GraphicRaycaster>();
            return new CardChipPool(root, _cardViewPrefab);
        }
```

(d) `RenderHand`(287–292줄)와 `AddHandChip`(294–311줄)을 다음으로 교체(`AddHandChip` 제거, 로직을 풀 루프로 인라인):

```csharp
        private void RenderHand()
        {
            _handPool.Begin();
            foreach (var card in _hand.OrderBy(SortKey))
            {
                var cv = _handPool.Next();
                cv.Set(card, _atlas, faceUp: true);
                cv.SetSize(66, 100);

                CardView.Highlight h = CardView.Highlight.Normal;
                if (Exchanging)
                {
                    if (_exPick.HasValue && card.Equals(_exPick.Value)) h = CardView.Highlight.Selected;
                    else if (IsAssigned(card)) h = CardView.Highlight.Assigned;
                }
                else if (_selection.Contains(card)) h = CardView.Highlight.Selected;
                cv.SetHighlight(h);

                var cap = card;
                cv.SetInteractable(CardSelectable, () => ToggleCard(cap));
            }
            _handPool.End();
        }
```

- [ ] **Step 4: 테스트 통과 확인** — Unity 검증 절차 실행.
Expected: `RuntimeTableViewPoolingTests` 2건 PASS, `ITableViewContractTests` 등 회귀 0.

- [ ] **Step 5: 커밋**

```bash
git add Assets/_Project/Presentation/Views/RuntimeTableView.cs Assets/_Project/Presentation/Tests/RuntimeTableViewPoolingTests.cs Assets/_Project/Presentation/Tests/RuntimeTableViewPoolingTests.cs.meta
git commit -m "feat(p1d): D3 RenderHand 풀 전환 + 손패 서브 Canvas+GraphicRaycaster

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 4: `RenderTrick`·`RenderBacks` 풀 전환 + 트릭/뒷면 서브 Canvas

**Files:**
- Modify: `Assets/_Project/Presentation/Views/RuntimeTableView.cs`
- Test: `Assets/_Project/Presentation/Tests/RuntimeTableViewPoolingTests.cs` (확장)

**Interfaces:**
- Consumes: `MakeDynamicPool`(Task 3), `CardChipPool`(Task 2), `_backRoots`/`_trickRoot`.
- Produces: private `CardChipPool _trickPool`; private `CardChipPool[] _backPools = new CardChipPool[4]`(좌석0=null). `RenderTrick`/`RenderBacks`가 풀 사용.

- [ ] **Step 1: 실패하는 테스트 작성** — `RuntimeTableViewPoolingTests.cs`에 메서드 추가(클래스 닫는 `}` 앞).

```csharp
        [Test]
        public void TrickAndBackRoots_are_subcanvases_without_raycaster()
        {
            var canvasGo = new GameObject("TestCanvas", typeof(Canvas));
            try
            {
                ITableView view = new RuntimeTableView();
                view.Bind(new TableViewModel(0), canvasGo.GetComponent<Canvas>(), CancellationToken.None);

                var trick = FindByName(canvasGo, "TrickRow");
                Assert.IsNotNull(trick.GetComponent<Canvas>(), "트릭은 서브 Canvas");
                Assert.IsNull(trick.GetComponent<GraphicRaycaster>(), "트릭은 버튼 없음 → 레이캐스터 불필요");

                foreach (var seat in new[] { 1, 2, 3 })
                {
                    var backs = FindByName(canvasGo, $"Backs{seat}");
                    Assert.IsNotNull(backs, $"Backs{seat} 루트가 있어야 한다");
                    Assert.IsNotNull(backs.GetComponent<Canvas>(), $"Backs{seat} 는 서브 Canvas");
                    Assert.IsNull(backs.GetComponent<GraphicRaycaster>(), $"Backs{seat} 는 레이캐스터 불필요");
                }
            }
            finally { Object.DestroyImmediate(canvasGo); }
        }

        [Test]
        public void Backs_reuse_pool_on_count_change()
        {
            var canvasGo = new GameObject("TestCanvas", typeof(Canvas));
            try
            {
                var vm = new TableViewModel(0);
                ITableView view = new RuntimeTableView();
                view.Bind(vm, canvasGo.GetComponent<Canvas>(), CancellationToken.None);

                vm.ApplySnapshot(GameEngine.NewRound(777UL));
                var backs1 = FindByName(canvasGo, "Backs1");
                int afterDeal = backs1.GetComponentsInChildren<CardView>(true).Length;
                Assert.Greater(afterDeal, 0, "상대 뒷면이 렌더돼야 한다");

                // 같은 스냅샷 재적용 → 인스턴스 수 불변(풀 재사용).
                vm.ApplySnapshot(GameEngine.NewRound(777UL));
                Assert.AreEqual(afterDeal, backs1.GetComponentsInChildren<CardView>(true).Length,
                    "뒷면도 동일 크기 재렌더 시 새 칩을 만들지 않는다");
            }
            finally { Object.DestroyImmediate(canvasGo); }
        }
```

- [ ] **Step 2: 테스트 실패 확인** — Unity 검증 절차 실행.
Expected: `TrickAndBackRoots_are_subcanvases_without_raycaster`는 트릭/뒷면에 `Canvas` 없어 실패; `Backs_reuse_pool_on_count_change`는 재렌더 시 인스턴스 증가로 실패.

- [ ] **Step 3: 최소 구현** — `RuntimeTableView.cs` 수정.

(a) 풀 필드 추가(Task 3에서 추가한 `private CardChipPool _handPool;` 아래):

```csharp
        private CardChipPool _trickPool;
        private readonly CardChipPool[] _backPools = new CardChipPool[4]; // 좌석0(나)=null
```

(b) `_trickRoot` 셋업 직후(99줄 `_trickRoot = NewRow("TrickRow", ...);` 다음 줄)에 풀 생성:

```csharp
            _trickPool = MakeDynamicPool(_trickRoot, interactive: false);
```

(c) `BuildOpponent`의 `_backRoots[seat]` 셋업 직후(197줄 `_backRoots[seat].GetComponent<HorizontalOrVerticalLayoutGroup>().spacing = 8;` 다음 줄)에 풀 생성:

```csharp
            _backPools[seat] = MakeDynamicPool(_backRoots[seat], interactive: false);
```

(d) `RenderBacks`(338–352줄)를 풀 사용으로 교체:

```csharp
        private void RenderBacks(int seat, int count)
        {
            var pool = _backPools[seat];
            if (pool == null) return; // 좌석0(나)은 뒷면 없음
            bool side = (seat == 1 || seat == 3);
            float w = side ? 44 : 30, h = side ? 30 : 44; // 측면도 파트너와 같은 카드 치수(눕힌 44×30)
            int show = Mathf.Min(count, 14);
            pool.Begin();
            for (int i = 0; i < show; i++)
            {
                var cv = pool.Next();
                cv.Set(default(Card), _atlas, faceUp: false);
                cv.SetSize(w, h);
            }
            pool.End();
        }
```

(e) `RenderTrick`(356–367줄)을 풀 사용으로 교체(빈 트릭 = Begin+End 로 전체 비활성화):

```csharp
        private void RenderTrick(Trick? trick)
        {
            if (trick?.Top == null)
            {
                _trickPool.Begin(); _trickPool.End(); // 모든 칩 비활성화
                _trickOwnerText.text = "트릭: (없음)";
                return;
            }
            _trickPool.Begin();
            foreach (var card in trick.Top.Cards.OrderBy(SortKey))
            {
                var cv = _trickPool.Next();
                cv.Set(card, _atlas, faceUp: true);
                cv.SetSize(60, 88);
            }
            _trickPool.End();
            _trickOwnerText.text = $"{TypeKo(trick.Top.Type)} · 소유 {SeatNames[trick.TopOwnerSeat]}";
        }
```

- [ ] **Step 4: 테스트 통과 확인** — Unity 검증 절차 실행.
Expected: `RuntimeTableViewPoolingTests` 4건 전부 PASS, 회귀 0.

- [ ] **Step 5: 커밋**

```bash
git add Assets/_Project/Presentation/Views/RuntimeTableView.cs Assets/_Project/Presentation/Tests/RuntimeTableViewPoolingTests.cs
git commit -m "feat(p1d): D3 RenderTrick/RenderBacks 풀 전환 + 트릭/뒷면 서브 Canvas

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 5: 오라클 회귀 게이트 + 전체 EditMode + 수동 PlayMode 스모크

**Files:** (코드 변경 없음 — 검증 게이트)

**Interfaces:**
- Consumes: 전체 `Tichu.Presentation.Tests` 어셈블리(오라클 교차검증 테스트 포함), Unity PlayMode.

- [ ] **Step 1: 전체 EditMode 회귀 실행** — `run_tests(mode=EditMode, assembly_names=["Tichu.Presentation.Tests"])`(필요 시 `["Tichu.Core.Tests","Tichu.Presentation.Tests"]`).
Expected: 0 실패. D3 신규 테스트(CardView 2 + Pool 2 + View 4 = 8건) 포함, **오라클 동기==비동기 ComputeHash 테스트 그린**(풀링이 onApply=null 경로를 건드리지 않았음을 입증). 베이스라인 311 대비 +8 → 그린 수 증가, skip 3 유지.

- [ ] **Step 2: 수동 PlayMode 스모크(GraphicRaycaster 회귀 가드)** — EditMode는 컴포넌트 존재만 확인하므로, 실제 클릭 라우팅은 PlayMode로 확인한다.
  - App(또는 Table) 씬 PlayMode 진입 → AI 대전 한 라운드 진입.
  - **손패 카드 클릭 → 선택 강조(노랑+lift)·결정 버튼 활성** 확인(ToggleCard 발화 = 손패 서브캔버스 raycaster 정상).
  - 카드 여러 장 플레이 → 트릭/손패/상대 뒷면이 정상 갱신·잔상 없음·콘솔 에러 0 확인.
  - (MCP 스크린샷 흰화면 우회는 [[unity-mcp-session-reload]] 절차 참고.)
Expected: 클릭 선택 동작·렌더 정상·콘솔 에러 0.

- [ ] **Step 3: (변경 없으면 커밋 생략)** — 코드 변경이 없으면 이 태스크는 게이트 통과로 종료. 스모크 중 결함 발견 시 별도 fix 커밋.

---

## Self-Review

**1. Spec coverage:**
- §5 `CardChipPool` → Task 2. ✓
- §5 `CardView` 변경(중립화·HasClickListener·주석) → Task 1. ✓
- §5 `RuntimeTableView` 풀 배선·Canvas 분리·렌더 교체 → Task 3(손패)·Task 4(트릭/뒷면). ✓
- §7 Canvas 분리(손패만 raycaster·overrideSorting=false) → Task 3 `MakeDynamicPool` + Task 3·4 구조 테스트. ✓
- §8 오라클 불변 → Global Constraints + Task 5 회귀 게이트. ✓
- §9 테스트 5종(풀 불변식·리스너 누수·렌더 동치·구조·오라클) → 풀 불변식=Task2, 리스너 누수=Task1, 렌더 동치=Task3·4, 구조=Task3·4, 오라클=Task5. ✓
- §4 비범위(`_actionRoot` 등·플레이로그·DoTween·아트) → Global Constraints에 명시, 손대지 않음. ✓

**2. Placeholder scan:** TBD/TODO/"적절히 처리"/빈 코드블록 없음. 모든 step에 실제 코드/명령. ✓

**3. Type consistency:** `CardChipPool(RectTransform, CardView)`·`Begin()`/`Next()`/`End()`·`CreatedCount`/`ActiveCount`/`FreeCount`가 Task 2 정의와 Task 3·4 사용처 일치. `MakeDynamicPool(RectTransform, bool)`는 Task 3 정의·Task 4 재사용 일치. `HasClickListener`는 Task 1 정의·CardView 테스트 사용 일치. ✓

---

## 의존 · 다음
의존: D3 ⊃ {D0 ITableView, D1·D2 가로/CardView/Atlas}. 다음 = D4(`ICardAnimator`+DoTween+AnimationQueue) · D5(`IAudioService`+Bank) · D6(실기기 60fps 게이트).
