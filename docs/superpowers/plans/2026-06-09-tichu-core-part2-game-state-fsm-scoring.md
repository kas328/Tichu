# Tichu.Core Part 2 — 게임 상태기계 · 합법수 · 정산 · 헤드리스 시뮬 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.
>
> **경량 계획서 안내:** 사용자 요청에 따라 이 계획서는 **태스크 분해 · 테스트 전략 · 핵심 설계 결정**에 집중한다. Part 1 계획서와 달리 매 스텝의 완성 코드를 미리 적지 않는다(타입 시그니처/인터페이스는 "설계 결정"이라 명시함). 각 태스크는 TDD 사이클(실패 테스트 → 최소 구현 → 통과 → 커밋)을 따르되, 구체 테스트 케이스는 **이름 + 어서션 요지**로 기술한다. 구현자는 이 명세를 만족하는 테스트 코드를 먼저 작성한다.

**Goal:** Part 1 조합 엔진 위에, 한 판(Round)을 처음부터 끝까지 굴리는 결정적·헤드리스 게임 상태기계(딜/교환/콜/트릭/소원/특수카드/아웃/정산)와, 합법수 생성기, 그리고 10만 판 무결성을 검증하는 시뮬레이터를 `dotnet test`로 검증되게 구축한다.

**Architecture:** Part 1과 동일한 순수 C#(`netstandard2.1`, C# 9) 정적 함수형 룰 코드 + 가변 상태 객체. 새 네임스페이스 `Tichu.Core.Game`. 상태 전이는 단일 진입점 `GameEngine.Apply(state, action)`가 페이즈 FSM 가드로 강제하고, 결정성은 시드 주입 RNG(Part 1 `Rng`)와 결정적 반복 순서로 보장한다(부동소수점 미사용 — 봉황 비교는 Part 1의 정수×2 유지).

**Tech Stack:** C# 9 / .NET (`netstandard2.1` 라이브러리, `net9.0` 테스트), NUnit. Part 1 산출물(`Card`/`Deck`/`Rng`/`Combination`/`CombinationRecognizer`/`CombinationComparer`) 재사용. Unity·UniTask·R3·Photon 미사용.

**기획서 매핑:** 2.3(특수카드 동작), 4.1~4.5(진행/콜/정산), 11.1(상태 타입), 11.3(룰엔진 책임 ③④⑥), 11.5(라운드 FSM), 11.6(정산 요지), 11.7(엣지: 소원/봉황/개/용), 11.8(테스트 전략), 12.3 Phase 0 DoD.

---

## 핵심 설계 결정 (Key Design Decisions)

기획서와 Part 1 **실제 구현** 사이에 조정이 필요한 지점. ⚠️ 표시는 **착수 전 사용자 승인 필요**.

