# Grand Tichu 콜 헤드 (B1) — 설계 스펙

- **날짜:** 2026-07-23
- **레버:** Phase 2 · AI 강화 · B1 (D4 스코핑의 "넷과 무관·직교한 가장 확실한 클린 윈")
- **상태:** 설계 승인됨(HTML 보고 `티츄_B1_Grand콜헤드_설계.html`). 구현 착수.

## 1. 목표

crude 휴리스틱 게이트 `HandPower(hand) >= 10`(용4·봉황3·개1·A2·K1 선형합)을,
학습된 **P(이 8장 손패가 라운드에서 먼저 나갈 확률)** 로 교체한다.
`P > τ` 이면 Grand Tichu 콜.

D4/Müller 근거: 라운드끝 보상 RL은 "티츄 절대 안 부름"으로 붕괴 → 별도의
"이 손이 먼저 나갈까?" 보정 헤드가 문서화된 롤아웃 콜 천장을 직격. Müller +~10점/R 보고.

## 2. 성공 기준 (DoD)

- **격리 벤치**: 미러드 self-play, A팀 플래그 ON vs B팀 OFF, 그 외 전부 동일, 학습과 분리된 시드.
- **채택**: `margin/R ≥ 0` **and** 승률 **Wilson LB > 0.5** **and** 명백한 회귀 없음 → 플래그 ON, 전 티어 채택.
- **파킹**: wash/음수 → 코드 보존, 플래그 OFF (기존 레버 관례 동일).
- 방향 참고치 = Müller +~10/R. **캐비앗**: self-play 라벨분포 ≠ 인간분포(Müller는 BSW 인간로그) — 격리 self-play 벤치로 판정하고 한계 명시.

## 3. 핵심 구조 발견

Normal/Hard/Expert **모든 PIMC 티어가 티츄 콜을 휴리스틱 `AiAgent`에 위임**한다:

```csharp
// PimcAgent.cs:303, :321
public bool CallGrandTichu(in DecisionContext ctx) => _policy.CallGrandTichu(ctx);
public bool CallTichu(in DecisionContext ctx)      => _policy.CallTichu(ctx);
```

→ 콜 헤드를 `AiAgent.CallGrandTichu` 한 곳에 꽂으면 전 티어가 혜택. 인-턴 EV 탐색과 **직교**
(과거 리드 변경들의 상습 회귀와 달리 EV 탐색 불침범).

Grand 결정 지점: `RoundPhase.GrandTichuDecision`, 좌석당 **8장** 손패 (`GameDriver.cs:60-65`).
라벨 소스: `RoundOutcome.State.Seats[seat].FinishOrder == 1` (먼저 나감=1). 라운드당 4행.

## 4. 아키텍처 — 5개 격리 유닛

| # | 유닛 | 위치 | 역할 |
|---|------|------|------|
| 1 | `GrandTichuFeatures` | 런타임(양 미러) | 순수함수 `Encode(hand8) → float[F]`. 데이터생성·추론 공유 → 스큐 0 |
| 2 | `CallNet` | 런타임(양 미러) | 베이크 가중치 + `Predict(x) → double`. matmul+sigmoid. 로지스틱이면 dot+sigmoid |
| 3 | `GrandTichuWeights.g.cs` | 런타임(양 미러) | 트레이너가 생성한 static 가중치 배열(체크인) |
| 4 | `AiAgent.CallGrandTichu` 통합 | 런타임(양 미러) | `PolicyConfig.UseGrandCallNet` 플래그. OFF=현행 비트동일 · ON=`Predict(Encode)>τ` |
| 5 | `CallNetTrainer` + `GrandCallHeadBench` | `core/tests` [Explicit] | 데이터생성·SGD 학습·가중치 방출 / 미러드 벤치·Wilson LB |

### 미러 규칙
- 런타임 유닛(1~4): `Assets\_Project\GameFlow\Agents\` + `core\src\Tichu.GameFlow\Agents\` **양쪽 동일**.
- 단위 테스트: `Assets\_Project\Tests\EditMode\` + `core\tests\Tichu.Core.Tests\` 양쪽.
- 오프라인 유닛(5): `core\tests\Tichu.Core.Tests\` 만 (`[Explicit]`, dotnet).

## 5. 피처 세트 (v0 초안, ~18, 8장 손패만 / hand-only)

`#A #K #Q #J #10` · `용/봉황/마작/개 보유(0/1)` · `#페어 #트리플 #포카드(폭탄)` ·
`최장 스트레이트 길이` · `랭크≥11 장수` · `랭크합(scaled)` · `고특수(용+봉황)수`.
전부 ~[0,1] 스케일. 상대 콜·좌석 순서는 v0 제외(YAGNI).

## 6. 모델 사다리

- **v0 = 로지스틱 회귀** (선형 + sigmoid). 해석가능(학습된 계수 ↔ HandPower 계수 직접 대조).
  벤치서 HandPower 이기면 **여기서 멈춤**(YAGNI).
- **v1 = 은닉층 1개(~16 ReLU)** — v0 언더핏일 때만. 벤치가 심판.

## 7. 임계값 τ

Grand EV ≈ `200·P − 200·(1−P)` > 0 ⟺ `P > 0.5`. 기본 τ=0.5.
벤치가 τ∈{0.45 … 0.65} 스윕(벤치 시드에서만) → 최적 선택·보고.

## 8. 데이터 위생 · 재현성

- 학습 시드 `[1..N]` / 벤치 시드 `[10_000_000..]` **분리**(누수 0).
- 가중치는 생성된 소스로 체크인(트레이너 재실행으로 재생성 가능).
- 새 asmdef 0 · 패키지 0 · 파이썬 0. 전부 `dotnet test --filter`.
- ⚠️ 전체 `Tichu.Core.Tests` 실행 금지(Sim 10만판 hang) → 항상 클래스/이름 필터.

## 9. 테스트 (TDD)

- **인코더**: 알려진 손패 → 기대 피처, 결정성, 데이터생성/추론 경로 패리티.
- **CallNet**: matmul 손계산 대조, sigmoid 경계, **OFF 비트동일성**.
- **통합**: CallGrandTichu ON/OFF 동작, 기본 OFF가 기존 테스트 보존.
- **Trainer/Bench**: `[Explicit]`(기본 스위트 제외, 장시간).

## 10. 범위 밖 (YAGNI) · 리스크

**안 하는 것**: Small Tichu 헤드(Grand 입증 후 같은 인프라로 후속) · 상대콜/좌석 피처 ·
Fork A 가치망 · ONNX/Sentis · 새 asmdef · 파이썬.

**리스크·완화**: 라벨분포 스큐→벤치 판정+캐비앗 · 콜 빈도 급변(±200)→4000+ 미러로 Wilson 조임 ·
과거 다수 개선이 회귀→엄격 게이트·기본 OFF.

## 11. 결정된 사항 (사용자 승인)

1. 모델 사다리: **로지스틱 v0 먼저**.
2. 피처: **§5 초안대로**.
3. 범위/기준: **Grand 먼저 · Wilson LB>0.5 · 순수 C#**.
