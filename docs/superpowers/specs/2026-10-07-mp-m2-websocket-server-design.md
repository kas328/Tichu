# 멀티플레이 M2 — WebSocket 전송 · 로컬 .NET 서버 (설계 스펙)

- **날짜:** 2026-10-07
- **트랙:** Phase 3 멀티플레이(친구방). M1(`bc941bd`) → 클라이언트 재배선(`4698eed`) 다음 증분.
- **상태:** 설계 승인됨(brainstorming). 구현 계획 수립 전.
- **배경:** M1 스펙 `2026-10-04-mp-m1-session-protocol-design.md` · 재배선 스펙 `2026-10-05-mp-client-rewiring-design.md` · 세션 폴트 → 메뉴 복귀 후속 수정(`d10e177`)

## 1. 확정된 결정

1. **범위 = 로컬 서버까지.** PC에서 `dotnet run`으로 띄운 서버에 에디터와 S23(같은 Wi-Fi)이 접속해 한 매치를 완주한다. **클라우드 배포(VPS·TLS·`wss://`)는 M2b로 분리한다.** 배포는 운영 작업이 대부분이라 게임 코드를 건드리지 않고, 공개 주소는 M3의 방·초대 코드가 생긴 뒤에 여는 것이 순서상 맞다.
2. **서버 빈자리 AI = PIMC를 지금 core로 승격.** 원래 M3 몫이던 일을 당긴다. PIMC 파일은 UnityEngine 참조가 0이라 이동만 하면 된다. 네트워크 판의 AI가 싱글플레이와 같아야 플레이테스트 관찰이 "네트워크 탓"과 "AI 탓"으로 섞이지 않는다.
3. **직렬화 = Newtonsoft.Json.** Unity 공식 패키지 `com.unity.nuget.newtonsoft-json`(AOT 대응판)과 core NuGet에서 같은 라이브러리를 쓴다. 기존 프로토콜 클래스를 그대로 직렬화하고 변환기는 `Card`·`Combination` 두 개만 둔다. 기각: 수기 바이너리 코덱(필드가 늘 때마다 코덱 수정, 로그 판독 불가), System.Text.Json(Unity 미포함·IL2CPP 리플렉션 불안).

## 2. 목표 (M2)

M1 프로토콜을 진짜 네트워크에 올린다. `LoopbackTransport` 자리에 WebSocket을 끼우고 `core/`를 그대로 돌리는 서버 프로세스를 만든다. **`MatchSession` · `RemoteAgent` · `GameDriver` · 프로토콜 의미론 · 싱글플레이 동작은 바꾸지 않는다.**

## 3. 성공 기준 (DoD)

1. **자동 테스트 그린** — dotnet 전체(E2E 포함) · 유니티 EditMode `Tichu.Presentation.Tests` 전체 · `tools/sync-core-to-unity.ps1 -Check` 무드리프트.
2. **실기 완주** — PC에서 `dotnet run --project core/src/Tichu.Server -- --seats 0,2 --target 200`을 띄우고 에디터와 S23이 같은 Wi-Fi로 접속해 매치를 끝까지 두며, 둘 다 결과 화면이 나온다.
3. **실기 끊김** — 매치 중 S23 앱을 강제 종료하면 에디터가 메뉴로 돌아간다.
4. **IL2CPP** — S23 IL2CPP 개발 빌드에서 직렬화가 정상 동작한다(스트리핑 회귀 없음).
5. **싱글플레이 무변화** — 스모크 한 판에서 체감 차이가 없다.

## 4. 구조

```
 Unity 클라이언트 (S23 / 에디터)                 PC: Tichu.Server (dotnet run)
 ┌─────────────────────────────────┐            ┌──────────────────────────────────┐
 │ TableViewModel ← TichuClient    │            │ WebSocketServerTransport         │
 │                   │             │            │   : ITichuTransport (기존)        │
 │          IClientTransport (신규) │   ws://    │ MatchSession (기존, 무변경)       │
 │   ├ LoopbackClient (싱글)        │            │   인간 좌석 = RemoteAgent         │
 │   └ WebSocketClientTransport ◀──┼──JSON────▶│   나머지   = PacedPimcAgent       │
 └─────────────────────────────────┘            └──────────────────────────────────┘
```

### 4.1 신규 `core/src/Tichu.Server/` (Unity 미러 없음)

