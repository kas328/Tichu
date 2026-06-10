# Tichu Phase 1 P1-A — 헤드리스 GameFlow + 기본 AI Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.
>
> **경량 계획서:** 사용자 요청대로 매 스텝의 완성 코드를 미리 적지 않는다(타입 시그니처/인터페이스는 설계 결정이라 명시). 각 태스크는 TDD(실패 테스트 → 최소 구현 → 통과 → 커밋)를 따르고, 테스트는 **이름 + 어서션 요지**로 기술한다. 설계 근거: `docs/superpowers/specs/2026-06-10-tichu-phase1-mvp-design.md`.

**Goal:** Tichu.Core(Part 0) 위에, 사람/AI 공통 `IAgent` 포트로 한 게임(여러 라운드 → 1000점)을 굴리는 결정적·헤드리스 게임 드라이버 + Normal 휴리스틱 AI를, `dotnet test`로 완주·결정성·무결성이 검증되게 구축한다. Unity 무관.

**Architecture:** 신규 `Tichu.GameFlow`(netstandard2.1, Tichu.Core만 참조) = 얇은 드라이버 + 포트 + AI. 기획서의 "GameFlow FSM"은 Part 2가 FSM을 Core에 넣었으므로 드라이버로 축소(중복 FSM 없음). 동기 `IAgent`(UniTask는 P1-B). 유일한 Core 변경 = 용 양도 선택(캡처-가드-재개).

**Tech Stack:** C# 9 / netstandard2.1(라이브러리), net9.0(테스트), NUnit. Tichu.Core(Card/Deck/Combination/GameState/GameEngine/LegalMoveGenerator/ScoreCalculator/Rng) 재사용. UnityEngine·UniTask·R3·DoTween 미사용(P1-A 범위).

**기획서 매핑:** 8.5(IDecisionAgent → 동기 IAgent), 10(레이어/asmdef), 12.3 Phase 1 DoD(헤드리스 부분), 4.4(용 양도 — Part 2 결정론 임시고정을 선택으로 교체).

---

## 핵심 설계 결정 (스펙 §2~6 요약)

- **DD-A: GameFlow는 Core 위 얇은 드라이버 + 포트.** 중복 FSM 없음. AI는 같은 어셈블리 `Agents/`(IAiService 포트는 YAGNI). UnityEngine/UniTask-free.
- **DD-B: 동기 `IAgent`** 6메서드 = 9 결정포인트. `DecisionContext`는 `(GameState, seat)` 읽기전용 뷰. RNG는 에이전트 생성자 주입.
- **DD-C: 용 양도 = 유일한 Core 변경.** ⚠️ **CRITICAL(검증 발견)**: `CheckRoundEnd`은 `CollectTrick` 밖(`ApplyPlay`/`ApplyPass`)에서 호출되므로 "Turn 전 return"만으론 라운드종료를 못 막는다. → `ApplyPlay`/`ApplyPass`에 `if(PendingDragonGiftWinner==null) CheckRoundEnd(s);` 가드 + `ApplyGiveDragon`이 보류 종료 실행. `FinalizeOpenTrick`(라운드종료 open 트릭)도 동일.
- **DD-D: 태스크 순서** — 용 양도 Core(Task 3)를 FlowQuery(Task 4)보다 앞에(FlowQuery가 `TryGetPendingDragonGift`에 의존).
- **DD-E: 시나리오/휴리스틱 테스트는 손패 직접구성 seam**(`PlayState` 패턴)으로 `GameDriver`에 주입(랜덤딜로 도달 불가).
- **DD-F: 1000-동점은 순수함수 `Decide(teamA,teamB,target)`** 로 단위테스트(실제 플레이로 구성 불가).
- **DD-G: 폭탄 순서 = Turn+1부터 시계방향**(FlowQuery가 그 순서로 반환).
- **DD-H: 무브 정렬 헬퍼**(`MoveValue`/`Compare`)로 AI의 "최저/최소" 기준 객관화.

---

## File Structure

