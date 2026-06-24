# Tichu Phase 2 — AI 고도화 설계 스펙 (PIMC)

- 작성일: 2026-06-24
- 상태: 설계 승인됨(브레인스토밍 종료) → 구현계획(writing-plans) 대기
- 도출: 멀티에이전트 워크플로(Ground 3 + Research 2[28 학술출처] + 설계 패널) → 메인루프 종합·적대검증
- 결정: ① 탐색 코어 = **PIMC** ② 완료 기준 = **기획 DoD**(Hard가 현 휴리스틱봇 대비 통계적 유의 승률 우위)

> ⚠️ 본 문서의 `file:line` 참조는 작성 시점 기준이다. 메모리는 시간이 지나며 어긋날 수 있으므로, 각 단계 구현 전 해당 파일을 실제로 확인한다.

---

## 0. 요약 (TL;DR)

기획서 §8의 "휴리스틱 + 결정화 몬테카를로" 강 AI를, **PIMC(Perfect-Information Monte Carlo)**로 구현한다:

> 미관측 손패를 룰에 맞게 N개 "세계(world)"로 결정화 → 각 세계를 현 `AiAgent` 휴리스틱을 기본 정책으로 끝까지 롤아웃 → 라운드 종료 보상(팀 점수차)을 수별로 평균 → 최고 기댓값 수에 투표.

핵심 이점: **현 `AiAgent` 휴리스틱을 롤아웃 디폴트 정책으로 거의 그대로 재사용** → 신규 룰/평가 코드 최소(CLAUDE.md 단순함·수술적). 트릭게임은 PIMC가 실증적으로 강하다(Long et al. 2010: 높은 leaf-correlation·disambiguation). 약점(strategy fusion)은 Hard 단계에서 자기대국 진단 후 EPIMC식 "초반 ply 결정화 연기" 또는 ISMCTS로 **선택적** 강화한다.

탐색 AI는 기존 추상 경계(`IAgent`/`IDecisionAgent`)로 **나란히 삽입**되어 드라이버·룰엔진을 한 줄도 바꾸지 않는다.

## 0.1 비목표 (이번 Phase 2에서 안 함)

- 강화학습(DQN/PPO) — 연구: 순수 RL Tichu는 150M스텝/32CPU+4M행동 BC로도 amateur 수준에 그침. 탐색이 더 강하고 학습 불요. **far-future 옵션으로만 보류.**
- CFR/내시균형 — 트릭게임은 모든 카드가 가치를 흔들어 추상화가 어려움. 탐색이 정답.
- 슈퍼휴먼 — 현실적 상한 ≈ "준수한 인간 수준".
- `core/` dotnet 트리 부활 — stale(P1-B 이후 방치). 단일 정본 Assets/_Project만 사용.

---

## 1. 배경 & 목표

### 1.1 현 AI (Normal 휴리스틱) — 재사용 자산
`Assets/_Project/GameFlow/Agents/AiAgent.cs`. 공개정보 + `LegalMoveGenerator`만 사용, `Clone()` 룩어헤드 없음. 9결정포인트별 휴리스틱(HandPower 게이트 콜, 최저카드 리드, 파트너Top 패스, 상대 부유트릭 회수, 상대Top 15점+ 최소폭탄 등). `MoveOrder`(Strength = Rank·256 + Length·16 + Type, 결정적 총순서)가 정렬 오라클. → **이 로직이 PIMC 롤아웃의 디폴트 정책**이 된다.

### 1.2 완료 기준 (DoD, 기획 §12/§13)
1. **Hard AI가 현 `AiAgent`(휴리스틱봇) 대비 통계적 유의 승률 우위**(Wilson 95% CI 하한 > 0.5, 다수판).
2. 1수당 모바일 ≤ 300ms 목표(§13), 평균 ≤ 1s·최악 ≤ 2s(§12). UniTask 백그라운드 비차단 + anytime(시간초과 → best-so-far).
3. 결정적 시드 재현 — "고정 노드수 모드"에서 같은 (state, seed, config) → 같은 수.
4. 4티어(Easy/Normal/Hard/Expert) 강도 단조성 + 난이도별 평균/최악 사고시간 리포트.
5. AI는 별도 룰 구현을 갖지 않는다(동일 `IDecisionAgent`·동일 룰엔진).

