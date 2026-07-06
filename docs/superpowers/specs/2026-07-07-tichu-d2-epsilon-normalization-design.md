# D2 — ε·롤아웃수 정상화 설계

**날짜**: 2026-07-07 · **상태**: 구현·측정 완료 · **레버**: 로드맵 D2(라이브가드/효율, S·고신뢰·저리스크)

## 문제

`PolicyConfig.For(Expert)` = (Worlds 24, RolloutsPerWorld **6**, Epsilon **0.00**). ε=0이면 롤아웃 디폴트
정책이 **결정적**이다 — `HeuristicRolloutPolicy.DecideTurn`(`HeuristicRolloutPolicy.cs:27`)은 ε>0일 때만
RNG를 쓰고, 비-턴 결정은 전부 `AiAgent`에 위임한다. `AiAgent`의 유일한 RNG 사용은
`ChooseDragonRecipient`의 좌/우 코인플립 1곳(`AiAgent.cs:386`)뿐이다. 따라서 **ε=0에서 R회 롤아웃은
용 양도 좌우 타이가 걸리는 드문 라운드를 제외하면 전부 비트동일** — Expert는 후보·세계마다 6× 동일
롤아웃을 반복하며 ~6× 연산을 낭비한다. 인게임 anytime 예산(900ms) 내 완주 세계수가 줄어 "굶주린
Expert"(24세계 중 ~10만 완주)의 한 원인이다.

## 접근 — 유효 롤아웃 수 정규화 (플래그 없음)

순수 static 헬퍼 추출:
```
public static int EffectiveRollouts(double epsilon, int rolloutsPerWorld)
{
    int r = rolloutsPerWorld < 1 ? 1 : rolloutsPerWorld;
    return epsilon <= 0.0 ? 1 : r;   // ε≤0 → 결정적 → 1회로 정규화
}
```
`PimcAgent.DecideTurnAnytime`의 `rolloutsPerWorld` 산출을 이 헬퍼로 교체(`PimcAgent.cs:115`).

- **플래그 없음** — 순수 효율이라(LegalMoves −91% 선례) 게이트 불필요. ε>0이면 자동으로 원복(무영향).
- **Expert 프리셋 6 유지** — 헬퍼가 유효값을 1로 붕괴. `PolicyConfigTests` 프리셋 검증(6) 불변.
- 영향받는 티어=Expert만(유일한 ε=0·Worlds>0 프리셋). Easy(ε.25·Worlds0)·Normal/Hard(ε.05)·default(ε.10) 무영향.

## 측정 (Expert config, 동일 딜 PimcBench 12페어=24R, RolloutsPerWorld 6 vs 1)

| 구성 | margin/R | wins | 시간(24R) |
|---|---|---|---|
| 6-rollout (현 Expert) | 87.50 | 15/24 | 557s (~23s/R) |
| 1-rollout (붕괴) | 98.33 | 16/24 | 96s (~4s/R) |
| delta / speedup | **+10.83/R** (노이즈 내) | +1 | **5.80×** |

- **중립성**: delta +10.83/R은 24R 노이즈(~±40/R) 내, 승 동률 → 회귀 없음(예측된 near-비트동일; 차이는 드문 용양도 타이 라운드뿐).
- **페이오프**: **5.80× 가속** → 같은 900ms 예산에 ~5.8× 세계 완주 = 굶주린 Expert 직격.

## 테스트

- **TDD 순수(`PimcAgentTests`)**: `EffectiveRollouts` — ε=0→1(R=6,1)·ε>0→R 유지(.05→4·.25→3)·r<1 가드(→1).
- 기존 탐색 테스트 전부 그린(EditMode 50/50). Expert 결정은 near-비트동일이라 불변.

## 성공 기준

1. `EffectiveRollouts` 테스트 그린 + 회귀 없음(50/50). ✅
2. 격리측정: 6 vs 1 마진 중립(회귀 없음) + speedup ~6×. ✅ (+10.83/R 노이즈 내, 5.80×)
3. main 머지·origin 푸시.

## 범위 밖 / 후속

- **ε A/B(ε=0 vs 0.05 강도)**: 로드맵 D2 2차 목표. 별도 실험(현재는 Expert ε=0 유지).
- 워크플로 랭킹상 다음 레버는 전부 defer/skip(C3·C2·D3 wash 예측·A1 대칭리드 회귀·A3 음성). 재현버그/강한 근거 전까지 미착수.
