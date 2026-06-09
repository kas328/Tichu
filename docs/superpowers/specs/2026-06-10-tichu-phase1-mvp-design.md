# Tichu Phase 1 MVP — 설계 스펙

> **상태:** 사용자 승인 완료(2026-06-10). 검토 보고서: `docs/reports/2026-06-10-phase1-mvp-design-report.html`. 후속: P1-A 구현 계획서(`docs/superpowers/plans/`) → 서브에이전트 TDD 구현.
>
> 본 스펙은 **설계(아키텍처·계약·결정)** 를 정의한다. 단계별 TDD 태스크는 별도 계획서가 담당한다. 멀티에이전트 워크플로우(그라운딩→설계패널→계획초안→적대적검증)로 다졌고, 검증이 잡은 결함(특히 §5 용 양도 CRITICAL)을 반영했다.

## 0. 배경 / 목표

Phase 0(코어 룰엔진 = Part 1 카드·조합 + Part 2 상태기계·정산·시뮬)은 `main` 머지·검증 완료(`dotnet test` 155 + 10만 판 무결성). Phase 1 MVP의 목표(기획서 12.3): **한 기기에서 vs AI 한 판(1000점)을 크래시 없이 완주**하고 "재미"가 검증되는 최소 제품.

범위가 커서 3개 서브프로젝트로 분해하고, 헤드리스부터 간다. **본 스펙·계획의 상세 범위는 P1-A.** P1-B/C는 골격만.

**잠긴 결정(사용자 확정):**
1. 3 서브프로젝트, 헤드리스부터 → P1-A 먼저 상세.
2. 2D uGUI 플레이스홀더 먼저(P1-B).
3. 단일 "Normal" 휴리스틱 AI(ISMCTS·난이도 티어·확률추론 = Phase 2).

## 1. 분해 (3 서브프로젝트, 의존 A→B→C)

- **P1-A — 헤드리스 GameFlow + 기본 AI** (이번 상세): 순수 .NET(`Tichu.GameFlow`)으로 Tichu.Core 위에 한 게임(여러 라운드 → 1000점)을 굴리는 결정적 드라이버 + Normal 휴리스틱 AI. `dotnet test`로 완주·결정성·무결성 검증. Unity 무관.
- **P1-B — Presentation** (골격만): Unity 2D uGUI 플레이스홀더 4좌석 테이블, 손패 선택/제출, R3 상태바인딩, DoTween 애니, `HumanAgent`(UI 입력 대기) + P1-A 동기 계약의 비동기(UniTask) 래퍼.
- **P1-C — App/흐름** (골격만): 메인→모드선택→테이블→결과 화면, 일시정지/재시작, VContainer Composition Root.

## 2. 레이어 아키텍처 (기획서 ch10 ↔ Part 2 현실 정합)

의존은 항상 안쪽(Core). 기획서 ch10.5의 "GameFlow = 별도 FSM" 레이어는 **Part 2가 라운드 FSM을 이미 `Tichu.Core.Game`(GameEngine.Apply)에 구현**했으므로, GameFlow는 Core 위의 **얇은 드라이버 + 포트(IAgent) + AI**로 축소한다(중복 FSM 없음).

| 레이어 | 어셈블리 | 책임 | 의존 |
|---|---|---|---|
| Core ✅(존재) | `Tichu.Core` (netstandard2.1) | 룰·조합·GameState·FSM(Apply)·정산·시드RNG | 없음(UnityEngine/UniTask 무참조) |
| GameFlow (P1-A, 신규) | `Tichu.GameFlow` (netstandard2.1) | 드라이버 + 포트 + AI (얇음) | → Core만 |
| Presentation (P1-B) | `Tichu.Presentation` (Unity) | uGUI·R3·DoTween·HumanAgent·UniTask 래퍼 | → GameFlow, Core(+Unity) |
| App (P1-C) | `Tichu.App` (Unity) | 화면흐름·DI Composition Root | → 전부 |

**문서화한 의도적 일탈(검증 통과):**
- AI를 기획서의 Core `Ai/` 대신 `Tichu.GameFlow/Agents/`에 둔다 — "Core는 룰 전용"(CLAUDE.md/메모리) 원칙. 단일 구현이라 `IAiService` 포트는 도입하지 않음(YAGNI; P1-B/C에서 Network/DI가 필요로 하면 도입).
- 동기 `IAgent`는 기획서의 비동기 `IDecisionAgent`(UniTask)와 다름 — Core/GameFlow를 UniTask-free로 유지하기 위함. UniTask 비동기 트윈은 P1-B에서 `IDecisionAgent` + 어댑터(`SyncOverAsyncAgent`)로 추가.

## 3. P1-A 에이전트 계약 — `IAgent` (9 결정포인트 → 6 메서드)

