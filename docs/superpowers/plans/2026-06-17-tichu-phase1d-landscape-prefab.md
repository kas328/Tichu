# Phase 1-D (D1+D2) 가로 셸 + 앵커화 + CardView 구현 계획

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:executing-plans (인라인) — 이 작업은 Unity MCP 브리지/HTTP 헬퍼에 stateful 의존이라 서브에이전트보다 인라인이 적합. Steps use checkbox (`- [ ]`).

**Goal:** placeholder 세로 테이블을 가로 1920×1080·앵커 기반으로 전환하고 카드 1장을 CardView 프리팹으로 추출(게임 로직·구독 계약 불변).

**Architecture:** 단일 `ITableView` 구현(`RuntimeTableView`)을 제자리 진화. 독립 컴포넌트(SafeAreaFitter/CardSpriteAtlas/CardView)를 TDD로 먼저 만들고, 뷰가 이를 사용하도록 통합한 뒤, 마지막에 가로 전환+앵커화를 한 번에(C-1).

**Tech Stack:** Unity 6000.3 / uGUI / R3 / UniTask. 테스트=UTF EditMode. 검증=MCP HTTP 헬퍼.

## Global Constraints

- 단위 검증 = EditMode. 베이스라인: **EditMode 297 total / 294 pass / 3 explicit skip (Passed)**. 회귀 0 유지.
- MCP 헬퍼(세션 로컬): `bash C:/Users/user/AppData/Local/Temp/mcp_tool.sh <tool> '<argsJson>'`.
  - 컴파일: `refresh_unity {"mode":"force","scope":"all","compile":"request","wait_for_ready":true}` 후 `read_console {"action":"get","types":["error"]}`에서 `CS` 0 확인.
  - 테스트: `run_tests {"mode":"EditMode","include_failed_tests":true}` → `data.job_id` → `get_test_job {job_id, wait_timeout:30}` 폴링, `data.summary` 확인.
  - `read_console` 남발 금지(콘솔 "Access version" 폭주 유발) — 컴파일 확인 1회만.
- 신규 코드는 `Tichu.Presentation` asmdef 안 폴더(`Views`/`Visuals`)에 둠(신규 asmdef 0).
- 진실/연출 분리 불변: 구독(Subscribe)·제출·합법성 로직 변경 금지. `onApply=null` 오라클 경로 무영향.
- 카드 라벨 표기 규칙(현행 유지): 용/봉/개/마작=특수, 숫자=Rank+Suit, 빨강=Pagoda/Star.
- 커밋 메시지 말미: `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>`.

## File Structure

- `Assets/_Project/Presentation/Visuals/SafeAreaMath.cs` (신규) — 순수 앵커 계산.
- `Assets/_Project/Presentation/Visuals/SafeAreaFitter.cs` (신규) — MonoBehaviour 적용기.
- `Assets/_Project/Presentation/Visuals/CardSpriteAtlas.cs` (신규) — SO, Card→Sprite(null 폴백).
- `Assets/_Project/Presentation/Views/CardFormat.cs` (신규) — 카드 라벨/색/정렬 포맷(RuntimeTableView에서 추출, DRY).
- `Assets/_Project/Presentation/Views/CardView.cs` (신규) — 카드 1장 뷰 컴포넌트.
- `Assets/_Project/Presentation/Resources/CardView.prefab` (신규) — CardView 프리팹.
- `Assets/_Project/Presentation/Resources/CardSpriteAtlas.asset` (신규, 미할당 가능) — 아틀라스 인스턴스.
- `Assets/_Project/Presentation/Views/RuntimeTableView.cs` (변경) — 앵커화 + CardView 통합.
- `Assets/_Project/Presentation/RoundBootstrap.cs` (변경) — 가로 CanvasScaler + targetFrameRate.
- `ProjectSettings/ProjectSettings.asset` (변경) — 가로 방향 잠금.
- `Assets/_Project/Presentation/Tests/SafeAreaMathTests.cs`, `CardFormatTests.cs`, `CardViewTests.cs` (신규).

---

### Task 1: SafeAreaMath + SafeAreaFitter

