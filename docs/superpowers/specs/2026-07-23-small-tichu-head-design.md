# Small Tichu 콜 헤드 — 설계 스펙 (B1 미러 + Small 델타)

- **날짜:** 2026-07-23
- **레버:** Phase 2 · AI 강화 · 콜 헤드 확장 (B1 Grand 콜 헤드 `f0a483b`의 후속, 같은 인프라)
- **상태:** 설계 승인됨(brainstorming). 구현 착수.

## 1. 목표

`AiAgent.CallTichu` 의 **강도 게이트**(`(용/봉황 보유 + HandPower≥7) OR 폭탄`)를,
14장(교환 후) 손패로 학습한 로지스틱 헤드 `P(먼저 나갈 확률)>τ` 로 교체.
**컨텍스트 게이트(리드시점·상대아웃 없음·파트너콜 없음)와 폭탄 단축은 보존** — 룰/전략 제약이라 학습 대상 아님.

ON 경로: 컨텍스트 게이트 통과 → `return hasBomb || SmallTichuNet.Predict(hand14) > τ`.

## 2. 성공 기준 (DoD) — B1과 동일

격리 미러드 벤치(신 AiAgent ON vs OFF, 학습과 분리된 시드). 채택: 마진 통계적 유의(홀드아웃 95% CI 0 배제)
& 회귀 없음. B1 선례대로 승률(Wilson)은 고분산 레버 저평가 지표로 취급하되 홀드아웃 마진으로 판정.
Small Tichu = ±100(±200 아님) → 원칙 τ=0.5, 스윕.

## 3. B1과 공유(재사용)

`CallNet`(제네릭 로지스틱 추론) 그대로. 라벨 `FinishOrder==1`. 벤치 패턴(마진 CI·홀드아웃·τ스윕).
전 PIMC 티어가 `AiAgent.CallTichu` 단일 그라운드트루스에 위임(PimcAgent→HeuristicRolloutPolicy→AiAgent).
미러 규칙(런타임=core/src+Assets 바이트동일, 오프라인=core/tests만). 순수 C#/dotnet.

## 4. Small 델타 (신규/변경)

- **`SmallTichuFeatures.Encode(hand14)→float[16]`** — B1의 16피처 동일 의미, **14장 스케일**(pairs/7·triples/4·bombs/3·longest/14·highCount/14·rankSum/196; aces..tens/4·binaries·nHighSpecial/2 동일). 전용 클래스.
- **`SmallTichuWeights.g.cs`** — 트레이너 생성(별도 가중치).
- **`AiAgent.CallTichu` 통합** — `useSmallTichuNet`/`smallThreshold` 생성자(B1과 병렬). ON: 강도게이트→`hasBomb || CallNet.Small.Predict(Encode(hand))>τ`. `PolicyConfig.UseSmallTichuNet`(Assets전용) + HeuristicRolloutPolicy·PimcAgent 배선.
- **데이터생성(B1보다 한 단계 복잡)** — Small은 교환 후 14장이라, Grand(×4)+Exchange(×4)를 GameEngine.Apply로 수동 드라이브 → **Play 진입 시점(4좌석 14장·플레이 전) 스냅샷** → GameDriver.RunRound로 완주 → 라벨. 트레이너에 setup 스테핑 헬퍼.
- **벤치** `SmallCallHeadBench` — `useSmallTichuNet` 토글 미러드 + 홀드아웃.

## 5. 테스트 (TDD, B1과 동일 구조)

인코더(알려진 14장→기대 피처·결정성), CallNet(B1 재사용·Small 가중치 길이), 통합(OFF 비트동일·ON 라우팅·컨텍스트게이트 보존·폭탄단축), 트레이너 캡처(14장·라운드당 한 승자), 벤치([Explicit]).

## 6. 범위 밖 (YAGNI)

14장 리치 피처(콤보/스트레이트≥5 등) 추가 · 컨텍스트 게이트 학습화 · Grand 인코더 재사용.

## 7. 승인된 결정

① 강도게이트만 교체(컨텍스트·폭탄 보존) · ② 전용 SmallTichuFeatures(14 스케일) · ③ 스테핑 데이터생성.
