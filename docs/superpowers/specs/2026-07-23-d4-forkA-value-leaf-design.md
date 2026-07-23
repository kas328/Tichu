# D4 Fork A — 리프 가치망 (① 오프라인 리프 A/B) 설계 스펙

- **날짜:** 2026-07-23
- **레버:** Phase 2 · D4 Fork A (D4 스코핑의 "구조적 천장 돌파" 첫 단계, 디리스크 사다리 ①)
- **상태:** 설계 승인됨(brainstorming). 시임부터 착수. **다세션 연구.**

## 1. 목표

PIMC 롤아웃 리프(휴리스틱 완주 플레이 → 점수차)를, 학습 가치망 `V(world, seat) → EV` 로 교체.
이득 = **탈노이즈**(롤아웃 노이즈 표본 대신 기대값) + **속도**(~1000× → 같은 예산에 세계수↑).
디리스크 ①: 오프라인 리프 A/B로 "V 리프가 롤아웃 리프를 이기나?" 를 벤치로 판정. 통과 시에만 ②③.

## 2. 성공 기준 (DoD, ①)

`PimcBench.RunMirrored` 에서 V-리프 PIMC vs 롤아웃-리프 PIMC, **Wilson LB > 0.5** & 회귀 없음
→ 게이트 통과(②오라클 탐침으로). 실패(wash/음수) → 리프가 병목 아님 → 중단·파킹.
측정 2가지: (a) 동일 세계수(탈노이즈 격리) (b) 동일 예산(더-세계수 이득).

## 3. 핵심 구조 사실

- **시임**: `Pimc.Rollout(world, seat, seed, ε) → 관측팀 점수차`. 호출 2곳: `PimcAgent.cs:182`(후보별)·`:222`(패스).
- ⚠️ **PIMC 레이어(Pimc·PimcAgent·PolicyConfig·HeuristicRolloutPolicy·Determinizer)는 Unity 전용**(core 미러 없음). D4 Fork A 전체가 Unity(Assets)에서 진행. 벤치=`PimcBench.RunMirrored`(Unity EditMode). ⚠️PIMC 벤치 ~8s/R(느림).

## 4. 아키텍처 (① 조각)

| # | 조각 | 위치 | 역할 |
|---|------|------|------|
| 1 | `IWorldEvaluator.Evaluate(GameState, int seat, ulong seed) → double` | Assets GameFlow/Agents | 리프 평가 추상화 |
| 1 | `RolloutEvaluator : IWorldEvaluator` | Assets | `Pimc.Rollout` 래핑(현행·폴백·라벨생성기). seed 사용 |
| 1 | `ValueNetEvaluator : IWorldEvaluator` | Assets | `V(Encode(world,seat))`. seed 무시(결정적) |
| 1 | `PolicyConfig.UseValueNetLeaf` 플래그 | Assets | OFF=RolloutEvaluator(비트불변) |
| 2 | `WorldFeatures.Encode(world, seat) → float[F]` | Assets | 4좌석 집계 + 글로벌, ~40–120 float(하이브리드) |
| 3 | `ValueNet` MLP(F→은닉 H→1스칼라 EV) | Assets | 손코딩 matmul, CallNet 확장. 회귀 |
| 4 | 데이터생성 (world, 롤아웃값) | Assets 테스트 [Explicit] | self-play 결정화 + Pimc.Rollout 라벨 |
| 5 | 회귀 학습(MSE, C# SGD) | Assets 테스트 [Explicit] | 트레이너 회귀 일반화 |
| 6 | `PimcBench.RunMirrored` A/B | Assets 테스트 [Explicit] | Wilson 게이트 |

### 시임 상세(비트불변)
PimcAgent 가 `IWorldEvaluator _evaluator` 보유(생성자에서 config 로 선택:
`UseValueNetLeaf ? ValueNetEvaluator : new RolloutEvaluator(config.Epsilon)`).
`:182` → `_evaluator.Evaluate(sim, _seat, rolloutSeed)`, `:222` → `_evaluator.Evaluate(passSim, _seat, policyBase) * totalWeight`.
RolloutEvaluator.Evaluate = `(double)Pimc.Rollout(world, seat, seed, _epsilon)` → OFF 비트불변.
V 경로: 결정적이라 rolloutsPerWorld 반복 무의미 → `EffectiveRollouts` 유사 가드로 1회 붕괴(D2 선례) 또는 후속.

## 5. 인코더 (하이브리드, 승인)

4좌석 각각: 손 강도 집계(고카드·특수·장수·아웃여부·콜여부) + 글로벌(현재 트릭 top·누적점수·턴·소원).
콜헤드 인코더 패턴 재사용. ~40–120 float. 관측좌석 상대화(seat 기준 회전). 정보 압축이라 이득 상한 낮음(후속서 확장 여지).

## 6. 열린 결정 (다음 조각 착수 시)

- **데이터생성/벤치 툴체인**: Unity EditMode [Explicit](느림·MCP) vs PIMC 레이어 core 미러링(빠른 dotnet·큰 선투자). ①은 Unity로 시작, 반복 비용 크면 미러링 재고.
- 모델 크기(은닉 H)·데이터 규모·학습 하이퍼는 데이터 나오면 튜닝.

## 7. 정직한 기대 · 리스크

이득 **modest**(탈노이즈+세계수↑, 원리상 우월하나 작음). **휴리스틱 라벨 → V가 휴리스틱 복제 → 천장=휴리스틱**,
전략융합 못 고침. 벤치가 심판(사람눈·이론 아님). PIMC 벤치 느림 → 반복 비용 큼. Fork B/오라클은 ① 게이트 뒤.

## 8. 첫 착수 = 시임(조각 1)

IWorldEvaluator + RolloutEvaluator + UseValueNetLeaf + 2 호출부 교체 + ValueNetEvaluator 스텁.
비트불변 검증 = 기존 PimcAgentTests 그린 유지. 이후 인코더→넷→데이터→학습→벤치 순(다세션).

## 9. 결과 — 게이트 불통과·파킹 (2026-07-23 완료)

**전 조각 구현·테스트 완료**(시임 비트불변 49/49, WorldFeatures 4/4, ValueNet 4/4, 트레이너 캡처 2/2).
학습: valRmse **131.75 vs 기저 153.36 = 14.1%↓**(분산 ~26% 설명, modest).
**A/B(ValueLeafBench, Normal 미러드 60R): V-리프 −31.67/R·승률 45.0%·WilsonLB 0.331** → 롤아웃-리프에 **열세**, 게이트(V가 롤아웃을 이겨야) **불통과**.

**근본 이유**: ε=0 롤아웃은 정확 → V(근사, RMSE 131)는 동일세계수서 못 이김. Normal(ε=0.05)서도 근사오차+ε0편향이
탈노이즈 이득 압도. 집계 인코더(42) 압축이 분산 26% 뿌리. 속도 레버(세계수↑)는 anytime-예산 벤치 필요하나
PIMC Unity 전용·~22s/R 툴체인 블로커.

**판정=파킹**(전 티어 OFF·비트불변·코드 보존). 사다리 원칙("실패=리프 병목 아님→중단") 준수 → ②오라클·③증류 미투자.
재방문 레버(모두 큰 투자, 보류): 오라클 라벨(천장↑)·원시 멀티핫 인코더(분산↑)·PIMC 코어미러링(빠른 속도-레버 벤치).
리포트 `티츄_D4_ForkA_벤치결과.html`. **파킹 & 머지 승인**(main --no-ff, 재사용 자산 보존).
