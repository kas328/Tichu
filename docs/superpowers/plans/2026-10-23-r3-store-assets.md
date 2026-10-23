# R3 스토어 자산 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Google Play 비공개 테스트 게시에 필요한 스토어 자산(새 앱 정체성·아이콘·피처 그래픽·스크린샷·한국어 등록정보·방침 페이지 이름)을 만든다.

**Architecture:** 그림 원본은 `docs/store/src/` 의 HTML(인라인 SVG)로 두고 Edge 헤드리스로 PNG 를 뽑는다. Unity 쪽 변경은 `ProjectSettings.asset`(이름·패키지·아이콘)뿐이고 게임 코드는 건드리지 않는다. 모든 산출물은 검사 스크립트(크기·알파·글자 수)와 서명 AAB 매니페스트로 검증한다.

**Tech Stack:** Unity 6000.3.17f1(PlayerSettings API, MCP `execute_code`) · Edge 헤드리스 · Windows PowerShell 5.1 System.Drawing · bundletool 1.17.2/aapt2(Unity 번들)

**Spec:** `docs/superpowers/specs/2026-10-18-r3-store-assets-design.md`

## Global Constraints

- 스토어 제목 **용과 봉황: 팀 카드게임** · 홈 화면 이름(`productName`) **용과 봉황** · 패키지 **`com.kas328.dragonphoenix`** (사용자 2026-10-23 확인: Play 업로드 이력 없음)
- 스토어 언어 한국어만. 이름에 "Tichu" 없음. 자세한 설명에 "티츄(Tichu) 규칙" 사실 표기 + 면책 문구 그대로: **"본 앱은 Tichu 의 저작권자·상표권자 및 공식 라이선스 앱과 관련이 없는 독립 제작물입니다."**
- 그대로 둘 것: 서명 키(`~/.keys/tichumaster-upload.jks`, 별칭 `tichumaster`)·`TICHU_KEYSTORE_*`·빌드 산출물 파일명(`TichuMaster-*`)·게임 안 규칙 용어·과거 보고서/스펙/계획·`applicationIdentifier.Standalone`
- 그림은 기존 `Assets/_Project/Art/Cards/Icons/Dragon.svg`·`Phoenix.svg` 를 그대로 끌어 쓴다(공식 아트·새 그림체 없음). 펠트색 = `RuntimeTableView.Felt`(0.07, 0.22, 0.13) = `#123821`. 글꼴 Malgun Gothic.
- 문구에 측정 없는 주장 금지("최강 AI" 등). 기능 서술은 구현된 것만.
- 폰 실기 확인 안 함 → 보고서의 "실기 대기 목록"에 쌓는다. Play Console 입력·업로드는 사용자가 한다.
- 머지·푸시(gh-pages 포함)는 사용자 승인 후. gh-pages 는 `git add privacy/index.html` 만(`-A` 금지), 브랜치 삭제·이름 변경 금지.
- ⚠️ Unity 테스트는 `Tichu.Presentation.Tests` 필터로만(전체 실행 시 Sim 10만 판이 에디터를 멈춤).

### 스펙과 다르게 가는 점(근거 있음)

1. **versionCode**: `ReleaseBuild.BuildReleaseAab` 가 빌드마다 `bundleVersionCode += 1` 을 하므로 2 가 아니라 **3** 이 된다. 새 패키지의 첫 업로드는 어떤 값이든 되므로 그대로 둔다.
2. **Round·Legacy 아이콘**: `AndroidMinSdkVersion: 26` 이라 모든 지원 기기가 적응형 아이콘을 쓴다. Round·Legacy 에는 같은 `icon-legacy.png` 하나를 넣는다(별도 원형 그림 없음).
3. **스크린샷 알파**: Play 는 스크린샷도 JPEG 또는 **24비트 PNG(알파 없음)** 만 받는다. 피처 그래픽과 같은 검사를 스크린샷에도 적용한다.
4. **"온라인 친구방" → "온라인 대전(자동 매칭, 빈자리는 AI)"**: 구현된 것은 M3 랜덤 매칭+AI 충원이고 친구방(닉네임·코드)은 출시 후로 미뤘다(M4 스펙). 온라인 단락은 M2b 배포 전엔 서비스가 안 되므로 등록정보에 **"M2b 배포 전 게시 시 이 단락을 뺄 것"** 주석을 단다.

