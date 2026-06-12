# Tichu Phase 1 P1-B — Presentation(Unity 2D uGUI) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.
>
> **경량 계획서(P1-A 관례 계승, 사용자 선호):** 매 스텝의 완성 코드를 미리 적지 않는다 — **타입 시그니처/인터페이스는 설계 결정이라 명시**하고, 테스트는 **이름 + 어서션 요지**로 기술한다. 각 태스크는 TDD(실패 테스트 → 최소 구현 → 통과 → 커밋). 커밋 메시지는 한국어. 설계 근거: `docs/superpowers/specs/2026-06-11-tichu-phase1b-presentation-design.md`.

**Goal:** P1-A(헤드리스 GameFlow + Normal AI) 위에, 사람 1명이 placeholder 2D uGUI로 3 AI와 **한 라운드를 정산까지 완주**하는 Unity Presentation 수직 슬라이스를, 비동기 UniTask 드라이버 + HumanAgent로 구축하고 Unity EditMode로 검증한다.

**Architecture:** P1-A의 동기 `GameDriver`를 단일 메인 스레드 UniTask `AsyncGameDriver`로 미러링(규칙은 전부 `GameEngine`/`FlowQuery`에 위임, 동기 드라이버를 **오라클**로 교차검증). AI는 동기 `AiAgent`를 즉시 래핑(`AiDecisionAgent`), 사람은 `HumanAgent`가 `IHumanInputPort`(=`TableViewModel`)로 UI 입력을 await. 뷰는 R3 ViewModel을 구독만 하는 얇은 MonoBehaviour.

**Tech Stack:** Unity 6000.3.17f1, URP, 2D uGUI, C# 9, UniTask, R3, Unity Test Runner(EditMode NUnit). `Tichu.Core`/`Tichu.GameFlow`(engine-free)를 `core/`에서 동기화한 Unity asmdef 미러로 재사용.

---

## 핵심 설계 결정 (스펙 §1~8 요약)

- **DD-1: 한 라운드 수직 슬라이스.** 9 결정포인트 모두 UI 노출. 매치 루프/화면 흐름/DI/애니=P1-C.
- **DD-2: 비동기 A.** `AsyncGameDriver`(UniTask, 단일 스레드)가 `GameDriver`의 구조 미러 — 에이전트 호출만 `await`, 규칙은 P1-A 위임.
- **DD-3: 오라클 교차검증.** 같은 시드+결정 → 동기 `GameDriver` == `AsyncGameDriver`의 `ComputeHash` (발산 차단).
- **DD-4: 소스 공유 S1.** `core/` 정본 → 동기화 스크립트(한 방향) → `Assets/_Project` 미러 + 드리프트 가드. 현재 미러는 P1-A 이전으로 드리프트됨 → Task 1이 끌어올림.
- **DD-5: 테스트 가능 코어 + 얇은 뷰.** 드라이버·VM·에이전트는 EditMode NUnit, 뷰는 수동/PlayMode.
- **DD-6: 패키지 최소.** +UniTask, +R3. DoTween/VContainer 보류(P1-C).
- **DD-7: `DecisionContext`/`ExchangeChoice`/`TurnDecision`/`Combination`/`RoundOutcome`은 GameFlow 것 재사용.** 비동기 계약에 추가된 건 `CancellationToken`뿐.

---

## File Structure