- **DD1 — Rank 정수×2 유지(확정).** 기획서 11.1은 `Combination.Rank`를 `float`로 적었으나 Part 1은 11.7 권고대로 **정수×2 스케일**로 이미 구현함(봉황 단독만 홀수). Part 2는 이를 그대로 사용한다. 기획서의 `float Rank`는 폐기.
- **DD2 (확정 2026-06-09) — 엔진 형태: 정적 함수형 유지, `IRuleEngine` 인터페이스는 연기.** 기획서 11.3은 `IRuleEngine` 인터페이스를 제시하나, Part 1은 `CombinationRecognizer`/`CombinationComparer` 정적 클래스로 구현됨. Part 2도 **정적 클래스**(`GameEngine`, `LegalMoveGenerator`, `ScoreCalculator`)로 일관 유지하고, DI가 실제로 필요한 시점(Phase 1+ Application/AI 레이어)에 얇은 `IRuleEngine` 파사드를 씌우길 권장. *근거: 단순성(CLAUDE.md), Part 1과 일관, 코어엔 DI 소비자 없음.*
- **DD3 (확정 2026-06-09) — `Apply`는 가변 상태 in-place 변경 + 결과 반환.** 기획서 11.3은 `Apply → 새 GameState`(불변)지만, 11.1 타입은 `List<Card> Hand` 등 **가변**으로 설계됨. Part 2는 `Apply(GameState s, GameAction a) → ApplyResult`로 **`s`를 in-place 변경**하고, 거부 시 사유를 담아 반환(상태 불변). 결정성은 시드+반복순서로 보장(불변성 불필요). AI 탐색/리플레이 분기를 위해 **`GameState.Clone()` 딥카피**를 제공. *근거: 10만 판 시뮬 성능, 기획서 가변 타입과 정합, MCTS는 Clone으로 포크.*
- **DD4 — `GameAction`은 `Kind` 태그 + 페이로드 단일 클래스.** C# 9엔 판별 공용체가 없으므로 `enum GameActionKind { CallGrandTichu, DeclineGrandTichu, Exchange, CallTichu, Play, Pass }` + 필요한 페이로드 필드(좌석, 카드들, 소원, 교환 3장 등)를 든 `sealed class GameAction` + 정적 팩토리(`GameAction.Play(seat, cards, wish?)` 등).
- **DD5 — 결정성 해시.** `GameState.ComputeHash() → ulong`(좌석 손패·획득카드·점수·페이즈·턴·소원을 결정적 순서로 FNV-1a 누적). 결정성 테스트에서 동일 시드+동일 액션 로그 재생 시 비트 일치 검증에 사용.
- **DD6 — 폴더/어셈블리.** 소스는 `core/src/Tichu.Core/Game/`(네임스페이스 `Tichu.Core.Game`). Unity 미러는 `Assets/_Project/Core/Game/`. **새 asmdef 불필요** — 기존 `Tichu.Core.asmdef`가 `Core/` 하위 전체를 포함. 테스트는 Part 1과 동일 위치(`core/tests/Tichu.Core.Tests/` ↔ `Assets/_Project/Tests/EditMode/`).
- **DD7 — 시뮬레이터 위치.** 별도 CLI 프로젝트 없이 **테스트 프로젝트 내** `RandomBot`(시드로 `LegalMoves` 중 균등 선택) + `Simulator.PlayGame(seed)` 헬퍼로 구현. 10만 판 테스트는 `[Category("Heavy")]`로 분리해 기본 CI는 빠르게, 무결성 게이트는 명시 실행.
- **DD8 — Part 1↔Unity 이중복사 유지.** Part 1처럼 `core/`에서 .NET TDD를 먼저 완성하고, 마지막 태스크(Task 8)에서 Part 2 소스·테스트를 `Assets/_Project/`로 미러 + EditMode 그린 확인. (`Card.cs`의 `#nullable enable` 헤더 관례: nullable 참조 타입(`?`)을 쓰는 새 파일에만 헤더 1줄 추가.)

---

## File Structure

Part 2에서 생성/수정하는 파일:

```
core/src/Tichu.Core/Game/
├─ TichuCall.cs            # enum TichuCall { None, Tichu, GrandTichu }
├─ RoundPhase.cs          # enum RoundPhase { Deal8, GrandTichuDecision, Deal6, Exchange, Play, Scoring, RoundEnd }
├─ GameAction.cs          # enum GameActionKind, sealed class GameAction + 팩토리, ApplyResult
├─ Play.cs               # 한 번의 제출 로그(좌석, Combination, 폭탄 끼어들기 여부)
├─ Trick.cs              # LeadType/Length, Top, TopOwnerSeat, History, AccumulatedPoints, WonByDragon
├─ PlayerSeat.cs         # SeatIndex, Hand, IsOut, FinishOrder, Call, WonCards
├─ ScoreBoard.cs         # TeamA/TeamB, TargetScore, Rounds(List<RoundResult>)
├─ RoundResult.cs        # 판별 정산 결과(팀별 카드점수·티츄가감·최종)
├─ GameState.cs          # Phase, Seats[4], CurrentTrick, Turn, Wish, Scores, RngSeed, Rng + Clone()/ComputeHash()
├─ Seating.cs            # 좌석 헬퍼: TeamOf, Partner, NextActive(좌석 순환·아웃 스킵)
├─ GameEngine.cs         # Apply(FSM 디스패치) + 페이즈별 핸들러 + 트릭 회수/소원 해제
├─ LegalMoveGenerator.cs # LegalMoves(state,seat) + IsLegal(...,out reason) + 소원 강제
└─ ScoreCalculator.cs    # ScoreRound(state) → RoundResult (원-투/일반/티츄/용 양도)

core/tests/Tichu.Core.Tests/
├─ GameStateTests.cs          # Clone 독립성, ComputeHash 결정성
├─ TrickCompareTests.cs       # Beats(후보, Trick) 문맥 비교, 봉황 단독, 폭탄 인터럽트
├─ DealExchangeTests.cs       # Deal8/Deal6 결정성, 교환 1:1, 손패 14장 보존
├─ TichuCallTests.cs          # 큰/작은 티츄 콜 타이밍 가드
├─ PlayPhaseTests.cs          # 제출/패스/트릭회수/다음선, Dog 이양, Dragon 양도표시
├─ LegalMovesTests.cs         # 합법수 정확성 + 프로퍼티(LegalMoves ⊆ IsLegal)
├─ WishTests.cs               # 소원 강제·이월·해제
├─ ScoringTests.cs            # 원-투/일반/티츄/용 정산 + 불변식(카드점수 합=100)
├─ OutAndRoundEndTests.cs     # 아웃 순서, 3인 아웃, 원-투 즉시종료
└─ SimulationTests.cs         # RandomBot 시뮬, 결정성 재생, [Heavy] 10만 판 무결성

테스트 헬퍼(테스트 프로젝트 내):
└─ Sim/RandomBot.cs, Sim/Simulator.cs
```