- net10.0 ASP.NET Core 최소 API. `/ws` 엔드포인트 하나.
- 실행 인자: `--seats 0,2`(인간 좌석 목록, 기본 `0,2` = 친구와 같은 팀으로 AI 둘을 상대) · `--difficulty Hard`(기본 `Hard`) · `--target 1000`(목표 점수, 기본 1000) · `--port 7777`(기본 7777).
- **프로세스 하나 = 매치 하나.** 매치가 끝나면 종료한다. 방 개념은 M3.
- `WebSocketServerTransport : ITichuTransport` — 연결 관리 · 좌석 배정 · 좌석 덮어쓰기 · 레이트리밋 · 연결별 송신 큐.
- `Tichu.sln`에 추가. `Tichu.Core.Tests`가 참조한다(E2E용).

### 4.2 `Tichu.Net` 추가 (Unity로 미러됨)

| 파일 | 내용 |
|---|---|
| `Serialization/ProtocolJson.cs` | 공용 `JsonSerializerSettings`와 `Serialize(ServerEvent/ClientAction)` · `DeserializeServerEvent` · `DeserializeClientAction`. 모르는 `Kind`는 예외 |
| `Serialization/CardJsonConverter.cs` | `Card`(readonly struct) ↔ JSON |
| `Serialization/CombinationJsonConverter.cs` | `Combination`(생성자 전용 불변 클래스) ↔ JSON |
| `IClientTransport.cs` | `IReadOnlyList<ServerEvent> Drain()` · `void Submit(ClientAction)` |
| `LoopbackClient.cs` | `LoopbackTransport`를 좌석 하나에 묶는 어댑터(싱글플레이용) |
| `WebSocketClientTransport.cs` | `ClientWebSocket`. 수신 태스크 → `ConcurrentQueue` → 메인 스레드 `Drain()`. 송신 큐 + 송신 태스크 |
| `ServerEvent` 변경 | 종류 `Lobby` 추가 — 필드 `YourSeat` · `Connected` · `Required` |

`Tichu.Net`은 Newtonsoft.Json NuGet 패키지를 참조한다(core 최초의 외부 의존성). Unity 쪽은 `Packages/manifest.json`에 `com.unity.nuget.newtonsoft-json`을 추가하고 `Tichu.Net.asmdef`가 `Newtonsoft.Json.dll`을 참조한다(`precompiledReferences` + `overrideReferences`).

### 4.3 PIMC 승격

- **PIMC 8개 파일** — `PimcAgent` · `Pimc` · `Determinizer` · `HeuristicRolloutPolicy` · `PolicyConfig` · `ReachWeight` · `Difficulty` · `IWorldEvaluator` — 을 `Assets/_Project/GameFlow/Agents/`에서 정본 `core/src/Tichu.GameFlow/Agents/`로 옮긴다. **내용 변경 0.** 미러 위치가 원래 자리와 같으므로 기존 `.meta`(GUID)가 그대로 유지된다.
- **`PacedPimcAgent`** — `Assets/_Project/Presentation/Agents/`에서 `core/src/Tichu.GameFlow/Agents/`로 옮긴다. 네임스페이스가 `Tichu.Presentation` → `Tichu.GameFlow.Agents`로 바뀌고(`RoundBootstrap`의 using 갱신), core의 `Nullable` 설정에 맞춰 `Func<bool> fastForward = null` → `Func<bool>? fastForward = null`, "PIMC가 core에 없어 미러되지 않는다" 주석과 Presentation 전용 `PimcDecisionAgent` cref를 사실에 맞게 고친다. **동작 변경 0.** 원래 `.meta`는 미러 위치로 같이 옮긴다.
- **테스트** — `DeterminizerTests` · `HeuristicRolloutPolicyTests` · `PimcAgentTests` · `PolicyConfigTests` · `ReachWeightTests`(현재 `Assets/_Project/Tests/EditMode/` 유니티 전용)와 `PacedPimcAgentTests`(`Assets/_Project/Presentation/Tests/`)를 `core/tests/Tichu.Core.Tests/`로 옮기고 미러한다. 벤치 계열(`PimcBench` · `ValueLeafBench` · `BenchStats` · `BenchStatsTests` · `PimcBenchTests`)은 **유니티에 그대로 둔다**(범위 밖, 미러된 PIMC를 계속 참조하므로 동작 불변).
- 서버에서는 빨리감기가 없으므로 `fastForward`에 `() => false`(또는 null)를 넘긴다.

