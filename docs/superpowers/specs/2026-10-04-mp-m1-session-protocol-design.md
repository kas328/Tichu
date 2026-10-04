# 멀티플레이 M1 — 프로토콜 · 권위 세션 · 로컬 루프백 (설계 스펙)

- **날짜:** 2026-10-04
- **트랙:** Phase 3 멀티플레이(친구방). 전체 분해 M1~M6 중 첫 조각.
- **상태:** 설계 승인됨(brainstorming). 구현 착수.
- **배경:** 기획서 7장(전송 무관 논리 프로토콜 — 그대로 채택) · 9장(2026-06 Photon 확정안 — **본 스펙이 재검토해 대체**) · `docs/reports/design/티츄_멀티플레이_친구방_아키텍처.html`

## 1. 확정된 상위 결정

- **1차 범위 = 친구방 한 종류.** 초대 코드로 모이고 빈자리는 AI로 채워 4인 보장. 랭킹전·계정·MMR·매치메이킹은 범위 밖.
- **권위 = 전용 .NET 서버**(`core/`를 그대로 실행). 호스트 모델(Photon Fusion 2 / Supabase Realtime 릴레이)은 기각 — ⓐ 모바일 백그라운딩으로 호스트가 끊기면 네 명의 매치가 죽고, 손패를 숨긴 상태로는 호스트 마이그레이션이 성립하지 않는다, ⓑ 상대 손패가 한 플레이어 기기에 실재한다(기획서 7.5 위반), ⓒ 랭킹전으로 가면 어차피 전용 서버가 필요하다.
- **Supabase = M6 메타 백엔드**(Auth · 프로필 · 랭킹 · 매치 히스토리). 권위 세션 호스트로는 쓸 수 없다 — Edge Functions는 Deno 런타임의 상태 없는 핸들러(단일 실행 2초 이내 권장)이고, 거기서 권위를 돌리려면 룰 엔진을 TypeScript로 재구현(진실이 둘이 되어 코어↔유니티 해시 동기 불변식을 파괴)하거나 C#을 WASM으로 컴파일해야 한다. 서버가 Supabase 발급 JWT를 검증하는 구성으로 M6에서 합류한다.
- **세션 드라이버 = 기존 동기 `GameDriver` + 블로킹 `RemoteAgent`, 매치당 스레드 1개.** 새 드라이버를 만들지 않는다 — 저장소에서 가장 많이 검증된 경로를 그대로 쓴다. 파킹된 대안: `AsyncGameDriver`를 `Task` 기반으로 core 이식(세 번째 드라이버), Presentation 비동기 드라이버를 core로 통일(이미 플레이테스트된 경로를 침범).

## 2. 목표 (M1)

멀티플레이가 들어갈 자리를 `core/` 안에 만든다. **네트워크 0줄 · 서버 0대 · Unity 변경 0줄.** 순수 추가라 기존 기능에 위험이 없다.

지금 돌아가는 싱글플레이는 **재배선하지 않는다.** D시리즈 손맛·애니메이션이 `onApply`에 걸려 있어 테이블 뷰를 투영 뷰로 갈아타는 작업은 M4에 속한다.

## 3. 성공 기준 (DoD)

1. **오라클** — AI 4좌석을 `MatchSession` 경로로 돌린 라운드가 `GameDriver` 직접 실행과 `GameState.ComputeHash()` 및 액션 로그까지 동일하다(복수 시드).
2. **마스킹 불변식** — 어떤 상태·어떤 viewer에 대해서도 `PlayerView` 안에 타 좌석의 손패 카드가 존재하지 않는다. 가린 ID도 넣지 않는다(기획서 7.5: 패킷 스니핑 노출 방지).
3. **루프백 완주** — 스크립트 인간 2 + AI 2로 매치 1건을 목표 점수까지 완주하고, 적용된 모든 액션이 엔진 검증을 통과한다.
4. **회귀 0** — 기존 core 테스트와 Unity EditMode 전부 그린 + 미러 동기 검사 통과.

## 4. 신규 어셈블리 `Tichu.Net`

정본 `core/src/Tichu.Net/` → 미러 `Assets/_Project/Net/`.
`Tichu.Net.asmdef`(`noEngineReferences: true`, 참조 = `Tichu.Core` · `Tichu.GameFlow`).

