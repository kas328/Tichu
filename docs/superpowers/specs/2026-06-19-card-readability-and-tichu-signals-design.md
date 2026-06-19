# 가독성·손맛 개선 (D4.1) — 설계 스펙

**날짜:** 2026-06-19
**브랜치:** `feat/p1d-d4` (D4에 이어붙임 — 머지 경계 §7)
**전제:** P1-D D4(카드 아트 코드생성 + DoTween 연출) 구현 완료·리뷰 통과(미머지). 본 스펙은 그 위 가독성/손맛 3개 개선.

## 1. 목표 (플레이테스트 피드백)

플레이 후 사용자 피드백 3건을 해소한다.

1. **무늬색 정리 + 폭탄 글로우** — 빨강 카드(하트/다이아) 테두리가 과도하게 강조됨. 플러시 없는 게임이라 무늬색 구분 불필요. 대신 **내가 폭탄을 보유하면 그 카드들만 빨갛게** 빛나게 해 폭탄 인지를 돕는다.
2. **결과 배너 합산만** — 라운드 종료 배너가 "카드 X / 티츄 Y" 분해로 표시됨. 그냥 팀 총점 "우리 N : 상대 M"으로.
3. **티츄 콜 가시화** — 상대/파트너가 티츄를 외쳐도 인지하기 어려움. 좌석별 **영구 배지** + 콜 **순간 플래시**로 알린다.

## 2. 전역 불변식 (D4에서 승계, 반드시 유지)

- **오라클 비트동일성:** 모든 신규 상태/연출은 `ApplySnapshot`/렌더 반응 경로에만 붙는다. `onApply=null`(테스트/오라클/헤드리스)이면 `ApplySnapshot`이 호출되지 않아 새 ReactiveProperty도 안 바뀌고 렌더 훅도 안 돈다 → 비트동일성 무영향.
- **새 asmdef 0개.** 신규 파일은 기존 `Tichu.Core` / `Tichu.Presentation` 어셈블리 안.
- **DoTween 격리:** `DoTweenPlayAnimator`만 `DG.Tweening` 참조. EditMode 테스트는 DoTween 비의존(NoOp/순수 로직만). 모든 트윈 `SetAutoKill`, 재사용 대상 `DOKill` 선행, **localScale만** 트윈(레이아웃이 position/size 제어).
- **풀링 호환:** 카드 칩은 `CardChipPool` SetActive 재사용. 풀 재사용 시 새 상태 플래그(폭탄 멤버 등)는 `CardView.Set`에서 중립화.

## 3. 기능 1 — 무늬색 정리 + 폭탄 글로우

### 3.1 무늬색 제거 (1a)
- `CardArtFactory.FrameStyle`을 `{ Black, Red, Special }` → **`{ Normal, Special }`**로 축소.
  - `EdgeFor`: `Normal` → 중립 어두운 테두리(`EdgeBlk`), `Special` → 금색(`EdgeGold`). 빨강 전용 테두리(`EdgeRed`) 제거.
  - `StyleFor(Card)`: 특수 → `Special`, 그 외(빨강/검정 무관) → `Normal`.
- 빨강 무늬 식별은 **라벨 텍스트 색**으로만 유지(기존 `CardView.Refresh`의 `CardFormat.IsRed(_card) ? CardRed : CardInk`). 무늬 정보 손실 없음(♥/♦ 글리프 + 빨강 텍스트).
- 영향: `CardArtFactoryTests`의 스타일 관련 단언 갱신(아래 §6). `CardSpriteAtlas.Frame`/`CardView`는 `StyleFor`/`Frame(card)`만 호출하므로 시그니처 무변경.

### 3.2 폭탄 탐지 — `BombScanner` (신규 Core 순수 헬퍼)
- 위치: `Assets/_Project/Core/Combinations/BombScanner.cs`, 네임스페이스 `Tichu.Core.Combinations`. 어셈블리 `Tichu.Core`. Unity 비의존(순수).
- API:
  ```csharp
  public static class BombScanner
  {
      /// <summary>손패에서 폭탄(4카드 동값 / 같은무늬 5+연속)에 속한 카드들의 집합.
      /// 봉황 제외, 턴·트릭 문맥 무관(들고만 있으면 됨).</summary>
      public static HashSet<Card> BombCards(IReadOnlyList<Card> hand);
  }
  ```
- 알고리즘(문맥 무관, 합법성 무관 — 순수 "폭탄 조합에 속하는가"):
  - **4카드 폭탄:** 비특수 카드를 랭크별로 모아 카운트 ≥4인 랭크의 카드 전부를 집합에 추가.
  - **스트레이트플러시 폭탄:** 4무늬 각각, 해당 무늬가 보유한 랭크(2..14)에서 **연속 길이 ≥5인 극대 구간**의 카드 전부를 추가(길이 5 이상 구간의 모든 카드는 어떤 5-윈도우엔 속하므로). 길이 <5 구간은 제외. 마작(Suit.Special)·봉황 제외.
  - 두 폭탄에 동시에 속하는 카드는 `HashSet`이 1회로 처리.