## Review Focus

1. **피처 그래픽·스크린샷에 알파 채널이 섞여 들어옴**(Edge·Unity 캡처는 RGBA 를 낼 수 있음) → Play 업로드 거부. 검사 스크립트가 잡고 24비트로 다시 저장한다(Task 1 Step 4, Task 3 Step 4).
2. **적응형 전경의 용 카드가 안전 영역 밖으로 나감** → 원·물방울 마스크 런처에서 카드 모서리가 잘림. 카드 외곽 사각형이 중심 지름 264px 원 안에 들어가야 한다(Task 1 Step 3 의 검사).
3. **한글 `productName` 이 Gradle/빌드 경로를 깨뜨림** → AAB 빌드 실패. Task 5 가 게이트, 실패 시 스펙 4장의 대안(영문 productName + Android 라벨 리소스).
4. **빌드가 비정상 종료되어 키스토어 경로가 `ProjectSettings.asset` 에 남음** → 로컬 절대경로 커밋. Task 5 Step 4 의 내용 검사(드라이브 문자·경로 구분자·사용자명).
5. **스크린샷에 개발 표시(FPS 오버레이·주소 입력칸 등)가 찍힘**, 또는 Overlay 캔버스가 빠져 빈 테이블만 찍힘 → 화면과 다른 인상. 모든 캡처를 사람 눈 대신 Read 로 열어 확인한다(Task 3 Step 3).

참고(낮음): 에디터 `productName` 변경으로 에디터 PlayerPrefs(난이도·볼륨 등) 저장 위치가 바뀌어 에디터에서 한 번 초기화돼 보일 수 있다 — 기능 결함 아님, 보고서에 한 줄 적는다.

---

## File Structure

| 경로 | 책임 |
|---|---|
| `docs/store/src/icon.html` · `feature.html` · `appicon-bg.html` · `appicon-fg.html` · `appicon-legacy.html` | 그림 원본(인라인 SVG, 외부 리소스 0) |
| `docs/store/src/render.ps1` | Edge 헤드리스로 위 HTML → PNG 일괄 렌더(산출 경로 고정) |
| `docs/store/src/check-store-images.ps1` | 모든 산출 PNG 의 크기·알파 규칙 검사, 위반 시 exit 1 |
| `docs/store/src/check-listing.ps1` | `listing.html` 의 각 필드 글자 수 검사, 위반 시 exit 1 |
| `docs/store/src/README.md` | 다시 뽑는 명령과 규격 표 |
| `docs/store/icon-512.png` · `feature-1024x500.png` | Play 업로드용 |
| `docs/store/screens/01-*.png` … | 스크린샷 4~6장 |
| `docs/store/listing.html` | 한국어 등록정보 |
| `Assets/_Project/Art/AppIcon/icon-bg.png` · `icon-fg.png` · `icon-legacy.png` | 런처 아이콘 원본(432×432) |
| `ProjectSettings/ProjectSettings.asset` | productName · 패키지 · 아이콘 |
| `README.md` | 첫 줄 제목만 |
| `gh-pages:privacy/index.html` | "Tichu Master" → 새 이름. 스펙은 4곳이라 했으나 실제 5곳(줄 6·29·31·66·68), Task 4 참조 |

---

### Task 1: 아이콘·피처 그래픽 원본과 PNG

**Files:**
- Create: `docs/store/src/{icon,feature,appicon-bg,appicon-fg,appicon-legacy}.html`, `docs/store/src/render.ps1`, `docs/store/src/check-store-images.ps1`, `docs/store/src/README.md`
- Create(산출): `docs/store/icon-512.png`, `docs/store/feature-1024x500.png`, `Assets/_Project/Art/AppIcon/icon-{bg,fg,legacy}.png`