**Files:**
- Create: `Assets/_Project/Presentation/Visuals/SafeAreaMath.cs`
- Create: `Assets/_Project/Presentation/Visuals/SafeAreaFitter.cs`
- Test: `Assets/_Project/Presentation/Tests/SafeAreaMathTests.cs`

**Interfaces:**
- Produces: `SafeAreaMath.ComputeAnchors(Rect safeArea, Vector2 screen) -> (Vector2 min, Vector2 max)`; `SafeAreaFitter : MonoBehaviour`.

- [ ] **Step 1: Write the failing test**

```csharp
using NUnit.Framework;
using Tichu.Presentation.Visuals;
using UnityEngine;

namespace Tichu.Presentation.Tests
{
    public class SafeAreaMathTests
    {
        [Test]
        public void FullScreen_safeArea_maps_to_unit_anchors()
        {
            var (min, max) = SafeAreaMath.ComputeAnchors(new Rect(0, 0, 1920, 1080), new Vector2(1920, 1080));
            Assert.AreEqual(Vector2.zero, min);
            Assert.AreEqual(Vector2.one, max);
        }

        [Test]
        public void Notch_inset_maps_to_fractional_anchors()
        {
            // 왼쪽 96px 노치 인셋(가로)
            var (min, max) = SafeAreaMath.ComputeAnchors(new Rect(96, 0, 1824, 1080), new Vector2(1920, 1080));
            Assert.AreEqual(0.05f, min.x, 1e-4f);
            Assert.AreEqual(1.0f, max.x, 1e-4f);
            Assert.AreEqual(0f, min.y, 1e-4f);
        }

        [Test]
        public void Zero_screen_falls_back_to_full()
        {
            var (min, max) = SafeAreaMath.ComputeAnchors(new Rect(0, 0, 0, 0), Vector2.zero);
            Assert.AreEqual(Vector2.zero, min);
            Assert.AreEqual(Vector2.one, max);
        }
    }
}
```

- [ ] **Step 2: Run to verify FAIL** — `run_tests` EditMode; expect compile error `SafeAreaMath` 부재(RED).

- [ ] **Step 3: Implement SafeAreaMath**

```csharp
using UnityEngine;

namespace Tichu.Presentation.Visuals
{
    /// <summary>화면 내 safe area를 정규화 앵커(0..1)로 변환하는 순수 함수.</summary>
    public static class SafeAreaMath
    {
        public static (Vector2 min, Vector2 max) ComputeAnchors(Rect safeArea, Vector2 screen)
        {
            if (screen.x <= 0f || screen.y <= 0f) return (Vector2.zero, Vector2.one);
            var min = new Vector2(safeArea.xMin / screen.x, safeArea.yMin / screen.y);
            var max = new Vector2(safeArea.xMax / screen.x, safeArea.yMax / screen.y);
            return (min, max);
        }
    }
}
```

- [ ] **Step 4: Implement SafeAreaFitter**

```csharp
using UnityEngine;

namespace Tichu.Presentation.Visuals
{
    /// <summary>부착된 RectTransform을 Screen.safeArea에 맞춰 앵커 인셋한다(노치 회피).
    /// 해상도/회전 변경 시 자동 재적용. 노치 없는 기기/에디터는 인셋 0(무영향).</summary>
    [RequireComponent(typeof(RectTransform))]
    public sealed class SafeAreaFitter : MonoBehaviour
    {
        private RectTransform _rt;
        private Rect _lastSafe;
        private Vector2 _lastScreen;

        private void Awake() => _rt = (RectTransform)transform;
        private void OnEnable() => Apply();

        private void Update()
        {
            if (Screen.safeArea != _lastSafe ||
                _lastScreen.x != Screen.width || _lastScreen.y != Screen.height)
                Apply();
        }

        private void Apply()
        {
            _lastSafe = Screen.safeArea;
            _lastScreen = new Vector2(Screen.width, Screen.height);
            var (min, max) = SafeAreaMath.ComputeAnchors(_lastSafe, _lastScreen);
            _rt.anchorMin = min;
            _rt.anchorMax = max;
            _rt.offsetMin = Vector2.zero;
            _rt.offsetMax = Vector2.zero;
        }
    }
}
```

- [ ] **Step 5: Run to verify PASS** — `run_tests` EditMode; SafeAreaMathTests 3개 통과, 기존 전부 그린.