```
core/
  Tichu.sln                              (+ Tichu.GameFlow 프로젝트, src 폴더 네스팅)
  src/Tichu.Core/Game/                   (변경: 용 양도만 — Trick/GameState/GameAction/GameEngine/ScoreCalculator)
  src/Tichu.GameFlow/                    신규 (netstandard2.1 → Tichu.Core)
    Tichu.GameFlow.csproj
    Agents/IAgent.cs                     (IAgent + DecisionContext + ExchangeChoice + TurnDecision)
    Agents/AiAgent.cs                    ("Normal" 휴리스틱)
    Agents/RandomAgent.cs               (결정적 random; RandomBot 대체)
    MoveOrder.cs                        (MoveValue/Compare — 무브 정렬 헬퍼)
    FlowQuery.cs                         (NextStep, StepKind, Next, SeatsWithLegalBomb, PendingDragonGift)
    GameDriver.cs                        (RunRound + RoundOutcome)
    MatchRunner.cs                       (MatchState, MatchResult, Decide, RunMatch)
  tests/Tichu.Core.Tests/                (+ ProjectReference → Tichu.GameFlow)
    GameFlowHelpers.cs                   (PlayState 류 상태구성 + DecisionContext 빌더)
    ScriptedAgent.cs                     (테스트 전용 IAgent)
    GameFlow*Tests.cs                    (계약/용양도/FlowQuery/드라이버/AI/매치/풀게임/시나리오)
```

**책임 분리:** `IAgent`=결정 포트, `FlowQuery`=다음 행동 디스패치, `GameDriver`=1라운드 오케스트레이션, `MatchRunner`=매치 루프+종료판정, `AiAgent`=정책, `MoveOrder`=선택 기준. Core 변경은 용 양도로 한정.

---

## Task 1: `Tichu.GameFlow` 프로젝트 스캐폴딩 + sln/테스트 배선

**Files:** Create `core/src/Tichu.GameFlow/Tichu.GameFlow.csproj`, `core/src/Tichu.GameFlow/AssemblyMarker.cs`; Modify `core/Tichu.sln`, `core/tests/Tichu.Core.Tests/Tichu.Core.Tests.csproj`; Test `GameFlowScaffoldTests.cs`.

**설계:** csproj는 `Tichu.Core.csproj` 미러(netstandard2.1, LangVersion 9.0, Nullable enable, ImplicitUsings disable, RootNamespace Tichu.GameFlow) + `ProjectReference → ..\Tichu.Core\Tichu.Core.csproj`. `AssemblyMarker`(public const)로 테스트가 어셈블리를 참조. `dotnet sln core/Tichu.sln add src/Tichu.GameFlow/Tichu.GameFlow.csproj`로 등록 후 **src 폴더 네스팅 라인 명시 추가**(자동 안 됨). 테스트 csproj에 `ProjectReference → ..\..\src\Tichu.GameFlow\...`.

- [ ] **Step 1:** `GameFlowScaffoldTests` 작성 — `AssemblyMarker.Name == "Tichu.GameFlow"` 단언.
- [ ] **Step 2:** `dotnet build core/Tichu.sln` 실패(프로젝트 없음) 확인.
- [ ] **Step 3:** csproj + AssemblyMarker + sln 등록(+네스팅) + 테스트 ProjectReference.
- [ ] **Step 4:** `dotnet test core/Tichu.sln` → 3프로젝트 빌드, ScaffoldTests 그린, 기존 테스트 무회귀.
- [ ] **Step 5:** 커밋 `chore(gameflow): scaffold Tichu.GameFlow project + sln/test wiring`.

---

## Task 2: `IAgent` 계약 + `DecisionContext` 읽기전용 뷰

**Files:** Create `Agents/IAgent.cs`(IAgent, DecisionContext, ExchangeChoice, TurnDecision); Test `AgentContractTests.cs`.

**설계(스펙 §3):** 동기 6메서드. `DecisionContext`는 readonly struct `(GameState, int Seat)` + 위임 접근자(`MyHand`, `LegalMoves`=`LegalMoveGenerator.LegalMoves`, `CanPass`, `LeftSeat`/`PartnerSeat`/`RightSeat`). `ExchangeChoice`=`(Card ToLeft/ToPartner/ToRight)`. `TurnDecision`=`(bool IsPass, Combination? Move, int? Wish)` + `Pass`/`Play(move,wish=null)`. 타입만; 동작은 후속 태스크.