---

## 2. 아키텍처

### 2.1 삽입 지점 (드라이버 무수정)
`IAgent`(동기, `GameFlow/Agents/IAgent.cs`)와 `IDecisionAgent`(비동기, `Presentation/IDecisionAgent.cs`)가 드라이버가 보는 **유일한 추상 경계**. 탐색 AI는 두 새 구현체로 삽입:

- **`PimcAgent : IAgent`** (동기) — 탐색 코어. 벤치마크(`MatchRunner` 동기 대량 대국)·결정성 회귀에 사용. UnityEngine 비의존.
- **`PimcDecisionAgent : IDecisionAgent`** (비동기) — `PimcAgent`를 감싸 `state.Clone()` 후 `UniTask.RunOnThreadPool`로 탐색, anytime(시간예산 → best-so-far) 반환. 메인스레드 마샬링·시간예산 담당. 인게임에 사용.

`GameDriver`/`AsyncGameDriver`/`MatchRunner`/`HumanAgent` 무수정. 난이도 주입은 `RoundBootstrap.RunMatchAsync`의 `IDecisionAgent[]` 생성부 + `GameLaunchArgs.Difficulty` 필드 추가(팩토리).

### 2.2 asmdef 배치 (신규 0개)
- **탐색 코어** (`Determinizer`/`Rollout`/`Pimc`/`PolicyConfig`/`IPolicy`/`PimcAgent`) → **`Tichu.GameFlow.Agents`**. `Tichu.GameFlow.asmdef`는 `noEngineReferences=true`·`references=[Tichu.Core]` → (a) 순수 C#으로 `GameState.Clone`/`LegalMoveGenerator`/`Apply`/`ScoreCalculator` 전부 접근, (b) UnityEngine 비의존이라 `UniTask.RunOnThreadPool` 워커에서 **Unity API 비접촉 → 스레드 안전**, (c) `System.Threading`만 필요.
- **비동기 어댑터** `PimcDecisionAgent` → **`Tichu.Presentation`**(이미 `Cysharp.UniTask`·R3 참조, `AiDecisionAgent`/`DelayedAiDecisionAgent`와 동거).
- **벤치 러너** → Unity EditMode 테스트의 **비기본 카테고리**(`[Explicit]` 또는 `[Category("Bench")]`). 신규 asmdef 불요. (전량 Heavy 10만판처럼 MCP-stuck 유발 금지 — §10.)

### 2.3 데이터 흐름 (한 결정)
```
드라이버 → PimcDecisionAgent.DecideTurnAsync(ctx, ct)
   ctx.State.Clone()                         # 가변 공유 → 백그라운드 진입 전 필수
   await UniTask.RunOnThreadPool(() =>
       PimcAgent.Search(snapshot, seedDerived, config, deadline, ct))
   ── 스레드풀 ──────────────────────────────
   for world in 1..config.Worlds:            # 결정화
       det = Determinizer.Sample(snapshot, observerSeat, worldRng)   # 미관측 손패만 재샘플
       for move in LegalMoves(det, observerSeat) ∪ {Pass?} ∪ {GiveDragon?}:
           for r in 1..config.RolloutsPerWorld:
               score += Rollout(det.ApplyClone(move), defaultPolicy, rolloutRng)  # 끝까지 → ScoreRound 팀점수차
           ev[move] += score / RolloutsPerWorld
       if deadline exceeded or ct canceled: break  # anytime
   return argmax(ev[move]) (동점깨기 = MoveOrder)
   ── 메인스레드 복귀 ──
   TurnDecision 반환
```

---

## 3. 탐색 코어 (PIMC)