- [ ] **Step 6: Commit**

```bash
git add Assets/_Project/Presentation/Visuals/SafeAreaMath.cs* Assets/_Project/Presentation/Visuals/SafeAreaFitter.cs* Assets/_Project/Presentation/Tests/SafeAreaMathTests.cs*
git commit -m "feat(p1d): D1 SafeAreaFitter + 순수 ComputeAnchors (EditMode TDD)"
```

---

### Task 2: CardFormat (라벨/색 추출, DRY)

**Files:**
- Create: `Assets/_Project/Presentation/Views/CardFormat.cs`
- Modify: `Assets/_Project/Presentation/Views/RuntimeTableView.cs` (라벨 헬퍼를 CardFormat 호출로 위임)
- Test: `Assets/_Project/Presentation/Tests/CardFormatTests.cs`

**Interfaces:**
- Produces: `CardFormat.Label(Card) -> string` (예: `"A\n♥"`, 특수=`"용"`), `CardFormat.IsRed(Card) -> bool`, `CardFormat.SortKey(Card) -> int`, `CardFormat.Key(Card) -> string`(아틀라스 키).

- [ ] **Step 1: Write failing test**

```csharp
using NUnit.Framework;
using Tichu.Core.Cards;
using Tichu.Presentation.Views;

namespace Tichu.Presentation.Tests
{
    public class CardFormatTests
    {
        [Test] public void Normal_card_label_has_rank_and_suit()
            => Assert.AreEqual("A\n♥", CardFormat.Label(Card.Normal(14, Suit.Star)));

        [Test] public void Special_card_label_is_korean()
            => Assert.AreEqual("용", CardFormat.Label(Card.Dragon()));

        [Test] public void Red_suits_are_pagoda_and_star()
        {
            Assert.IsTrue(CardFormat.IsRed(Card.Normal(5, Suit.Star)));
            Assert.IsFalse(CardFormat.IsRed(Card.Normal(5, Suit.Jade)));
        }

        [Test] public void Key_is_distinct_per_card()
            => Assert.AreNotEqual(CardFormat.Key(Card.Normal(5, Suit.Star)), CardFormat.Key(Card.Normal(5, Suit.Jade)));
    }
}
```
> 검증 필요: `Card.Dragon()`/`Card.Normal(rank,suit)` 팩토리 시그니처 — 구현 전 `Card.cs` 확인(없으면 동등 생성자로 교체).

- [ ] **Step 2: Run to verify FAIL** (RED — CardFormat 부재).

- [ ] **Step 3: Implement CardFormat** — `RuntimeTableView`의 `CardLabel`/`RankLabel`/`SuitGlyph`/`IsRed`/`SortKey` 로직을 그대로 옮긴다(동작 동일). 추가로 `Key(Card)` = 특수면 special명, 아니면 `$"{rank}_{suit}"`.

- [ ] **Step 4: Delegate in RuntimeTableView** — 뷰의 private static 라벨 헬퍼들을 `CardFormat.*` 호출로 교체(중복 제거). 호출부 동작 동일.

- [ ] **Step 5: Run to verify PASS** — CardFormatTests 통과 + 기존 전부 그린(라벨 위임 회귀 없음).

- [ ] **Step 6: Commit**

```bash
git add Assets/_Project/Presentation/Views/CardFormat.cs* Assets/_Project/Presentation/Views/RuntimeTableView.cs Assets/_Project/Presentation/Tests/CardFormatTests.cs*
git commit -m "refactor(p1d): 카드 라벨/색/키 포맷을 CardFormat으로 추출(DRY)"
```

---

### Task 3: CardSpriteAtlas (SO, null 폴백)

**Files:**
- Create: `Assets/_Project/Presentation/Visuals/CardSpriteAtlas.cs`
- Test: `Assets/_Project/Presentation/Tests/CardSpriteAtlasTests.cs`

**Interfaces:**
- Consumes: `CardFormat.Key(Card)`.
- Produces: `CardSpriteAtlas : ScriptableObject` with `Sprite Back { get; }`, `Sprite Face(Card)`.

- [ ] **Step 1: Write failing test**