- [ ] **Step 1:** `AgentContractTests` 작성 — 좌석 계산(seat1→Left2/Partner3/Right0), `MyHand`가 `State.Seats[Seat].Hand`와 동일, GrandTichuDecision 상태에서 `LegalMoves` 비어있음/`CanPass` false, `TurnDecision.Pass`/`Play(combo,5)` 필드, `ExchangeChoice` 슬롯.
- [ ] **Step 2:** 컴파일/실패 확인.
- [ ] **Step 3:** IAgent.cs 구현.
- [ ] **Step 4:** `dotnet test` 그린.
- [ ] **Step 5:** 커밋 `feat(gameflow): add IAgent contract + read-only DecisionContext view`.

---

## Task 3: 용 양도 Core 변경 (캡처-가드-재개 + `??` 폴백) — **유일한 Core 편집**

**Files:** Modify `core/src/Tichu.Core/Game/{Trick,GameState,GameAction,GameEngine,ScoreCalculator}.cs`; Test `GameFlowDragonGiftTests.cs`.

**설계(스펙 §5, CRITICAL 반영):** ① `Trick.DragonGiftRecipient:int?`. ② `GameState.PendingDragonGiftWinner:int?`(internal) + `TryGetPendingDragonGift(out int)`; `Clone`/`CloneTrick` 복사; `ComputeHash`에 둘을 null 센티넬(`HasValue?val:ulong.MaxValue`)로 폴드. ③ `GameActionKind.GiveDragon` + `RecipientSeat` + `GiveDragon(seat,recipientSeat)`. ④ `GameEngine`: `CollectTrick`/`FinalizeOpenTrick`에서 `WonByDragon`이면 `PendingDragonGiftWinner=winner`·트릭append·**Turn 미할당·return**; `ApplyPlay`·`ApplyPass`의 CollectTrick 이후 `if(PendingDragonGiftWinner==null) CheckRoundEnd(s);`; `ApplyGiveDragon`(검증→recipient 기록→플래그 해제→Turn 설정→**보류 CheckRoundEnd 실행**). ⑤ `ScoreCalculator`: `credited = WonByDragon ? (DragonGiftRecipient ?? (TopOwnerSeat+1)%SeatCount) : TopOwnerSeat`.

- [ ] **Step 1:** RED 먼저 — 시나리오 테스트(손패 구성 또는 집중 Play 시퀀스)로:
  - `GiveDragon_pauses_on_dragon_win`: 용 단독으로 트릭 승리 시 `TryGetPendingDragonGift` true·winner 일치·Turn 불변.
  - `Dragon_won_trick_that_ends_round_still_pauses`(라운드종료 트릭도 양도 대기 — CRITICAL 회귀).
  - `GiveDragon_left_vs_right_credits_chosen_opponent`(정산 좌/우 다름 — 하드코드 제거 증명).
  - `GiveDragon_rejects_partner_or_non_winner`.
  - `Fallback_null_recipient_credits_plus1`(기존 ScoringTests 그린 유지).
  - `Hash_differs_left_vs_right` + `Hash_equal_same_side`.
  - `Bomb_over_dragon_top_unmarks_WonByDragon`.
- [ ] **Step 2:** 실패 확인(GiveDragon 미정의/미정지).
- [ ] **Step 3:** 5파일 편집.
- [ ] **Step 4:** `dotnet test` 그린 + 기존 ScoringTests/SimulationTests 전부 통과.
- [ ] **Step 5:** 커밋 `feat(core): capture+use dragon-gift recipient (pending flag + ?? fallback)`.

---

## Task 4: `FlowQuery` — NextStep 디스패치 + SeatsWithLegalBomb(시계순)

**Files:** Create `FlowQuery.cs`; Test `FlowQueryTests.cs`. (Task 3 이후 — `TryGetPendingDragonGift` 의존.)

**설계(스펙 §4, DD-D/DD-G):** `StepKind{GrandTichu,Exchange,Play,DragonGift,Scoring}`, `NextStep{Kind,Seat}`. `Next(s)`: Phase 디스패치, Play일 때 `PendingDragonGift`면 `(DragonGift,winner)` 아니면 `(Play,Turn)`. `SeatsWithLegalBomb(s)`: off-turn·non-out 중 Top 깨는 폭탄 보유 좌석을 **Turn+1부터 시계방향** 순서로. `PendingDragonGift(s,out w)`=`s.TryGetPendingDragonGift`.