드라이버는 참가자가 사람인지 AI인지 모른다. 동기·Unity무관 계약:

```
public interface IAgent {
    bool           CallGrandTichu(in DecisionContext ctx);     // 큰 티츄 콜/거절
    ExchangeChoice ChooseExchange(in DecisionContext ctx);     // 좌/파트너/우 1장씩
    bool           CallTichu(in DecisionContext ctx);          // 작은 티츄(첫 플레이 전)
    TurnDecision   DecideTurn(in DecisionContext ctx);         // 리드/팔로우/패스 (+마작 소원)
    Combination?   DecideBomb(in DecisionContext ctx);         // 차례 밖 폭탄, null=안 함
    int            ChooseDragonRecipient(in DecisionContext ctx); // 용 양도 상대(상대 2명 중)
}
```

- `DecisionContext` = `(GameState State, int Seat)` 읽기전용 뷰. 접근자: `MyHand`, `LegalMoves`(=`LegalMoveGenerator.LegalMoves`), `CanPass`, `LeftSeat=(seat+1)%4`, `PartnerSeat=Seating.Partner`, `RightSeat=(seat+3)%4`. **상대 손패 비공개.** 에이전트는 상태를 변경하지 않는다.
- `ExchangeChoice` = `(Card ToLeft, ToPartner, ToRight)` readonly struct.
- `TurnDecision` = `(bool IsPass, Combination? Move, int? Wish)` + 팩토리 `Pass` / `Play(move, wish=null)`. 마작 소원은 리드 액션의 서브필드.
- RNG는 에이전트 생성자에 주입(`new AiAgent(roundSeed, seat)`), 컨텍스트에 싣지 않음.

**계약 불변식(검증 도출):** Play 페이즈에서 행동 좌석은 항상 `LegalMoves ≠ ∅ ∨ CanPass`(소원강제는 충족수가 존재할 때만 발동하므로 데드엔드 도달 불가). 방어 패스 대신 이 불변식을 테스트로 고정.

## 4. 드라이버 — `GameDriver` · `MatchRunner` · `FlowQuery`

- **`FlowQuery.Next(GameState) → NextStep{StepKind, seat}`**: `Phase`로 디스패치(GrandTichu/Exchange/Play/DragonGift/Scoring). Play일 때 용 양도 대기면 `(DragonGift, winner)`, 아니면 `(Play, Turn)`. **`SeatsWithLegalBomb`**: off-turn·non-out 좌석 중 Top을 깨는 폭탄 보유 좌석을 **Turn+1부터 시계방향** 순서로 반환(결정성). 셋업 진행(누가 다음)은 Core 미노출 → 드라이버가 0..3 로컬 커서로 처리(셋업은 순서 무관).
- **`GameDriver.RunRound(GameState) → RoundOutcome{State, RoundResult, Log}`**: `Phase != Scoring` 동안 루프(MaxSteps 가드). 셋업은 0..3 커서. **DrivePlay 매 반복 고정 순서**: ① 용 양도 대기면 `ChooseDragonRecipient`→`GiveDragon`(continue) ② 폭탄창(시계방향 `DecideBomb`, 성사 시 루프 재시작) ③ 작은티츄 훅(해당 좌석 첫 행동 전 1회, 턴 미소모, 폴백 X) ④ 차례 행동 `DecideTurn`→Play/Pass. **모든 Apply는 Ok 단언**(거부 시 사유와 함께 throw — "예외 없이 완주"=불법상태 0 증명). 종료 후 `ScoreCalculator.ScoreRound`.
- **`MatchRunner.RunMatch(masterSeed, agentFactory, target=1000, maxRounds) → MatchResult`**: `Master` Rng로 라운드별 `roundSeed` 파생 → `agentFactory(roundSeed, seat)`로 매 라운드 에이전트 재시드(라운드 독립 재현·AI 무상태) → `GameDriver.RunRound(NewRound(roundSeed))` → **누적 `TeamA/TeamB += Total`(MatchState에 실제 carry-over)**. 종료 판정은 **순수함수 `Decide(teamA, teamB, target) → {Continue|TeamAWins|TeamBWins}`**(둘 다 ≥target이고 동점이면 Continue, 아니면 strict leader 승). `maxRounds` 초과 시 `WinningTeam=-1`(테스트 가드).

> Part 2 테스트 `Simulator` 대비 두 버그 수정: (1) 누적점수를 새 라운드의 `s.Scores` 재할당이 아니라 `MatchState`에 실제 누적, (2) 1000-동점 시 속행.

## 5. 유일한 Core 변경 — 용 양도(Dragon-gift) 선택

Part 2는 용 트릭 수혜자를 `(winner+1)%4`로 결정론적 고정. Phase 1은 승자(사람/AI)가 상대 2명 중 **선택**한다. ~25줄/5파일, 기존 공개 시그니처 무변경.