⚠️ `PacedPimcAgent`의 "연산 먼저 + 남은 시간만 sleep" 겹침은 난이도 티어 사다리(Normal16/Hard20/Expert24, 예산 900 통일)의 전제다. 이동만 하고 수정하지 않는다.

### 4.4 좌석 회전 (계획 수립 중 발견 · 승인됨)

테이블 UI는 **"나 = 좌석 0"을 가정**한다 — `RuntimeTableView.cs:29` `SeatNames = { "나", "오른쪽", "파트너", "왼쪽" }`를 절대 좌석으로 인덱싱하고(`:163`·`:315`도 좌석 0 고정), 라운드 결과(`:516` "우리 {TeamA} : 상대 {TeamB}")와 매치 결과(`WinningTeam == 0` → "우리 팀 승리")가 팀 A를 우리 팀으로 고정한다. 싱글플레이는 항상 좌석 0이라 드러나지 않았지만, `--seats 0,2`의 두 번째 접속자는 좌석 2를 받는다.

**해법: 클라이언트 경계 회전.** `Tichu.Net/SeatRotation.cs`(순수 함수):
- `ServerEvent ToLocal(ServerEvent ev, int mySeat)` — `Lobby.YourSeat`는 회전하지 않는다(대기실이 회전 전에 읽는 절대 좌석 배정). 받은 이벤트를 "내 좌석 = 0" 좌표로 바꾼 **새 객체**를 돌려준다. 회전 `local = (abs − mySeat + 4) % 4`. 대상: `PlayerView.Viewer`·`Turn`·`DragonGiftWinner`·좌석별 배열(`HandCounts`·`Calls`·`IsOut`·`FinishOrder`·`WonCards`)·`TrickView.TopOwnerSeat`·`PlayView.Seat`·`TrickSummary.TopOwnerSeat`·`DragonGiftRecipient`·`Awaiting.Seat`·`AppliedAction.Seat`·`RecipientSeat`(−1 유지)·`mySeat`가 홀수면 팀이 뒤집히므로 `TeamAScore`↔`TeamBScore`, `RoundResult`의 A/B 필드 전부, `MatchEnded`의 `WinningTeam`(0↔1)·`TeamA`↔`TeamB`도 맞바꾼다.
- `ClientAction ToServer(ClientAction a, int mySeat)` — `Seat`과 `RecipientSeat`(GiveDragon)을 서버 좌표로 되돌린다. 교환의 `ToLeft`·`ToPartner`·`ToRight`는 원래 상대 좌표라 그대로 둔다.
- 적용 지점: `TichuClient`가 `mySeat`로 생성되어 수신 이벤트에 `ToLocal`, 송신 액션에 `ToServer`를 적용하고, 뷰모델은 **항상 `new TableViewModel(0)`**. 싱글플레이(`mySeat = 0`)에서는 항등 변환이다. **뷰모델·뷰는 무변경.**
- 기각: 뷰 안의 절대 좌석 인덱싱 10여 곳을 상대 좌석으로 수정(넓은 침습·D시리즈 연출 코드 접촉).

### 4.5 난이도 예산표 core 이동

`RoundBootstrap.BudgetMsFor`(Unity 전용)를 `core/src/Tichu.GameFlow/Agents/PimcBudget.cs`의 `PimcBudget.For(Difficulty)`로 옮기고, `RoundBootstrap.BudgetMsFor`는 위임만 남긴다(기존 단조성 테스트 `PimcDecisionAgentTests.BudgetMsFor_is_monotonic_non_decreasing_across_tiers` 무변경). 서버가 같은 표를 쓴다 — 티어 사다리 불변식을 표 두 벌로 나눠 들지 않는다. 서버의 AI 지연은 `GameLaunchArgs.AiDelayMs` 기본값과 같은 900ms.

### 4.6 기타 변경

- `TichuClient`가 `LoopbackTransport` 대신 `IClientTransport`에 의존한다(재배선 때 넘어온 이월 항목). `RoundBootstrap`의 싱글플레이 경로는 `LoopbackClient`를 넘긴다 — 동작 동일.
- 서버 테스트는 **별도 프로젝트 `core/tests/Tichu.Server.Tests`(net10)** 에 둔다(§10-1). `Tichu.Core.Tests`는 net9.0 그대로. CI는 9.0.x와 10.0.x SDK를 둘 다 설치한다.

## 5. 접속과 데이터 흐름

### 5.1 좌석 배정과 매치 시작

