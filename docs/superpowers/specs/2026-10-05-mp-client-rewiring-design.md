# 멀티플레이 클라이언트 재배선 — 설계 스펙

- **날짜:** 2026-10-05
- **트랙:** Phase 3 멀티플레이(친구방). M1 다음 증분. **계획서의 M4 핵심을 M2(서버) 앞으로 당겼다.**
- **상태:** 설계 승인됨(brainstorming). 구현 착수.
- **배경:** M1 스펙 `2026-10-04-mp-m1-session-protocol-design.md` · M1 머지 `bc941bd`

## 1. 왜 M2보다 먼저인가

M1이 만든 프로토콜은 **실제 UI를 한 번도 만나지 않았다** — 지금까지 소비자는 테스트뿐이다. 현재 UI는 서버 상태를 직접 읽는다: `TableViewModel.ApplySnapshot(GameState)`가 권위 상태 전체를 받고, `CurrentTrick`은 엔진의 `Trick` 타입이며, `IHumanInputPort`의 여섯 메서드가 전부 `DecisionContext`(= 전체 `GameState` 래퍼)를 받는다. 얇은 클라이언트가 받는 것은 `PlayerView`/`TrickView`/`Awaiting`뿐이다.

소켓과 서버가 프로토콜에 의존한 **뒤에** 그 틈을 발견하는 것이 비싼 순서다. 이 증분은 인프라 0·운영 0으로 프로토콜·세션·메인스레드 마샬링을 한꺼번에 검증하고, 기존 싱글플레이 플레이테스트가 그대로 회귀 검사가 된다. 실제로 설계 단계에서 이미 두 가지가 드러났다(§5.1, §5.2).

## 2. 목표

싱글플레이가 멀티플레이와 **동일한 경로**를 돈다 — `MatchSession`(전용 스레드) + `LoopbackTransport` + 좌석 0 `RemoteAgent`. UI는 `PlayerView`/`Awaiting`만 보고 `ClientAction`만 보낸다. 서버는 여전히 0대다.

## 3. 성공 기준 (DoD)

1. **세션 경로 완주** — 싱글플레이가 목표 점수 도달까지 예외 없이 한 매치를 끝낸다.
2. **UI가 권위 상태를 보지 않는다** — `TableViewModel`과 `RuntimeTableView`에 `GameState`·`Trick`·`DecisionContext`·`GameAction` 참조가 0이다. 열거형과 값 타입(`GameActionKind`·`Card`·`CombinationType`·`TichuCall`·`RoundPhase`)과 불변 `Combination` 은 권위 상태가 아니므로 금지 대상이 아니다.
3. **회귀 0** — dotnet 전체와 유니티 EditMode 전부 그린(Presentation 145 포함), 미러 동기.
4. **사람 실기 플레이테스트** — 손맛이 이전과 구별되지 않는다: 플레이 팝·차례 펄스·결과 팝, 효과음, 플레이 로그, 개 에코, 폭탄 빨강 테두리, 티츄 좌석 배지·콜 플래시. 이 기준은 사람만 판정할 수 있다.

## 4. 구조

```
MatchSession (전용 스레드)                  TichuClient (메인 스레드)
  GameDriver + RemoteAgent(좌석0)   ──┐       매 프레임 Drain(mySeat)
  + PacedPimcAgent×3                       │   ┌── StateUpdate  → vm.Apply(PlayerView)
  onApply/OnAwait → LoopbackTransport─┘   ├── Awaiting      → vm.Awaiting
                       ▲                  ├── RoundScored   → vm.RoundResult
                       │                  ├── MatchEnded    → 셸로 전달
                       └── Submit ────────┴── vm.Submitted(ClientAction) 구독
```

`RoundBootstrap`이 세션·전송·클라이언트를 조립한다. `TableViewModel`은 **프로토콜을 모르는 표시 상태 홀더**로 남고, 내보내는 쪽은 기존 R3 관용구(`Observable<GameAction> Played`와 같은 모양)로 `Observable<ClientAction> Submitted` 하나를 노출한다.

## 5. 프로토콜 확장 (`core/src/Tichu.Net`)

### 5.1 `AppliedAction` — `StateUpdate`가 "직전에 적용된 액션"을 싣는다