배선 변경 3곳: `core/Tichu.sln`에 프로젝트 추가 · `Tichu.Core.Tests`가 참조 · `tools/sync-core-to-unity.ps1` 매핑 1개와 `.github/workflows/core-tests.yml` 미러 쌍 1개 추가.

| 파일 | 내용 |
|---|---|
| `Protocol/ClientAction.cs` | C→S. 기존 `GameAction` 7종과 1:1 + `Seat` · `ActionSeq`. M1 범위는 **필드 정의 + 좌석별 단조 증가 검증**(중복·역행은 `ActionRejected`)까지이고, 중복 수신 시 현재 `StateUpdate` 재전송은 M2(실제 전송)에서 |
| `Protocol/ServerEvent.cs` | S→C. `StateUpdate(PlayerView, Awaiting)` · `ActionRejected(reason)` — 발신자에게만, 브로드캐스트 금지(정보 노출 방지) · `RoundScored(RoundResult)` · `MatchEnded` |
| `PlayerView.cs` | 좌석별 값 스냅샷. 아래 §5 |
| `ViewProjector.cs` | `ProjectFor(GameState, int viewer) → PlayerView`. 깊은 복사 |
| `RemoteAgent.cs` | `IAgent` 구현 + 스레드 안전 메일박스. 아래 §7 |
| `MatchSession.cs` | 권위 세션. 아래 §8 |
| `ITichuTransport.cs` · `LoopbackTransport.cs` | 전송 이음새와 동일 프로세스 구현 |

## 5. `PlayerView` 필드 (기획서 7.5 표에 1:1 대응)

- **자기만:** `MyHand`(카드 전체)
- **전 좌석 공개:** `HandCounts[4]` · `Calls[4]` · `IsOut[4]` · `FinishOrder[4]` · `WonCards[4]`(이미 공개된 카드라 전체 — 카드 카운팅 UI 근거)
- **전체 공개:** `Phase` · `Turn` · `Wish` · `PendingDragonGiftWinner` · `Scores`(TeamA · TeamB · TargetScore) · `CurrentTrick`(`Top` · `LeadType` · `LeadLength` · `TopOwnerSeat` · `AccumulatedPoints` · `History`) · `CompletedTricks` 요약(`TopOwnerSeat` · `AccumulatedPoints` · `WonByDragon` · `DragonGiftRecipient`)
- **절대 미포함:** 타 좌석 `Hand` · 미배분 덱(`Setup.Undealt`)
- `Awaiting { FlowQuery.StepKind Kind, int Seat, DateTime? DeadlineUtc }` — 기존 `StepKind`(GrandTichu·Exchange·Play·DragonGift)를 그대로 재사용한다. 폭탄·작은 티츄는 대기 대상이 아니라 예약이므로 종류가 필요 없다. `DeadlineUtc`는 M1에서 항상 null(M5가 채운다).

`PlayerView`는 `GameState`를 참조로 들고 있지 않다 — 루프백에서 참조 공유로 마스킹이 새는 걸 막는다.

## 6. `GameDriver` 변경 (추가만, 동작 불변)

`AsyncGameDriver`가 이미 가진 것과 **같은 모양**의 선택적 훅 3개를 `RunRound`에 선택 인자로 더한다. 기본값 `null` → 기존 호출부 무변경, 기존 오라클이 비트 동일성을 계속 보증한다.

- `onApply(GameState, GameAction)` — 매 적용 직후. 세션의 브로드캐스트 지점.
- `takePendingTichu(GameState) → int` — 예약된 작은 티츄 좌석(-1 = 없음).
- `takePendingBomb(GameState) → GameAction` — 예약된 차례밖 폭탄(null = 없음). 거부되면 throw 하지 않고 조용히 버린다(인간 입력이므로).

폭탄을 예약 경로로 두는 이유: 드라이버의 폭탄 창은 좌석을 순회하며 각자에게 묻는데, 사람에게 이걸 동기로 물으면 **매 트릭마다 전원이 대기한다.** 예약은 기획서 7.4의 "동시 도착 폭탄은 수신 순서로 직렬화"와 그대로 맞는다.

## 7. `RemoteAgent`