```
tools/
  sync-core-to-unity.ps1                 신규 — core/src·tests → Assets/_Project 한방향 미러 + -Check 드리프트 가드
Assets/_Project/
  Core/**                                미러(P1-A 용 양도 반영) — 생성물
  GameFlow/                              신규 미러
    Tichu.GameFlow.asmdef                신규(→Tichu.Core, noEngineReferences)
    **                                   GameFlow 소스 미러 — 생성물
  Presentation/
    Tichu.Presentation.asmdef            신규(→Core, GameFlow, UniTask, R3, UnityEngine, ugui)
    IDecisionAgent.cs                    비동기 포트(6 메서드 + ct)
    IHumanInputPort.cs                   사람 입력 포트(6 RequestXxxAsync + ct)
    AsyncGameDriver.cs                   RunRoundAsync — GameDriver 미러
    Agents/AiDecisionAgent.cs            동기 AiAgent → 비동기 어댑터
    Agents/HumanAgent.cs                 IHumanInputPort 대기
    ViewModel/DecisionRequest.cs         현재 사람 결정 기술
    ViewModel/TableViewModel.cs          R3 상태투영 + IHumanInputPort 구현 + 로컬 합법성 게이팅
    Views/TableView.cs                   얇은 뷰(좌석·트릭·점수)
    Views/HandView.cs                    얇은 뷰(내 손패)
    Views/DecisionPromptView.cs          얇은 뷰(6 프롬프트)
    RoundBootstrap.cs                    수동 배선 + 라운드 기동
    Scenes/Table.unity                   단일 씬(Canvas+카메라/라이트)
    Tests/
      Tichu.Presentation.Tests.asmdef    신규(→Core, GameFlow, Presentation, UniTask, R3, TestRunner; Editor)
      ScriptedDecisionAgent.cs           테스트 더블(async ScriptedAgent)
      *Tests.cs                          오라클·드라이버·HumanAgent·AiDecisionAgent·VM
Assets/_Project/Tests/EditMode/
  Tichu.Core.Tests.asmdef                수정(+Tichu.GameFlow 참조)
  **                                     Core+GameFlow 테스트 미러 — 생성물
Packages/manifest.json                   수정(+UniTask, +R3)
```

**책임 분리:** `sync 스크립트`=단일 정본 보장, `IDecisionAgent`=비동기 결정 포트, `AsyncGameDriver`=1라운드 비동기 오케스트레이션(규칙은 P1-A 위임), `IHumanInputPort`=사람 입력 seam, `TableViewModel`=상태투영+입력중재, 뷰=바인딩만.

---

## Task 1: 소스 동기화 스크립트 + 미러 끌어올리기 + GameFlow asmdef

**Files:** Create `tools/sync-core-to-unity.ps1`, `Assets/_Project/GameFlow/Tichu.GameFlow.asmdef`; Modify `Assets/_Project/Tests/EditMode/Tichu.Core.Tests.asmdef`; Mirror(생성물) `Assets/_Project/Core/**`, `Assets/_Project/GameFlow/**`, `Assets/_Project/Tests/EditMode/**`.

**설계:** 스크립트는 한 방향 복사 — `core/src/Tichu.Core/**/*.cs`→`Assets/_Project/Core/`, `core/src/Tichu.GameFlow/**/*.cs`→`Assets/_Project/GameFlow/`, `core/tests/Tichu.Core.Tests/**/*.cs`→`Assets/_Project/Tests/EditMode/`. `.cs`만 복사(기존 `.meta` 보존, 신규는 Unity가 임포트 시 생성). `-Check` 스위치: 미러와 정본을 비교해 다르면 비-0 종료(드리프트 가드). GameFlow asmdef는 Core 미러 패턴 미러: `{ name:"Tichu.GameFlow", references:["Tichu.Core"], noEngineReferences:true, autoReferenced:true }`. Tests asmdef references에 `"Tichu.GameFlow"` 추가.

- [ ] **Step 1:** `sync-core-to-unity.ps1` 작성(복사 매핑 + `-Check` 드리프트 비교). `obj/`,`bin/` 제외.
- [ ] **Step 2:** `Tichu.GameFlow.asmdef` 작성(위 시그니처) + `Tichu.Core.Tests.asmdef`에 GameFlow 참조 추가.
- [ ] **Step 3:** `pwsh tools/sync-core-to-unity.ps1` 실행 → 미러 갱신(용 양도 Core + GameFlow 소스 + GameFlow 테스트가 Assets에 반영).
- [ ] **Step 4:** `pwsh tools/sync-core-to-unity.ps1 -Check` → 종료코드 0(동기화됨) 확인.
- [ ] **Step 5(검증):** Unity 도메인 리로드 후 `read_console` 무에러 + Unity Test Runner EditMode 그린(Core 용 양도 포함 + GameFlow 미러 테스트, `[Explicit]` 제외).
- [ ] **Step 6:** 커밋 `chore(p1b): core↔Unity 동기화 스크립트 + 미러를 P1-A까지 끌어올림`.

---

## Task 2: 패키지 추가 (UniTask + R3)

**Files:** Modify `Packages/manifest.json`(+ 필요한 scoped registry/NuGet 설정).