```csharp
using NUnit.Framework;
using Tichu.Core.Cards;
using Tichu.Presentation.Visuals;
using UnityEngine;

namespace Tichu.Presentation.Tests
{
    public class CardSpriteAtlasTests
    {
        [Test] public void Unpopulated_atlas_returns_null_face_and_back()
        {
            var atlas = ScriptableObject.CreateInstance<CardSpriteAtlas>();
            Assert.IsNull(atlas.Face(Card.Normal(14, Suit.Star)));
            Assert.IsNull(atlas.Back);
            Object.DestroyImmediate(atlas);
        }
    }
}
```

- [ ] **Step 2: Run to verify FAIL** (RED).

- [ ] **Step 3: Implement**

```csharp
using System.Collections.Generic;
using Tichu.Core.Cards;
using Tichu.Presentation.Views;
using UnityEngine;

namespace Tichu.Presentation.Visuals
{
    /// <summary>Card→Sprite 매핑(미할당/미발견=null → CardView 텍스트 폴백). 아트 도입 시 채움.</summary>
    [CreateAssetMenu(menuName = "Tichu/Card Sprite Atlas", fileName = "CardSpriteAtlas")]
    public sealed class CardSpriteAtlas : ScriptableObject
    {
        [System.Serializable]
        public struct Entry { public string key; public Sprite sprite; }

        [SerializeField] private Sprite back;
        [SerializeField] private Entry[] faces = new Entry[0];

        private Dictionary<string, Sprite> _map;

        public Sprite Back => back;

        public Sprite Face(Card card)
        {
            if (faces == null || faces.Length == 0) return null;
            if (_map == null)
            {
                _map = new Dictionary<string, Sprite>(faces.Length);
                foreach (var e in faces) if (!string.IsNullOrEmpty(e.key)) _map[e.key] = e.sprite;
            }
            return _map.TryGetValue(CardFormat.Key(card), out var s) ? s : null;
        }
    }
}
```

- [ ] **Step 4: Run to verify PASS** — CardSpriteAtlasTests 통과 + 전부 그린.

- [ ] **Step 5: Create empty asset** — MCP `manage_asset`(create ScriptableObject) 또는 에디터로 `Assets/_Project/Presentation/Resources/CardSpriteAtlas.asset`(faces 비움, back 미할당).

- [ ] **Step 6: Commit**

```bash
git add Assets/_Project/Presentation/Visuals/CardSpriteAtlas.cs* Assets/_Project/Presentation/Resources/CardSpriteAtlas.asset* Assets/_Project/Presentation/Tests/CardSpriteAtlasTests.cs*
git commit -m "feat(p1d): D2 CardSpriteAtlas SO(미할당=no-op 폴백)"
```

---

### Task 4: CardView (컴포넌트 + 프리팹)

**Files:**
- Create: `Assets/_Project/Presentation/Views/CardView.cs`
- Create: `Assets/_Project/Presentation/Resources/CardView.prefab`
- Test: `Assets/_Project/Presentation/Tests/CardViewTests.cs`

**Interfaces:**
- Consumes: `CardFormat`, `CardSpriteAtlas`.
- Produces: `CardView : MonoBehaviour` with `void Set(Card card, CardSpriteAtlas atlas, bool faceUp)`, `void SetSelected(bool)`, `void SetInteractable(bool on, System.Action onClick)`, `Card Card { get; }`.

- [ ] **Step 1: Write failing test** (코드로 CardView 구성 — 프리팹 없이 컴포넌트 단위 검증)

```csharp
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
        private static CardView NewCardView(out GameObject go)
        {
            go = new GameObject("CardView", typeof(RectTransform), typeof(Image));
            var face = new GameObject("Face", typeof(RectTransform), typeof(Image));
            face.transform.SetParent(go.transform, false);
            var labelGo = new GameObject("Label", typeof(RectTransform), typeof(Text));
            labelGo.transform.SetParent(go.transform, false);
            labelGo.GetComponent<Text>().font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var cv = go.AddComponent<CardView>();
            cv.WireForTest(go.GetComponent<Image>(), face.GetComponent<Image>(), labelGo.GetComponent<Text>());
            return cv;
        }

        [Test] public void FaceUp_no_atlas_shows_label_and_hides_face()
        {
            var cv = NewCardView(out var go);
            cv.Set(Card.Normal(14, Suit.Star), null, faceUp: true);
            Assert.AreEqual("A\n♥", cv.LabelTextForTest);
            Assert.IsFalse(cv.FaceVisibleForTest);
            Object.DestroyImmediate(go);
        }

        [Test] public void FaceDown_shows_back_not_label()
        {
            var cv = NewCardView(out var go);
            cv.Set(Card.Normal(14, Suit.Star), null, faceUp: false);
            Assert.IsFalse(cv.LabelVisibleForTest);
            Object.DestroyImmediate(go);
        }
    }
}
```
> 테스트 보조 접근자(`WireForTest`/`*ForTest`)는 프리팹 SerializeField 주입을 대신하는 테스트 전용 훅. 프로덕션 경로는 프리팹 직렬화로 주입.