**Interfaces:**
- Produces: `check-store-images.ps1` — 인자 없음, 저장소 루트 기준 상대 경로를 검사하고 위반 줄마다 `FAIL <path> <이유>` 출력 후 exit 1, 전부 통과면 `OK n files` exit 0. `screens/` 가 비어 있으면 스크린샷 규칙은 건너뛴다(Task 3 에서 채움). Task 2 는 `Assets/_Project/Art/AppIcon/*.png` 를 소비한다.

- [ ] **Step 1: 검사 스크립트 먼저 작성 — `check-store-images.ps1`**

`powershell.exe`(5.1) 로 돈다. `System.Drawing.Image.FromFile` 로 열어 `Width/Height` 와 `PixelFormat`(문자열에 `Alpha` 포함 여부)을 본다. 규칙 표:

| 경로 | 크기 | 알파 |
|---|---|---|
| `docs/store/icon-512.png` | 512×512 | 상관없음 |
| `docs/store/feature-1024x500.png` | 1024×500 | **없어야 함** |
| `docs/store/screens/*.png` | 1920×1080 | **없어야 함** |
| `Assets/_Project/Art/AppIcon/icon-bg.png` | 432×432 | 상관없음 |
| `Assets/_Project/Art/AppIcon/icon-fg.png` | 432×432 | **있어야 함**, 그리고 네 모서리 픽셀 alpha=0 |
| `Assets/_Project/Art/AppIcon/icon-legacy.png` | 432×432 | 상관없음 |

추가 규칙(Review Focus 2): `icon-fg.png` 에서 alpha>0 인 픽셀은 모두 중심(216,216)에서 거리 ≤ 132 여야 한다(지름 264px = 66dp 안전 영역). 위반 시 `FAIL ... outside safe zone (max r=NNN)`.

- [ ] **Step 2: 실행해 실패 확인**

Run: `powershell.exe -NoProfile -File docs/store/src/check-store-images.ps1`
Expected: `FAIL ... missing` 줄들, exit 1

- [ ] **Step 3: HTML 원본 5장 + `render.ps1` 작성, 렌더**

- 공통: `<body>` 여백 0, 고정 크기 `<svg>` 하나, `Dragon.svg`/`Phoenix.svg` 의 `<path>`/`<circle>` 을 `<g transform>` 으로 인라인. 카드 = 크림색 둥근 사각형 + 금색 테두리(특수 카드 느낌), 가운데에 해당 아이콘.
- `icon.html` 512×512: 펠트 `#123821` 꽉 채움 + 살짝 기운(약 −8°) 용 카드.
- `feature.html` 1024×500: 펠트 배경, 왼쪽에 용·봉황 카드 두 장(겹치게 기울임), 오른쪽에 "용과 봉황"(큰 글씨)·"팀 카드게임"(부제), `font-family: 'Malgun Gothic'`.
- `appicon-bg.html` 432×432: 펠트 단색(+선택: 아주 옅은 비네트).
- `appicon-fg.html` 432×432: **투명 배경**, 용 카드가 중심 지름 264px 원 안(기운 상태 포함). 
- `appicon-legacy.html` 432×432: 투명 배경 위 둥근 사각 펠트(안쪽 여백 약 24px) + 용 카드.
- `render.ps1`: 각 HTML 을 `msedge.exe --headless=new --disable-gpu --hide-scrollbars --default-background-color=00000000 --window-size=W,H --screenshot=<out> file:///<abs>` 로 렌더. Edge 경로 `C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe`. 출력 경로는 위 File Structure 의 산출 경로 그대로.

Run: `powershell.exe -NoProfile -File docs/store/src/render.ps1`

- [ ] **Step 4: 검사 통과까지 수정**

Run: `powershell.exe -NoProfile -File docs/store/src/check-store-images.ps1`
Expected: `OK 6 files` (screens 없음), exit 0.
피처 그래픽에 알파가 나오면 `render.ps1` 끝에서 System.Drawing 으로 `Format24bppRgb` 로 다시 저장(검사 스크립트가 아니라 렌더 스크립트가 고친다). 전경이 투명하지 않게 나오면 `--headless=old` 로 바꿔 본다.