- 좌석별 메일박스. `Submit(ClientAction)`은 전송 스레드에서 호출되며 스레드 안전하다.
- **버퍼링** — 드라이버가 묻기 전에 도착한 액션도 보관한다. 덕분에 교환 4인 동시 제출이 자연히 동작한다(드라이버는 좌석 순서로 소비).
- **종류 불일치는 버리지 않고 보관한다** — 기대하는 `StepKind`와 다른 액션은 선행 큐에 남긴다(위 버퍼링과 같은 메커니즘). 거부는 **좌석 불일치 · `ActionSeq` 중복/역행 · 내용 불법**(손에 없는 카드, 모호한 봉황 배치, 불가능한 패스, 상대가 아닌 용 양도 대상)에만 보내며, 상태를 바꾸지 않고 발신자에게만 간다. ⚠️ 협조하지 않는 클라이언트가 선행 큐를 무한히 키울 수 있다 — 레이트리밋은 실제 전송이 붙는 M2 몫이다(기획서 7.7도 레이트리밋을 그 층의 항목으로 둔다).
- `DecideTurn` · `ChooseExchange` · `CallGrandTichu` · `ChooseDragonRecipient` — 해당 종류가 올 때까지 `Wait(timeout)`.
- `DecideBomb` — **항상 즉시 null.** 폭탄은 예약 경로로만 들어온다.
- `CallTichu` — 예약 플래그를 즉시 반환.
- **타임아웃** — M1의 정책은 **세션 실패 종료**다: `Wait`가 만료되면 세션 스레드가 루프를 중단하고 전 좌석에 실패 이벤트를 발행한 뒤 끝낸다(테스트가 매달리지 않게 하는 안전장치이며, 기본값은 무한 대기라 평상시엔 발동하지 않는다). 만료를 자동 패스·오토플레이·AI 인계로 처리하는 정책은 M5.
- **취소** — 세션 종료 시 `CancellationToken`으로 `Wait`를 해제한다.

## 8. `MatchSession`

- 생성: `seats[4]`(`RemoteAgent` 또는 기존 `AiAgent`/`PimcAgent` 혼합) · `masterSeed` · `targetScore` · `ITichuTransport`.
- `Start()` — `IsBackground` 전용 스레드 1개. **모든 엔진 변이는 이 스레드에서만** 일어난다.
- 루프 — 라운드 시드 = `master.NextULong()` → `GameEngine.NewRound` → 훅 3개를 연결한 `GameDriver.RunRound` → 누적 → `MatchRunner.Decide` → `Continue`면 다음 라운드. `RoundBootstrap.RunMatchAsync`와 같은 모양이며 `MatchRunner.RunMatch`는 쓰지 않는다(그쪽은 라운드마다 에이전트를 재생성해 원격 좌석과 맞지 않는다).
- 브로드캐스트 — `onApply`에서 좌석 4개에 각각 `ProjectFor`로 `StateUpdate`를 발행한다.
- **스레드 모델** — `onApply`은 세션 스레드에서 발생하고 `LoopbackTransport`는 동기로 호출한다. Unity 클라이언트가 붙는 M4에서는 메인 스레드 마샬링이 필요하다.

## 9. 테스트 (TDD — 전부 dotnet, CI 포함)

- `ViewProjectorTests` — 여러 페이즈·여러 viewer에서 타 좌석 손패 비어 있음 · 투영 후 서버 상태를 바꿔도 뷰 불변(값 스냅샷) · 공개 항목 보존(장수·콜·소원·트릭 top·점수)
- `RemoteAgentTests` — 버퍼링(묻기 전 도착) · 종류 불일치 거부 · 타임아웃 · 취소
- `MatchSessionOracleTests` — AI 4좌석 세션 vs `GameDriver` 직접: `ComputeHash` + 로그 동일(복수 시드)
- `LoopbackMatchTests` — 스크립트 인간 2 + AI 2 매치 완주 · `StateUpdate` 수량/순서 · `ActionRejected`가 불법 액션에만 발생
- `GameDriverHookTests` — 훅 null일 때 비트 동일 · 폭탄 예약 적용 · 티츄 예약 적용

## 10. 범위 밖 (YAGNI)

- **M2** 직렬화(JSON) · 실제 WebSocket 전송 · 서버 호스팅·배포
- **M3** 방 · 초대 코드 · 좌석/팀 배치 · 빈자리 AI 충원
- **M4** 로비 UI · 턴 타이머 표시 · 연결 상태 · **싱글플레이를 세션 경로로 재배선**
- **M5** 타임아웃 자동 플레이 · 끊김 유예 · 재접속 스냅샷 · AI 인계
- **M6** 계정 · 랭킹 · MMR · Supabase 합류

