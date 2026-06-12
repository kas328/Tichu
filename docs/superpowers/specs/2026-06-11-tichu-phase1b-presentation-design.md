# Tichu Phase 1 P1-B — Presentation(Unity 2D uGUI) 설계 스펙

> **상태:** 사용자 승인 완료(2026-06-11). 브레인스토밍 대화(범위→검증→비동기 아키텍처→소스 공유→섹션 A~D)로 다졌다. 검토 리포트(HTML+SVG): `docs/reports/2026-06-11-phase1b-presentation-design-report.html`.
>
> 본 스펙은 **설계(아키텍처·계약·결정)** 를 정의한다. 단계별 TDD 태스크는 별도 계획서(`docs/superpowers/plans/`)가 담당한다. 선행: P1-A 완료(`Tichu.GameFlow` + 용 양도 Core 변경, main 머지 `f288ede`).

## 0. 배경 / 목표

P1-A(헤드리스 GameFlow + Normal AI)는 `main` 머지·검증 완료(비-Explicit 242 + Heavy 10만판 + AI품질 그린). P1-B의 목표: **사람 1명이 placeholder 2D uGUI로 3 AI와 한 라운드를 정산까지 완주**하는 수직 슬라이스. 이로써 핵심 아키텍처(동기 P1-A를 비동기 사람 입력과 잇는 드라이버 + HumanAgent)와 "플레이 가능성"을 검증한다. 매치 루프·화면 흐름·DI·애니메이션은 P1-C/후속.

## 1. 잠긴 결정 (사용자 확정 2026-06-11)

1. **범위 = 한 라운드 수직 슬라이스.** 9 결정포인트를 모두 UI로 노출하는 한 라운드. 매치 루프/결과 화면은 P1-C.
2. **플레이어 구성.** 사람 1명 = 고정 좌석 **South(seat 0)**, 나머지 3좌석 = Normal `AiAgent`. 사람 손패만 앞면, 상대는 뒷면 수량만.
3. **검증 = 테스트 가능 코어 + 얇은 뷰.** 비동기 드라이버 + HumanAgent + ViewModel은 순수 C#로 **Unity EditMode NUnit** 테스트. uGUI MonoBehaviour는 바인딩만 하는 얇은 뷰(수동/PlayMode 확인).
4. **비동기 아키텍처 = A(UniTask 비동기 드라이버).** `AsyncGameDriver`가 단일 메인 스레드에서 `IDecisionAgent`를 `await`. AI는 동기 `AiAgent`를 즉시 래핑, `HumanAgent`는 UI 입력을 await. 스레딩(B)·코루틴(C) 기각.
5. **소스 공유 = S1(core/ 정본 + 동기화 스크립트).** `core/`가 단일 정본, 스크립트가 한 방향으로 `Assets/_Project`에 미러, 드리프트 가드.
6. **패키지.** **+UniTask, +R3**만 추가. DoTween(애니 없음)·VContainer(DI=P1-C)는 YAGNI 보류.

## 2. 소스 공유 (S1) — core/ ↔ Unity 미러

현 상태: `core/`(dotnet sln)와 `Assets/_Project`(Unity)가 Tichu.Core를 **이중 사본**으로 보유, Unity 사본은 P1-A 이전으로 **드리프트**(용 양도·GameFlow 없음).

- **정본 = `core/src`, `core/tests`.** `Assets/_Project`는 생성물 — 직접 편집 금지.
- **동기화 스크립트**(예: `tools/sync-core-to-unity.ps1`): 한 방향 복사
  - `core/src/Tichu.Core/**/*.cs` → `Assets/_Project/Core/`
  - `core/src/Tichu.GameFlow/**/*.cs` → `Assets/_Project/GameFlow/`(신규)
  - `core/tests/Tichu.Core.Tests/**/*.cs` → `Assets/_Project/Tests/EditMode/`
  - `.cs` 소스만 미러. 기존 `.meta`는 보존, 신규 파일 `.meta`는 Unity가 임포트 시 생성.
