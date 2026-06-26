# Tichu — 기본 AI 강화 (검증 후 승격) 설계 스펙

- 작성일: 2026-06-26
- 상태: 설계 승인됨(브레인스토밍 종료) → 구현계획(writing-plans) 대기
- 배경: Phase 2(PIMC) P2-A~P2-E 완료. 사용자 플레이테스트에서 기본 난이도(Normal=4세계)가 "나쁜 수"(K부터 리드, 저-싱글 전쟁)를 둠 → 지난 세션 진단 = **4세계 EV 노이즈**.
- 결정: ① 목표 = **기본 AI 강화**(티어 이름/구조는 부차) ② 방법 = **세계수↑**, 단 격리벤치로 **검증 후 승격** ③ 지연 = **딜레이·연산 겹치기**

> ⚠️ 본 문서의 `file:line` 참조는 작성 시점 기준이다. 각 단계 구현 전 해당 파일을 실제로 확인한다.

---

## 0. 요약 (TL;DR)

게임에는 난이도 선택 UI가 없어 **항상 기본값(Normal)으로 실행**된다. 따라서 "플레이어가 만나는 AI"는 곧 `Normal`(4세계×2롤아웃, ε0.10). 사용자가 본 약한 수는 **4세계 EV 노이즈**로 진단됐고, 정공법은 **세계수↑**다(지난 세션 결론, 미검증).

이번 작업은 *데이터 없이 결정해 손해 본 전례*(미들리드 −20/R, 리드게이트 −15/R, reach-prob 무효)를 반복하지 않기 위해 **검증 후 승격**한다:

1. **P1 — 검증**: 세계수별 후보 프리셋을 격리벤치로 돌려 `세계수→강도`·`세계수→지연(ms/수)` 두 곡선을 얻는다.
2. **P2 — 인에이블러**: `PimcDecisionAgent`에서 관전 딜레이와 탐색 연산을 **겹쳐서**(동시 실행) 돌려, 딜레이 이내 연산은 추가 지연 0으로 만든다.
3. **P3 — 승격**: 검증으로 정해진 우승 설정을 `Normal` 기본값으로 re-point. 강도 향상이 없으면(킬 브랜치) 승격하지 않고 정직 보고.

**범위 메모**: `AiAgent` 미수정 → core/Assets 미러 불필요. 변경은 전부 `Assets/_Project`의 Tests·GameFlow·Presentation만.

---

## 1. 배경 & 현 상태

### 1.1 난이도 = PolicyConfig 주입 (별도 룰 없음)
`Assets/_Project/GameFlow/Agents/PolicyConfig.cs` `For(Difficulty)`:

| 티어 | Worlds×Rollouts | ε | UseCallerAggression | 예산(ms/수, `BudgetMsFor`) |
|---|---|---|---|---|
| Easy | 0 (탐색 OFF) | 0.25 | false | 0 |
| **Normal (현 기본)** | **4×2** | 0.10 | **true** | 80 |
| Hard | 16×4 | 0.05 | false | 250 |
| Expert | 24×6 | 0.00 | false | 300 |

- 기본값: `GameLaunchArgs.Difficulty = Difficulty.Normal` (`Presentation/Shell/GameLaunchArgs.cs:25`). 난이도 선택 UI 미구현.
- 배선: `RoundBootstrap.cs:78` `PolicyConfig.For(_args.Difficulty)` + `:118` `BudgetMsFor`.
- ⚠️ **caller-aggression(+22/R)은 Normal에만 ON**, Hard/Expert엔 OFF. 승격 시 이 플래그를 반드시 가져가야 한다(안 하면 +22/R 손실).

### 1.2 지연 구조 — 딜레이·연산은 현재 **순차**
`Presentation/Agents/PimcDecisionAgent.cs:45` `DecideTurnAsync`:
```
await DelayAsync(ct);                 // 관전 딜레이 (기본 900ms, fast-forward시 0)
... snap = ctx.State.Clone();
await UniTask.RunOnThreadPool(() => _pimc.DecideTurnAnytime(..., budget, ct));  // 그 다음 연산
```
→ per-move ≈ **딜레이 + 연산**. 예산을 키우면 체감 지연이 그대로 더해진다. (16세계 ≈ 400ms/수 dotnet, 모바일 Mono ~2.5–4× → 수당 최대 ~2.4s까지 갈 수 있음.)

### 1.3 검증 자산
- `Tests/EditMode/PimcBench.cs` `RunMirrored(pairs, baseSeed, pimcConfig)`: PIMC(config) vs 휴리스틱 `AiAgent` 미러드딜. 순수 C# → 백그라운드 Task.Run 가능.
- `Tests/EditMode/BenchStats.cs` `WilsonLowerBound`.
- `Tests/EditMode/PimcBenchTests.cs` `[Explicit]+[Category("Bench")]` 패턴.