- [ ] **Step 5: 눈으로 확인**

생성된 PNG 5장을 Read 로 열어 본다: 카드가 잘리지 않았는지, 글자가 깨지지 않았는지(□ 두부 글자 없음), 펠트색이 게임과 같은 계열인지.

- [ ] **Step 6: `docs/store/src/README.md` 작성 후 커밋**

README = 규격 표(Step 1 표) + 두 명령(render, check) + "원본은 HTML 을 고치고 다시 렌더".

```bash
git add docs/store Assets/_Project/Art/AppIcon
git commit -m "feat(r3): 스토어 아이콘·피처 그래픽·런처 아이콘 원본과 렌더·검사 스크립트"
```
(`Assets/_Project/Art/AppIcon/*.png.meta` 는 Task 2 에서 Unity 가 만든 뒤 함께 커밋.)

---

### Task 2: 앱 정체성과 런처 아이콘을 Unity 에 적용

**Files:**
- Modify: `ProjectSettings/ProjectSettings.asset`(productName 줄 16, applicationIdentifier.Android 줄 170, 아이콘 블록)
- Modify: `README.md:1`
- Create: `Assets/_Project/Art/AppIcon/*.png.meta`, `Assets/_Project/Art/AppIcon.meta`

**Interfaces:**
- Consumes: Task 1 의 `Assets/_Project/Art/AppIcon/icon-{bg,fg,legacy}.png`
- Produces: Task 5 가 확인할 값 — `PlayerSettings.productName == "용과 봉황"`, `PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android) == "com.kas328.dragonphoenix"`

- [ ] **Step 1: 텍스처 임포트**