- 기존 `LegalMoveGenerator`의 폭탄 열거 아이디어만 차용한 **독립·턴무관** 함수(GameState 불요). LegalMoveGenerator는 변경하지 않는다.

### 3.3 `CardView` — 폭탄 멤버 글로우
- 추가: `private bool _bombMember;` + `public void SetBombMember(bool on)` (값 설정 후 `Refresh`).
- `Set(card, atlas, faceUp)`에서 풀 재사용 안전을 위해 `_bombMember = false`로 중립화(기존 `_highlight = Normal` 초기화 옆).
- `HighlightColor()` 우선순위: **선택(노랑) > 폭탄(빨강 글로우) > 배정(초록) > 일반(흰색)**.
  - 신규 색 `CardBomb = new Color(0.96f, 0.55f, 0.52f)` (따뜻한 빨강 글로우 — 흰 프레임을 붉게 틴트). 배정(교환)은 Play 페이즈와 겹치지 않으므로 우선순위 충돌 없음.
- lift 없음(폭탄은 높이 변화 없음 — 선택만 lift 유지).

### 3.4 `RuntimeTableView` — 내 손패 폭탄 반영
- 필드 `private HashSet<Card> _bombCards = new HashSet<Card>();`
- `MyHand` 구독에서 `_hand` 설정 직후 `_bombCards = BombScanner.BombCards(_hand);` (선택 토글 때 재스캔 방지 — 손패 변경 시에만 스캔).
- `RenderHand`에서 각 카드에 `cv.SetBombMember(_bombCards.Contains(card));` (내 손패 seat0 전용 — RenderHand 자체가 내 손패만 그림).
- `using Tichu.Core.Combinations;` (이미 `CombinationRecognizer`로 참조 중).

## 4. 기능 2 — 결과 배너 합산만

- `RuntimeTableView.RenderResult` 문자열 1줄 변경:
  - 기존: `$"라운드 종료 — 우리 {r.TeamATotal} : 상대 {r.TeamBTotal}  (카드 {r.TeamACardPoints}/{r.TeamBCardPoints}, 티츄 {r.TeamATichuDelta}/{r.TeamBTichuDelta})"`
  - 신규: `$"라운드 종료 — 우리 {r.TeamATotal} : 상대 {r.TeamBTotal}"`
- N/M = 이번 **라운드** 합산 점수(`TeamATotal`/`TeamBTotal`). 누적(매치) 총점은 좌상단에 별도 유지. `_anim.ResultShown` 배너 팝 훅은 그대로.

## 5. 기능 3 — 티츄 콜 배지 + 플래시

### 5.1 `TableViewModel` — 좌석별 콜 투영
- 추가: `private readonly ReactiveProperty<TichuCall>[] _calls = new ReactiveProperty<TichuCall>[4];` (생성자에서 각 `new ReactiveProperty<TichuCall>(TichuCall.None)`).
- `public ReactiveProperty<TichuCall> SeatCall(int seat) => _calls[seat];`
- `ApplySnapshot(s)`에서 `for (i=0..3) _calls[i].Value = s.Seats[i].Call;` (좌석 손패수 투영 옆).
- 오라클 격리: `ApplySnapshot`은 `onApply=null` 경로 미호출 → 무영향.

### 5.2 `RuntimeTableView` — 배지 + 전이 플래시
- 좌석별 배지 `_callBadges[4]`: 작은 패널(`Image` 배경 + `Text`). `None`이면 `SetActive(false)`.
  - 텍스트/색: `Tichu` → "티츄" 보라(`new Color(0.55f,0.35f,0.78f)`), `GrandTichu` → "大 티츄" 금색(`new Color(0.85f,0.66f,0.22f)`).
  - 배치: 상대(1,2,3)는 프로필 박스 위/이름 옆 오버레이(레이아웃 스택 밖 절대배치 — Info 스택 오버플로 방지). 나(0)는 남쪽 라벨 옆.
- 구독: `SeatCall(i)` → `UpdateCallBadge(i, call)`: 활성·텍스트·색 갱신. 전이 감지용 `_prevCalls[4]`(초기 `None`) 보관. 로직 순서 — `var prev = _prevCalls[i]; _prevCalls[i] = call;`(**항상** 갱신) → `if (call != None && call != prev) _anim.TichuDeclared((RectTransform)badge.transform);`.
  - `_prevCalls`를 항상 갱신해야 라운드 리셋(콜→None)이 반영돼 다음 라운드 같은 좌석의 재콜에서도 플래시가 발화한다(플래시 분기에서만 갱신하면 누락 버그).
  - ApplySnapshot이 매 스냅샷마다 같은 콜을 재투영해도 `call == prev`라 중복 발화 없음.