**⚠️ 적대적 검증이 잡은 CRITICAL(반드시 이렇게 구현):** 단순히 `CollectTrick`에서 "Turn 할당 전 return"만으로는 일시정지가 안 된다 — `CheckRoundEnd`은 `CollectTrick` **밖**(`ApplyPlay`/`ApplyPass`에서 CollectTrick 리턴 후) 호출되므로 라운드 종료를 못 막는다. 용이 라운드를 끝내는 트릭(마지막 카드/원-투)에서 양도가 조용히 누락된다.

**올바른 캡처-가드-재개:**
1. **`Trick`**: `int? DragonGiftRecipient`.
2. **`GameState`**: `internal int? PendingDragonGiftWinner` + `public bool TryGetPendingDragonGift(out int)`. `Clone()`·`CloneTrick`에 복사. `ComputeHash()`에 `PendingDragonGiftWinner` 및 각 완료트릭 `DragonGiftRecipient`를 **null 센티넬**(`HasValue ? val : ulong.MaxValue`)로 폴드(골든 픽스처 없음 → 가산 안전).
3. **`GameAction`**: `GameActionKind.GiveDragon` + `RecipientSeat` + 팩토리 `GiveDragon(seat, recipientSeat)`.
4. **`GameEngine`**:
   - `CollectTrick`/`FinalizeOpenTrick`: `MarkDragonIfApplicable` 후 `WonByDragon`이면 `PendingDragonGiftWinner=winner` 세팅, 트릭 append, **Turn 미할당·return**.
   - `ApplyPlay`·`ApplyPass`: CollectTrick 이후 `if (s.PendingDragonGiftWinner == null) CheckRoundEnd(s);` — **양도 대기 중엔 라운드 종료 보류**.
   - `ApplyGiveDragon`: `Phase==Play` && `seat==PendingDragonGiftWinner` && `recipient ∈ {(seat+1)%4,(seat+3)%4}` 검증 → 트릭 `DragonGiftRecipient` 기록 → 플래그 해제 → Turn 설정(out이면 NextActive) → **보류했던 `CheckRoundEnd` 실행**. 한 상대가 아웃이어도 양도는 필수(에이전트가 유효 좌석 반환).
   - 라운드 종료 시점 open 트릭(`FinalizeOpenTrick`)의 용 양도도 동일 가드로 `Phase=Scoring` 전 해결.
5. **`ScoreCalculator`**(현재 74-76): `credited = t.WonByDragon ? (t.DragonGiftRecipient ?? (t.TopOwnerSeat+1)%SeatCount) : t.TopOwnerSeat;` — `??` 폴백으로 기존 ScoringTests 전부 그린 유지.

> 부수: 폭탄이 용 Top을 덮으면 `MarkDragonIfApplicable`이 최종 Top을 보므로 `WonByDragon`은 자동 해제(전용 테스트로 고정).

## 6. Normal 휴리스틱 `AiAgent`

단일 `AiAgent : IAgent`, 결정적(라운드시드^상수^좌석으로 시드), **공개정보 + `LegalMoveGenerator`만** 사용, `Clone()` 룩어헤드 없음(P1-C "Hard" 몫), 임계치는 private const(설정면 없음). "최저/최소/가장 싼" 선택은 **무브 정렬 헬퍼**(`MoveValue`/`Compare`, 점수+랭크 기반)로 객관 기준화(LegalMoves가 무순서이고 봉황 단독이 반-랭크라 필요).

- **큰 티츄**: 8장 손패 점수화(용/봉황/A·K 수/+개), 높은 임계치 이상만 콜(실패 −200, 보수적).
- **교환**: 최저 비특수 3장(용/봉황/개/마작 보존), 최저 2장은 상대(좌/우), 중간 1장 파트너.
- **작은 티츄**: 강한 손 + 상대 미아웃 + 파트너 미콜 시, 낮은 비율.
- **리드**: 폭탄 안 냄(보존), 최저 비점수 단/페어 우선, 손 약하면 개로 선 양보, 거의 아웃이면 강한 콤보로 마무리. 마작 소원은 강제 가능성 높을 때만.
- **팔로우**: 파트너가 Top이면 패스(점수 우리편 유지), 상대가 점수 많은 Top이면 최소 오버킬로 받기, 무가치 트릭에 비싼 수밖에 없으면 패스(가능 시). 폭탄은 여기서 안 씀.
- **폭탄(DecideBomb)**: 트릭 `AccumulatedPoints ≥ 15` && 상대 Top일 때만, 최소 폭탄으로. 파트너엔 안 씀.
- **용 양도**: 카드 많이 남은 상대(공개 수), 동점은 아웃 안 임박한 쪽, 최종 `_rng`. 항상 상대 2명 중.

