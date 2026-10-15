# 멀티플레이 M4 — 로비 · 출시 주소 · 거부 메시지 (설계 스펙)

- **날짜:** 2026-10-15
- **트랙:** Phase 3 멀티플레이. M5(턴 타이머·AI 인계·재접속, main `c7d162b`) 다음 증분.
- **상태:** 설계 승인됨(brainstorming). 구현 계획 수립 전.
- **브랜치:** `feat/mp-m4` ← main `0c0b908`.
- **목적:** 앞선 스펙들이 "M4로" 미뤄 둔 출시 차단 요소를 정리한다. ① 출시 빌드에서 온라인 대전이 아예 열리지 않는다, ② 대기 화면이 문구 한 줄뿐이다, ③ 판 중 거부 사유가 영문으로 로그에만 남는다. **서버·프로토콜은 바꾸지 않는다**(클라이언트만 수정).

## 1. 확정된 결정

1. **로비 범위 = 대기 화면 정식화(A안).** 서버가 이미 보내는 정보(대기 인원·AI 충원 남은 초·붐빔 순번·방 접속 수)를 4칸 자리 표시와 카운트다운으로 보여 준다. 닉네임(B안: 프로토콜·UGC 정책 부담)과 초대코드 친구방(C안: 새 서버 서브시스템)은 출시 후로 미룬다.
2. **출시 주소 = 빌드에 고정, 비어 있으면 '준비 중'(1안).** 설정 에셋 하나에 주소를 넣는다. 서버·도메인은 M2b(과금 작업 일괄)에서 생기므로 지금은 비워 둔다. 원격 설정(gh-pages `server.json`)과 출시 빌드 주소 입력은 기각.
3. **구조 = 테이블 위 오버레이 유지(가안).** 접속·대기·게임·재접속은 지금처럼 `RoundBootstrap` 한 흐름이 맡는다. 오버레이 그리기만 별도 뷰로 뺀다.
   - 기각 나(메뉴의 별도 로비 화면): 연결 객체가 씬 경계를 넘어 소유권·정리·재접속 경로가 둘로 갈리고, M5에서 안정화한 흐름을 다시 뜯어야 한다.

## 2. 출시 빌드 주소

### 2.1 설정 에셋

- 새 `ScriptableObject` `ReleaseServerConfig`(필드 `string address`), 에셋 경로 `Assets/_Project/Presentation/Resources/ReleaseServer.asset`. 커밋 시 `address`는 빈 문자열.
- 읽기: `Resources.Load<ReleaseServerConfig>("ReleaseServer")`. 에셋이 없거나 `address`가 비었거나 `ServerAddress.TryParse`가 실패하면 "출시 주소 없음"으로 본다.

### 2.2 진입 판단 (순수)

새 순수 클래스 `OnlineEntry` (`Presentation/Shell/`, 유니티 무참조):

```
OnlineEntry.Decide(bool isDebugBuild, string? releaseAddress) → OnlineEntryDecision
  Kind  : AskAddress | StartDirect | Unavailable
  Uri   : StartDirect일 때 정규화된 주소
```

| 빌드 | 출시 주소 있음(파싱 성공) | 출시 주소 없음/잘못됨 |
|---|---|---|
| 출시 (`!Debug.isDebugBuild`) | `StartDirect(uri)` | `Unavailable` |
| 개발 (에디터 포함) | `AskAddress` | `AskAddress` |

- `MenuShellPresenter.OnFriendRoom`이 이 결정을 따른다.
  - `Unavailable` → 지금과 같다: `SelectFriendRoomStub` + 토스트 "온라인 대전은 준비 중입니다".
  - `StartDirect` → `_settings.ServerAddress = uri`, `_settings.Difficulty`는 건드리지 않고 `StartNetMatch` 전송.
  - `AskAddress` → `OpenNetJoin`(지금과 같음).