---

## 2. 가설 & 성공 기준

- **가설**: 세계수↑ → EV 추정 노이즈↓ → argmax가 더 나은 lead/follow 수 선택 → 강도(승률·점수차)↑.
- **성공(승격) 기준**: 후보 설정이 현 Normal `(4,2,.10,caller)` 대비 **유의한 강도 향상**(미러드 페어링, 라운드당 점수차 양수 + Wilson 하한 > 0.5, 충분 N) AND 그 설정이 **지연 전략(가) 하에서 실현가능**(아래 §4).
- **킬 기준**: 세계수를 16/24로 올려도 향상이 노이즈 범위 내 → **가설 기각**. 승격 안 함, 재검토(원인이 세계수 아닌 다른 곳일 가능성).

---

## 3. P1 — 검증 (데이터 수집)

### 3.1 강도 스윕 (기존 하니스 재사용, 신규 코드 0)
동일 `baseSeed`로 `RunMirrored`를 후보 프리셋마다 호출 → 각 설정이 **같은 딜·같은 휴리스틱 상대**를 상대. 딜 단위 페어링으로 딜 운 상쇄.

후보(전부 `useCallerAggression: true`로 통일 — 세계수만 변수로 격리, +22/R 보존):

| 라벨 | Worlds | Rollouts | ε | 비고 |
|---|---|---|---|---|
| `today` | 4 | 2 | 0.10 | 현 Normal = 기준선 |
| `cand8` | 8 | 4 | 0.05 | 중간 |
| `cand16` | 16 | 4 | 0.05 | = 현 Hard 두뇌 + caller |
| `cand24` | 24 | 6 | 0.00 | = 현 Expert 두뇌 + caller |

> 숫자는 시작값(현 티어 프리셋 추종). writing-plans/실측서 조정 가능. 1D(세계수) 우선 — ε·rollouts 별도 튜닝은 후속.

- 비교: 각 설정의 `평균 점수차`·`승률(Wilson 하한)`. `cand16 − today` 등 페어 차이로 단조성 판정.
- 신호 애매(노이즈 범위)면 **그때만** PIMC-vs-PIMC 직접대결 `RunDuel(pairs, baseSeed, cfgA, cfgB)`를 소량 추가(`RunMirrored`의 최소 일반화: 양 팀에 서로 다른 PIMC config 주입). **투기적으로 미리 만들지 않는다.**

### 3.2 지연 측정 (신규: 타이밍 데코레이터 1개)
`PimcAgent`(IAgent)를 감싸 **호출수·누적 Stopwatch**를 재는 데코레이터 `TimingAgent : IAgent`를 벤치에 끼워 세계수별 **ms/수**(평균·최악)를 얻는다.
- TDD: 고정시드에서 호출수·위임 결과가 래핑 전과 동일(결정성), 누적시간 ≥ 0.
- dotnet 값 × 알려진 Mono ~2.5–4× → 모바일 추정.

### 3.3 실행 방식
스윕 테스트는 `[Explicit]+[Category("Bench")]` (기본 스위트 제외, MCP-stuck 회피). 실제 실행은 **백그라운드 Task.Run + 파일 결과 폴링**(메모리 기록 워크플로). ⚠️ 도메인 리로드시 ThreadAbort 가능 → 재실행.

### 3.4 산출
`세계수→강도` + `세계수→ms/수` 두 곡선. HTML 리포트(`티츄_기본AI강화_검증.html`, 루트). → **§5 결정 게이트**.

---

## 4. P2 — 인에이블러 (딜레이·연산 겹치기)

`PimcDecisionAgent.DecideTurnAsync`를 순차→**겹치기**로:
- 관전 딜레이 태스크와 스레드풀 탐색 태스크를 **동시 시작**, 둘 다 await → per-move = `max(딜레이, 연산)`. 딜레이(~900ms) 이내 연산은 **추가 지연 0**.
- 예산(`budgetMs`)은 딜레이 수준(~900ms)으로 상향 → 기기가 빠르면 목표 세계수 완주, 느리면 anytime이 그만큼만(**우아한 degrade**).
- **fast-forward**: 딜레이 스킵 시 탐색도 짧은 예산으로 축소(관전 스킵=빠른 진행 의도 유지). 정확한 축소값은 구현계획서.
- **결정성/스레드 안전 불변**: `snap = Clone()` 후 진입(기존), Unity API 비접촉(기존). 토큰 None 경로(테스트)·폭탄 인터럽트(외부 ct) 동작 유지.
- 범위: `Presentation`만. TDD(`PimcDecisionAgentTests` 확장 — 겹침 시 per-move ≈ max, fast-forward 축소, 취소 전파).

