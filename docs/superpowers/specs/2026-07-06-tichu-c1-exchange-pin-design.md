# C1 — 교환 카드 핀 (결정화 정확도) 설계

**날짜**: 2026-07-06 · **상태**: 승인 · **레버**: 로드맵 C1(믿음/결정화, S·"항상 옳고 변량 0")

## 문제

`Determinizer.Sample`(`Assets/_Project/GameFlow/Agents/Determinizer.cs:62-73`)은 미관측 풀을
**균등 셔플**해 상대 손패를 재구성한다. 그런데 관측자가 교환에서 넘긴 3장은 "어느 좌석에 있는지
정확히 아는" 공개 정보다. 균등 셔플은 이 정보를 버려 넘긴 카드를 **~2/3 확률로 엉뚱한 좌석**에 둔다.

특히 최근 채택한 #4(약패/파트너-티츄 콜 시 파트너에게 최고 카드 헌납, `AiAgent.ChooseExchange`)에서,
롤아웃이 그 최고 카드를 파트너 손이 아닌 상대 손에 두고 EV 를 계산 → 팀 강화 가치를 과소평가한다.
이는 세계수·롤아웃 천장과 무관한 **순수 믿음 오류**이며, 라이브 EV 오버라이드가 아니라 결정화
정확도 개선(#2 봉황 후보필터와 동류의 "무해" 범주)이다.

## 접근 — PimcAgent 로컬 기억 (A안)

관측자 좌석의 `PimcAgent` 는 **라운드당 좌석별 1개**로 유지되고(`PimcDecisionAgent.cs:28`),
`ChooseExchange` 도 이 객체가 처리한다(`PimcAgent.cs:249`). 따라서 GameState 를 건드릴 필요 없이
PimcAgent 로컬 필드에 "넘긴 3장 + 수령 좌석"을 캡처해 결정화 시점까지 운반한다.

기각한 대안:
- **B. GameState 영속화** — `RoundSetup` 은 Play 진입 시 null 화되므로 별도 필드 추가 필요 →
  Clone/ComputeHash 파급, Core 침습. 과함.
- **C. DecisionContext 스레딩** — 매 결정에 교환정보 주입 → 드라이버·IAgent 계약 광범위 변경. 과함.

스레드 안전: 캡처(ChooseExchange)는 교환 페이즈(메인스레드)에서, 사용(DecideTurn)은 플레이 페이즈
(스레드풀, `PimcDecisionAgent.cs:67`)에서 일어난다. 교환은 플레이 시작 전 전부 완료 → happens-before,
동시성 없음. 좌석별 인스턴스라 좌석 간 공유도 없음.

## 좌석 매핑

`ExchangeChoice{ToLeft, ToPartner, ToRight}`(`IAgent.cs:62-72`)의 수령 좌석은 giver `g` 기준
(`GameEngine.cs:617-627`):
- ToLeft → `(g+1)%4`
- ToPartner → `(g+2)%4` (= `Seating.Partner`)
- ToRight → `(g+3)%4`

관측자 `_seat` 에 대해 캡처: `[(ToLeft,(_seat+1)%4), (ToPartner,(_seat+2)%4), (ToRight,(_seat+3)%4)]`.

## 변경 지점 (전부 Unity 전용 — core 미러 없음, 동기화 불필요)

1. **`Determinizer.Sample`** — 선택 파라미터 추가:
   `Sample(GameState src, int observerSeat, ref Rng rng, IReadOnlyList<(Card card, int seat)> pinned = null)`.
   기본 `null` = 기존 동작 완전 불변(기존 `DeterminizerTests`·타 호출부 무영향).
   `pinned != null` 이면 셔플 전에: 각 핀 카드가 **풀에 남아있으면(=미플레이)** 수령 좌석 손패에 직접
   배치하고 풀에서 제거. 이후 잔여 풀만 셔플해 각 좌석의 **남은 슬롯**(hand.Count − 이미 핀된 수)에 분배.
   폐쇄 불변식(풀 크기 == 상대 손패 장수 합) 유지.

2. **`PimcAgent`** — 필드 `(Card,int)[] _passed`(기본 null) 추가.
   - `ChooseExchange`(`:249`): `_policy.ChooseExchange(ctx)` 결과를 좌석 매핑해 `_passed` 에 캡처 후 반환.
   - `DecideTurnAnytime` 2개 호출부(세계샘플 `:131`, 패스세계 `:181`): `_config.UseExchangePin ? _passed : null`
     을 `Sample` 에 전달.

3. **`PolicyConfig`** — 플래그 `UseExchangePin`(기본 false·비트불변) 추가. 프리셋은 벤치 통과 후 결정.

## 엣지

- 이미 플레이된 넘긴 카드 → `visible` 집합에 잡혀 풀에서 이미 제외 → 핀 시 풀에 없으므로 자동 무시.
- 특수 카드(개/봉황/용/마작)는 교환에서 안 넘김(`AiAgent.ChooseExchange` = 3 최저 비-특수) →
  개 보정 로직 무영향(핀은 개 보정 이후 수행).
- 좌석당 최대 1장 핀(좌/파트너/우 = 서로 다른 3좌석) → 슬롯 초과 배정 불가.
- `_passed == null`(교환 없는 상태에서 결정, 또는 플래그 OFF) → 기존 균등 분배로 폴백.

## 게이트

`PolicyConfig.UseExchangePin` 기본 false → OFF 경로는 `Sample(..., null)` 로 **비트불변**.
벤치 통과(≥중립) 후 별도 커밋에서 전 PIMC 티어 ON(결정화 정확도라 무해). 회귀 시 파킹(플래그 OFF·코드 보존).

## 테스트 계획

- **TDD 단위(`DeterminizerTests`)**:
  - 핀한 미플레이 카드가 지정 수령 좌석에 **100%** 안착(핀 없으면 확률적). 반복 시드로 결정성 확인.
  - 핀한 카드가 이미 플레이됨(풀에 없음) → 미충돌·정상 분배, 폐쇄 유지.
  - 핀 적용 후에도 56장 폐쇄·관측 손패 불변·치트 없음(기존 불변식 재검).
- **단위(`PimcAgentTests`)**: `ChooseExchange` 호출 후 `_passed` 캡처의 좌석 매핑 정확성(관측용 접근 방식은
  구현 시 결정 — public 헬퍼 또는 화이트박스).
- **격리벤치(`PimcBench.RunMirrored`, PIMC vs 휴리스틱)**: 동일 딜, `UseExchangePin` OFF vs ON.
  C1 은 **PIMC 결정화만** 개선(휴리스틱 상대는 결정화 안 함) → A1 류 대칭-wash 없이 깨끗이 측정.
  백그라운드 Task.Run + 파일 폴링. 회귀 없음(≥중립) → 채택.

## 성공 기준

1. 신규 `DeterminizerTests` 핀 케이스 그린 + 기존 Determinizer/PimcAgent 테스트 전부 그린(OFF 비트불변).
2. 격리벤치 `UseExchangePin` ON 이 OFF 대비 **회귀 없음**(margin/R ≥ OFF, 부호 안정).
3. 통과 시 전 PIMC 티어 ON, `main` 머지·origin 푸시.

## 범위 밖

- **D2 ε 정상화**(Expert ε=0 의 6× 중복 롤아웃 제거)는 C1 랜딩 후 후속 레버로 별도 처리.
- A1 리드 셰딩(고위험·wash), B1 α-μ(보류 OFF)는 이 스펙 범위 아님.