- [ ] **Step 1:** `FlowQueryTests` — 신선 NewRound→`(GrandTichu,-1)`; 4명 결정 후→`(Exchange,-1)`; 4명 교환 후→`(Play, 마작좌석)`; Scoring→`(Scoring,-1)`; 리드 위치 `SeatsWithLegalBomb` 빈 목록; off-turn 폭탄 보유 좌석 포함·Turn 제외·**시계순**; 용양도 대기 시 `(DragonGift,winner)`.
- [ ] **Step 2:** 실패 확인.
- [ ] **Step 3:** FlowQuery.cs 구현.
- [ ] **Step 4:** 그린.
- [ ] **Step 5:** 커밋 `feat(gameflow): add FlowQuery dispatch + clockwise SeatsWithLegalBomb`.

---

## Task 5: `ScriptedAgent` + 상태구성 헬퍼 (테스트 seam)

**Files:** Create `GameFlowHelpers.cs`(PlayState 류 GameState 빌더 + DecisionContext 빌더), `ScriptedAgent.cs`; Test `ScriptedAgentTests.cs`.

**설계(DD-E):** `GameFlowHelpers`: `PlayPhaseTests.PlayState` 패턴 확장 — `Play(turn, hands…)`/`GrandState(hands…)`로 임의 손패·트릭의 GameState를 직접 구성(Setup=null), `Context(state,seat)`로 DecisionContext 생성. `ScriptedAgent:IAgent`: 6메서드를 옵션 `Func<DecisionContext,T>`로 백킹, 안전 기본값(grand/tichu 거절, 교환=손패 첫 3 distinct, DecideTurn=첫 LegalMove 리드 또는 추종 시 CanPass면 패스, DecideBomb=null, DragonRecipient=(Seat+1)%4). 실제 `GameDriver`로 시나리오를 강제하기 위함.

- [ ] **Step 1:** `ScriptedAgentTests` — 기본 거절/패스/교환 distinct·in-hand, 람다 오버라이드(DragonRecipient→RightSeat), DecideBomb null; 헬퍼가 만든 상태의 좌석/손패 정확.
- [ ] **Step 2:** 실패 확인.
- [ ] **Step 3:** 헬퍼 + ScriptedAgent 구현.
- [ ] **Step 4:** 그린.
- [ ] **Step 5:** 커밋 `test(gameflow): add ScriptedAgent + state-construction helpers`.

---

## Task 6: `GameDriver.RunRound` — 1라운드 오케스트레이션

**Files:** Create `GameDriver.cs`(RunRound, RoundOutcome); Test `GameFlowDriverTests.cs`. (Tasks 2,3,4,5 의존.)

**설계(스펙 §4):** `GameDriver(IAgent[])`. `RoundOutcome{GameState State; RoundResult Result; IReadOnlyList<GameAction> Log}`. `RunRound(s)`: `Phase!=Scoring` 루프(MaxSteps 가드, throw with phase/turn). 셋업은 로컬 0..3 커서. **DrivePlay 고정 순서**(스펙 §4): ①용양도 ②폭탄창(FlowQuery 시계순, 성사 시 루프 재시작) ③작은티츄 훅(좌석당 1회, 첫 행동 전, 턴 미소모, **폴백 없이 같은 반복에서 ④로**) ④차례 행동. 모든 Apply Ok 단언(거부=throw). 종료 후 `ScoreRound`. 각 적용 액션을 Log에 적재. **불변식**: 행동 좌석은 `LegalMoves≠∅ ∨ CanPass`(데드엔드 없음).

- [ ] **Step 1:** `GameFlowDriverTests` — RED: 기본 ScriptedAgent 4명·NewRound(42)→RoundEnd 도달·Result 비null·Log>0. 불법행동 throw(LegalMoves 밖 Play 주입)·사유 포함. 셋업 후 Turn=마작좌석. 작은티츄 훅 1회·턴 유지. 리플레이(Log 재적용→동일 ComputeHash). 용양도가 DrivePlay①로 라우팅(Task 3 크로스체크).
- [ ] **Step 2:** 실패 확인.
- [ ] **Step 3:** GameDriver 구현.
- [ ] **Step 4:** 그린.
- [ ] **Step 5:** 커밋 `feat(gameflow): add GameDriver.RunRound (single-round orchestration)`.