- [ ] **Step 2: Run to verify FAIL** (RED — CardView 부재).

- [ ] **Step 3: Implement CardView** (faceUp: atlas?.Face(card) 있으면 face.sprite 표시·label off; 없으면 label=CardFormat.Label, 색=IsRed?red:black, face off. faceDown: face=back sprite 또는 단색, label off. SetSelected: rect 높이/위치 lift. SetInteractable: Button 토글 + onClick.RemoveAllListeners 후 AddListener — 풀 재사용 안전.) 테스트 훅 포함.

- [ ] **Step 4: Run to verify PASS** — CardViewTests 통과 + 전부 그린.

- [ ] **Step 5: Author prefab** — MCP `batch_execute`로 `CardView.prefab` 구성: RectTransform+Image(bg) 루트 → 자식 Face(Image, disabled), Label(Text). CardView 컴포넌트의 SerializeField에 bg/face/label 연결. `Assets/_Project/Presentation/Resources/CardView.prefab`로 저장.

- [ ] **Step 6: Commit**

```bash
git add Assets/_Project/Presentation/Views/CardView.cs* Assets/_Project/Presentation/Resources/CardView.prefab* Assets/_Project/Presentation/Tests/CardViewTests.cs*
git commit -m "feat(p1d): D2 CardView 컴포넌트+프리팹(스프라이트 슬롯+텍스트 폴백)"
```

---

### Task 5: RuntimeTableView — CardView 통합 (세로 유지)

**Files:**
- Modify: `Assets/_Project/Presentation/Views/RuntimeTableView.cs`

**Interfaces:**
- Consumes: `CardView`(Resources.Load), `CardSpriteAtlas`(Resources.Load), `CardFormat`.

- [ ] **Step 1: 회귀 가드 확인** — 기존 `ITableViewContractTests` 그린이 이 태스크의 안전망. 먼저 `run_tests` 베이스라인 그린 확인.

- [ ] **Step 2: Bind에서 리소스 로드** — `Bind` 시작에 `_cardViewPrefab = Resources.Load<CardView>("CardView"); _atlas = Resources.Load<CardSpriteAtlas>("CardSpriteAtlas");` 추가(필드 보관).

- [ ] **Step 3: 카드 빌드 교체** — `AddHandChip`/`RenderBacks`/`RenderTrick`의 `NewCardChip(...)+AddCardLabel(...)`를 `Instantiate(_cardViewPrefab, parent)` + `cv.Set(card, _atlas, faceUp)` (+손패는 `SetSelected`/`SetInteractable`)로 교체. 레이아웃/위치 코드는 그대로(세로 유지).

- [ ] **Step 4: Run to verify** — `ITableViewContractTests`(Bind+스냅샷) 그린 + 전부 그린. desync 없음.

- [ ] **Step 5: 세로 PlayMode 스크린샷(육안)** — MCP로 Table 씬 PlayMode 진입 후 캡처, 손패/트릭/상대 카드가 CardView로 정상 렌더되는지 확인(메모리 unity-mcp-session-reload §4 캡처 워크플로).

- [ ] **Step 6: Commit**

```bash
git add Assets/_Project/Presentation/Views/RuntimeTableView.cs
git commit -m "feat(p1d): D2 RuntimeTableView 카드 렌더를 CardView로 통합(세로 유지)"
```

---

### Task 6: 가로 전환 번들 (C-1: 앵커화 + 가로 CanvasScaler + 방향 + SafeArea)