**책임 분리:** 상태 타입(데이터) / `GameEngine`(전이·FSM) / `LegalMoveGenerator`(합법성·소원) / `ScoreCalculator`(정산) / `Seating`(좌석 산수). 각 파일 단일 책임.

---

## 테스트 전략 (기획서 11.8 + 12.3 DoD)

1. **골든 케이스 테이블** — 특수카드/폭탄/콜 시나리오를 명시 NUnit 케이스로 1:1 고정(DoD: 명세 표 1:1 매핑).
2. **프로퍼티 기반** — 랜덤 시드 수천 손패에서 `LegalMoves`가 반환한 모든 수가 `IsLegal`을 통과(불변식). 소원 지정 시 합법수에 항상 소원 포함.
3. **정산 회귀** — 원-투/일반/티츄 실패 시나리오 고정. **불변식: CASE B에서 티츄 가감 제외 양 팀 카드점수 합 = 100.**
4. **결정성** — 동일 시드+동일 액션 로그 재생 → `GameState.ComputeHash()` 비트 일치.
5. **10만 판 무결성**(`[Category("Heavy")]`) — RandomBot 자동 대국 10만 판: 예외/불법상태 0건, 매 판 점수 정합성(원-투 200/0 또는 카드합 100) 100%.

---

## Task 1: 상태 타입 (`Tichu.Core.Game`)

**Files:** Create `Game/TichuCall.cs`, `RoundPhase.cs`, `Play.cs`, `Trick.cs`, `PlayerSeat.cs`, `ScoreBoard.cs`, `RoundResult.cs`, `GameAction.cs`, `GameState.cs`, `Seating.cs`. Test: `GameStateTests.cs`.

**설계 결정 반영:** DD3(가변 + `Clone()`), DD4(`GameAction`), DD5(`ComputeHash`). 타입 윤곽은 기획서 11.1을 따르되 `Combination.Rank`는 정수(DD1). `GameState`는 `RngSeed`와 함께 진행용 `Rng`(Part 1 struct) 인스턴스를 보유.

- [ ] **Step 1: 실패 테스트 작성** — `GameStateTests`:
  - `Clone_produces_independent_deep_copy`: `Clone()` 후 원본 손패에 카드 추가/제거해도 사본 불변(좌석 Hand/WonCards, Trick, ScoreBoard 모두 딥카피).
  - `ComputeHash_is_stable_for_equal_states`: 동일 내용 두 상태의 해시 일치; 한 손패 1장 차이 시 해시 상이.
  - `Seating_partner_and_team`: `Seating.TeamOf(0)==TeamOf(2)`, `Partner(0)==2`, `Partner(1)==3`.
  - `NextActive_skips_out_seats`: 좌석 2가 아웃이면 1→3 순환.
- [ ] **Step 2: 컴파일/실패 확인** — `dotnet test core/Tichu.sln` → 타입 미정의 실패.
- [ ] **Step 3: 최소 구현** — 위 10개 파일. 가변 컬렉션 + `Clone()`(딥카피) + `ComputeHash()`(FNV-1a, 결정적 순서) + `Seating` 헬퍼.
- [ ] **Step 4: 통과 확인** — `dotnet test core/Tichu.sln`.
- [ ] **Step 5: 커밋** — `feat(core): add Tichu.Core.Game state types (state, action, seating)`.

---

## Task 2: 트릭 비교 문맥 (`Beats` against `Trick`)

**Files:** Modify `Combinations/CombinationComparer.cs` (또는 `Game/`에 트릭 어댑터 추가). Test: `TrickCompareTests.cs`.