추가로 `RandomAgent`(결정적 random over LegalMoves/CanPass)를 같이 둔다 — AI 품질 비교 베이스라인(Task 9). 기존 테스트 전용 `Sim/RandomBot`은 유지(SimulationTests가 참조; 별도 cleanup PR 권고).

## 7. 결정성 / 테스트 전략

- 결정성: `masterSeed → Master Rng → roundSeed`(딜+에이전트 시드). 좌석 RNG `roundSeed ^ 상수 ^ seat`(딜과 비상관). 드라이버 무작위 0, 고정 반복순서. ⇒ 동일 시드 → 동일 게임/ComputeHash, 액션로그 리플레이 일치. `System.Random`/`DateTime`/가변 static 금지.
- 테스트(전부 `Tichu.Core.Tests`, NUnit; 대형은 `[Explicit, Category("Heavy")]`; Part 0 불변식 재사용):
  1. **풀게임 완주**: `RunMatch`가 여러 시드에서 `WinningTeam∈{0,1}`, max≥1000, strict leader.
  2. **라운드 점수 불변식**: 원-투 (200,0)/(0,200), 아니면 카드합 100.
  3. **불법상태 0**: 구조적(드라이버 throw) + 종료 유형별 불변식(정상=마지막1명만 손패; 원-투=2명 잔류; FinishOrder 프리픽스). *원-투에서 "마지막 1명만 손패" 가정은 거짓이므로 분기.*
  4. **결정성**: 동일 시드 2회 → 동일 `TeamA/TeamB` + 라운드별 `ComputeHash`; 액션로그 리플레이 일치. 용 양도 좌/우 다르면 해시 다르고, 같으면 같음.
  5. **시나리오(ScriptedAgent + 손패 직접구성 seam)**: 용 양도(좌/우), 차례 밖 폭탄, 소원 강제, 원-투, 1000-동점 속행. *NewRound 랜덤딜로 도달 불가하므로 `PlayState` 패턴으로 손패를 명시 구성해 `GameDriver`에 주입.*
  6. **1000-동점**: 순수함수 `Decide()` 단위테스트(A=B=1000→Continue; A>B→A승 등), 통합은 [Explicit].
  7. **AI 품질([Explicit])**: AiAgent vs RandomAgent 다수 시드, AI 팀 평균 ≥ Random.
  8. **[Heavy] 10만 판 무결성**: 예외/불법 0 + 점수 불변식 100%(`SimulationTests`의 Heavy 관례 미러).

## 8. 변경 매니페스트

**신규(`Tichu.GameFlow`):** `Tichu.GameFlow.csproj`; `Agents/IAgent.cs`(+DecisionContext/ExchangeChoice/TurnDecision); `Agents/AiAgent.cs`; `Agents/RandomAgent.cs`; `FlowQuery.cs`(+NextStep/StepKind); `GameDriver.cs`(+RoundOutcome); `MatchRunner.cs`(+MatchState/MatchResult/Decide); 무브정렬 헬퍼.
**Core 변경(용 양도만):** `Game/Trick.cs`, `Game/GameState.cs`, `Game/GameAction.cs`, `Game/GameEngine.cs`, `Game/ScoreCalculator.cs`.
**빌드:** `core/Tichu.sln`(+프로젝트, src 폴더 네스팅 명시), `core/tests/Tichu.Core.Tests/Tichu.Core.Tests.csproj`(+ProjectReference).
**신규 테스트:** `Tichu.Core.Tests`에 GameFlow 스위트 + `ScriptedAgent.cs` + 상태구성 헬퍼.
**플래그(이번 변경 안 함, 별도 cleanup PR):** `Sim/Simulator.cs`·`Sim/RandomBot.cs`(RandomAgent로 대체), `PlayerSeat.WonCards`(엔진 미사용 死코드).

## 9. P1-A 완료 기준 (DoD)

- `dotnet test core/Tichu.sln` 그린 + `--filter Category=Heavy` 10만 판 무결.
- AI-vs-AI 풀게임(1000점) 완주, 불법상태/예외 0, 점수 불변식 100%.
- 결정성(동일 시드→동일 결과) + 용 양도 회귀 테스트(좌/우 선택 반영, 라운드종료 용트릭 포함).
- `Tichu.GameFlow`가 UnityEngine/UniTask 무참조, Core 변경은 용 양도뿐.

## 10. 후속

P1-A 완료 후 → P1-B(Presentation: Unity asmdef 미러 + 비동기 `IDecisionAgent` 트윈 + uGUI/R3/DoTween + HumanAgent) → P1-C(App/흐름/DI). P1-A의 `GameDriver`가 P1-B 비동기 드라이버의 구조 템플릿·룰 권위.
