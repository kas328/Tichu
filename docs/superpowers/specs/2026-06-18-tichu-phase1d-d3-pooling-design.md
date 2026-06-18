# Tichu Phase 1-D (D3) — 카드 풀링 + Canvas 정적/동적 분리 설계

- 날짜: 2026-06-18
- 상태: 승인됨(브레인스토밍 완료, 옵션 a = 전체 설계) → 구현 계획 단계로
- 선행: **D0 + D1+D2 완료**(`ITableView` 추출 · 가로 1920×1080 · SafeArea · CardView 프리팹 · CardSpriteAtlas no-op, 머지커밋 `da9925c` on `main`, EditMode 311/308 그린)
- 상위 설계: `티츄_Phase1_잔여_아키텍처.html` §6.2 · 본 묶음 설계 보고서 `티츄_P1D_D3_카드풀링_설계.html`
- 검증: 멀티에이전트 적대적 검증 5차원(오라클 · 렌더 호출부 · Canvas 시맨틱 · EditMode 테스트 가능성 · 상태 누수) — 두 결함 사전 차단(아래 §2)

## 1. 목적

P1-D 60fps 묶음의 첫 걸음. 측정된 렌더 핫스팟 — `RenderHand/RenderBacks/RenderTrick`이 **매 스냅샷마다 자식 CardView를 Destroy + Instantiate**(GC 스파이크 · 전체 Canvas 리빌드) — 을 **루트별 CardChipPool**(SetActive 토글 + 내용 재설정)과 **정적/동적 Canvas 분리**로 전환한다. 게임 로직·구독 계약·오라클(onApply=null) 경로는 비트동일로 보존한다. 실측 수치(60fps/GC/드로콜)는 D6 실기기 게이트의 몫이며, D3는 **동치 + 풀 불변식 + 구조**를 EditMode로 못박는다.

## 2. 검증이 잡은 두 결함 (구현 전 반영)

1. **중첩 Canvas는 자체 GraphicRaycaster가 필요하다.** "루트 GraphicRaycaster 하나가 자식 캔버스까지 레이캐스트한다"는 가정은 **거짓**. 서브 캔버스는 자체 `GraphicRaycaster`가 없으면 그 안의 Button이 클릭을 못 받는다 → `_handRoot`에 Canvas만 붙이면 **손패 카드 클릭(ToggleCard)이 조용히 죽는다**(유일한 조작면). **반영:** `_handRoot`는 Canvas + GraphicRaycaster 둘 다; 트릭·뒷면은 Button이 없어 Canvas만.
2. **CardView 버튼 상태가 풀 재사용 시 샌다(잠재 버그).** `_button.interactable`/`onClick`은 `Set/SetSize/SetHighlight`로 초기화되지 않고 오직 `SetInteractable`로만 된다. 트릭·뒷면 칩은 `Set+SetSize`만 부른다 → 손패 칩이던 인스턴스가 풀에서 트릭·뒷면으로 재사용되면 **옛 카드를 캡처한 onClick 리스너가 살아남아** 뒷면 클릭이 엉뚱한 카드를 선택한다. **반영:** 루트별 토폴로지(교차 재사용 원천 차단) + `Set()` 버튼 중립화(소비자 규율 무관 안전) 이중 차단.

나머지 3차원은 `holds:true` — 오라클은 뷰와 구조적으로 분리(Card=readonly struct 값복사, `TableViewModel.cs:132` 방어적 List 복사), 렌더 호출부 8곳·ClearChildren 7곳 전수 동치 보존, EditMode 테스트는 기존 패턴으로 가능.

## 3. 핵심 결정 (브레인스토밍 합의)

1. **범위 = 풀 + Canvas 분할**(설계안 전체). 플레이 로그 Text는 풀링하지 않음.
2. **토폴로지 = 루트별 풀.** CardView 전용(범용 제네릭은 단일 타입이라 YAGNI) 작은 풀 클래스를 동적 루트마다 1개. 칩이 제 루트에 머물러 `SetParent`(레이아웃 dirty) 없이 `SetActive` 토글 + 내용 재설정만. **좌석0(=나)은 뒷면이 없어 풀 총 5개**(손패1 · 트릭1 · 뒷면3).
3. **단일 뷰 제자리 진화.** `RuntimeTableView`를 그대로 진화(단일 `ITableView` 구현 유지). 롤백=git.
4. **완료 기준 = 동치 + 풀 불변식 + 구조 검증.** 실측은 D6.