- 클라이언트가 `ws://<PC IP>:7777/ws`에 접속하면 **접속 순서대로** `--seats` 목록에서 아직 비어 있는 첫 좌석을 받는다.
- 들어올 때마다 서버가 전원에게 `Lobby { YourSeat, Connected, Required }`를 보낸다(`YourSeat`는 수신자별). 클라이언트는 여기서 자기 좌석을 알고 뷰모델을 만든다. "기다리는 중 1/2" 표시도 이걸로 한다.
- `Connected == Required`가 되면 서버가 `MatchSession`을 시작한다. 그 뒤 흐름(`StateUpdate` · `RoundScored` · `MatchEnded` · `ActionRejected`)은 루프백과 같다.
- 좌석이 다 찬 뒤 들어오는 접속은 즉시 닫는다.

### 5.2 메시지 형식

WebSocket 텍스트 프레임 하나에 JSON 메시지 하나. `ClientAction`과 `ServerEvent`를 봉투 없이 그대로 보낸다 — 둘 다 이미 `Kind` 구분자를 가진다.

### 5.3 스레드

- **서버** — 세션 스레드가 `SendTo`를 동기로 호출하는 현 구조를 유지한다. 연결마다 송신 큐 + 송신 태스크를 둬서 **세션 스레드가 네트워크에서 막히지 않는다.** WebSocket은 한 소켓에 동시 전송이 금지라 이 큐가 자연스러운 직렬화 지점이 된다.
- **클라이언트** — 수신 태스크가 `ConcurrentQueue`에 쌓고 메인 스레드가 `Pump()`에서 `Drain()`한다(루프백 폴링과 같은 모양). `Submit`은 송신 큐에 넣고 즉시 돌아온다.

### 5.4 좌석 사칭 방지

서버 전송이 받은 `ClientAction.Seat`를 **그 연결에 배정된 좌석으로 덮어쓴다.** `MatchSession`은 `a.Seat`로 우편함을 고르므로, 덮어쓰지 않으면 클라이언트가 `Seat=2`를 보내 파트너의 수를 대신 둘 수 있다.

### 5.5 매치 수명

`MatchEnded`를 전원에게 보낸 뒤 소켓을 정상 종료하고 프로세스를 끝낸다. 싱글플레이는 서버 없이 지금처럼 프로세스 안 루프백으로 돈다.

## 6. 오류 처리와 한계

원칙: **멈추면 깔끔하게 끝낸다.** 재접속·AI 인계·턴 시계는 M5.

### 6.1 인간 끊김 (서버)

- **매치 시작 전** — 그 좌석을 빈자리로 돌리고 `Lobby`를 다시 보낸다. 다음 접속자가 그 좌석을 받는다.
- **매치 중** — 서버 호스트가 `session.Stop()`을 부르고 남은 연결에 `SessionFaulted("좌석 N 연결 끊김")`을 보낸 뒤 종료한다. `MatchSession.Stop()`은 `OperationCanceledException`을 조용히 삼켜 아무것도 방송하지 않으므로 방송은 호스트 몫이다. **`MatchSession` 무변경.**

### 6.2 클라이언트

- **접속 실패**(5초 타임아웃·주소 오류) — "서버에 연결할 수 없습니다" 토스트 후 메뉴로.
- **매치 중 끊김** — `WebSocketClientTransport`가 수신 큐에 `SessionFaulted("서버 연결 끊김")`을 직접 넣는다. `TichuClient`는 서버 폴트와 똑같이 처리하고 `RoundBootstrap`의 `onFault` → `AppFlowEvent.ReturnToHub`로 메뉴에 돌아간다(`d10e177`). `TichuClient` 무변경.
- `ActionRejected` 처리는 현행 그대로. 거부 메시지 표출은 M4.

### 6.3 악성·고장 입력 (M1이 M2로 넘긴 레이트리밋)

- JSON 파싱 실패 · 모르는 `Kind` · **16KB 초과 프레임** → 그 연결을 닫는다(매치 중이면 §6.1로 이어짐).
- **연결당 토큰 버킷: 초당 10건, 버스트 20건.** 초과 시 연결을 닫는다. 정상 클라이언트는 한 판 내내 수십 건뿐이다. M1이 지적한 `RemoteAgent` 선행 큐 무한 증가의 속도를 이 층에서 묶는다. `RemoteAgent` 무변경.

### 6.4 로그

서버 콘솔에 접속 · 끊김 · 좌석 배정 · 매치 시작/종료 · 거부 · 폴트를 남긴다. 메시지 본문 추적 로그는 두지 않는다.

