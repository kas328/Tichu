# Tichu Phase 1-D (D1+D2) — 가로 셸 + 앵커화 + CardView 설계

- 날짜: 2026-06-17
- 상태: 승인됨(브레인스토밍 완료) → 구현 계획 단계로
- 선행: **D0 완료**(`ITableView` 추출, `RuntimeTableView`, EditMode 299/296 그린, commit 69f6bdf on `feat/p1d-visual`)
- 상위 설계: `티츄_Phase1_잔여_아키텍처.html` §6.2–6.4, §8(C-1)
- 레이아웃 목업: `.superpowers/brainstorm/p1d-landscape-layout-v2.html`(gitignore)

## 1. 목적

Phase 1-D 비주얼의 첫 두 묶음. 세로 절대좌표 기반 placeholder 테이블을 **가로 1920×1080·앵커 기반**으로 전환하고, 카드 1장을 재사용 가능한 **CardView 프리팹**으로 추출한다(D3 풀링·후속 아트의 토대). 게임 로직·구독 계약(진실/연출 분리)은 불변.

## 2. 핵심 결정 (브레인스토밍 합의)

1. **시각 목표 = 구조 전환 중심, 아트 후속.** 카드 면은 현행 placeholder(텍스트/색 칩) 유지. `CardSpriteAtlas`는 미할당=no-op 드롭인.
2. **실체화 = 앵커 코드빌드 + CardView 프리팹.** 테이블 크롬은 코드로 빌드하되 절대좌표→앵커. 카드 1장만 `.prefab` 자산으로 추출.
3. **단일 뷰 진화.** 새 뷰를 추가하지 않고 `RuntimeTableView`를 제자리에서 가로·앵커로 리팩터(단일 `ITableView` 구현 유지). 롤백=git.
4. **가로 레이아웃**: 나=하단(중앙정렬 가로 손패 팬) / 파트너=상단(가로 뒷면 팬) / 좌·우 상대=양 끝(세로 뒷면 팬) / 트릭=중앙. 점수·페이즈·소원=좌상단, 최근 플레이=우상단, **결정 패널=우하단**, 스몰티츄=좌하단, 스킵=결정 패널 위.
5. **SafeArea 적용**(노치 회피).
6. **프리팹/SO 로딩 = `Resources.Load`**(RuntimeTableView가 POCO라 Inspector 주입 불가; 코드-퍼스트 일관).

## 3. 범위

**포함 (D1):** CanvasScaler 가로 전환, 방향 가로 잠금, `targetFrameRate=60`, `SafeAreaFitter`.
**포함 (D2):** `RuntimeTableView` 레이아웃 앵커화, `CardView` 프리팹+컴포넌트, `CardSpriteAtlas` SO(no-op 가능), 손패/트릭/상대 카드를 CardView로 렌더.
**제외:** D3 카드 풀링(CardView를 풀 단위로만 설계), D4 `ICardAnimator`/DoTween, D5 `IAudioService`, 실제 카드 아트 제작.

## 4. 컴포넌트

| 항목 | 종류 | 역할 |
|---|---|---|
| `RoundBootstrap.CreateCanvas` | 변경 | CanvasScaler `referenceResolution=1920×1080`, `matchWidthOrHeight=1`(높이 기준). `Application.targetFrameRate=60`(Begin에서 1회) |
| `Visuals/SafeAreaFitter.cs` | 신규 MonoBehaviour | **콘텐츠 컨테이너**(펠트 배경 제외, 그 위 모든 UI를 담는 `Content` RectTransform)를 `Screen.safeArea`에 맞게 앵커 인셋. 펠트 배경 Image는 전체화면 유지(노치 뒤까지 채움). 순수 변환 `static (Vector2 min, Vector2 max) ComputeAnchors(Rect safeArea, Vector2 screen)` 분리(테스트용). `OnEnable`/해상도 변경 시 재적용 |
| `Views/CardView.cs` + `Resources/CardView.prefab` | 신규 | 카드 1장 뷰. 자식: bg `Image`, face `Image`(스프라이트), `Text` 라벨(폴백), 선택 시 lift. API: `Set(Card card, CardSpriteAtlas atlas, bool faceUp)`, `SetSelected(bool)`, `SetInteractable(bool, Action onClick)`. SerializeField로 자식 참조 |
| `Visuals/CardSpriteAtlas.cs` + `Resources/CardSpriteAtlas.asset` | 신규 SO | `Sprite Face(Card)`, `Sprite Back` 매핑. 미할당/미발견=null → CardView 텍스트 폴백 |
| `Views/RuntimeTableView.cs` | 변경 | (a) 레이아웃 메서드 절대좌표→앵커(anchorMin/Max+offset). (b) `NewCardChip`/`Back`/트릭 카드 빌드를 `CardView` 인스턴스화로 교체. (c) `Bind`에서 `Resources.Load`로 프리팹·아틀라스 1회 확보. **구독(Subscribe)·제출·합법성 로직 불변** |
| ProjectSettings | 변경 | `allowedAutorotateToPortrait=0`, `...PortraitUpsideDown=0`, `...LandscapeLeft/Right=1` |