**설계:** UniTask는 git URL 의존(`com.cysharp.unitask` — `https://github.com/Cysharp/UniTask.git?path=src/UniTask/Assets/Plugins/UniTask`). R3는 (a) R3 코어(NuGetForUnity 또는 OpenUPM `com.cysharp.r3`) + (b) `R3.Unity`(git) + (c) 의존 `ObservableCollections`. **실행자는 현재 Unity 6000.3 호환 최신 버전·정확 설치 경로를 확인**(설치 방식이 R3 버전에 따라 다름). 목표: `using Cysharp.Threading.Tasks;`·`using R3;`가 컴파일되는 상태.

- [ ] **Step 1:** manifest에 UniTask 추가 → 패키지 해결 → `read_console` 무에러.
- [ ] **Step 2:** R3(+ObservableCollections) 추가(확인한 방식으로) → 해결 → 무에러.
- [ ] **Step 3(검증/RED→GREEN 스모크):** `Assets/_Project/Presentation/Tests`에 임시 EditMode 테스트 — `UniTask.FromResult(1)` await 결과 1, `new R3.ReactiveProperty<int>(0)` 값 변경 관찰. 컴파일+그린 확인 후 임시 테스트 제거(또는 Task 3 계약 테스트로 대체).
- [ ] **Step 4:** 커밋 `chore(p1b): UniTask·R3 패키지 추가`.

---

## Task 3: `Tichu.Presentation` asmdef + `IDecisionAgent`/`IHumanInputPort` 계약

**Files:** Create `Assets/_Project/Presentation/Tichu.Presentation.asmdef`, `IDecisionAgent.cs`, `IHumanInputPort.cs`, `Assets/_Project/Presentation/Tests/Tichu.Presentation.Tests.asmdef`; Test `AsyncContractTests.cs`.

**설계:** `Tichu.Presentation.asmdef` references `["Tichu.Core","Tichu.GameFlow","UniTask","R3"]`(engine refs on, autoReferenced true). Tests asmdef references `["Tichu.Core","Tichu.GameFlow","Tichu.Presentation","UniTask","R3","UnityEngine.TestRunner","UnityEditor.TestRunner"]`, includePlatforms `["Editor"]`, precompiled `nunit.framework.dll`, defineConstraints `["UNITY_INCLUDE_TESTS"]`.
- `IDecisionAgent`: 6 메서드 `UniTask<bool> CallGrandTichuAsync(DecisionContext, CancellationToken)` / `UniTask<ExchangeChoice> ChooseExchangeAsync(...)` / `UniTask<bool> CallTichuAsync(...)` / `UniTask<TurnDecision> DecideTurnAsync(...)` / `UniTask<Combination?> DecideBombAsync(...)` / `UniTask<int> ChooseDragonRecipientAsync(...)`. (DecisionContext 등은 `Tichu.GameFlow.Agents` 재사용.)
- `IHumanInputPort`: 동일 6 결정의 `RequestXxxAsync(DecisionContext, CancellationToken)`.

- [ ] **Step 1:** 두 asmdef 작성.
- [ ] **Step 2(RED):** `AsyncContractTests` — 더미 `IDecisionAgent`/`IHumanInputPort` 구현(즉시 기본값 반환)을 만들고, 6 메서드 시그니처/반환형이 GameFlow 타입과 맞물려 컴파일·호출되는지(예: `await dummy.DecideTurnAsync(ctx, default)`가 `TurnDecision` 반환). 어서션 요지: 더미 기본값 일치.
- [ ] **Step 3:** 컴파일 실패(인터페이스 미정의) 확인.
- [ ] **Step 4:** `IDecisionAgent.cs`/`IHumanInputPort.cs` 구현 → EditMode 그린.
- [ ] **Step 5:** 커밋 `feat(p1b): Tichu.Presentation asmdef + IDecisionAgent/IHumanInputPort 계약`.

---

## Task 4: `AiDecisionAgent` (동기 → 비동기 어댑터)

**Files:** Create `Assets/_Project/Presentation/Agents/AiDecisionAgent.cs`; Test `AiDecisionAgentTests.cs`.

**설계:** `AiDecisionAgent(ulong roundSeed, int seat) : IDecisionAgent`, 내부 `AiAgent _inner`. 각 `XAsync(ctx, ct) => UniTask.FromResult(_inner.X(ctx))`(즉시 완료, ct 무시 가능). 결정성·휴리스틱·시드 P1-A 그대로.