- **새 리듀서 전이:** `(ModeSelect, StartNetMatch) → InGame`. 기존 `(NetJoin, StartNetMatch) → InGame`은 유지.
- **주소 입력란 미리 채우기:** 개발 빌드의 NetJoin 입력란은 `PlayerPrefs` 기억값을 쓰고, 없으면 출시 주소(있으면)를 쓴다. 이 선택도 `OnlineEntry.InitialAddress(saved, release)`로 순수하게 둔다.

## 3. 대기 화면

### 3.1 표시 상태 (순수)

`LobbyWaiter`에 표시용 스냅샷을 더한다. 기존 `StatusText`는 이 스냅샷으로 대체하고 제거한다(소비자는 `RoundBootstrap` 하나).

```
LobbyDisplay
  Title      : string
  Seats      : int   // 채워진 칸 수. 0이면 4칸 영역을 숨긴다
  Detail     : string
```

| 상태 (판정 순서대로) | Title | 4칸 | Detail |
|---|---|---|---|
| 재접속 중 (`Reconnecting` 플래그) | 연결이 끊겼습니다 | 숨김 | 재접속 중… 같은 자리로 돌아갑니다 |
| 방 배정됨 (`Required > 0`) | 곧 시작합니다 | `Connected`칸 채움 | 다른 플레이어 기다리는 중 |
| 서버 붐빔 (`Busy`) | 서버가 붐빕니다 | 숨김 | 대기 순번 {Position} · 자리가 나면 자동으로 들어갑니다 |
| 대기열 (`Queued` 받음, 남은 초 있음) | 상대를 찾는 중 | `Waiting`칸 채움(나 포함) | {남은 초}초 뒤 빈자리는 AI로 채우고 시작합니다 |
| 그 밖(첫 이벤트 전) | 서버 접속 중… | 숨김 | — |

- `Waiting`은 서버 대기열 길이(나 포함, 4명이 되면 즉시 방 배정이라 1~3). 4칸 중 첫 칸은 "나", 나머지 채운 칸은 "플레이어", 빈칸은 "빈자리".
- 남은 초는 기존대로 `Queued`를 받은 시각부터 클라이언트 시계로 센다(0 아래로 내려가지 않음).
- `Reconnecting`은 `RoundBootstrap`이 재접속 루프에서 `LobbyWaiter`를 만들 때 넘긴다(생성자 인자). 재접속 중에 대기열 이벤트를 받으면 지금처럼 "진행 중인 판이 끝났습니다"로 끝낸다(동작 불변).

### 3.2 뷰

- 새 `LobbyOverlayView` (`Presentation/Views/`): `RoundBootstrap.CreateLobbyOverlay`(약 40줄)를 옮기고 4칸 영역과 Detail 줄을 더한다. API는 `Create(Canvas)`, `Render(LobbyDisplay)`, `Cancel`(Button), `Destroy()`.
- 레이아웃: 1920×1080 기준 중앙. Title 48pt, 칸 4개(각 180×160, 간격 24), Detail 30pt, 취소 버튼(360×96). 채운 칸은 테이블 녹색, 빈칸은 점선 대신 반투명 테두리(내장 UI만 사용).
- `RoundBootstrap`은 매 프레임 `overlay.Render(lobby.Display)`를 부른다. 취소 배선·재접속 시 재생성 흐름은 그대로다.

## 4. 거부·중단 메시지

### 4.1 문구 변환 (순수)

새 순수 클래스 `NetMessages` (`Presentation/Net/`):

```
NetMessages.ForRejection(string reason) → string?   // null = 알리지 않음
NetMessages.ForFault(string? reason)    → string?   // null = 사용자 취소(알림 없음, 지금과 같음)
```

| 입력 | 출력 |
|---|---|
| 거부 `stale answer` | 시간이 지나 AI가 대신 처리했습니다 |
| 거부 `already decided this round` · `stale action from a previous round` | 없음(`null`, 토스트 안 띄움) — 재접속 뒤 다시 뜬 질문처럼 **내 답이 이미 쓰인** 경우, 또는 라운드가 끝나 **버려진 수**(차례 밖 폭탄)라 "AI가 대신" 문구가 틀린 안내가 된다(최종 리뷰에서 정정) |
| 그 밖의 모든 거부 | 낼 수 없는 동작입니다 |
| 폴트: 이미 아는 한국어 사유(서버 연결 끊김 · 다른 곳에서 접속 · 진행 중인 판이 끝났습니다 · 서버에 연결할 수 없습니다 · 서버와 연결이 끊어졌습니다 · `ProtocolVersion.UpdateRequiredReason`) | 그대로 |
| 폴트: 그 밖(예: `MatchSession`이 보내는 예외 메시지) | 서버 오류로 판이 중단되었습니다 |