## 5. 가로 레이아웃 사양 (앵커)

기준 해상도 1920×1080. 모든 요소는 모서리/중앙 앵커 + offset(절대 px 계산 금지).
계층: `Canvas → Root(felt 배경, 전체화면) → Content(SafeAreaFitter, safe area 인셋) → 아래 요소들`.

- **좌상단(anchor 0,1)**: 점수(총점) / Phase / 소원 — 세로 스택.
- **우상단(anchor 1,1)**: 최근 플레이 로그(최신 위, 항목 5초 페이드는 현행 유지).
- **상단 중앙(anchor 0.5,1)**: 파트너 프로필(사진자리+이름+장수) + 그 아래 가로 뒷면 팬.
- **좌측 중앙(anchor 0,0.5)**: 왼쪽 상대 프로필(좌끝) + 세로 뒷면 팬(오른쪽).
- **우측 중앙(anchor 1,0.5)**: 오른쪽 상대 프로필(우끝) + 세로 뒷면 팬(왼쪽).
- **중앙(anchor 0.5,0.5)**: 트릭 앞면 카드 + 소유자 텍스트. 라운드 결과 배너 오버레이.
- **하단 stretch(anchor 0..1,0)**: 내 손패 — **중앙정렬** 가로 팬. 선택 카드 위로 lift. 위에 내 좌석 라벨/교환 행.
- **우하단(anchor 1,0)**: 결정 패널(프롬프트 + 가로 버튼). 그 위에 스킵 버튼.
- **좌하단(anchor 0,0)**: 스몰 티츄 버튼.

좌석 매핑(시각): seat0=나(하), seat1=오른쪽, seat2=파트너(상), seat3=왼쪽 — 현행과 동일(반시계).

## 6. 데이터 흐름 (불변)

`AsyncGameDriver → vm.ApplySnapshot/RecordPlay → R3 ReactiveProperty → RuntimeTableView 구독 핸들러 → CardView 렌더`. `ITableView` 계약(D0 11종 구독) 그대로. `onApply=null`(오라클)이면 ②③ 미발화 불변.

## 7. 테스트 (TDD, EditMode 우선)

- `SafeAreaFitterTests`: `ComputeAnchors(safeArea, screen)` 순수 함수 — safeArea가 화면보다 작을 때 정확한 정규화 앵커(min/max) 산출; safeArea=전체화면이면 (0,0)~(1,1).
- `CardViewTests`: `Set` 일반카드 → 라벨 "A♥"·색(빨강/검정); atlas에 스프라이트 있으면 face 표시·라벨 숨김; `faceUp=false` → 뒷면; `SetSelected(true)` → lift 적용.
- `ITableViewContractTests`(기존): D2 후에도 그린 = 앵커화 desync 회귀 가드.
- **가로 PlayMode 스크린샷(MCP)**: 좌석/손패중앙/패널 위치 육안 확인. (SafeArea 인셋은 에디터 safeArea=full이라 시각검증 불가 → 단위테스트로 커버.)

## 8. 검증 게이트(DoD)

1. EditMode 전체 그린(기존 296 + 신규 SafeArea/CardView 테스트, 0 fail).
2. 컴파일 클린.
3. 가로 PlayMode 스크린샷에서 레이아웃 정상(노치 없는 에디터 기준).

## 9. 리스크 / 완화

- **C-1(major)**: 가로 전환과 앵커화는 반드시 동시(한 PR). 부분 적용 시 절대좌표 잔존으로 레이아웃 파손.
- **앵커화 회귀**: 770줄 레이아웃 수정 → 계약 테스트 + 스크린샷 가드, 롤백 git.
- **Resources 사용**: MVP placeholder 한정 허용(Addressables는 과투자). 카드 아트 도입(후속) 시 재검토.
- **CardView GUID**: 프리팹은 `Resources.Load<CardView>("CardView")`로 경로 로드 — 씬/직렬화 참조 없음.

## 10. 비범위(명시)

D3 풀링, D4 애니(DoTween), D5 사운드, 실제 카드 스프라이트 제작. 메뉴 셸은 P1-C에서 가로 대응 완료(범위 밖).