명시적 보류 두 건: **교환 제출 현황 표시**(`RoundSetup`이 `internal`이라 공개 접근자 또는 `InternalsVisibleTo`가 필요 — M4에서 결정), **PIMC를 core로 승격**(서버 측 AI 인계에 필요 — M5. README가 이미 남은 정리 대상으로 기록).

## 11. 구현 계획 수립 중 확정된 정제 3건

계획(`docs/superpowers/plans/2026-10-04-mp-m1-session-protocol.md`)을 쓰면서 코드로 내려가 보니 세 군데가 더 정해져야 했다. 스펙을 뒤집는 변경은 없고, 모두 덧붙임이다.

1. **`ClientAction.Play`의 `Rank`(×2 스케일) — 당초 근거가 거짓으로 판명되어 용도를 바꿨다.**
   당초 주장: 손패 `3,4,5,6,봉황`이 봉황을 2로 쓰는 스트레이트와 7로 쓰는 스트레이트를 모두 만들어 카드 집합만으로는 해소가 안 된다.
   **실제**: `CombinationRecognizer.RecognizeStraight`(`CombinationRecognizer.cs:229-233`)는 끝 확장 시 `상단 확장 우선`이라 `max+1 ≤ 14`이면 무조건 상단으로 확장한다. 그래서 `3,4,5,6,봉황`은 항상 7-high 하나만 생성되고 봉황을 2로 쓰는 변형은 존재하지 않는다. 더 일반적으로 `Recognize`는 카드 다중집합(+단독 봉황에만 영향을 주는 `TrickContext`)의 순수 함수이므로 **두 합법수가 같은 카드 집합을 가질 수 없다** — Task 5 구현 중 생성 호출 24,000회·합법수 40,191개로 동일-다중집합 쌍 0건을 실측했다.
   **결론**: 모호성 해소 분기는 도달 불가라 제거한다. `Rank`는 필드로 남기되 **선택적 의도 검증**으로 쓴다 — `Rank != 0`인데 카드 집합이 일치하는 합법수의 `Rank`와 다르면, 클라이언트의 뷰가 낡았다는 뜻이므로 적용하지 않고 거부한다. 불변식("두 합법수는 카드 집합을 공유하지 않는다")은 전용 테스트로 지킨다.
   ⚠️ 파생 관찰(M1 범위 밖): 이 결정성은 **엔진의 룰 제약**이다. 기획서 3장은 봉황이 스트레이트의 어느 칸이든 메울 수 있다고 보고 `PhoenixAssignment` 필드를 예고했지만, 현재 엔진은 상단 확장이 가능하면 하단 선택을 아예 제공하지 않는다. 싱글플레이에도 동일하게 적용되는 선재 동작이라 여기서 바꾸지 않는다.

2. **`Awaiting`에 `LegalMoves`·`CanPass` 추가 — 단 대기 좌석에게만.** 클라이언트는 `PlayerView`만 들고 있어 `LegalMoveGenerator`(전체 `GameState` 필요)를 돌릴 수 없다. 합법수는 **그 좌석 자기 손패와 공개 트릭 상태에서만** 유도되므로 은닉 정보가 아니다. 다른 좌석에게는 `Kind`·`Seat`만 남긴 축약판을 보낸다(차례 표시용). `LegalMoves`가 돌려주는 `Combination`은 보관해도 안전하다 — `LegalMoveGenerator.cs:218-219` 주석대로 `Recognize()`가 카드를 새 배열로 복사하므로 `ThreadStatic` 스크래치 버퍼를 참조하지 않는다.

3. **`Awaiting` 발행 시점은 `RemoteAgent`가 대기에 들어가는 순간.** 드라이버의 `grandNext`/`exchangeNext` 카운터는 세션이 볼 수 없고 `RoundSetup`은 `internal`이라 "지금 누구를 기다리는지"를 상태에서 역산할 수 없다. 묻는 주체인 에이전트가 직접 알리는 것이 정확하며, 덤으로 그 좌석에만 합법수를 싣는 자리가 된다.

## 12. 승인된 결정

① 범위 = 친구방 ② 권위 = 전용 .NET 서버 ③ Supabase = M6 메타 ④ 세션 드라이버 = 동기 `GameDriver` + 블로킹(매치당 스레드 1) ⑤ M1은 `core/` 전용 · Unity 0줄 · 싱글플레이 재배선 없음.