MCP `execute_code`(CodeDom C#6, raw HTTP 헬퍼) 로 세 PNG 를 `AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate)` → 각 `TextureImporter` 를 `textureType=Default`, `alphaIsTransparency=true`, `mipmapEnabled=false`, `npotScale=None`, `textureCompression=Uncompressed`, `maxTextureSize=512` 로 두고 `SaveAndReimport()`.

- [ ] **Step 2: 이름·패키지·아이콘 설정**

같은 방식으로:
- `PlayerSettings.productName = "용과 봉황"`
- `PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "com.kas328.dragonphoenix")`
- `PlayerSettings.GetPlatformIcons(NamedBuildTarget.Android, AndroidPlatformIconKind.Adaptive)` 의 모든 슬롯에 `SetTextures(bg, fg)`; `AndroidPlatformIconKind.Round`·`Legacy` 의 모든 슬롯에 `SetTexture(legacy)`; 각각 `SetPlatformIcons` 로 되돌려 넣기.
- `AssetDatabase.SaveAssets()`

- [ ] **Step 3: 직렬화 확인**

Run: `git diff --stat ProjectSettings/ProjectSettings.asset` 그리고 `grep -n "productName\|Android: com.kas328\|m_BuildTargetPlatformIcons" -A3 ProjectSettings/ProjectSettings.asset`
Expected: `productName: "용과 봉황"`(유니코드 이스케이프 형태여도 됨), `Android: com.kas328.dragonphoenix`, 아이콘 블록에 AppIcon 텍스처 GUID 3개(`.png.meta` 의 guid 와 일치). 다른 설정이 같이 바뀌었으면(예: `runInBackground`, `preloadedAssets`) 되돌린다. 디스크에 아직 안 써졌으면(메모: PlayerSettings 직렬화 지연) `AssetDatabase.SaveAssets()` 를 한 번 더 부르고 다시 확인.

- [ ] **Step 4: README 제목**

`README.md` 첫 줄 `# Tichu Master` → `# 용과 봉황`. 본문은 그대로.

- [ ] **Step 5: 회귀**

MCP `run_tests{mode:"EditMode", assembly_names:["Tichu.Presentation.Tests"]}` → `get_test_job`. 판정: 실패 없음 + total>0 (메모의 3갈래 판정).
Expected: 기존 통과 수(최근 271) 그대로 전부 통과.

- [ ] **Step 6: 커밋**

```bash
git add ProjectSettings/ProjectSettings.asset README.md Assets/_Project/Art/AppIcon Assets/_Project/Art/AppIcon.meta
git commit -m "feat(r3): 앱 이름 '용과 봉황'·패키지 com.kas328.dragonphoenix·적응형 런처 아이콘"
```

---

### Task 3: 스크린샷 1920×1080

**Files:**
- Create: `docs/store/screens/01-*.png` … (4~6장)

**Interfaces:**
- Consumes: Task 1 의 `check-store-images.ps1`(스크린샷 규칙)

- [ ] **Step 1: 캡처 방식 확정**

Play 모드에서 MCP `execute_code` 한 번으로: 1920×1080 `RenderTexture` 를 만들고, Overlay 캔버스들을 잠시 `ScreenSpaceCamera`+`worldCamera=Camera.main` 으로 바꾼 뒤 `Camera.main.targetTexture=rt; Render()`, `Texture2D(1920,1080, RGB24)` 로 `ReadPixels` → `EncodeToPNG` → `docs/store/screens/<name>.png` 저장, 캔버스·카메라 원복. RGB24 라 알파가 생기지 않는다. 첫 장을 찍어 Read 로 열고 화면이 게임 뷰와 같은 배치인지(CanvasScaler 1920×1080 기준) 확인한 뒤 나머지를 찍는다. 이 방식이 배치를 틀리게 만들면 Game 뷰를 1920×1080 고정으로 두고 `ScreenCapture.CaptureScreenshotAsTexture()` → RGB24 로 복사해 저장으로 바꾼다.
- 개발 표시 제거: 캡처 전 `Resources.FindObjectsOfTypeAll<Tichu.Presentation.Diagnostics.FpsOverlay>()` 의 GameObject 를 비활성화(OnGUI 라 카메라 렌더엔 원래 안 찍히지만 ScreenCapture 경로 대비).
- `Application.runInBackground` 를 켰다면 끝나고 `PlayerSettings.runInBackground=false` + `SaveAssets()` 로 되돌린다(ProjectSettings 오염 방지).

- [ ] **Step 2: 장면 만들고 찍기**

스펙 2.3 순서대로, 화면 구동은 메모의 방법(활성 Button 을 자식 Text 로 찾아 `onClick.Invoke()`, 셸은 `AppScope.Container.Resolve(typeof(AppFlowMachine))` 후 `Send`):
1. `01-midgame.png` 한창 진행 중인 판(테이블 위 카드 있음)
2. `02-select.png` 내 차례, 카드 몇 장 선택해 올라간 상태
3. `03-bomb.png` 폭탄 글로우 또는 티츄 배지 — 폭탄은 시드를 바꿔 최대 10판 시도, 안 나오면 `03-tichu.png`(티츄 선언 배지)
4. `04-result.png` 라운드 결과·점수
5. `05-difficulty.png` 난이도 선택 화면
6. (선택) `06-lobby.png` 온라인 대기 화면 — 로컬 서버(`tools/smoke-server.ps1` 참고)를 띄워야 하므로 1~5 가 끝나고 시간이 남을 때만. 개발 빌드의 주소 입력칸이 찍히면 이 장은 뺀다.

- [ ] **Step 3: 한 장씩 눈으로 확인**

각 PNG 를 Read 로 열어: 빈 테이블/흰 화면 아님, FPS 숫자·디버그 텍스트 없음, 카드 무늬 아이콘 보임, 한글 깨짐 없음. 하나라도 걸리면 다시 찍는다.

- [ ] **Step 4: 규격 검사**

Run: `powershell.exe -NoProfile -File docs/store/src/check-store-images.ps1`
Expected: `OK 10~11 files`, exit 0. Play 최소 2장, 이 Task 의 완료 조건은 **4장 이상**.

- [ ] **Step 5: 커밋**

```bash
git add docs/store/screens
git commit -m "feat(r3): 스토어 스크린샷 1920×1080"
```
(ProjectSettings 가 바뀌어 있으면 커밋하지 말고 원복.)

---

### Task 4: 한국어 등록정보와 방침 페이지 이름

**Files:**
- Create: `docs/store/listing.html`, `docs/store/src/check-listing.ps1`
- Modify(gh-pages worktree): `privacy/index.html`

**Interfaces:**
- Produces: `listing.html` 안의 필드 = `<pre id="title">`, `<pre id="short">`, `<pre id="full">` (복사하기 쉬운 원문 그대로). `check-listing.ps1` 이 이 id 로 읽는다.

- [ ] **Step 1: 검사 스크립트 먼저 — `check-listing.ps1`**

`listing.html` 을 UTF-8 로 읽어 정규식으로 세 `<pre id=...>` 내용을 뽑고 `[System.Net.WebUtility]::HtmlDecode` 후 앞뒤 공백 제거, `.Length`(UTF-16 단위; 한글 BMP 라 글자 수와 같음)를 센다. 규칙: title ≤ 30, short ≤ 80, full ≤ 4000, 그리고 `full` 이 면책 문구(Global Constraints 의 문장)를 그대로 포함, `title` 이 정확히 `용과 봉황: 팀 카드게임`, 어떤 필드에도 `최강`·`최고의` 없음. 결과를 `title 12/30` 식으로 출력, 위반 시 exit 1.

Run: `powershell.exe -NoProfile -File docs/store/src/check-listing.ps1` → Expected: 파일 없음으로 FAIL, exit 1

- [ ] **Step 2: `listing.html` 작성**

- 짧은 설명: 2:2 팀 카드게임 + AI 와 대전 핵심 한 줄.
- 자세한 설명 구성: ① 소개("4명이 둘씩 팀을 이뤄 겨루는 트릭테이킹 카드게임, 티츄(Tichu) 규칙으로 즐기는") ② 특징 — AI 난이도 4단계(쉬움·보통·어려움·전문가), 라지·스몰 티츄 선언, 폭탄·용·봉황·개·참새 특수 카드, 가로 화면 ③ 온라인 대전(자동 매칭, 빈자리는 AI·차례 20초·재접속) — 단락 위에 HTML 주석 `<!-- M2b 서버 배포 전 게시라면 이 단락을 뺄 것 -->` ④ 면책 문구.
- 페이지 상단에 필드별 글자 수 표기(검사 스크립트 결과와 같은 숫자를 손으로 적지 말고, 스크립트 출력값을 확인해 일치시킨다).
- 외부 리소스 0, 다크모드 대응 불필요(내부 문서).

- [ ] **Step 3: 검사 통과**

Run: `powershell.exe -NoProfile -File docs/store/src/check-listing.ps1`
Expected: 세 줄 모두 한도 안, exit 0.

- [ ] **Step 4: 방침 페이지 이름 바꾸기(gh-pages worktree)**

```bash
git worktree add ../Tichu-gh-pages gh-pages
```
`../Tichu-gh-pages/privacy/index.html` 에서 "Tichu Master" 를 바꾼다: 한국어 쪽(줄 6 제목의 한국어 부분 포함, 29, 31) → `용과 봉황`, 영어 쪽(66, 68) → `Dragon & Phoenix`. 줄 6 `<title>` 은 `개인정보처리방침 · Privacy Policy — 용과 봉황 (Dragon &amp; Phoenix)`. 본문 문장·시행일은 그대로.
Run: `grep -c "Tichu Master" ../Tichu-gh-pages/privacy/index.html` → Expected: `0`; `grep -n "http" ../Tichu-gh-pages/privacy/index.html` → 외부 리소스 로드 없음(기존과 같음).

- [ ] **Step 5: 커밋(푸시 안 함)**

```bash
git add docs/store/listing.html docs/store/src/check-listing.ps1
git commit -m "feat(r3): 한국어 스토어 등록정보와 글자 수 검사"
cd ../Tichu-gh-pages && git add privacy/index.html && git commit -m "docs(privacy): 앱 이름을 용과 봉황(Dragon & Phoenix)으로"
```
gh-pages 푸시는 사용자 승인 후(방침 URL 라이브 페이지).

---

### Task 5: 서명 AAB 재빌드로 정체성 검증, 보고서

**Files:**
- Create: `docs/reports/release/티츄_R3_스토어자산_결과.html`
- (산출, 커밋 안 함) `Build/TichuMaster-1.0.0-3.aab`

**Interfaces:**
- Consumes: Task 2 의 productName·패키지·아이콘

- [ ] **Step 1: 빌드(에디터 닫고 배치 모드)**

실행 중 에디터는 User 환경변수를 상속하지 않았을 수 있으므로(메모 함정 2) **사용자에게 에디터를 닫아 달라고 요청**한 뒤, 같은 PowerShell 호출 안에서 `TICHU_KEYSTORE_PATH/PASS`, `TICHU_KEY_ALIAS/PASS` 를 User 범위에서 하이드레이션하고 `E:\6000.3.17f1\Editor\Unity.exe -batchmode -quit -projectPath <repo> -executeMethod ReleaseBuild.BuildReleaseAab -logFile %TEMP%\r3-aab.log` 를 백그라운드로 실행(~15분).
Expected: 로그에 `[ReleaseBuild] 성공 ...TichuMaster-1.0.0-3.aab / versionCode=3`. 실패하고 원인이 한글 productName 이면 스펙 4장의 대안으로 Task 2 를 고친 뒤 다시.

- [ ] **Step 2: 매니페스트 확인**

Unity 번들 bundletool 로 `dump manifest --bundle Build/TichuMaster-1.0.0-3.aab`.
Expected: `package="com.kas328.dragonphoenix"`, `android:versionCode="3"`, `android:icon`·`android:roundIcon` 가 `@mipmap/...`. 라벨은 `@string/app_name` 이므로 `dump resources --bundle ... --resource string/app_name` 으로 값 `용과 봉황` 확인. 적응형 아이콘: `unzip -l` 로 `base/res/mipmap-anydpi-v26/` 아래 xml 존재.

- [ ] **Step 3: 서명 확인**

`jarsigner -verify Build/TichuMaster-1.0.0-3.aab` → Expected: `jar verified.`

- [ ] **Step 4: 키 유출 게이트(Review Focus 4)**

Run: `git diff ProjectSettings/ProjectSettings.asset`
Expected: 바뀐 줄은 `AndroidBundleVersionCode: 3` 과 (있다면) `AndroidKeystoreName: '{inproject}: '` 뿐. `AndroidKeystoreName`·`AndroidKeyaliasName` 값에 드라이브 문자(`C:`)·`\`·`/`·`user`·`.jks`·`tichumaster` 가 있으면 **멈추고** 원복. versionCode 3 은 커밋한다(다음 빌드가 4 가 되도록).

- [ ] **Step 5: 보고서와 실기 대기 목록**

`티츄_R3_스토어자산_결과.html`(기존 보고서 스타일): 산출물 미리보기(아이콘·피처·스크린샷 상대 경로 `<img>`), 검사 결과(크기·알파·글자 수·매니페스트), 스펙과 다르게 간 4가지, 사용자 할 일(Play 본인 확인 → 앱 생성 → 업로드 순서, 등록정보 복사 위치, gh-pages 푸시 승인), **실기 대기 목록 추가분**: 홈 화면 아이콘·"용과 봉황" 이름 잘림 여부, 적응형 아이콘 모양(원·물방울 등 런처별), 에디터 PlayerPrefs 위치 변경 참고.

- [ ] **Step 6: 커밋**

```bash
git add ProjectSettings/ProjectSettings.asset "docs/reports/release/티츄_R3_스토어자산_결과.html"
git commit -m "docs(r3): 스토어 자산 결과 보고서 — AAB 정체성 검증·실기 대기 목록"
```
머지·푸시·gh-pages 푸시는 사용자 승인 후.