- [ ] **Step 1(RED):** `AiDecisionAgentTests` — 동일 ctx에서 `await ai.DecideTurnAsync(ctx, default)` == `new AiAgent(seed,seat).DecideTurn(ctx)`(같은 시드); `DecideBombAsync`/`ChooseDragonRecipientAsync` 등 한두 개 더; UniTask가 **즉시 완료**(`.Status == Succeeded` 또는 `.GetAwaiter().IsCompleted`).
- [ ] **Step 2:** 실패 확인.
- [ ] **Step 3:** `AiDecisionAgent` 구현.
- [ ] **Step 4:** EditMode 그린.
- [ ] **Step 5:** 커밋 `feat(p1b): AiDecisionAgent (동기 AiAgent → 비동기 어댑터)`.

---

## Task 5: `AsyncGameDriver.RunRoundAsync` + 오라클 교차검증

**Files:** Create `Assets/_Project/Presentation/AsyncGameDriver.cs`, `Assets/_Project/Presentation/Tests/ScriptedDecisionAgent.cs`; Test `AsyncGameDriverTests.cs`.

**설계:** `AsyncGameDriver(IDecisionAgent[] agents)` + `UniTask<RoundOutcome> RunRoundAsync(GameState s, CancellationToken ct)`. `RoundOutcome`는 `Tichu.GameFlow` 것 재사용. 본문은 `GameDriver.RunRound`의 **구조 미러**: 같은 고정순서(큰티츄 커서→교환 커서→DrivePlay[①용양도 ②폭탄창 `FlowQuery.SeatsWithLegalBomb` 시계순 ③작은티츄 훅 ④차례행동]), 같은 `GameEngine.Apply`(거부=throw)·`FlowQuery`·`ScoreCalculator.ScoreRound`. 유일 차이: `agent.X(ctx)` → `await agent.XAsync(ctx, ct)`. `ScriptedDecisionAgent`는 `ScriptedAgent`의 async 미러(옵션 `Func` 백킹 + 동일 안전 기본값, 각 메서드 `UniTask.FromResult`).
**EditMode 실행:** 모든 결정이 즉시 완료되므로 `RunRoundAsync(s, default).GetAwaiter().GetResult()`로 동기 취득 가능(plain `[Test]`).

- [ ] **Step 1(RED):** `ScriptedDecisionAgent` 작성 + `AsyncGameDriverTests`:
  - `Async_round_reaches_end`: 기본 ScriptedDecisionAgent×4 + `NewRound(42)` → `State.Phase==RoundEnd`·`Result≠null`·`Log.Count>0`.
  - `Async_replay_reproduces_hash`: `Log` 재적용(`GameEngine.Apply`)+`ScoreRound` → 동일 `ComputeHash`.
  - **`Oracle_matches_sync_driver`(핵심):** 같은 시드로 (a) 동기 `new GameDriver(AiAgent×4).RunRound(NewRound(seed))` (b) 비동기 `new AsyncGameDriver(AiDecisionAgent×4).RunRoundAsync(NewRound(seed))` → **동일 `State.ComputeHash()` + 동일 `Log.Count`** (여러 시드).
- [ ] **Step 2:** 실패 확인(AsyncGameDriver 미정의).
- [ ] **Step 3:** `AsyncGameDriver` 구현(미러).
- [ ] **Step 4:** EditMode 그린(오라클 포함).
- [ ] **Step 5:** 커밋 `feat(p1b): AsyncGameDriver + 동기 드라이버 오라클 교차검증`.

---

## Task 6: `HumanAgent` + `IHumanInputPort` 흐름

**Files:** Create `Assets/_Project/Presentation/Agents/HumanAgent.cs`; Test `HumanAgentTests.cs`(+ 테스트용 `FakeInputPort` 또는 TCS 백킹 더블).

**설계:** `HumanAgent(IHumanInputPort input, int seat) : IDecisionAgent`. 각 `XAsync(ctx, ct) => _input.RequestXAsync(ctx, ct)`. 입력 대기/취소는 포트가 담당. 테스트 더블 `FakeInputPort`: `RequestTurnDecisionAsync`가 `UniTaskCompletionSource<TurnDecision>`를 노출, 테스트가 `Complete(d)` 호출 시 await 풀림; ct 등록으로 취소.

- [ ] **Step 1(RED):** `HumanAgentTests`:
  - `Awaits_until_submitted`: `var t = human.DecideTurnAsync(ctx, default)` 미완 → `fake.CompleteTurn(d)` → `await t == d`.
  - `Cancellation_propagates`: ct 취소 시 await가 `OperationCanceledException`(또는 합의된 취소 결과) → 드라이버 정상 종료 가능.