- **드리프트 가드**(`-Check` 모드): 미러와 정본을 비교해 다르면 비-0 종료 → CI/사전점검에서 차단.
- **asmdef는 동기화 대상 아님:** `Tichu.GameFlow.asmdef`·`Tichu.Presentation.asmdef`는 Unity측 1회 작성(`core/`는 `.csproj` 사용).
- **P1-B 첫 작업:** 스크립트 작성 → 미러를 P1-A까지 끌어올림(용 양도 Core + GameFlow + GameFlow 테스트).

## 3. 레이어 / asmdef

| asmdef | 위치 | 책임 | 참조 | engine |
|---|---|---|---|---|
| `Tichu.Core` | Assets/_Project/Core | 룰·GameState·정산(미러, **P1-A 변경 반영**) | — | noEngineReferences |
| `Tichu.GameFlow` **신규** | Assets/_Project/GameFlow | 동기 드라이버/포트/AI(미러) = **룰 권위 + 오라클** | Core | noEngineReferences |
| `Tichu.Presentation` **신규** | Assets/_Project/Presentation | 비동기 드라이버·ViewModel·뷰·부트스트랩 | Core, GameFlow, UniTask, R3, UnityEngine, ugui | engine |
| `Tichu.Core.Tests` | Assets/_Project/Tests/EditMode | EditMode 테스트(Core+GameFlow 미러) | Core, **GameFlow**, TestRunner | Editor |
| `Tichu.Presentation.Tests` **신규** | Assets/_Project/Presentation/Tests | 비동기 드라이버·VM·오라클 테스트 | Core, GameFlow, Presentation, UniTask, R3, TestRunner | Editor |