**규칙(기획서 11.4, 2.3):** 추종 후보 vs 현재 `Trick.Top` 2자 비교로 환원. 봉황 단독 비교값은 **트릭 문맥**(직전 단독 Rank)에서 산정 — Part 1 `TrickContext`를 `Trick`에서 만들어 `Recognize`에 주입. 폭탄은 비추종(타입 불일치)이라도 끼어들 수 있음. 용 단독 최강, 봉황은 용 못 이김(Part 1 `Beats`가 이미 처리).

- [ ] **Step 1: 실패 테스트** — `TrickCompareTests`:
  - `Following_same_type_higher_rank_beats_top` / `lower_or_different_does_not`.
  - `Bomb_interrupts_non_bomb_trick`(타입 불일치라도 폭탄은 받기 허용).
  - `Phoenix_single_uses_prev_single_rank_from_trick`(Top이 K 단독 → 봉황=K+0.5 → 이김).
  - `Phoenix_cannot_beat_dragon_single`.
  - `TrickContext` 생성 헬퍼가 `Trick`에서 올바른 `TopIsSingle`/`CurrentSingleRankScaled` 산출.
- [ ] **Step 2: 실패 확인.**
- [ ] **Step 3: 구현** — `Trick` → `TrickContext` 변환 헬퍼 + `CombinationComparer.Beats(Combination cand, Trick top)` 오버로드(내부적으로 Part 1 `Beats(cand, top.Top)` 재사용, 폭탄 인터럽트 분기 추가).
- [ ] **Step 4: 통과 확인.**
- [ ] **Step 5: 커밋** — `feat(core): compare candidate against trick context (phoenix/bomb interrupt)`.

---

## Task 3: `Apply` 셋업 페이즈 (Deal8 → 큰 티츄 → Deal6 → Exchange)

**Files:** Create `Game/GameEngine.cs`(Apply 디스패처 + 셋업 핸들러). Test: `DealExchangeTests.cs`, `TichuCallTests.cs`(큰 티츄 부분).

**규칙(기획서 4.1 1~4, 11.5):** 시드로 셔플(Part 1 `Deck`)→각 4명 8장. 큰 티츄는 9번째 받기 전(8장 상태)만. Deal6로 14장. 교환은 각자 좌/파트너/우에 1장씩 지정 → 동시 적용(보낸 3·받은 3, 손패 14 유지). FSM 가드: 페이즈 밖 액션 거부.

- [ ] **Step 1: 실패 테스트**:
  - `Deal8_is_deterministic_for_seed`(동일 시드 동일 분배) / `each_seat_gets_8`.
  - `GrandTichu_only_before_deal6`(Deal6 이후 큰 티츄 액션 거부).
  - `Deal6_brings_each_hand_to_14`.
  - `Exchange_swaps_one_to_each_and_preserves_14`(교환 후 각 손패 14장, 보낸 카드 사라지고 받은 카드 존재).
  - `Exchange_rejected_if_not_three_distinct_targets`.
  - `Action_in_wrong_phase_is_rejected_with_reason`.
- [ ] **Step 2~4:** 실패 확인 → 셋업 핸들러 구현 → 통과.
- [ ] **Step 5: 커밋** — `feat(core): apply deal/grand-tichu/exchange setup phases with FSM guards`.

---

## Task 4: `Apply` Play 페이즈 (트릭 루프 + 개·용 + 작은 티츄)

**Files:** Modify `Game/GameEngine.cs`. Test: `PlayPhaseTests.cs`, `TichuCallTests.cs`(작은 티츄 부분).

**규칙(기획서 4.1 5~7, 2.3, 11.7):** 마작 보유자 첫 선. 제출=합법 조합(Task 2 비교), 또는 패스, 또는 폭탄 끼어들기. 전원 패스 시 마지막 제출자가 트릭 회수(점수 누적)→다음 선. **개**: 선·단독 전용, 트릭 미형성, 선을 파트너(아웃이면 다음)로 이양. **용**: 단독 전용, 트릭 승리 시 `WonByDragon=true`(정산서 양도). 작은 티츄: 자신의 첫 카드 내기 전까지만.

- [ ] **Step 1: 실패 테스트**:
  - `Mahjong_holder_leads_first`.
  - `Submit_then_pass_around_collects_trick_to_last_player` + `winner_becomes_next_lead` + `accumulated_points_correct`.
  - `Bomb_can_be_played_out_of_turn_to_take_trick`.
  - `Dog_passes_lead_to_partner_no_trick` + `Dog_to_next_when_partner_out`.
  - `Dragon_win_sets_WonByDragon`.
  - `Tichu_call_rejected_after_first_card_played`.
  - `Phoenix_single_follow_resolves_via_trick_context`(Task 2 연동).