---

## Task 7: Normal `AiAgent` + `RandomAgent` + 무브 정렬 헬퍼

**Files:** Create `MoveOrder.cs`, `Agents/AiAgent.cs`, `Agents/RandomAgent.cs`; Test `AiAgentTests.cs`, `MoveOrderTests.cs`. (Tasks 2,6 의존.)

**설계(스펙 §6, DD-H):** `MoveOrder`: `Value(Combination)`(점수+랭크 기반 "비용/강도")·`Compare` — "최저/최소/가장 싼" 선택의 객관 기준(봉황 단독 반-랭크·개 포함 순서 정의). `AiAgent:IAgent` 결정적(`new Rng(roundSeed ^ 0xA1A1…0001 ^ seat)`), 공개정보+LegalMoveGenerator만, Clone 룩어헤드 없음, 임계치 private const. 휴리스틱(스펙 §6): 큰티츄 게이트, 교환(최저 비특수 3장·방향), 작은티츄(강한손+조건), 리드(폭탄보존·최저 비점수·약하면 개·near-out 강콤보·소원), 팔로우(파트너Top→패스·상대 점수Top→최소오버킬·무가치면 패스·폭탄 안 씀), DecideBomb(점수≥15&상대Top·최소폭탄·파트너 제외), 용양도(카드 많은 상대). `RandomAgent:IAgent`(결정적 random over LegalMoves/CanPass, 동일 시드 방식; AI품질 베이스라인).

- [ ] **Step 1:** `MoveOrderTests`(정렬 기준 단위) + `AiAgentTests` — 결정성(동일 ctx 동일 결정), 큰티츄 게이트(용+2A→true/약함→false), 교환 특수보존·distinct, 팔로우(파트너Top→패스·상대 점수Top→최소오버킬), DecideBomb(점수<15→null·≥15&상대→최소폭탄·파트너→안 씀), 용양도∈{Left,Right}·카드많은쪽, RandomAgent 4명 풀라운드 무예외.
- [ ] **Step 2:** 실패 확인.
- [ ] **Step 3:** MoveOrder + AiAgent + RandomAgent 구현.
- [ ] **Step 4:** 그린.
- [ ] **Step 5:** 커밋 `feat(gameflow): add Normal AiAgent + RandomAgent + move ordering`.

---

## Task 8: `MatchRunner.RunMatch` — 1000점, 실 carry-over, `Decide()` 순수함수

**Files:** Create `MatchRunner.cs`(MatchState, MatchResult, Decide, RunMatch); Test `GameFlowMatchTests.cs`. (Tasks 6,7 의존.)

**설계(스펙 §4, DD-F):** `MatchState{MasterSeed; Master Rng; RoundIndex; TeamA; TeamB; List<RoundResult>}`. `MatchResult{WinningTeam(0|1; -1=maxRounds); TeamA; TeamB; Rounds}`. **`Decide(teamA,teamB,target)→{Continue|TeamAWins|TeamBWins}`**(둘 다 ≥target&동점→Continue, 아니면 strict leader; 둘 다 미달→Continue). `RunMatch(masterSeed, Func<ulong,int,IAgent> factory, target=1000, maxRounds=10000)`: 라운드별 `roundSeed=Master.NextULong()`→`factory(roundSeed,seat)`로 4명 재시드→`RunRound(NewRound(roundSeed))`→**`TeamA/TeamB += Result.Total`(MatchState 실누적)**→`Decide`로 종료/속행.

- [ ] **Step 1:** `GameFlowMatchTests` — `Decide` 단위(A=B=1000→Continue·A=1010,B=1000→A승·A=1000,B=990→A승·둘다미달→Continue); RED: `RunMatch(seed, AiAgent factory)`→WinningTeam∈{0,1}·max≥1000·strict; 실 carry-over(누적=라운드 Total 합); 결정성(2회 동일); maxRounds 가드(-1, 무예외).
- [ ] **Step 2:** 실패 확인.
- [ ] **Step 3:** MatchRunner 구현.
- [ ] **Step 4:** 그린.
- [ ] **Step 5:** 커밋 `feat(gameflow): add MatchRunner (real carry-over + Decide tie rule)`.

---

