# 멀티플레이 M5.1 — 끊겨도 흐르는 차례 시계 · 남의 차례 남은 초 (설계 스펙)

- **날짜:** 2026-10-17
- **트랙:** Phase 3 멀티플레이. M4(로비·출시 주소·거부 메시지, main `1a28acf`) 다음 증분.
- **상태:** 설계 승인됨(brainstorming). 구현 계획 수립 전.
- **브랜치:** `feat/mp-m51` ← main `1a28acf`.
- **목적:** M4 실기 피드백 두 건. ① 내 차례 20초 도중 Wi-Fi 를 끊었다 돌아오면 20초가 다시 시작돼 시간을 벌 수 있다(악용). ② 끊긴 사람·고민하는 사람을 기다리는 쪽엔 남은 초가 보이지 않아 답답하다.

## 1. 확정된 결정

1. **한 결정에 마감은 하나.** 차례(Play)·용 양도는 처음 물은 순간 마감을 정하고, 그 결정이 끝날 때까지 다시 묻더라도 남은 초만 준다.
2. **사람이 아무도 없어 판이 멈췄다 돌아오면 새 20초(현행 유지).** 기다리는 사람이 없어 악용해도 손해 보는 사람이 없고, 시간을 흘리면 늦게 돌아온 사람이 곧바로 '자리 비움'이 된다.
3. **남의 차례 남은 초는 그 사람 이름표 옆**(예: `왼쪽 · 12초`). 내 차례는 지금처럼 아래 가운데 "남은 시간 N초".
4. **프로토콜 필드는 늘리지 않는다.** 이미 있는 `Awaiting.SecondsLeft`·`AskId` 를 다른 좌석의 축약판에도 채운다. `ProtocolVersion` 은 올리지 않는다(예전 앱은 값을 무시, 예전 서버는 값이 비어 초가 안 보일 뿐).

## 2. 현재 동작과 리셋 경로 (코드 확인)

`RemoteAgent.Await` 는 사람에게 물을 때마다 대기 상한을 `_timeout`(20초)으로 새로 잡는다(큰 티츄·교환 제외 — 단계 시작 공유 마감). 그래서 같은 결정을 **다시 묻는** 다음 경로가 모두 20초를 새로 준다.

| # | 경로 | 위치 |
|---|---|---|
| A | 재접속·자리 비움 복귀 → `SetConnected`/`Submit` 이 `WakeLocked` → `Resume` 표식 → 루프 `continue` 로 재질문. Wi-Fi 끄기는 닫힘 신호가 없어 서버가 끊김을 늦게 알고, 그 사이 새 연결이 붙으면 이 경로를 탄다 | `Await` 루프 |
| B | 내 차례 중 누군가 작은 티츄 → `WakeTurnForTichu` → `DecideTurn` 이 `Yield` → 드라이버가 콜 적용 후 같은 좌석에 `DecideTurn` 재호출 | `DecideTurn` |
| C | 낼 수 없는 수·패스 거부 → `DecideTurn`/`ChooseDragonRecipient` 루프 `continue` → 새 `Await` | 두 결정 메서드 |
| D | 방에 연결된 사람이 아무도 없어 멈춤 → 복귀(`resumedFromPause`) | `Await` 루프 — **결정 2에 따라 유지** |

(참고: 다른 사람이 연결된 판에서 서버가 끊김을 바로 알면 지금도 대리 AI 가 즉시 둔다 — 리셋 없음.)

## 3. 서버 — 결정 마감 (`Tichu.Net/RemoteAgent.cs`, core 와 `Assets/_Project/Net` 미러 동일 수정)

### 3.1 상태

- 새 필드 `DateTime? _decisionDeadlineUtc` (`_gate` 보호). 지금 진행 중인 차례·용 양도 결정의 마감. 없으면 null.

### 3.2 규칙

`Await(kind, …)` 에서 `kind` 가 `Play` 또는 `DragonGift` 이고 사람에게 물을 때(`human`, `_timeout` 유한):

| 상황 | 대기 상한(`waitFor`) |
|---|---|
| `_decisionDeadlineUtc` 가 null | 마감 = 지금 + `_timeout`, `waitFor = _timeout` |
| 마감이 있음 | `waitFor = 마감 − 지금`, 0 아래면 0, `_timeout` 위면 `_timeout`(벽시계 역행 방어) |
| 이번 재질문이 멈춤에서 깨어난 것(`resumedFromPause`) | 마감 = 지금 + `_timeout` 으로 **다시 잡는다**(결정 2) |

- `SecondsLeft` 는 지금처럼 `ceil(waitFor)` 를 싣는다 — 재질문에는 남은 초가 나간다.
- `waitFor == 0` 이면 지금처럼 기다리지 않고 시간 초과로 간다(미리 낸 답 스캔은 그 전에 끝남). 즉 마감이 지난 뒤 돌아오면 그 결정은 바로 대리 AI 가 두고, `_maxTimeouts`(1) 규칙대로 자리 비움이 된다(M5 복귀 동작으로 풀림 — 변경 없음).
- 큰 티츄·교환은 기존 단계 공유 마감을 그대로 쓴다(이 필드를 읽거나 쓰지 않는다).

### 3.3 마감 해제 (결정이 끝났을 때만)

- `DecideTurn` 이 `Pass`·`Play` 를 돌려줄 때, 또는 `Await` 가 null 이라 대리 AI 결정을 돌려줄 때 → 해제.
- `DecideTurn` 이 `Yield`(경로 B) 를 돌려줄 때 → **유지**. 드라이버가 같은 좌석에 다시 묻는다.
- 거부 후 `continue`(경로 C)·`Resume` 재질문(경로 A) → **유지**.
- `ChooseDragonRecipient` 가 반환할 때 → 해제.
- `ResetForNewRound` → 해제(라운드 경계에서 남은 마감이 새 라운드로 새지 않게).
- 해제는 `ClearDecisionDeadline()` 한 메서드로 모은다.