## 7. Unity 진입 UI (개발용 — M4 로비가 대체)

- 모드 선택의 **"친구방" 버튼**: `Debug.isDebugBuild`(에디터 포함)일 때 새 화면 `NetJoin`을 연다 — 서버 주소 입력칸 · 접속 · 뒤로. 주소는 `PlayerPrefs`에 기억한다. 릴리스 빌드는 현행 "Phase 3에서 제공" 토스트 유지.
- `ScreenState.NetJoin`과 `AppFlowEvent` 두 개(`OpenNetJoin` · `StartNetMatch`) 추가. 리듀서 전이: `(ModeSelect, OpenNetJoin) → NetJoin` · `(NetJoin, StartNetMatch) → InGame` · `(NetJoin, Back) → ModeSelect`.
- `MatchSettings` · `GameLaunchArgs`에 `ServerAddress` 추가(null = AI 대전). 값이 있으면 `RoundBootstrap`은 로컬 세션·AI를 만들지 않고 `WebSocketClientTransport`로 접속하고, "다른 플레이어 기다리는 중 n/m"을 띄운 채 `Lobby`로 좌석을 받은 뒤 테이블을 만든다. 접속 실패는 §6.2.
- 네트워크 판에서 빨리감기는 효과가 없다(AI 속도를 서버가 정한다). 의도된 동작.
- `Assets/link.xml`에서 `Tichu.Net` 어셈블리를 보존한다(Newtonsoft 리플렉션 대상).
- 안드로이드 평문 `ws://`: 원시 소켓이라 Java 네트워크 보안 정책 대상이 아닐 것으로 본다. 실기에서 확인하고, 막히면 개발 빌드 한정 예외를 둔다.

## 8. 테스트 (TDD — 전부 dotnet, CI 포함)

- `ProtocolJsonTests` — 모든 `ClientActionKind` · `ServerEventKind` 왕복. `PlayerView` 전체(`TrickView.Top` 조합 · `Awaiting.LegalMoves` · 특수카드 4종 · `RoundResult`) 포함. 모르는 `Kind` → 예외.
- PIMC 승격 — 옮긴 테스트가 dotnet과 Unity 양쪽에서 그린, `sync -Check` 무드리프트.
- `ServerTransportTests` — 좌석 덮어쓰기(사칭 무력화) · 접속순 배정과 끊긴 좌석 재사용 · 만석 거절 · 16KB 초과/깨진 JSON 시 연결 종료 · 레이트리밋 초과 시 연결 종료.
- **E2E**(프로세스 안 실제 Kestrel, 임의 포트) — 스크립트 클라이언트 2 + 서버 PIMC 2로 낮은 목표 점수 매치를 `MatchEnded`까지 완주. **각 클라이언트가 받은 JSON 원문에 타 좌석 손패 카드가 없다**(유선 위 마스킹 불변식). 빠른 실행을 위해 E2E의 AI는 Easy 난이도와 지연 0.
- **E2E 끊김** — 매치 중 클라이언트 하나를 닫으면 다른 클라이언트가 `SessionFaulted`를 받고 서버가 종료된다.
- `SeatRotation` — 좌석 0~3 각각에서 `ToLocal`/`ToServer` 왕복, 홀수 좌석 팀 뒤집기(점수·`RoundResult`·`WinningTeam`), `RecipientSeat = -1` 보존, 좌석 0 항등.
- Unity — 리듀서 신규 전이 테스트 · Presentation EditMode 전체 그린. 싱글플레이 경로는 기존 테스트가 회귀를 막는다.
- ⚠️ NUnit 버전 차이: Unity는 NUnit 3.5라 미러되는 테스트에 NUnit 4 전용 API를 쓰지 않는다. dotnet 그린 ≠ 유니티 그린.

## 9. 범위 밖 (YAGNI)

- **M2b** 클라우드 배포 · TLS(`wss://`) · 도메인/인증서
- **M3** 방 · 초대 코드 · 한 서버 다중 매치 · 좌석/팀 선택 UI
- **M4** 로비 UI · 거부 메시지 표출 · 교환 제출 현황 카운터 · 연결 상태 표시
- **M5** 턴 시계 · 타임아웃 자동 플레이 · 재접속 스냅샷 · AI 인계 · 차례밖 폭탄 결정 중단 토큰 · 중복 `ActionSeq` 수신 시 상태 재전송
- **M6** 계정 · 랭킹 · Supabase
- 메시지 본문 추적 로그 · 서버 측 메트릭