- [ ] **Step 2:** 실패 확인.
- [ ] **Step 3:** `HumanAgent` 구현(+필요 시 FakeInputPort는 Tests에).
- [ ] **Step 4:** EditMode 그린.
- [ ] **Step 5:** 커밋 `feat(p1b): HumanAgent (IHumanInputPort 입력 대기)`.

---

## Task 7: `TableViewModel` + `DecisionRequest` (R3 상태투영 + IHumanInputPort + 로컬 합법성 게이팅)

**Files:** Create `Assets/_Project/Presentation/ViewModel/DecisionRequest.cs`, `ViewModel/TableViewModel.cs`; Test `TableViewModelTests.cs`.

**설계:** `DecisionRequest`: `Kind`(enum: GrandTichu/Exchange/Tichu/Turn/Bomb/DragonRecipient) + 옵션(합법수 `IReadOnlyList<Combination>`, `CanPass`, 소원 필요 여부, 좌/우 좌석 등). `TableViewModel : IHumanInputPort` — R3 `ReactiveProperty`들(`Phase`, `Seats[4]` 투영 모델, `MyHand`, `CurrentTrick`, `RoundResult`, `PendingDecision`). `ApplySnapshot(GameState s)`(드라이버 통지) → 속성 갱신. `RequestXxxAsync`: `PendingDecision` 세팅 + `UniTaskCompletionSource` await. `SubmitXxx(...)`: **로컬 합법성 검증**(`LegalMoveGenerator`/`CombinationRecognizer`로 합법수 여부) 통과 시 TCS 완료·`PendingDecision=null`, 불합격 시 무시(거부).

- [ ] **Step 1(RED):** `TableViewModelTests`:
  - `Snapshot_projects`: 구성한 `GameState`(또는 `GameFlowHelpers.PlayState`) `ApplySnapshot` → `MyHand`/`Seats[i].HandCount`/`CurrentTrick`/`Phase` R3 현재값 일치.
  - `Request_then_legal_submit_completes`: `RequestTurnDecisionAsync(ctx)` → `PendingDecision.Value.Kind==Turn`·합법수 포함; `SubmitTurn(합법)` → await 완료·`PendingDecision==null`.
  - `Illegal_submit_rejected`: 합법수 아닌 제출 → await 미완·`PendingDecision` 유지.
- [ ] **Step 2:** 실패 확인.
- [ ] **Step 3:** `DecisionRequest` + `TableViewModel` 구현.
- [ ] **Step 4:** EditMode 그린.
- [ ] **Step 5:** 커밋 `feat(p1b): TableViewModel + DecisionRequest (R3 상태투영 + 로컬 합법성 게이팅)`.

---

## Task 8: 얇은 uGUI 뷰 (MonoBehaviour) — 바인딩

**Files:** Create `Assets/_Project/Presentation/Views/TableView.cs`, `HandView.cs`, `DecisionPromptView.cs`; uGUI Canvas/프리팹(에디터 수동).

**설계(자동테스트 없음 — 얇은 뷰, 수동/PlayMode):** 각 뷰 `Bind(TableViewModel vm)`에서 R3 속성 구독(`vm.X.Subscribe(...)`)→uGUI 갱신, 입력은 vm 메서드 호출.
- `TableView`: `Seats[4]`(라벨·뒷면 수량·티츄 마커·차례 하이라이트)·`CurrentTrick`·`RoundResult` 렌더.
- `HandView`: `MyHand` 구독 → 카드 버튼 생성/선택 토글, 선택 집합을 vm에 전달.
- `DecisionPromptView`: `PendingDecision` 구독 → Kind별 프롬프트(큰티츄/교환/티츄/차례[+소원]/폭탄/용양도) 표시 → `vm.SubmitXxx(...)` 호출. 합법수만 활성화.

- [ ] **Step 1:** 세 뷰 스크립트 작성(구독/렌더/입력 위임).
- [ ] **Step 2:** 에디터에서 Canvas + 카드/좌석/프롬프트 프리팹 배치, 뷰에 참조 연결.
- [ ] **Step 3(검증):** `read_console` 무에러(컴파일). 통합 PlayMode 검증은 Task 9에서.
- [ ] **Step 4:** 커밋 `feat(p1b): 얇은 uGUI 뷰(TableView/HandView/DecisionPrompt) 바인딩`.