- 원문은 항상 `Debug.LogWarning`으로 남긴다(호출하는 쪽에서).
- 알려진 폴트 문구는 `NetMessages` 안에 목록으로 둔다. 공개 상수가 있는 것은 참조하고(`ProtocolVersion.UpdateRequiredReason`), 나머지 리터럴("서버 연결 끊김"은 `WebSocketClientTransport`의 private 상수, "다른 곳에서 접속"·"진행 중인 판이 끝났습니다"는 서버 전용)은 core를 건드리지 않도록 복제하며 `NetMessages` 테스트가 고정한다. 서버 문구가 바뀌어 목록에서 빠지면 일반 문구로 떨어질 뿐 깨지지 않는다.

### 4.2 게임 화면 토스트

- `TableViewModel`에 `Observable<string> Notice`(`Subject<string>`)를 더한다. `OnActionRejected(reason)`가 `NetMessages.ForRejection(reason)`이 null이 아닐 때만 흘린다. 기존 동작(이미 결정된 답 거부면 프롬프트를 다시 띄우지 않음)은 그대로다.
- `RuntimeTableView`에 작은 토스트 하나(화면 상단 중앙, 2초 유지 후 숨김 — 이 파일의 기존 `Observable.EveryUpdate` 타이머 관용구를 따른다)를 더하고 `Notice`를 구독한다. `ITableView` 인터페이스는 바꾸지 않는다.
- 로컬 AI 대전은 거부가 없으므로 영향 없음.

### 4.3 메뉴 토스트

- `GameSessionPresenter`가 폴트 사유를 `PendingNotice`에 넣기 전에 `NetMessages.ForFault`를 거친다.

## 5. 하지 않는 것

- 프로토콜·서버 변경(거부 코드 enum화는 출시 후 필요할 때).
- 닉네임·친구방·핑/지연 표시.
- 메뉴 패널 디자인 개편, AAB 재빌드(M2b에서 주소를 채울 때 한꺼번에).
- M5.1 이월 항목(재접속 시 차례 시계 재시작 등).

## 6. 검증

- **TDD(EditMode, `Tichu.Presentation.Tests`):**
  - `OnlineEntry`: 표 2.2의 네 칸 + 잘못된 주소 + 공백 주소, `InitialAddress` 우선순위.
  - `AppFlowReducer`: `(ModeSelect, StartNetMatch) → InGame` 추가, 기존 무시 케이스 유지.
  - `LobbyWaiter.Display`: 표 3.1의 다섯 상태 + 판정 순서(붐빔이면서 Required>0이면 방 배정 우선) + 카운트다운 감소·0 하한(주입 시계).
  - `NetMessages`: 표 4.1 전 행.
  - `TableViewModel.Notice`: 거부 시 변환 문구 1회 발행, 이미 결정된 답 거부는 발행하지 않음.
- **회귀:** Presentation EditMode 전체 + core dotnet(변경 없음 확인). ⚠️Unity 러너에서 `Tichu.Core.Tests` 전체 실행 금지(시뮬 10만판).
- **실기(에디터 + S23, 로컬 서버, 개발 APK):** 혼자 대기 → 1칸·카운트다운 → AI 충원 시작 / 둘이 대기 → 2칸 / `--max-rooms 1` 붐빔 → 순번 표시 → 자동 입장 / 판 중 Wi-Fi 끊기 → "연결이 끊겼습니다" 화면 → 복귀 / 시간 초과 직후 수 → 토스트.
- 출시 빌드 '준비 중' 경로는 `OnlineEntry` 단위 테스트로 확인한다.