## 4. 범위

**포함:** `CardChipPool` 신규 + 루트별 5개 배선 · `RenderHand/RenderBacks/RenderTrick` 풀 전환 · 동적 루트 3종 Canvas 분리(+손패 GraphicRaycaster) · `CardView` 버튼 중립화 + `HasClickListener` 플래그 + 주석 수정 · EditMode 테스트 5종.

**제외(그대로 둠):** `_actionRoot`/`_wishPickRoot`/`_exchangeRoot` 및 `_playsRoot`의 ClearChildren+Destroy · 플레이 로그 Text(`_playEntries`, 5초 페이드) · DoTween 트윈 풀(D4) · 실 스프라이트 아트·60fps 실측(D6) · PrefabTableView 전환(P1-D 후속).

## 5. 컴포넌트

| 항목 | 종류 | 역할 |
|---|---|---|
| `Views/CardChipPool.cs` | 신규 | CardView 전용 루트별 풀. 생성자 `(RectTransform root, CardView prefab)`. `Begin()`(커서 0) → `Next()`(비활성 칩 재사용; 없으면 `Object.Instantiate(prefab, root)` 후 추적 리스트에 추가; `SetActive(true)`) → `End()`(커서 이후 추적 칩 `SetActive(false)`). **풀링 경로에서 `Object.Destroy` 0.** 잉여 정리는 추적 리스트 순회 — `while(childCount>N)` 패턴 금지(과거 OOM 전력). 테스트 시임: `int CreatedCount`/`ActiveCount`/`FreeCount` 공개 read-only |
| `Views/CardView.cs` | 변경 | (a) `Set()` 끝에 버튼 중립화: `if (_button != null) { _button.onClick.RemoveAllListeners(); _button.interactable = false; }`. (b) `public bool HasClickListener { get; private set; }` — `SetInteractable(on,cb)`에서 `on && cb!=null`이면 true, `Set()` 중립화에서 false. (c) 오해 소지 주석(69줄) 수정 |
| `Views/RuntimeTableView.cs` | 변경 | (a) `BuildLayout`에서 동적 루트 5개에 대응하는 `CardChipPool` 생성·보관(배열/필드). (b) 동적 루트 3종(`_handRoot`/`_trickRoot`/`_backRoots[1..3]`)에 `Canvas` 추가, `_handRoot`에만 `GraphicRaycaster` 추가, 전부 `overrideSorting=false`. (c) `RenderHand`/`RenderBacks`/`RenderTrick`을 `ClearChildren`+`Instantiate` → 풀 `Begin/Next/End`로 교체(소비자는 기존 `Set/SetSize/SetHighlight/SetInteractable` 그대로 호출). **구독·제출·합법성·오라클 배선 불변** |

## 6. 풀 API 및 사용 패턴

```csharp
sealed class CardChipPool {
    public CardChipPool(RectTransform root, CardView prefab) { … }
    public void Begin();        // 커서 0으로
    public CardView Next();     // 비활성 칩 재사용; 없으면 Instantiate+추가; SetActive(true); 커서++
    public void End();          // 커서 이후 칩 SetActive(false)
    public int CreatedCount { get; }   // 총 Instantiate 횟수(테스트)
    public int ActiveCount  { get; }
    public int FreeCount    { get; }
}
```

```csharp
// RenderHand 예시
_handPool.Begin();
foreach (var card in _hand.OrderBy(SortKey)) {
    var cv = _handPool.Next();
    cv.Set(card, _atlas, faceUp: true);
    cv.SetSize(66, 100);
    cv.SetHighlight(HighlightFor(card));
    var cap = card;
    cv.SetInteractable(CardSelectable, () => ToggleCard(cap));
}
_handPool.End();
```

커서 순서 = 형제(sibling) 순서 = 정렬(`OrderBy(SortKey)`) 순서 → sibling 재정렬 불필요. 재활성화는 sibling 인덱스를 유지하고, 신규 칩은 말미에 추가되므로 커서 0..N 채움이 곧 정렬 위치다. 비활성 자식은 Unity 레이아웃 그룹에서 제외되므로 잉여 칩이 손패 폭을 차지하지 않는다.

## 7. Canvas 분리 사양