- [ ] **Step 2~4:** 실패 확인 → Play 핸들러(제출/패스/회수/개/용/콜) 구현 → 통과.
- [ ] **Step 5: 커밋** — `feat(core): apply play phase (trick loop, dog, dragon, tichu call)`.

---

## Task 5: 합법수 생성 + 검증 + 소원 강제

**Files:** Create `Game/LegalMoveGenerator.cs`. Test: `LegalMovesTests.cs`, `WishTests.cs`.

**규칙(기획서 11.3③, 11.7 소원):** `LegalMoves(state,seat)`=손패에서 현재 트릭을 받을 수 있는 모든 합법 조합(+선이면 임의 합법 조합) + 패스(추종 상황만). 폭탄은 항상 후보. **소원**: `Wish` 설정 시 "그 숫자를 합법적으로 포함해 낼 수 있으면 반드시 포함한 수만" 반환(패스/타 수 거부); 충족 불가하면 평소대로. 소원 충족 제출 시 `Wish=null` 해제(폭탄 끼어들기에도 적용).

- [ ] **Step 1: 실패 테스트**:
  - `Lead_legal_moves_include_all_recognized_combos_and_no_pass`.
  - `Following_only_returns_beating_moves_plus_pass_plus_bombs`.
  - `Property_all_legal_moves_pass_IsLegal`(랜덤 시드 손패 N건 — 불변식).
  - `Wish_forces_inclusion_when_satisfiable`(소원=7, 7 포함 가능 → 7 미포함 수·패스 제외).
  - `Wish_not_forced_when_unsatisfiable`.
  - `Wish_cleared_after_satisfying_play`.
- [ ] **Step 2~4:** 실패 확인 → 생성기/검증기/소원 필터 구현 → 통과.
- [ ] **Step 5: 커밋** — `feat(core): legal-move generation, validation, and wish enforcement`.

---

## Task 6: 종료 판정 + 정산 (`ScoreCalculator`)

**Files:** Create `Game/ScoreCalculator.cs`; Modify `GameEngine.cs`(아웃/원-투/Scoring 전이). Test: `OutAndRoundEndTests.cs`, `ScoringTests.cs`.

**규칙(기획서 4.4, 11.6):** 아웃=손패 0 → `FinishOrder` 부여. 3인 아웃 또는 원-투(한 팀 1·2등)시 Scoring 전이. **CASE A 원-투**: 해당 팀 +200, 상대 0, 카드점수 무시. **CASE B 일반**: 마지막 1인 잔여 손패 점수→상대팀, 자신이 딴 트릭→1등에게; 팀별 카드점수 합산. **용 트릭**: `WonByDragon` 트릭은 상대팀 1명에게 양도(점수 귀속 반전). 티츄/큰 티츄 성패 ±100/±200 가산(1등 아웃 성공).

- [ ] **Step 1: 실패 테스트**:
  - `Out_order_assigned_on_empty_hand`.
  - `OneTwo_ends_round_immediately` + `OneTwo_scores_200_0_ignoring_card_points`.
  - `Normal_end_distributes_last_hand_and_tricks` + **`Invariant_card_points_sum_100`**(티츄 제외).
  - `Dragon_trick_points_go_to_opponent`.
  - `Grand_tichu_success_plus_200_failure_minus_200` / `Tichu_partner_out_first_means_failure`.
  - 기획서 4.4 "정산 예시"(25/75 + 작은티츄 → 125/75) 골든 케이스 1:1 재현.
- [ ] **Step 2~4:** 실패 확인 → 종료 판정 + 정산 구현 → 통과.
- [ ] **Step 5: 커밋** — `feat(core): round-end detection and scoring (one-two, dragon gift, tichu)`.

---

## Task 7: 헤드리스 시뮬레이터 + 프로퍼티/결정성/10만 판

**Files:** Create `Sim/RandomBot.cs`, `Sim/Simulator.cs`(테스트 프로젝트). Test: `SimulationTests.cs`.

**규칙(기획서 11.8, 12.3 Phase 0 DoD):** `RandomBot`=시드로 `LegalMoves` 중 균등 선택(콜은 단순 정책 또는 안 함). `Simulator.PlayGame(seed)`=한 게임(여러 판)을 1000점까지 또는 1판 모드로 구동, 모든 액션을 `Apply`로 통과. 10만 판은 `[Category("Heavy")]`.