**재배선이 드러낸 진짜 틈이다.** 뷰의 `Played`(`Observable<GameAction>`) 채널이 세 가지를 구동하는데 상태 스냅샷만으로는 전부 불가능하다 — 플레이 로그(사람이 카드를 세는 근거), `SfxMap.For(액션)` 효과음, 개 카드 에코(개는 트릭을 만들지 않는다). `TrickView.History`로는 부족하다: 티츄 콜·교환·용 양도는 트릭 엔트리가 없고, 트릭을 끝낸 마지막 플레이는 직후 `CurrentTrick`이 `null`이 되어 사라진다. 기획서 7.2의 메시지 카탈로그가 이미 `StateUpdate ... lastCombo`로 이를 예고했다.

**좌석 안전성은 공짜로 해결된다.** 뷰가 실제로 읽는 필드를 전수 확인했다 — `Kind` · `Seat` · `Cards`(Play만) · `RecipientSeat`(용 양도만)뿐이고, **교환은 `FormatAction`이 `"?"`로 떨어뜨려 페이로드를 쓰지 않는다**. 적용된 액션은 전부 공개 정보다(낸 카드는 앞면, 콜은 이미 `PlayerView.Calls`에 있다). 유일한 비밀인 교환 카드는 뷰가 원하지도 않으므로 `Kind`+`Seat`만 싣고 페이로드를 생략한다 — 기획서 7.5의 "교환 결과는 자신이 받은 것만"과 일치한다.

`AppliedAction { GameActionKind Kind; int Seat; List<Card>? Cards; int RecipientSeat; }` 를 추가하고 — 엔진이 **적용한** 것을 서술하므로 `ClientActionKind`(클라이언트→서버)가 아니라 `Tichu.Core.Game.GameActionKind` 를 쓴다. `SfxMap.For` 가 이미 그 열거형으로 분기하므로 스위치가 거의 그대로 살아남는다. 둘은 설계상 1:1이다. `MatchSession.OnApply`가 `StateUpdate`에 함께 실어 보낸다. `Exchange`는 `Cards`를 채우지 않는다. 마스킹 불변식은 그대로이며, M1의 누설 가드 테스트가 계속 지킨다.

### 5.2 `TrickView`가 `Combination? Top`을 그대로 싣는다

뷰는 `trick.Top`을 `Combination`으로 직접 쓴다 — `CardFormat.TrickSortKey(trick.Top, c)`에 넘기고, 선택 하이라이트용 `TrickContext`를 `Top.Type`·`Top.Rank`로 만든다. 현재 `TrickView`는 이를 `TopType`/`TopRank`/`TopLength`/`TopCards`로 분해해 들고 있다.

`TrickView`가 `Combination? Top`을 그대로 싣고 분해 필드를 없앤다. 근거는 `Awaiting.LegalMoves`와 동일하다 — `Combination`은 불변이고 `Recognize`가 카드를 새 배열로 복사하므로 보관이 안전하며(`LegalMoveGenerator.cs:218-219`), 트릭의 Top은 앞면이라 공개 정보다. `Top == null`이 "아직 리드 없음"을 명확히 표현하므로 `CombinationType.Invalid` 판별도 불필요해진다. **M1 설계의 단순화이고 재배선이 찾아낸 것이다.** 대가는 `ViewProjectorTests`의 `TopCards` 단언을 `Top.Cards`로 고치는 것뿐이다.

### 5.3 `PacedPimcAgent` — AI 관전 지연 (위치·기법 정정)

지연이 **모든** 결정에 걸려 있지 않다. `PimcDecisionAgent`는 턴 결정과 **실제로 폭탄을 던질 때만**(`if (bomb != null)`) 지연하고, 큰 티츄·교환·작은 티츄·용 양도는 즉시 반환한다. 폭탄 조건이 중요하다 — 폭탄 창은 매 트릭 세 좌석을 폴링하므로 무조건 지연을 걸면 트릭마다 3배 지연이 붙는다.

⚠️ **계획 수립 중 이 절의 두 가지가 거짓으로 판명되어 정정한다.**