- `_handRoot`: **Canvas + GraphicRaycaster**(버튼 있음, 필수).
- `_trickRoot`, `_backRoots[1]`, `_backRoots[2]`, `_backRoots[3]`: **Canvas만**(버튼 없음, 레이캐스터 불필요).
- 전부 `overrideSorting = false`(기본값 유지) — 드로 순서 보존, 특히 `_resultPanel`(결과 배너)이 트릭 위 레이어로 남도록.
- Canvas 컴포넌트는 RectTransform 앵커/위치를 바꾸지 않으며, 세 루트는 부모 LayoutGroup의 자식이 아니므로(각자 자기 LayoutGroup으로 배치) 레이아웃 깨짐 없음.
- 이 이득은 풀링과 결합돼야 실현된다: 풀링이 destroy/instantiate churn을 없애므로, 카드 탭 시 손패 서브캔버스만 dirty→리빌드되고 정적 캔버스는 보존된다.

## 8. 오라클 보존 불변식

풀링은 `RuntimeTableView`의 GameObject 수명만 바꾼다. 다음을 **건드리지 않는다**:

- `AsyncGameDriver` · `onApply` 콜백 배선 — `onApply=null` 경로 비트동일.
- `TableViewModel.ApplySnapshot` 및 `cs:132`의 방어적 `new List<Card>(...)` 복사("최적화" 명목으로도 제거 금지 — aliasing 차단의 핵심).
- `RenderTrick`이 읽는 `trick.Top.Cards` 등 읽기전용 컬렉션 — in-place 정렬/변형 금지(`OrderBy`는 새 시퀀스 반환이라 안전).

**회귀 게이트:** 기존 오라클 교차검증(동기 GameDriver vs 비동기 ComputeHash, 다중 시드)을 D3 전후로 그린 확인.

## 9. 검증 · 테스트 (EditMode, Editor-only asmdef)

| 테스트 | 단언 | 비고 |
|---|---|---|
| 풀 불변식 | N개 빌드→`CreatedCount==N`; 축소(M<N)→여전히 N(신규 할당 0); 재확장→여전히 N. release된 칩 `activeSelf==false` | `childCount` 금지(Destroy 프레임 지연) → 풀 카운터로 |
| 리스너 누수 | 콜백 arm→release(재사용)→다른 카드로 re-Get(재무장 X)→onClick 호출→옛 콜백 안 불림 | 거동 단언(또는 `HasClickListener`) |
| 렌더 동치 | Bind→스냅샷 적용→활성 자식 수·라벨이 손패와 일치. 동일 크기 반복 렌더에 `CreatedCount` 불변 | `ITableViewContractTests` 확장 |
| 구조 | `_handRoot`=Canvas+GraphicRaycaster; `_trickRoot`·`_backRoots[1..3]`=Canvas | Bind 후 컴포넌트 존재 확인 |
| 오라클 회귀 | 동기==비동기 ComputeHash(다중 시드) — D3 전후 그린 | **게이트** |

테스트 패턴은 기존 준수: `new GameObject(...) + AddComponent<CardView>()`, `Object.DestroyImmediate` teardown, `Resources.Load<CardView>("CardView")`/`Instantiate` EditMode 동작(기존 그린 테스트로 입증됨).

## 10. 위험 · 함정

- **Destroy 프레임 지연:** 풀 release는 `SetActive(false)`(즉시) 사용, `Object.Destroy` 금지. 테스트 단언은 `childCount`가 아닌 풀 내부 카운터로.
- **리스너 잔존:** `Set()` 버튼 중립화로 차단(루트별 토폴로지가 1차 차단, 중립화가 2차).
- **sibling 순서:** 커서 채움 순서로 보장 — 단, 재활성 칩이 정렬 위치를 벗어나지 않도록 풀이 칩을 추적 리스트의 고정 순서로 재사용해야 함.
- **Canvas 분할 win 의존성:** 풀링이 선행/동반돼야 분할 이득 실현(검증 dim3). 같은 PR로 함께 랜딩.

## 11. 의존 · 다음

의존: D3 ⊃ {D0 ITableView, D1·D2 가로/CardView/Atlas}. 다음 = D4(`ICardAnimator`+DoTween+AnimationQueue) · D5(`IAudioService`+Bank) · D6(실기기 60fps 게이트).