> ⚠️ P2는 P1 결과와 독립적으로 유익(어느 세계수든 지연 절감). P1 데이터가 "세계수 무효"여도 P2는 별도 가치 판단(작은 개선이라 보류 가능). 순서상 P3 전에 둔다.

---

## 5. 결정 게이트 & P3 — 승격

P1 산출 곡선으로:
- **단조 증가 + 지연 OK** → 가장 강한 *실현가능* 세계수를 기본값으로(예: 16; 24가 16 대비 유의 향상 + 지연 OK면 24).
- **8세계서 포화** → 8세계(싸게).
- **유의차 없음(킬)** → 승격 안 함. 정직 보고 후 재검토.

승격(수술적):
1. `PolicyConfig.For(Normal)` → 우승 `(Worlds, Rollouts, ε, useReachProb:false, useCallerAggression:true)`로 re-point.
2. `BudgetMsFor(Normal)` → 딜레이(~900ms) 수준 상향(P2 겹치기 전제).
3. Normal=Hard로 중복되면 **티어 정리**(최소 변경): Hard를 Normal에 흡수하거나 상위로 재배치. enum 삭제는 데이터 확정 후 최소로(사용자 확인).
4. 테스트 갱신: `PolicyConfigTests`(프리셋 값), `PimcAgentTests`(불변), EditMode 그린. **오라클 불가침**.

> caller-aggression은 4세계서 측정된 +22/R이라 새 세계수에서의 효과는 미검증. **보존이 기본**(회귀 위험 회피); 새 세계수에서의 재검(WITH/WITHOUT)은 선택적 후속.

---

## 6. 테스트 & 검증

- **TDD**: 신규 `TimingAgent`(결정성), (필요시)`RunDuel`(미러 대칭성·결정성), `PimcDecisionAgent` 겹치기.
- **회귀**: EditMode PolicyConfig/PimcAgent/PimcDecisionAgent 그린. 오라클(AsyncGameDriver 등) 불가침. ⚠️ **전체 `Tichu.Core.Tests` 실행 금지**(Sim 10만판 → MCP-stuck) → `run_tests(test_names=[...])` 클래스 필터, run_tests 전 PlayMode 정지.
- **벤치**: 강도/지연 스윕은 `[Explicit]` 백그라운드 Task.Run + 파일 폴링.
- **수동 인게임**: 승격 후 한 판 — 기본 AI가 체감상 더 나은 수(K부터 리드/저-싱글 전쟁 감소)인지 육안 확인. (최종 강도 진실은 별도 D6 실기기 게이트.)

---

## 7. 리스크 & 미해결

- **벤치는 예산 무관(uncapped)** → "이상 강도" 측정. 인게임 실현 강도는 기기·예산 의존. 완화: §3.2 지연 측정 + §4 겹치기 + 생성 예산을 딜레이 수준으로. 최종 진실 = 실기기.
- **PIMC 벤치 백그라운드 Task.Run의 ThreadAbort**(도메인 리로드) → 재실행.
- **caller-aggression 세계수 의존성**: 보존이 기본, 재검은 후속.
- **킬 브랜치**: 향상 없으면 승격 안 함 — 이 경우 체감 약점의 진짜 원인(세계수 외)을 별도 진단.

---

## 8. 변경 파일 (예상)

| 파일 | 변경 | 페이즈 |
|---|---|---|
| `Tests/EditMode/PimcBench.cs` | (선택) `RunDuel` 추가 | P1 |
| `Tests/EditMode/TimingAgent.cs` | 신규 — 타이밍 데코레이터 | P1 |
| `Tests/EditMode/PimcBenchTests.cs` | 스윕 `[Explicit]` 테스트 추가 | P1 |
| `Presentation/Agents/PimcDecisionAgent.cs` | 딜레이·연산 겹치기 | P2 |
| `Presentation/Tests/PimcDecisionAgentTests.cs` | 겹치기 테스트 | P2 |
| `GameFlow/Agents/PolicyConfig.cs` | `For(Normal)` re-point (+티어 정리) | P3 |
| `Presentation/RoundBootstrap.cs` | `BudgetMsFor(Normal)` 상향 | P3 |
| `Tests/EditMode/PolicyConfigTests.cs` | 프리셋 값 갱신 | P3 |

신규 asmdef 0. `AiAgent`·`core/` 무수정.