**`Tichu.Presentation` 내부 4계층(단일 asmdef 안에서 클래스 단위로 분리):**
- **Async Port + Driver**(순수 C#): `IDecisionAgent`, `AsyncGameDriver`, `AiDecisionAgent`, `HumanAgent`, `IHumanInputPort`.
- **ViewModel**(순수 C# + R3): `TableViewModel`(= `IHumanInputPort` 구현), `DecisionRequest`.
- **Thin Views**(MonoBehaviour): `TableView`, `HandView`, 결정 프롬프트 패널들.
- **Bootstrap**(MonoBehaviour): `RoundBootstrap`(수동 배선).

**패키지 추가:** UniTask(`com.cysharp.unitask`), R3(`com.cysharp.r3` + 필요한 의존). DoTween/VContainer 미추가.

## 4. 비동기 포트 + 드라이버

### 4.1 `IDecisionAgent` — `IAgent`의 비동기 미러
```csharp
public interface IDecisionAgent {
    UniTask<bool>           CallGrandTichuAsync(DecisionContext ctx, CancellationToken ct);
    UniTask<ExchangeChoice> ChooseExchangeAsync(DecisionContext ctx, CancellationToken ct);
    UniTask<bool>           CallTichuAsync(DecisionContext ctx, CancellationToken ct);
    UniTask<TurnDecision>   DecideTurnAsync(DecisionContext ctx, CancellationToken ct);
    UniTask<Combination?>   DecideBombAsync(DecisionContext ctx, CancellationToken ct);
    UniTask<int>            ChooseDragonRecipientAsync(DecisionContext ctx, CancellationToken ct);
}
```
- `DecisionContext`·`ExchangeChoice`·`TurnDecision`·`Combination`은 GameFlow/Core 것 **그대로 재사용**(engine-free, 무변경). `in` 매개변수는 async 불가라 값 전달(작은 readonly struct).
- 추가 요소 = `CancellationToken`(라운드 중단/씬 종료).

### 4.2 `AsyncGameDriver.RunRoundAsync`
`GameDriver.RunRound`의 **구조 미러**(`Tichu.Presentation`).
- 시그니처: `UniTask<RoundOutcome> RunRoundAsync(GameState s, IDecisionAgent[] agents, CancellationToken ct)`.
- **동일한 고정순서**: 큰티츄 커서 → 교환 커서 → DrivePlay(①용 양도 ②폭탄창 시계순 ③작은티츄 훅 ④차례행동). 동일 `FlowQuery`/`GameEngine.Apply`/거부 시 throw/`ScoreCalculator.ScoreRound`.
- **유일한 차이:** 동기 `agent.X(ctx)` → `await agent.XAsync(ctx, ct)`. 규칙·판정은 전부 P1-A 권위 위임.
- **상태 통지:** 매 Apply 성공 후 ViewModel에 읽기전용 스냅샷 push(§5).

### 4.3 두 에이전트
- **`AiDecisionAgent : IDecisionAgent`** — 동기 `AiAgent`를 감싸 즉시 반환: `UniTask.FromResult(_inner.DecideTurn(ctx))`. 휴리스틱·시드 P1-A 그대로(스펙의 SyncOverAsync 어댑터).
- **`HumanAgent : IDecisionAgent`** — `await _input.RequestXxxAsync(ctx, ct)`. **`IHumanInputPort`에만 의존**(테스트 시 스크립트 입력 주입).

## 5. ViewModel / 상태 투영 + UI 중재

### 5.1 `TableViewModel`(순수 C# + R3, `IHumanInputPort` 구현)
드라이버가 매 Apply 후 푸시한 스냅샷을 R3 속성으로 투영:
- `Seats[4]`: 손패수·라벨·IsOut·FinishOrder·티츄콜·차례여부.
- `MyHand`: seat0 카드(앞면, 선택 가능).
- `CurrentTrick`: Top 조합·소유자·누적점수.
- `Phase`, `RoundResult`(종료 시).
- `PendingDecision: ReactiveProperty<DecisionRequest?>` — 지금 사람이 할 결정(아니면 null).

### 5.2 UI 중재 시퀀스(단일 스레드·단방향)
```
AsyncGameDriver → await DecideTurnAsync(ctx,ct)
  HumanAgent     → IHumanInputPort.RequestTurnDecisionAsync(ctx) → await TCS
    TableViewModel → PendingDecision 발행(R3)
      TableView    → 프롬프트·합법수 렌더 / 사람 클릭 대기
      TableView    → ViewModel.SubmitTurnDecision(d)
    TableViewModel → TCS.TrySetResult(d)
  HumanAgent     → return TurnDecision
AsyncGameDriver → GameEngine.Apply(d)[거부 시 throw] → 스냅샷 통지 → R3 갱신 → 뷰 갱신
```
- **로컬 합법성 게이팅:** View/VM이 `LegalMoveGenerator`/`CombinationRecognizer`로 합법수만 활성화·인식. 불법 선택은 로컬 거부, **드라이버 throw는 백스톱**(정상 경로 아님).
- **취소:** `CancellationToken`이 TCS를 취소 → 드라이버 정상 종료.

### 5.3 `DecisionRequest`
지금의 사람 결정을 기술: `Kind`(GrandTichu/Exchange/Tichu/Turn/Bomb/DragonRecipient) + 관련 옵션(합법수·CanPass·소원필요·좌/우 좌석 등). View는 이걸로 프롬프트를 렌더, Submit으로 응답.

## 6. 결정 프롬프트 (9 결정포인트 → 6 메서드, placeholder)

| 결정 | placeholder UI |
|---|---|
| 큰 티츄 | [선언][패스] 소형 다이얼로그 |
| 교환 | 손패에서 3장 → 좌/파트너/우 지정 |
| 작은 티츄 | 첫 패 전 [선언][패스] |
| 차례(리드/팔로우) | 카드 선택 → [내기]/[패스]; 마작 리드 시 소원 랭크 선택 |
| 차례밖 폭탄 | 합법 폭탄 있을 때 [폭탄 내기][넘기기] |
| 용 양도 | [왼쪽 상대][오른쪽 상대] |

카드는 랭크+무늬 텍스트(아트 없음). 상대는 라벨+뒷면 수량+티츄 마커+차례 하이라이트.

## 7. 씬 / 부트스트랩

- 단일 씬 `Assets/_Project/Presentation/Scenes/Table.unity` — 2D uGUI Canvas + 기본 카메라/라이트(URP).
- `RoundBootstrap : MonoBehaviour`(**VContainer 없음**): 인스펙터 시드 → `GameEngine.NewRound(seed)` → `TableViewModel` 생성 → `HumanAgent`(seat0, VM=IHumanInputPort) + `AiDecisionAgent`×3 → 뷰 바인딩 → `AsyncGameDriver.RunRoundAsync(...)` 기동 → 종료 시 `RoundResult` 표시.
- 시드는 인스펙터 필드(고정 재현용).

## 8. 테스트 전략 (Unity EditMode NUnit, 순수 C#)

Presentation은 UniTask/R3 의존이라 dotnet sln 밖 → **Unity Test Runner EditMode**에서 실행. 모든 결정이 즉시 완료(AI=즉시, 사람=스크립트 입력)면 한 라운드가 EditMode에서 동기적으로 완주 → 빠르고 결정적.

1. **오라클 교차검증(핵심):** 같은 시드 + 같은 스크립트 결정을 동기 `GameDriver`와 `AsyncGameDriver`에 넣어 **동일 Log/ComputeHash**.
2. **AsyncGameDriver 결정성:** 스크립트 `IDecisionAgent`×4 → RoundEnd·Result≠null·로그 리플레이 해시 일치.
3. **HumanAgent↔IHumanInputPort:** 요청 발행→Submit→TCS 완료로 await 풀림; `CancellationToken` 취소 시 정상 종료.
4. **AiDecisionAgent:** 동기 `AiAgent`와 동일 결정(즉시).
5. **TableViewModel:** 스냅샷→R3 속성 매핑; 로컬 합법성 게이팅(불법 선택 거부).
6. **뷰(MonoBehaviour):** 자동테스트 X — 얇은 뷰라 수동/PlayMode 눈 확인.

## 9. 변경 매니페스트

**동기화/미러:** `tools/sync-core-to-unity.ps1`; `Assets/_Project/Core`(P1-A 반영); `Assets/_Project/GameFlow`(신규 + `Tichu.GameFlow.asmdef`); `Assets/_Project/Tests/EditMode`(GameFlow 테스트 + asmdef에 GameFlow 참조 추가).
**신규(`Tichu.Presentation`):** `Tichu.Presentation.asmdef`; `IDecisionAgent.cs`, `AsyncGameDriver.cs`, `AiDecisionAgent.cs`, `HumanAgent.cs`, `IHumanInputPort.cs`; `TableViewModel.cs`, `DecisionRequest.cs`; 뷰 MonoBehaviour(`TableView`/`HandView`/프롬프트 패널); `RoundBootstrap.cs`; `Scenes/Table.unity`.
**신규 테스트(`Tichu.Presentation.Tests` 신규 asmdef, `Assets/_Project/Presentation/Tests`):** 오라클·비동기 드라이버·HumanAgent·AiDecisionAgent·ViewModel.
**패키지:** `Packages/manifest.json`에 UniTask·R3 추가.

## 10. 슬라이스 경계 / 완료 기준(DoD)

- **포함:** 한 라운드 풀 플레이(9결정 UI), 비동기 드라이버, HumanAgent, ViewModel, 얇은 뷰, 부트스트랩, Core/GameFlow 미러 동기화 + 드리프트 가드, UniTask/R3 추가, EditMode 테스트(오라클 포함).
- **제외(P1-C/후속):** 메뉴·모드선택·결과 화면 흐름, 1000점 매치 루프, 일시정지/재시작, VContainer, DoTween, 사운드, 모바일 빌드/터치 최적화.
- **DoD:** 에디터 PlayMode에서 사람이 seat0로 **한 라운드를 정산까지 크래시·불법상태 없이** 완주; EditMode 테스트 그린(특히 **오라클: 비동기==동기 ComputeHash**); 미러 드리프트 가드 통과.

## 11. 후속

P1-B 완료 후 → **P1-C(App/흐름/DI)**: 메인→모드선택→테이블→결과 화면, 1000점 매치 루프(P1-A `MatchRunner` 비동기 래핑), 일시정지/재시작, **VContainer Composition Root**, DoTween 애니. P1-B의 `AsyncGameDriver`·`TableViewModel`이 P1-C의 구조 기반.