---

## Task 9: `RoundBootstrap` + `Table.unity` 씬 — 한 라운드 PlayMode 완주

**Files:** Create `Assets/_Project/Presentation/RoundBootstrap.cs`, `Assets/_Project/Presentation/Scenes/Table.unity`.

**설계:** `RoundBootstrap : MonoBehaviour`(인스펙터 `ulong seed`) — `Start`에서: `GameEngine.NewRound(seed)` → `new TableViewModel()`(초기 `ApplySnapshot`) → 에이전트 `[ HumanAgent(input:vm, seat0), AiDecisionAgent(seed,1), AiDecisionAgent(seed,2), AiDecisionAgent(seed,3) ]` → 뷰 `Bind(vm)` → `new AsyncGameDriver(agents).RunRoundAsync(state, destroyCancellationToken)` 기동(매 Apply 후 `vm.ApplySnapshot`), 완료 시 `RoundResult` 표시. (VContainer 없음, 수동 배선.)

- [ ] **Step 1:** `RoundBootstrap` 작성(배선 + 기동 + 스냅샷 통지 연결).
- [ ] **Step 2:** `Table.unity` 씬: Canvas + 기본 카메라/Directional Light + 뷰 프리팹 + `RoundBootstrap`(seed 지정).
- [ ] **Step 3(검증·핵심):** PlayMode 진입 → 사람이 seat0로 **한 라운드를 정산까지 완주**(큰티츄→교환→트릭→폭탄/소원/용양도 발생 시 처리→RoundResult). `read_console`에 예외/드라이버 throw 0.
- [ ] **Step 4(검증):** Unity Test Runner EditMode 전체 그린(특히 `Oracle_matches_sync_driver`).
- [ ] **Step 5:** 커밋 `feat(p1b): RoundBootstrap + Table 씬 — 한 라운드 PlayMode 완주`.

---

## P1-B 완료 기준 (DoD)

- [ ] 동기화 스크립트 + 드리프트 가드 통과(미러 == core/), Unity가 Core+GameFlow 미러를 무에러 컴파일.
- [ ] Unity EditMode 테스트 그린: AiDecisionAgent·AsyncGameDriver·HumanAgent·TableViewModel + **오라클(비동기==동기 ComputeHash)**.
- [ ] 에디터 PlayMode에서 사람이 seat0로 **한 라운드를 정산까지 크래시·불법상태 없이 완주**(9 결정 모두 UI로 처리).
- [ ] `Tichu.Presentation`만 UnityEngine/UniTask/R3 참조, GameFlow/Core 미러는 engine-free 유지. DoTween/VContainer 미도입.

## Self-Review (작성자 점검)

- **Spec 커버리지:** §2 소스공유/동기화(T1)·§3 asmdef/패키지(T1,T2,T3)·§4 IDecisionAgent/AsyncGameDriver/AiDecisionAgent/HumanAgent(T3~T6)·§5 ViewModel/중재/게이팅(T7)·§6 결정 프롬프트(T8)·§7 씬/부트스트랩(T9)·§8 테스트/오라클(T5,T7,T9) — 스펙 전 항목 매핑.
- **타입 일관성:** `IDecisionAgent`(6 async+ct)·`IHumanInputPort`(6 RequestXxxAsync)·`AsyncGameDriver.RunRoundAsync→UniTask<RoundOutcome>`·`AiDecisionAgent(seed,seat)`·`HumanAgent(IHumanInputPort,seat)`·`TableViewModel:IHumanInputPort`+`ApplySnapshot`/`SubmitXxx`·`DecisionRequest{Kind,…}` — 태스크 간 시그니처 일치. `RoundOutcome`/`DecisionContext`/`TurnDecision` 등은 GameFlow 재사용(신규 정의 아님).
- **경량 범위:** 완성 코드 대신 시그니처 + 테스트명/어서션 요지(사용자 선호). Unity 고유(asmdef·패키지·씬·EditMode 실행)는 검증 게이트로 명시. R3 정확 설치 경로는 실행자가 버전 확인.
- **검증 반영:** 오라클 교차검증(T5)으로 비동기↔동기 발산 차단, 드리프트 가드(T1)로 미러 일관성, 로컬 합법성 게이팅+드라이버 throw 백스톱(T7), 단일 스레드 UniTask로 레이스 회피.