### 5.3 `IPlayAnimator` — 콜 플래시 훅
- 인터페이스에 `void TichuDeclared(RectTransform badge);` 추가. `NoOpPlayAnimator`는 무시.
- `DoTweenPlayAnimator.TichuDeclared`: null 가드 → `DOKill()` → `localScale = Vector3.one` → 강한 `DOPunchScale`(예: `new Vector3(0.4f,0.4f,0f)`, dur `AnimTiming.TurnPulse`, `SetAutoKill(true)`). 배지가 `SetActive(true)`로 나타나는 것 + 펀치 = 주목 효과. (색 플래시는 DOTween UI 모듈 의존이라 제외 — 코어 Transform 숏컷만.)
- `AnimTiming`에 필요 시 상수 추가하지 않고 `TurnPulse` 재사용(YAGNI).

## 6. 테스트 전략

| 대상 | 테스트 | 어셈블리 |
|---|---|---|
| `BombScanner` | 4카드→4장; 무폭탄→공집합; SF 5연속→5장; SF 6연속→6장; SF 4연속→공집합; 봉황 미포함; 4카드∩SF 중복카드 1회; 마작 SF 제외 | `Tichu.Core.Tests` (EditMode) |
| `CardArtFactory` (수정) | `Frame(Normal)`·`Frame(Special)` non-null·캐시; `Normal≠Special` 스프라이트; `StyleFor`: 빨강/검정→Normal, 특수→Special | `Tichu.Presentation.Tests` |
| `CardView` | 폭탄 멤버 + 비선택 → 배경=`CardBomb`; 선택이 폭탄보다 우선(노랑); `Set` 후 `_bombMember` 중립화(폭탄색 미잔류) | `Tichu.Presentation.Tests` |
| `TableViewModel` | `ApplySnapshot`가 각 좌석 `Call`을 `SeatCall(i)`로 투영 | `Tichu.Presentation.Tests` |
| `RuntimeTableView` 배선 | `RecordingAnimator`가 `SeatCall` None→Tichu 전이 시 `TichuDeclared` 1회 수신; 동일 콜 재투영 시 추가 발화 없음 | `Tichu.Presentation.Tests` |
| `RenderResult` | (육안) 괄호 분해 제거 확인 — 단순 문자열, PlayMode 육안 |
| 연출(팝/펀치/플래시) | (육안) PlayMode |

- 회귀: `Tichu.Presentation.Tests` 88/88 + 신규 그린 유지. `Tichu.Core.Tests`는 `BombScanner` 신규만 추가(기존 무영향). ⚠️ run_tests는 항상 **단일 어셈블리**씩(D3 교훈).
- 오라클: `BombScanner`/`SeatCall`/배지/플래시 모두 `onApply=null` 경로 미접촉 — 최종 리뷰에서 재확인.

## 7. 머지 경계 / 범위

- **머지 경계:** `feat/p1d-d4`에 이어붙여 D4와 함께 머지(§1-1a가 D4의 색 선택을 직접 수정 — "추가 후 제거"가 두 머지에 갈리는 것 방지). 최종 전체-브랜치 리뷰는 D4 8커밋 + 본 개선을 함께 본다.
- **범위 외(YAGNI):** 색 플래시(DoTween UI 모듈 의존), 폭탄 글로우 펄스(상시 다수 카드 펄스 부담 — 정적 틴트로 충분), 사운드(D5), 카드 펀치 외 추가 연출. 폭탄 글로우는 합법성 무관(들고만 있으면 표시 — 사용자 의도).

## 8. 영향 파일 요약

| 파일 | 변경 | 기능 |
|---|---|---|
| `Core/Combinations/BombScanner.cs` | 신규 | 1b |
| `Core/.../CardArtFactory.cs` | FrameStyle 축소 | 1a |
| `Presentation/Views/CardView.cs` | SetBombMember + 우선순위 | 1b |
| `Presentation/Views/RuntimeTableView.cs` | 폭탄 스캔/적용, 결과 문자열, 콜 배지·전이 플래시 | 1b·2·3 |
| `Presentation/ViewModel/TableViewModel.cs` | SeatCall 투영 | 3 |
| `Presentation/Views/IPlayAnimator.cs` | TichuDeclared | 3 |
| `Presentation/Visuals/DoTweenPlayAnimator.cs` | TichuDeclared 구현 | 3 |
| (각 테스트 파일) | 위 §6 | 전 기능 |