**위치**: `PimcAgent`·`Pimc`·`PolicyConfig`·`Determinizer`는 `core/`에 없다 — 유니티 쪽 `Assets/_Project/GameFlow/`에만 있다(README가 남겨 둔 "PIMC는 아직 유니티 GameFlow 에만" 그대로이며, 미러가 단방향이라 유니티가 추가 파일을 갖는다). 따라서 `Tichu.Net`의 데코레이터는 `PimcAgent`를 참조할 수 없다. **`PacedPimcAgent : IAgent` 를 `Assets/_Project/Presentation/Agents/` 에 둔다** — 기존 `PimcDecisionAgent` 의 동기 쌍둥이이고, 같은 이유로 `core/` 에 미러되지 않는다. 서버가 빈자리 AI를 페이싱하려면 PIMC를 `core/` 로 승격해야 하며, 그 승격은 이미 M5 항목으로 추적되고 있다.

**기법**: 순진한 "잠들고 계산"은 체감을 망친다. `PimcDecisionAgent` 는 탐색을 `UniTask.RunOnThreadPool` 로 먼저 띄우고 관전 지연과 **겹친다** — `per-move ≈ max(지연, 연산)`. 티어 사다리가 바로 이 겹침에 의존한다(탐색이 900ms 지연과 겹쳐 900 이내 무지연 → Normal 16 / Hard 20 / Expert 24, 예산 900 통일). 겹침을 잃으면 매 턴이 `지연 + 연산`이 되어 최대 2배로 늘고 티어 정합성 작업이 무효가 된다.

세션이 **자기 전용 스레드**를 소유하므로 스레드풀 핸드오프의 동기(유니티 메인 스레드 비차단)가 사라진다. 그래서 더 단순하게 같은 성질을 얻는다: **연산을 먼저 하고 남은 시간만 `Thread.Sleep` 한다.** 추가 스레드가 없고, 소유 스레드가 직접 계산하므로 방어적 `ctx.State.Clone()` 도 불필요해져 AI 턴마다 전체 상태 복사 하나가 사라진다. anytime 예산 CTS(`CancellationTokenSource(budgetMs)`)와 Easy 단축(`PolicyConfig.Worlds <= 0` → 예산 없는 `DecideTurn`)은 그대로 미러한다.

## 6. Presentation 변경

- **`TichuClient`(신규)** — `ITichuTransport`를 들고 `Pump()`에서 `Drain(mySeat)`을 비워 `StateUpdate`/`Awaiting`/`RoundScored`/`MatchEnded`/`ActionRejected`를 뷰모델에 반영하고, 뷰모델의 `Submitted`를 구독해 전송한다. `ActionSeq`를 좌석별로 단조 증가시킨다. 프로토콜 번역이 이 한 파일에 갇히므로 M2에서 전송을 갈아끼울 때 **여기만 손댄다.**
- **`TableViewModel`** — `ApplySnapshot(GameState)` → `Apply(PlayerView)`. `CurrentTrick`은 `ReactiveProperty<TrickView?>`. `PendingDecision`(`DecisionRequest`) → `ReactiveProperty<Awaiting?>`. `Played`는 `AppliedAction`을 흘린다. `IHumanInputPort` 구현 제거, 대신 `Observable<ClientAction> Submitted` 노출 — 기존 `SubmitGrandTichu`/`SubmitExchange` 등은 완료 소스를 채우는 대신 `ClientAction`을 발행한다. 이들의 `bool` 반환은 의미가 바뀐다: **현재 `Awaiting`이 그 제출을 허용하는가**(즉시 판정 가능한 로컬 UX 가드)이고, 권위 있는 답은 이후 `StateUpdate` 또는 `ActionRejected`로 온다. `ActionRejected`를 받으면 뷰모델은 입력을 다시 열고 일시 메시지를 노출한다 — M1의 재시도 설계상 거부 직후 **새 `Awaiting`이 반드시 따라오므로** 클라이언트는 그걸 기다리면 된다. 활성화 게이트는 `Awaiting`에서 나온다(`Awaiting.LegalMoves`/`CanPass`가 서버가 준 합법수이므로 뷰가 `LegalMoveGenerator`를 직접 돌릴 필요가 없다).
- **`RuntimeTableView`** — 바인딩 이름은 유지되므로 변경은 타입 교체에 집중된다(`Trick`→`TrickView`, `GameAction`→`AppliedAction`, `DecisionRequest`→`Awaiting`).
- **`RoundBootstrap`** — `AsyncGameDriver` 루프를 `MatchSession` 조립 + `TichuClient` 프레임 펌프로 교체한다. 펌프는 `UniTask.Yield(PlayerLoopTiming.Update)` 루프로 기존 `GetCancellationTokenOnDestroy()` 수명에 얹고, 씬 종료 시 세션 `Stop()`+`Join()`을 같은 자리에서 처리한다. 새 MonoBehaviour를 만들지 않는다.