- **결정화(Determinization)**: §4. 미관측 손패를 룰 제약 충족하게 N세계로 샘플.
- **롤아웃(Rollout)**: 결정화된 완전정보 세계를 **디폴트 정책**으로 라운드 끝까지 플레이 → `ScoreCalculator.ScoreRound`의 `TeamATotal − TeamBTotal`(관측 좌석 팀 부호)을 보상으로. 디폴트 정책 = 현 `AiAgent` 휴리스틱(`MoveOrder` 재사용) + **ε-랜덤**(연구: 순수 결정적 전문가 롤아웃은 무작위 가미 축소룰보다 약함). `Simulator.RunRound`의 페이즈 디스패치 루프를 골격으로 차용.
- **투표(Move averaging)**: 각 합법수의 세계·롤아웃 평균 기댓값 중 argmax. 동점깨기 = `MoveOrder`(사람다운 수 편향 — §8.3).
- **무거운 탐색은 2곳만**: `DecideTurn`(리드/팔로우)·`DecideBomb`. 나머지 7결정포인트는 경량(현 휴리스틱 유지 또는 콜만 얕은 결정화 EV). `DecideBomb`은 **경량 게이트(상대Top·누적점수) 선통과 후에만** 탐색(폭탄창은 매 좌석 호출 → 게이트 없이 탐색 시 예산 폭발).

### 3.1 strategy fusion (인지 + 단계적 방어)
PIMC는 각 세계를 독립으로 풀어 "숨은 카드를 아는 듯" 자신만만하게 둘 수 있다(과콜·무모폭탄). **1차(P2-A~C)는 이를 수용**하고, **Hard 자기대국 진단으로 검출**, 심하면 **Expert에서 EPIMC식 초반 ply 결정화 연기**(또는 ISMCTS) 추가. 트릭게임은 disambiguation이 높아(카드가 매 트릭 공개) fusion 영향이 제한적이라는 게 PIMC 채택 근거.

---

## 4. Determinizer (결정화 샘플러)

미관측 손패를 **룰 제약 충족(constraint-satisfaction)** 분배한다.

### 4.1 미지 카드 풀
```
미지풀 = 56장(Deck.CreateStandard)
        − 관측좌석 현재 Hand
        − 모든 좌석 WonCards (나온 카드 누적)
        − CurrentTrick.History 의 모든 Combination.Cards (현재 트릭에 깔린 카드)
        − 버려진 개 (Card.Dog — 어느 더미에도 안 듦)
        − 이미 나온 마작
```
이 풀을 상대 3좌석의 **공개된 `Hand.Count`에 정확히 일치**하게 분배(불일치 시 `GameEngine.Apply`가 Reject). 카드 총집합은 56장 폐쇄.

### 4.2 ⚠️ 치트 가드 (cheating 방지, CRITICAL)
`GameState`는 모든 `Seats[*].Hand`를 노출하므로, Determinizer 버그로 진짜 상대 손을 그대로 두면 **상대 패를 훔쳐보는 비현실적 AI**가 된다(불완전정보 위배). **단위테스트로 강제**: 관측좌석 외 Hand는 재샘플로 원본과 달라져야 하고, 관측좌석 Hand·공개정보는 불변이어야 한다.

### 4.3 비균등 샘플 (Hard+)
⚠️ 균등 샘플은 함정. Tichu의 3장 교환·티츄/큰티츄 콜이 **강한 정보 누출**이라, 균등 분배는 불가능/희박한 손을 환각해 오평가한다. **Hard부터 reach-probability 가중**(Rebstock et al. 2019): 가정한 상대/파트너 정책 하에서 관측 행동을 했을 확률의 곱으로 세계를 가중. Normal까지는 균등 허용.
- 단서: GrandTichu 콜 → 강한 손 사전확률 / Tichu 콜 → 14장 유지+무플레이 / 소원 패스 → 그 좌석 소원랭크 미보유(음의 단서) / Top 두고 패스 → 이기는 합법수 없었음(폭탄 보유 가능성 때문에 약제약).

---

## 5. 난이도 티어 (`PolicyConfig`)

단일 `PimcAgent` + `PolicyConfig` struct 주입으로 4티어. **차이는 정책+예산뿐**(별 구현 금지). 현 `AiAgent`의 private const 4개(Grand=10/Finish=5/Rich=15/BombMin=15)를 `PolicyConfig`로 외부화하되 기존 `AiAgent`는 그대로 둔다.

| 티어 | 세계수 | 롤아웃/세계 | 시간예산 | 추론/협력 | 비고 |
|---|---|---|---|---|---|
| **Easy** | 0(탐색 OFF) | — | 즉시 | 없음 | 현 휴리스틱 + ε 블런더·콜 보수 |
| **Normal** | ~4 | ~8 | ~80ms | 약(균등 샘플) | 휴리스틱 롤아웃 + ε랜덤 |
| **Hard** | ~16–20 | ~16 | ~250ms | reach-prob 가중 + 전협력 | 폭탄타이밍 결정화 |
| **Expert** | ~24 | ~24 | ~300ms 캡(인게임) | + 블러프/노이즈 | EPIMC 초반ply 연기 |