## 4. 메시지 — 축약판에 남은 초 (`Tichu.Net/Awaiting.cs`, 미러 동일)

- `Awaiting.Redacted()` 가 `Kind`·`Seat`·`DeadlineUtc` 에 더해 **`AskId`·`SecondsLeft`** 를 복사한다. `LegalMoves`(null)·`CanPass`(false)는 지금처럼 뺀다.
- 은닉 정보가 아니다: 남은 초는 누구나 아는 시계이고, `AskId` 는 좌석별 일련번호라 다른 좌석이 그 번호로 답해도 좌석 검증에서 걸린다.
- `SeatRotation` 은 이미 두 값을 복사한다(변경 없음). JSON 직렬화도 이미 두 필드를 다룬다.

## 5. 클라이언트 — 이름표 옆 남은 초

### 5.1 `TableViewModel` (순수 로직)

- 새 상태: `_otherSeat`(int, -1=없음), `_otherAsk`(long), `_otherDeadline`(double?, 로컬 시계).
- `OnState(v)` 마다 `TrackOtherDeadline(v.Awaiting)`:
  - `aw` 가 null 이거나 `aw.Seat == _mySeat` 이거나 `aw.Kind` 가 `Play`·`DragonGift` 가 아니거나 `SecondsLeft` 가 null → 남의 마감 해제.
  - 그 밖: `(aw.Seat, aw.AskId)` 가 지금 것과 다를 때만 `_otherDeadline = 지금 + SecondsLeft`(같은 질문의 재전송이 시계를 되돌리지 않게).
- 공개 조회: `int? SeatSecondsLeft(int seat)` — `seat == _otherSeat` 이고 마감이 있으면 `max(0, ceil(마감 − 지금))`, 아니면 null.
- 라운드·매치 초기화 경로(기존 `_turnDeadline = null` 하는 곳)에서 함께 해제.
- 서버는 수가 적용될 때 `Awaiting` 없이 방송하므로(`OnApply`) 차례가 끝나면 자연히 해제된다.

### 5.2 `RuntimeTableView`

- 좌석 1~3 이름표 텍스트를 한 곳에서 조립: `이름` + (자리 비움이면 `(AI)`) + (남은 초가 있으면 ` · N초`).
  - 기존 `SeatAway` 구독은 자리 비움 값만 저장하고, 텍스트는 기존 `Observable.EveryUpdate` 타이머에서 조립해 바뀔 때만 대입한다.
- 내 좌석(0) 이름표("나 · N장")와 아래 가운데 "남은 시간 N초"는 변경 없음.
- `ITableView` 인터페이스는 바꾸지 않는다.

### 5.3 표시하지 않는 경우

- 큰 티츄·교환(네 좌석 동시 질문 — 남의 초가 혼란만 준다).
- 대리 AI·채운 AI 좌석(서버가 묻지 않아 `Awaiting` 방송이 없다).
- 로컬 AI 대전(`SecondsLeft` 가 늘 null).

## 6. 하지 않는 것

- 큰 티츄·교환 단계의 남 시계 표시.
- 시간 초과 뒤 자리 비움 규칙(`_maxTimeouts`) 변경, 멈춤 판 정책 변경.
- 프로토콜 필드 추가·버전 변경.
- 서버의 끊김 감지 속도(keep-alive) 조정.

## 7. 검증

- **TDD (core dotnet `Tichu.Core.Tests` — `RemoteAgentTests` 클래스 필터, Unity 미러 동기화):**
  - 차례 도중 `SetConnected(false→true)` 재질문 → `SecondsLeft` 가 경과만큼 줄어 있다(주입 시계).
  - 작은 티츄 `Yield` 뒤 같은 좌석 재질문 → 남은 초 유지.
  - 낼 수 없는 수 거부 뒤 재질문 → 남은 초 유지.
  - 마감이 지난 뒤 재질문 → `SecondsLeft == 0`, 기다리지 않고 대리 AI.
  - 멈춤(연결된 사람 없음)에서 복귀 → 전체 시간(기존 테스트 유지 확인).
  - 결정이 끝난 뒤 다음 차례 → 전체 시간. 새 라운드 → 전체 시간.
  - 용 양도 재질문 → 남은 초 유지.
  - `Redacted()` 가 `AskId`·`SecondsLeft` 를 싣고 `LegalMoves` 는 null.
- **TDD (Unity `Tichu.Presentation.Tests`):**
  - `TableViewModel.SeatSecondsLeft`: 남의 Play 질문 → 초 계산·감소·0 하한 / 같은 `AskId` 재전송은 마감 불변 / 새 `AskId` 는 다시 잡음 / `Awaiting` null 방송 → null / 큰 티츄·교환 질문 → null / 내 좌석 질문 → 남의 값 null.
- **회귀:** core dotnet 전체(시뮬 포함은 dotnet 에서만), 서버 79, Unity Presentation 전체 + Unity 미러 `RemoteAgentTests` 클래스 필터. ⚠️Unity 러너에서 `Tichu.Core.Tests` 전체 실행 금지.
- **실기(에디터 + S23, 로컬 서버, 개발 APK):** 두 사람 판 — ① 폰 차례에 Wi-Fi 끄고 몇 초 뒤 켜기 → 남은 초가 이어진다 ② 에디터에서 폰 차례를 볼 때 폰 이름표 옆 초가 줄어든다 ③ 혼자 판에서 끊었다 돌아오면 새 20초.