## 7. 파킹 해제: 교환 제출 현황

M1 스펙이 "교환 제출 현황 표시는 `RoundSetup`이 `internal`이라 M4에서 접근 방식 결정"으로 파킹했다. **`AppliedAction`이 해결한다** — 드라이버가 교환을 좌석별로 적용하므로 `AppliedAction(Exchange, seat)`이 네 번 오고, 클라이언트가 그것을 세면 된다. `RoundSetup`을 열거나 `InternalsVisibleTo`를 쓸 필요가 없다.

## 8. 테스트

- `TrickView.Top` 전환 후 `ViewProjectorTests` 갱신(단언 의미는 보존).
- `AppliedAction`: 교환 액션에 `Cards`가 없음 · Play 액션에 카드가 있음 · 모든 `StateUpdate`가 직전 액션을 싣는지(로그 수와 일치).
- `PacedPimcAgent`: 지연이 턴 결정과 "폭탄을 실제로 던질 때"에만 걸리고 나머지 네 메서드는 즉시 반환 — 폴링당 지연 누적 회귀를 막는 가드.
- `TichuClient` 왕복: 루프백 세션 + 가짜 뷰모델로 `Awaiting` 수신 → `ClientAction` 제출 → 상태 반영까지. `ActionSeq` 단조성과 `ActionRejected` 경로 포함.
- `TableViewModel`: 새 모양의 헤드리스 테스트. 기존 Presentation 145개가 회귀 검사.
- **유니티 EditMode 필수** — dotnet 그린은 유니티 그린을 뜻하지 않는다(M1에서 `Does.Not.Contain(object)` 하나가 EditMode 어셈블리를 깨뜨렸다). 새 테스트는 NUnit 3.5 계열에서도 컴파일되는 단언만 쓴다.

## 9. 범위 밖 (YAGNI)

- **비동기 경로 삭제** — `AsyncGameDriver`·`IDecisionAgent`·`PimcDecisionAgent`·`AiDecisionAgent`·`HumanAgent`·`IHumanInputPort`·`DecisionRequest`가 죽지만 **지우지 않고 사용 중단 주석만 단다**. UI 재배선과 드라이버 2개·에이전트 4개·동기↔비동기 오라클 삭제를 한 증분에 넣으면 폭발 반경이 리뷰 불가 수준이 된다. 삭제는 별도 패스.
- 실제 전송·직렬화·서버·호스팅(M2) · 방·초대 코드·좌석 배치(M3) · 로비 UI·턴 타이머 표시(M3/M5) · 타임아웃 자동 플레이·재접속·AI 인계(M5) · 계정·랭킹(M6).
- 봉황 스트레이트 하단 확장은 **의도적으로 고치지 않는다**(누락 수가 전략적으로 지배됨 — M1 스펙 §11.1).

## 10. 승인된 결정

① 다음 증분 = 클라이언트 재배선(M2보다 먼저) ② 전송까지 전체 경로(싱글이 진짜 멀티 경로를 돈다) ③ 클라이언트 층 분리 신설(뷰모델은 프로토콜을 모른다) ④ `AppliedAction`을 `StateUpdate`에 추가, 교환 페이로드 생략 ⑤ `TrickView`가 `Combination? Top`을 그대로 싣는다 ⑥ `PacedPimcAgent`는 유니티 쪽 Presentation(PIMC가 core에 없음), 지연 배치 1:1 미러 + `max(지연, 연산)` 겹침 보존 ⑦ 비동기 경로는 사용 중단 표시만, 삭제는 별도 패스.