- [ ] **Step 1: 실패 테스트**:
  - `Simulator_plays_a_full_round_without_exception`(소규모).
  - `Determinism_same_seed_same_final_hash`(동일 시드 2회 → `ComputeHash` 일치; 액션 로그 재생도 일치).
  - `Property_no_illegal_state_over_many_rounds`(수천 판: 예외 0, 매 판 점수 정합).
  - `Heavy_100k_rounds_integrity` `[Category("Heavy")]`: 10만 판 예외/불법 0건 + 점수 불변식 100%.
- [ ] **Step 2~4:** 실패 확인 → RandomBot/Simulator 구현 → 통과(`dotnet test`로 기본, `--filter Category=Heavy`로 무결성 게이트).
- [ ] **Step 5: 커밋** — `test(core): headless simulator with determinism and 100k-round integrity`.

---

## Task 8: Unity 통합 (미러 + EditMode 그린)

**Files:** Mirror `Game/*.cs` → `Assets/_Project/Core/Game/`, 테스트 → `Assets/_Project/Tests/EditMode/`. (시뮬 헤비 테스트는 EditMode에서 `[Category("Heavy")]`로 분리, 기본 러너 제외.)

**설계 결정 반영:** DD6(새 asmdef 불필요), DD8(이중복사 + `#nullable enable` 관례). Part 1 통합과 동일 절차.

- [ ] **Step 1:** `core/src/Tichu.Core/Game/`의 모든 .cs를 `Assets/_Project/Core/Game/`로 복사(nullable 참조 타입 쓰는 파일에만 `#nullable enable` 헤더), 각 `.meta` 생성(고유 GUID).
- [ ] **Step 2:** Part 2 테스트 .cs + `Sim/` 헬퍼를 `Assets/_Project/Tests/EditMode/`로 미러 + `.meta`.
- [ ] **Step 3:** Unity refresh → `read_console` 컴파일 에러 0 확인.
- [ ] **Step 4:** EditMode 테스트 실행(Heavy 제외) → `dotnet test`(Heavy 제외) 결과와 카운트 일치 확인.
- [ ] **Step 5: 커밋** — `chore(unity): mirror Tichu.Core.Game and Part 2 tests into Assets/_Project`.

---

## Part 2 완료 기준 (DoD — 기획서 12.3 Phase 0)

- [ ] `dotnet test core/Tichu.sln`(Heavy 제외) 전체 통과 + `--filter Category=Heavy` 10만 판 통과.
- [ ] 10만 판 헤드리스: 불법 상태/예외 **0건**, 매 판 점수 정합성(원-투 200/0 또는 카드합 100) **100%**.
- [ ] 특수카드(개/용/봉황/마작 소원)·폭탄·티츄 콜 시나리오가 기획서 표와 **1:1** 골든 케이스로 검증.
- [ ] `Tichu.Core`가 `UnityEngine` 미참조(netstandard2.1 빌드로 보증), 봉황 비교 `float` 미사용(정수×2).
- [ ] Unity EditMode(Heavy 제외)가 `dotnet test`와 동일 결과(이중복사 동기).

> **Part 2는 여기까지.** 이후 Phase 1(MVP: 프레젠테이션 레이어 + 기본 AI + 인게임 UI/애니)은 룰엔진 위에 별도 계획서로 착수한다. 코어 커버리지 ≥90% 측정은 Task 7 이후 `coverlet` 등으로 별도 확인(선택).

## Self-Review (작성자 점검)
- **Spec 커버리지:** 상태타입(T1)·트릭비교(T2)·딜/교환/큰티츄(T3)·트릭루프/개/용/작은티츄(T4)·합법수/소원(T5)·아웃/원-투/정산/용양도(T6)·시뮬/결정성/10만판(T7)·Unity통합(T8) — 기획서 4장·11.3~11.8·12.3 DoD 전 항목 매핑.
- **Part 1 정합:** `Rank` 정수×2, `Recognize(ReadOnlySpan<Card>,TrickContext)`, `Beats(Combination,Combination)`, `Deck`/`Rng` 재사용 — 시그니처 일치.
- **결정 확정:** DD2(정적 클래스, IRuleEngine 연기), DD3(가변 Apply in-place + Clone) — 사용자 승인 완료(2026-06-09).
- **경량 범위:** 완성 코드 대신 테스트 케이스 명세 + 설계 결정으로 구성(사용자 요청). 구현자는 각 테스트를 먼저 코드화.