**Files:**
- Modify: `Assets/_Project/Presentation/Views/RuntimeTableView.cs` (레이아웃 앵커화 + Content 컨테이너 + SafeAreaFitter 부착)
- Modify: `Assets/_Project/Presentation/RoundBootstrap.cs` (CanvasScaler 가로 + targetFrameRate)
- Modify: `ProjectSettings/ProjectSettings.asset` (방향 잠금)

**Interfaces:**
- Consumes: `SafeAreaFitter`.

- [ ] **Step 1: CanvasScaler 가로** — `RoundBootstrap.CreateCanvas`: `referenceResolution = new Vector2(1920, 1080); matchWidthOrHeight = 1f;`. `Begin` 진입에 `Application.targetFrameRate = 60; QualitySettings.vSyncCount = 0;`.

- [ ] **Step 2: Content 컨테이너 + SafeArea** — `BuildLayout`: `Root`(felt, full-stretch) 아래 `Content`(full-stretch) 추가하고 `Content.AddComponent<SafeAreaFitter>()`. 이후 모든 UI는 `Content` 자식으로 부착(부모 rt 교체).

- [ ] **Step 3: 레이아웃 앵커화** — 각 영역을 절대좌표 계산에서 모서리/중앙 앵커+offset로 전환(스펙 §5):
  - 점수/페이즈/소원: anchor (0,1) 좌상.
  - 최근 플레이: anchor (1,1) 우상.
  - 파트너: anchor (0.5,1) 상단중앙(정보 위/가로 팬 아래).
  - 좌/우 상대: anchor (0,0.5)/(1,0.5) 좌우중앙(프로필 끝, 세로 팬 안쪽).
  - 트릭/결과배너/소원피커: anchor (0.5,0.5) 중앙.
  - 손패: bottom-stretch(anchor x0..1,y0) + HorizontalLayoutGroup MiddleCenter(중앙정렬).
  - 결정 패널: anchor (1,0) 우하. 스킵: 결정 위. 티츄: anchor (0,0) 좌하.

- [ ] **Step 4: 방향 잠금** — `ProjectSettings.asset`: `allowedAutorotateToPortrait: 0`, `allowedAutorotateToPortraitUpsideDown: 0`, `allowedAutorotateToLandscapeRight: 1`, `allowedAutorotateToLandscapeLeft: 1`.

- [ ] **Step 5: Run to verify** — `ITableViewContractTests` + 전부 그린(앵커화 후에도 Bind/스냅샷 통과).

- [ ] **Step 6: 가로 PlayMode 스크린샷(육안)** — 가로 1920×1080에서 좌석 매핑/손패 중앙/패널 위치 정상 확인.

- [ ] **Step 7: Commit**

```bash
git add Assets/_Project/Presentation/Views/RuntimeTableView.cs Assets/_Project/Presentation/RoundBootstrap.cs ProjectSettings/ProjectSettings.asset
git commit -m "feat(p1d): D1+D2 가로 전환 번들 — 앵커화+CanvasScaler 1920x1080+방향잠금+SafeArea (C-1)"
```

---

## Self-Review (기록)

- **Spec coverage:** §4 컴포넌트 전부 태스크 매핑(SafeArea=T1, Atlas=T3, CardView=T4, CardFormat=T2(DRY 보조), RuntimeTableView=T5+T6, Canvas/방향=T6). §5 레이아웃=T6 Step3. §7 테스트=T1/T2/T4 + 계약(T5/T6). §8 게이트=각 태스크 run_tests + 스크린샷.
- **Placeholder scan:** 큰 리팩터(T5/T6)는 전체 코드 대신 메서드/영역별 정확한 변경점·앵커값 명시(파일 770줄 전량 인용 비현실적, 실행 시 채움). 신규 소파일은 전체 코드 수록.
- **Type consistency:** `CardFormat.Label/IsRed/SortKey/Key`, `CardSpriteAtlas.Face/Back`, `CardView.Set/SetSelected/SetInteractable` — 태스크 간 시그니처 일치.
- **검증 필요(실행 중 확인):** `Card.Normal/Dragon` 팩토리 시그니처(T2 Step1), CardView 테스트 훅 방식(프리팹 직렬화 vs 테스트 와이어).
