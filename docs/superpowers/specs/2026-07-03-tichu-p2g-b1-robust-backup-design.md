# P2-G B1 — α-μ 강건 백업 (분산 페널티) 설계

**날짜**: 2026-07-03 · **상태**: 승인(downside=A 분산 페널티) · **레버**: 로드맵 최고 레버리지

## 문제

현 `PimcAgent.DecideTurnAnytime` 은 후보별 세계 EV 를 독립 평균 후 `argmax(mean)` 으로 고른다 —
이것이 **전략 융합(strategy fusion)** 그 자체다. "세 세계 +200 / 세 세계 −150"인 도박수가
평균 +25 로 뽑히지만, 실제 상대는 자기 패(=세계)를 알기에 −150 세계를 응징한다. 평균-EV 는
정보 은닉·혼합전략을 가치화 못 한다(P2-G 검증의 근본 원인 ①). 세계수(죽은 레버)로는 못 넘는다.

## 접근 — 가장 값싼 이식

선택 규칙만 `argmax(mean)` → **`argmax(mean − λ·std)`** 로 교체. 리프(결정화·롤아웃=휴리스틱+ε)·
후보 집합·파트너/D1 가드는 **불변**. 순수 C#·학습 불요.

**downside 측정 = A 분산 페널티**(승인): 세계 간 EV 표준편차. 누적은 합+제곱합 두 스칼라라 값싸고,
노이즈 롤아웃에 강건(worst 는 RNG 바닥을 쫓을 위험 → 배제). 로드맵의 "λ 감쇠" 취지와 정합.

## 데이터 구조 & 선택

`DecideTurnAnytime` 누적 루프에 후보별 **세계별 EV** 누적을 추가(플래그 ON 일 때만 할당):
- `robSum[i]`, `robSumSq[i]`: 각 세계의 EV(그 세계 롤아웃 평균)를 후보별로 합·제곱합.
- `worldsCounted`: 접힌 세계 수(부분 세계=완료 롤아웃>0 이면 포함).
- 기존 `weightedSum[i]` 는 그대로 유지(OFF 경로 + 패스 비교 스케일).

선택: `useRobust = UseRobustBackup && worldsCounted >= 2`
- ON: `bestIndex = RobustArgmax(robSum, robSumSq, worldsCounted, λ, candidates)` (동점깨기 `MoveOrder.Strength` 최소).
- OFF(또는 세계<2): 기존 `argmax(weightedSum)`.
- `bestSum = weightedSum[bestIndex]` → 패스 비교(passEv×totalWeight vs bestSum)는 **불변**.

## 순수 함수(테스트 대상, `PimcAgent` public static)

```
double RobustScore(double sum, double sumSq, int count, double lambda)
    = count<=0 ? -inf : mean - lambda*sqrt(max(0, sumSq/count - mean*mean)), mean=sum/count
int    RobustArgmax(double[] robSum, double[] robSumSq, int worldsCounted, double lambda, IReadOnlyList<Combination> candidates)
    = argmax RobustScore, tiebreak MoveOrder.Strength 최소
```

## 게이트

`PolicyConfig.UseRobustBackup`(기본 false·비트불변) + `RobustLambda`(기본 0). **어느 프리셋도 안 켬** —
벤치 통과 후 별도 결정(D1 과 동일 규율). ON+λ=0 이면 std 항 0 → mean 선택과 동일(비트근접).

## 테스트 계획

- **순수(Unity PimcAgentTests)**: `RobustScore` — 같은 평균·높은 분산 → 낮은 점수(λ>0). `RobustArgmax` —
  합성 배열로 고분산 X vs 저분산 Y 에서 λ 충분하면 Y 선택.
- **통합**: robust ON `FreshPlayState`(결정화 유효) 에서 결정적·합법. OFF 는 기존 Normal 테스트가 비트불변 보장.
- **벤치(게이트)**: self-play head-to-head **robust-ON vs 현 PIMC(OFF)**, 동일 딜 미러드, **λ 스윕
  {0.1, 0.25, 0.5, 1.0}**, **2배치 부호안정**(강도 주장이라 D1 비-회귀보다 엄격). 백그라운드 Task.Run+파일폴링.

## 성공 기준

self-play 벤치에서 어떤 λ 든 ON 이 OFF 대비 **2배치 부호안정 우세** → Hard 티어 정체성(알고리즘 축)으로 승격.
전 λ 에서 wash/회귀면 **OFF 유지·문서화**(리프 노이즈가 강건백업을 무력화 — 정직히 기록). 강도 주장은 격리벤치 필수.
```