**주 레버 = 시간예산·세계수**(가장 단조·검증 쉬움, 연구: 예산 캡이 가장 깨끗한 난이도 레버). 보조 = 노이즈·협력가중·추론 ON/OFF. 모든 티어 동일 `IDecisionAgent`·동일 룰엔진.

> 숫자는 시작값(추정)이며 **P2-C 벤치로 프로파일 후 확정**한다. 모바일 실측은 D6 연계.

---

## 6. 비동기 / 시간예산 (anytime)

- **패턴**: `PimcDecisionAgent.DecideTurnAsync`: `var snap = ctx.State.Clone(); var move = await UniTask.RunOnThreadPool(() => pimc.Search(snap, seedDerived, config, deadline, ct), ct);` 후 메인스레드 복귀해 `TurnDecision` 반환. `DecideBombAsync`도 동일(경량 게이트 후).
- **anytime**: 탐색은 세계/롤아웃 루프 매 반복마다 `deadline`(Stopwatch)·`ct` 체크 → 초과 시 **현재까지 최다투표 수 반환**(부분 탐색도 유효, 최소 1세계 완료 보장).
- **기존 골격 재사용**: `AsyncGameDriver.DrivePlayAsync` 4)의 폭탄인터럽트 `linked-CancellationTokenSource` + `try/catch(OperationCanceledException)` 패턴을 재사용. ⚠️ **예산만료 취소와 폭탄인터럽트 취소를 별 플래그로 구분**(같은 OCE로 와서 "이 턴 결정 폐기"와 "best-so-far 반환"이 섞이면 잘못된 턴 폐기 — Ground#2 리스크).
- **셋업 결정**(큰/작은티츄·교환)은 즉시 유지(연출 자연스러움). 무거운 탐색은 `DecideTurn`/`DecideBomb`/`DragonRecipient`에만.

---

## 7. 결정성 & 오라클 보존 (2층 분리)

- **1층(불침범)**: 기존 오라클(동기 `GameDriver` == `AsyncGameDriver`(onApply·인터럽트=null), `ComputeHash` 5시드 비트동일)은 **휴리스틱 `AiDecisionAgent`로만** 검증된다. 탐색 AI는 이 경로에 넣지 않으므로 **침범 0**. PIMC 도입 후에도 기존 오라클 테스트가 그대로 그린임을 회귀로 못박는다.
- **2층(신규 탐색 결정성)**: `PimcAgent`는 **"고정 노드수 모드"**(시간캡 OFF, worlds·rollouts 고정)에서 결정적이어야 한다. 모든 무작위는 **시드주입 `Rng`(struct SplitMix64)만** — `System.Random`/`DateTime`/`Stopwatch` 분기 금지. 세계샘플·롤아웃 RNG는 좌석·노드·세계별 시드 파생(`roundSeed ^ const ^ seat ^ worldIdx`, 기존 `AiAgent`·`Simulator`의 분리주입 패턴 차용·게임 셔플 Rng와 비상관). 검증: 같은 (state, seed, config) → 같은 선택수, `ComputeHash` 재생 일치.
- **시간캡 모드는 비결정적이 정상**(wall-clock 의존, 인게임 전용) — 오라클·결정성 회귀 대상 아님.
- ⚠️ `Rng`는 struct → 항상 로컬로 꺼내 전진 후 되쓰기(`var local = field; local.NextX(); field = local`). 필드 직접 호출 금지.

---

## 8. 특수카드 & 파트너 협력

### 8.1 특수카드 (별 룰구현 불요)
`GameEngine.Apply`/`LegalMoveGenerator`가 전부 처리 → 롤아웃은 자동 정확. Determinizer만 제약을 지키면 된다.
- **마작 소원**: `Wish` 활성+강제 시 `LegalMoves`가 이미 소원수만 남김 → 탐색 추가처리 불요. 과거 소원 패스 = 음의 단서(Hard 샘플러).
- **용 양도**: `PendingDragonGift` = 2지선다 노드. `ScoreRound`가 `DragonGiftRecipient`로 점수 귀속 → 팀보상에 직결(협력 점수몰아주기 후크). 경량 1-ply EV로 충분.
- **봉황**: `LegalMoves`가 봉황0/1 두 경우 전수 생성 → **합법수 폭증 주원인**. `MoveOrder` 정렬 + 상위 K만 탐색으로 제한(안 그러면 얕은 탐색). −25점은 `ScoreRound` 처리.
- **개**: 결정화 풀에서 항상 제외(버려짐). 트릭 미형성·파트너 선넘김 → 선양보 전술은 팀보상으로 발현.
- **폭탄 인터럽트** ⚠️ 핵심 난점: 차례밖 인터럽트라 충실 롤아웃은 매 전이마다 **비턴 좌석의 폭탄 기회**를 점검해야 한다(현 `RandomBot`은 `s.Turn`만 → 폭탄 미시뮬). **1차 PIMC 롤아웃은 단순화(턴 좌석만)로 시작, Hard부터 롤아웃에 폭탄창 점검 추가.**

### 8.2 파트너 협력 (팀보상 + 자기복제)
연구의 상용 표준: **팀 보상**(터미널 노드에서 팀 점수차) + **파트너 = 자기복제 정책**(파트너 턴 시뮬 시 같은 디폴트 정책, 적대 노드 아님). 기획 §8⑤의 점수몰아주기·티츄 보조·선양보·폭탄타이밍은 명시 규칙이 아닌 **팀보상을 통해 자연 발현**된다(+Hard에서 reach-prob 추론으로 파트너 신호 해석).

### 8.3 체감 강도 (perceived strength)
⚠️ 연구: 객관적으로 강한 탐색이 결판난 국면에서 "랜덤해 보이는 수"로 사람에겐 약해 보일 수 있다(AI Factory). **동점깨기를 `MoveOrder`로 사람다운 수 편향**(폭탄 보존·뻔한 승리 확보) + **인간 플레이테스트로 검증**(자기대국 델타만으로 불충분). Expert에서 강조.

---

## 9. 성능 / GC (최대 리스크)

`GameState.Clone()`이 노드/세계마다 Seats4×List + Trick + History + ScoreBoard 신규 할당 + `LegalMoves`가 후보 List 다수 생성. 모바일에서 수천 시뮬/수 → **GC 히칭으로 D6 60fps 게이트 위반**.
- **완화**: 세계당 1회 Clone 재사용(롤아웃은 가능한 한 in-place 진행 후 다음 세계서 재Clone), 노드/상태 스냅 풀링(기존 `CardChipPool` 규율). 현 엔진은 Undo 미지원이라 매 분기 Clone이 비용 핵심.
- **전략**: **1차 증분은 worlds·rollouts를 작게(Normal 4×8) 시작 → 프로파일 후 상향.** 조기 최적화 금지(CLAUDE.md), 단 풀링은 P2-D 모바일 강화 전 도입.
- 스레드: `UniTask.RunOnThreadPool`(메인스레드 비차단) + 메인 복귀해 수 적용. `Task.Run`(Unity SyncContext) 금지(프레임 스파이크). `LegalMoveGenerator`의 `[ThreadStatic]` 버퍼는 워커별 독립이라 안전(단일 워커 권장).

---

## 10. 벤치마크 & DoD 측정

⚠️ 기존 10만판 `SimulationTests`가 메인스레드 점유 → MCP/에디터 stuck. **AI-vs-AI 벤치는 절대 기본 스위트에 무거운 루프를 넣지 않는다.**
- **위치**: Unity EditMode 테스트의 **비기본 카테고리**(`[Explicit]`/`[Category("Bench")]`) + 반복수 캡. `run_tests` 클래스 필터로만 실행, PlayMode 외부.
- **동기 대량 승률**: `MatchRunner.RunMatch(masterSeed, Func<ulong,int,IAgent> agentFactory, target, maxRounds)`의 `agentFactory`에 `PimcAgent`(동기, 고정노드수 모드)를 꽂아 측정(IAgent 동기 경로는 UniTask 불요).
- **사고시간**(§12/§13): 별도 비동기 마이크로벤치로 1수당 `Stopwatch`(평균/p95/최악 리포트).
- **통계**: 미러드딜(같은 셔플·좌석 스왑)로 분산 절감, **팀 점수차**(기댓값 0 vs 동급) 1차 지표 + 고정풀 **승률 Wilson 95% CI**. 100-Elo(≈64% 승률) 게이트 ~150–200판, 운(deal variance) 제거엔 수천~10,000판. 자기대국 편향 방지 = 대조군 풀(`RandomAgent`/현 `AiAgent`/타시드 PIMC).
- **DoD 게이트**: Hard가 현 `AiAgent` 대비 팀점수차 평균 > 0 + Wilson 95% CI 하한 > 0.5.

---

## 11. 단계별 로드맵

각 단계는 머지 가능·검증 가능한 증분. **작은 것부터.**

### P2-A — 탐색 골격 (강도 ≈ 현 수준)
`Determinizer`(제약충족 분배 + 치트가드) + 현 휴리스틱을 `IPolicy` 디폴트 정책으로 추출(`AiAgent` 로직 재사용, `MoveOrder` 그대로) + **단일 세계** 롤아웃 → `ScoreRound` 보상. `PimcAgent`(고정노드수 모드, worlds=1)부터 — 사실상 현 `AiAgent`와 비슷하나 **탐색 배선을 검증**. 신규 asmdef 0.
- 검증(TDD): 결정성 · 치트가드 · 롤아웃 정확성.

### P2-B — PIMC 코어 + Normal 티어
다세계 결정화(worlds≈4) + `DecideTurn` 수별 EV 투표 + `PolicyConfig`(현 const 외부화) + `PimcDecisionAgent`(IDecisionAgent, Clone 후 `RunOnThreadPool`·anytime ≤80ms). Easy/Normal 티어 매핑(ε노이즈). `RoundBootstrap` 난이도 주입(`GameLaunchArgs.Difficulty`).
- 검증: PIMC 결정성 · anytime · **오라클 불침범 회귀**.

### P2-C — 벤치 하니스 + DoD 측정
비동기 헤드리스 러너(Heavy 격리·반복수 캡) + `MatchRunner.agentFactory` 동기 대량 승률 + Wilson CI·미러드딜·사고시간 p95 리포트. **현 `AiAgent` 대비 Normal 우위 확인**(없으면 P2-B 튜닝 루프).

### P2-D — Hard 강화 (협력·추론)
reach-probability 가중 세계샘플(균등→베이지안: 콜/패스/교환 단서) + 팀보상 전협력(파트너=자기복제) + 폭탄타이밍 결정화 + **롤아웃에 차례밖 폭탄 점검 추가**(worlds≈16·예산 250ms) + GC 풀링 도입.
- **DoD 게이트**: Hard가 휴리스틱봇 대비 통계적 유의 승률 우위.

### P2-E — Expert + 체감강도 + 실기기
EPIMC 초반 ply 연기(strategy fusion 완화) + 블러프/노이즈 + 동점깨기 사람다운 편향. **실기기 60fps·1수 ≤300ms 게이트(D6 연계)** + 인간 플레이테스트로 티어 검증.

---

## 12. TDD 검증 계획

1. **[결정성·치트가드]** `Determinizer`: (state,seed) 고정 시 (a) 미지풀 = 56−관측−WonCards−History−개−나온마작 정확, (b) 상대3좌석 재분배가 각 `Hand.Count`와 일치, (c) 관측좌석 Hand·공개정보 불변, (d) 분배 결과를 `Apply`에 넣어도 Reject 없음, (e) 관측좌석 외 Hand가 원본과 달라짐(치트 아님 증명).
2. **[롤아웃 정확성]** 결정화 완전정보 세계 1개를 디폴트 정책으로 끝까지 롤아웃 → `ScoreRound` throw 없이 `RoundResult` 반환. 특수카드 포함 시드로 마작소원/용양도/봉황/개 경로 통과.
3. **[PIMC 결정성 회귀]** 고정노드수 모드: 같은 (state,seed,config) → 같은 선택수 100회 일치 + `ComputeHash` 재생 일치(`Simulator.ReplayRound` 패턴). `System.Random`/`DateTime` 미사용 정적검사(grep).
4. **[오라클 불침범]** 기존 동기==비동기 `ComputeHash` 5시드 테스트가 PIMC 도입 후에도 그린 유지.
5. **[합법성 불변]** `PimcAgent` 반환 수(리드/팔로우/폭탄/소원/용양도)가 `LegalMoves ∪ CanPass ∪ GiveDragon` 범위 내 → `Apply` Ok(랜덤시드 다수판 fuzz).
6. **[anytime]** 시간예산 모드: deadline 초과 시 OCE 없이 best-so-far 반환, ct 취소 시 즉시 중단. 예산취소 ↔ 폭탄인터럽트 취소 플래그 구분 검증.
7. **[벤치 유의성]** 헤드리스 러너로 Hard vs 현 `AiAgent` N판(미러드딜) → 팀점수차 평균 > 0 + Wilson 95% CI 하한 > 0.5. 반복수 캡·PlayMode 외부·비기본 카테고리로 MCP-stuck 회피.
8. **[티어 단조성]** 동일 딜 풀에서 Easy < Normal < Hard 승률 단조 증가.

> ⚠️ 모든 벤치/시뮬 테스트는 **비기본 카테고리 + 반복수 캡**. `run_tests` 클래스 필터로만, 전체 `Tichu.Core.Tests` 실행 금지. `run_tests` 전 PlayMode 정지.

---

## 13. 리스크 & 미해결

| 리스크 | 완화 |
|---|---|
| **GC 스파이크**(최대) — Clone 대량 할당 → 60fps 위반 | 세계당 1회 Clone·풀링·작게 시작(4×8)·P2-D 전 프로파일 |
| **치트(정보누출)** — Determinizer 버그로 진짜 상대손 참조 | 치트가드 단위테스트(§4.2) 필수 |
| **폭탄인터럽트 미모델 → 약함** | 1차 단순(턴좌석만)·Hard부터 충실. DoD 미달 시 우선 강화 |
| **균등 세계샘플 함정** — 교환/콜 정보누출 | Hard부터 reach-prob 가중 필수 |
| **strategy fusion** — 과콜·무모폭탄 | 자기대국 진단·Expert EPIMC |
| **체감강도 ≠ 객관강도** | MoveOrder 사람다운 동점깨기 + 인간 플레이테스트 |
| **봉황 합법수 폭증** | MoveOrder 정렬·상위 K 가지치기 |
| **벤치 메인스레드 점유 → MCP stuck** | 비기본 카테고리·반복수 캡·PlayMode 외부 |

### 결정 필요/보류
- 세계수·롤아웃수·시간예산 **구체 수치는 P2-C 벤치로 확정**(현재는 추정 시작값).
- UCT는 PIMC에선 불필요(투표 방식). ISMCTS 강화 시 C 상수는 보상 정규화 후 스윕(연구: 스케일 의존, 트릭게임은 낮은 탐색 선호).
- ⚠️ stale `core/` dotnet 트리(P1-B 이후 방치) — Phase 2는 건드리지 않음. 별도 cleanup PR로 삭제/명시 deprecate 권고(이번 범위 밖).

---

## 14. 참고 (핵심 출처)

- Long, Sturtevant, Buro, Furtak (AAAI 2010), *Understanding the Success of PIMC Sampling* — leaf correlation·bias·disambiguation, 트릭게임 PIMC 친화성.
- Cowling, Powley, Whitehouse (2012), *Information Set Monte Carlo Tree Search* — ISMCTS, strategy fusion, 균등샘플 가정.
- AI Factory (2013), *Reducing the burden of knowledge* — 상용 모바일 ISMCTS(<250ms, 휴리스틱봇 +12~14%), 파트너 공유 AI, 체감강도≠객관강도, 고정배열 노드풀.
- Rebstock, Solinas, Buro (CoG 2019), *Policy Based Inference in Trick-Taking Card Games* — reach-probability 세계 가중(Skat).
- Arjonilla, Cazenave et al. (IEEE CoG 2024), *EPIMC* — 초반 ply 결정화 연기로 strategy fusion 완화.
- Wyss(Univ. Bern), Müller(ETH) — Tichu RL은 amateur 수준 그침(슈퍼휴먼 아님, 탐색이 더 강함).