## Task 9: 헤드리스 게임 테스트 — 완주·결정성·불변식·시나리오·AI품질

**Files:** Create `GameFlowFullGameTests.cs`, `GameFlowScenarioTests.cs`. (Tasks 3,5,6,7,8 의존.)

**설계(스펙 §7):** 대형은 `[Test, Explicit, Category("Heavy")]`(SimulationTests 관례 미러). Part 0 불변식 재사용.
- **풀게임 완주**: 시드 스프레드 `RunMatch(AiAgent)`→WinningTeam∈{0,1}·max≥1000·strict; per-seed try/catch→Fail(seed).
- **결정성**: `RunMatch(seed)` 2회→동일 TeamA/TeamB·라운드별 ComputeHash; 액션로그 리플레이→동일 해시.
- **라운드 점수 불변식**(~2000 시드 + [Explicit] 10만): 원-투 (200,0)/(0,200) 또는 카드합 100.
- **불법상태 0**(구조적: 드라이버 throw) + 종료유형별 불변식: **정상=마지막1명만 손패; 원-투=2명 잔류**(DD: one-two에서 "1명만" 가정 금지); 항상 `FinishOrder==1` 정확히 1개·프리픽스 유효.
- **시나리오(ScriptedAgent + 손패 seam)**: ①용양도 좌/우(DragonGiftRecipient·정산 반영) ②차례밖 폭탄(Top/Turn 갱신) ③소원강제(downstream CanPass=false) ④원-투(200,0+티츄델타) ⑤1000-동점 속행(추가 라운드).
- **AI품질([Explicit])**: AiAgent vs RandomAgent 다수 시드, AI 팀 평균 ≥ Random.

- [ ] **Step 1:** 위 테스트 작성.
- [ ] **Step 2:** 실패 확인(필요 부분).
- [ ] **Step 3:** 누락 구현/조정(대부분 앞 태스크에서 충족; 갭만).
- [ ] **Step 4:** `dotnet test core/Tichu.sln` 그린(Heavy 제외) + `--filter Category=Heavy` 그린.
- [ ] **Step 5:** 커밋 `test(gameflow): headless full-game completion, determinism, invariants, scenarios`.

> **Unity 미러 주의:** P1-A는 헤드리스 전용 — `Assets/_Project`에 미러하지 않는다. netstandard2.1라 P1-B에서 같은 .cs가 Unity asmdef로 무수정 링크되고, 비동기 IDecisionAgent 트윈/어댑터는 P1-B 작업.

---

## Part P1-A 완료 기준 (DoD)

- [ ] `dotnet test core/Tichu.sln`(Heavy 제외) 전체 통과 + `--filter Category=Heavy` 10만 판 통과.
- [ ] AI-vs-AI 풀게임(1000점) 완주, 불법상태/예외 0, 라운드 점수 불변식 100%.
- [ ] 결정성(동일 시드→동일 ComputeHash + 리플레이) + 용 양도 회귀(좌/우 선택 반영, 라운드종료 용트릭 포함).
- [ ] `Tichu.GameFlow`가 UnityEngine/UniTask 무참조; Core 변경은 용 양도뿐(기존 155 + 신규 테스트 전부 그린).

## Self-Review (작성자 점검)
- **Spec 커버리지:** §3 IAgent(T2)·§4 드라이버/FlowQuery(T4,T6)·§4 매치/Decide(T8)·§5 용양도(T3)·§6 AI/무브정렬(T7)·§7 테스트(T9)·스캐폴딩(T1)·테스트seam(T5) — 스펙 전 항목 매핑.
- **검증 반영:** CRITICAL 용양도 가드(T3), 태스크 재정렬(T3<T4), 상태구성 seam(T5), Decide 순수함수(T8), 폭탄 시계순(T4), 무브정렬(T7), one-two 불변식(T9) 모두 태스크에 반영.
- **타입 일관성:** `IAgent`(6메서드)·`DecisionContext`·`TurnDecision`·`GameDriver.RunRound`·`MatchRunner.RunMatch`/`Decide`·`FlowQuery.Next`/`SeatsWithLegalBomb`·`GiveDragon` — 태스크 간 시그니처 일치.
- **경량 범위:** 완성 코드 대신 테스트 명세 + 설계노트(사용자 요청). 구현자는 각 테스트를 먼저 코드화.