재배선 이월 중 이번에 처리하지 않는 것: `AppliedAction` 속성 get-only화(Newtonsoft는 setter가 있어야 역직렬화가 간단하므로 오히려 유지) · `MatchSession` `IDisposable`.

## 10. 구현 계획 수립 중 확정된 정제

계획(`docs/superpowers/plans/2026-10-07-mp-m2-websocket-server.md`)을 쓰며 코드로 내려가 정한 것들이다. 스펙을 뒤집는 변경은 없다.

1. **서버 테스트는 별도 프로젝트.** `Tichu.Core.Tests`는 Unity로 미러되는데 ASP.NET을 참조하는 서버 테스트는 미러될 수 없다. 그래서 `Tichu.Core.Tests`를 net10으로 올리는 대신 `Tichu.Server.Tests`(net10, 미러 없음)를 새로 둔다. 스크립트 클라이언트(`ScriptedClient.cs`)는 복제하지 않고 링크로 컴파일한다.
2. **`ProtocolException`.** 해석 실패를 Newtonsoft 예외 대신 `Tichu.Net.ProtocolException`으로 감싼다. 미러되는 테스트가 Newtonsoft를 직접 참조하지 못하고(Unity 테스트 asmdef가 `overrideReferences`), 서버도 라이브러리 타입을 몰라도 된다. 열거형은 이름 문자열로 실어 모르는 `Kind`를 예외로 거른다.
3. **유선 위 마스킹 테스트 위치.** E2E에서 원문 JSON을 가로채는 대신 `ProtocolJsonTests`가 갓 나눈 라운드의 직렬화 결과를 검사한다(내 카드 외 56장 중 어떤 카드 조각도 없다). 네트워크 없이 같은 불변식을 더 정확히 고정한다.
4. **서버 실행 인자 추가.** `--ai-delay`(기본 900)와 `--seed`. 라운드 사이 멈춤·레이트리밋 값은 테스트가 코드로 바꾸는 `ServerOptions` 속성이다.
5. **`ServerAddress.TryParse`** (`Tichu.Net`, 순수). `192.168.0.5` · `host:9000` · `ws://host:7777/ws`(앞뒤 공백 허용)를 받고, 스킴 생략 → `ws`, 포트 생략 → 7777, 경로 생략 → `/ws`. `http` 등은 거절.
6. **`LobbyWaiter`** (Presentation, 순수). 대기 중 `Lobby`·폴트를 읽고, 첫 매치 이벤트부터는 순서대로 백로그에 담아 `TichuClient(..., backlog)`에 넘긴다. 서버는 좌석 배정·`Lobby` 큐잉·만석 판정을 한 잠금 안에서 해서 마지막 `Lobby`가 첫 `StateUpdate`보다 먼저 큐에 들어감을 보장한다.
7. **폴트 알림 경로.** `RoundBootstrap.Begin`의 `onFault`가 사유 문자열을 받는다(`Action<string>`, null = 사용자 취소). 셸이 `MatchSettings.PendingNotice`에 담고 `ReturnToHub`하면, 메뉴가 메인 허브에서 토스트로 한 번 띄운다. 대기 화면에 **취소 버튼**을 둔다(영원히 안 차는 방에 갇히지 않게).
8. **정상 종료 판정.** `WebSocketClientTransport`는 `MatchEnded`나 서버의 `SessionFaulted`를 받은 뒤의 닫힘, 또는 스스로 `Dispose`한 뒤의 닫힘에는 "서버 연결 끊김"을 만들지 않는다. 서버의 닫기에는 송신 태스크를 거쳐 응답한다(동시 전송 방지).

## 11. 승인된 결정

① M2 = 로컬 서버까지, 배포는 M2b ② PIMC를 M2에서 core로 승격 ③ 직렬화 = Newtonsoft.Json ④ 구조(§4) ⑤ 접속·흐름(§5): 접속순 좌석 · `Lobby` 이벤트 · 좌석 덮어쓰기 · 프로세스당 매치 1 ⑥ 오류 처리(§6): 끊기면 깔끔히 종료 · 16KB/토큰 버킷 ⑦ 진입 UI · 테스트 · DoD(§3·§7·§8). ⑧ 좌석 회전(§4.4) · 예산표 core 이동(§4.5) — 계획 수립 중 발견, 승인됨.
