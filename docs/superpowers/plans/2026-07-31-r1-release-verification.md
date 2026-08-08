# R1 — 출시 검증 (Release Verification) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Tichu Master 빌드를 실기(S23)에서 검증하고 미확인 AI 부채를 전수 관찰해 Phase 1을 공식 종료, R2(빌드/서명) 착수 조건을 세운다.

**Architecture:** R1은 **코드 구현이 아니라 검증 트랙**이다. 에이전트가 지금 실행 가능한 건 (Task 1) 플레이테스트 대시보드에 부채 체크리스트를 확장하는 것과 (Task 4) 종료 산출물 작성뿐이다. 성능 스모크(Task 2)와 Expert 플레이테스트(Task 3)는 **사람(사용자)이 S23 실기로 실행하는 게이트**이며, 에이전트는 발견된 문제의 분류·수정·격리벤치를 지원한다.

**Tech Stack:** Unity 6000.3.17f1 + URP 3D · R3 · VContainer + DoTween · 자족형 HTML/JS 대시보드(localStorage) · Claude Artifact 호스팅 · HeuristicStrengthBench(core dotnet, 격리 강도벤치) · UnityMCP.

## Global Constraints

이 섹션의 모든 항목은 아래 모든 Task에 암묵적으로 적용된다.

- **게임명 = Tichu Master** (확정). 인게임 "티츄/그랜드 티츄" 콜 명칭은 그대로 유지. 패키지명 정리는 R2(예: `com.kas328.tichumaster`) — R1 범위 아님.
- **저사양 미대응**(제품 결정). 성능 게이트 = S23(현대 기기) 스모크 하나. 저사양 60fps 확정은 범위 제외.
- **강도 레버 철칙**: 플레이테스트 발견이 AI 강도 관련(리드/셰딩/밟기 오버라이드)이면 **반드시 `HeuristicStrengthBench` 페어드 격리벤치 먼저**. 벤치 **≥0(회귀 없음)일 때만 채택**. ⚠️ 라이브 EV 오버라이드(리드/셰딩)는 상습 회귀 이력 — 인간 직관이 국소적으론 옳아도 강도 이득은 대개 없음. 관측교정(회귀 없는 체감 개선)은 허용, 강도 주장은 벤치로만.
- **브랜치 규율**: 이미 `feat/r1-release-verification` 브랜치에서 작업 중. main 직커밋 금지. 머지는 사용자 승인 후 `--no-ff`.
- **테스트 안전**: ⚠️ **전체 `Tichu.Core.Tests` 실행 금지**(Sim/Simulator 10만판 → 메인스레드 점유 → MCP stuck). 항상 클래스 필터: `run_tests(test_names=["...AiAgentTests"])`. **`run_tests` 전 PlayMode 정지 필수.**
- **HeuristicStrengthBench 실행법**: core dotnet 전용. `[Explicit]` 임시 제거 후 `dotnet test core/tests/.../Tichu.Core.Tests.csproj --filter Name=New_vs_Old_heuristic_mirrored`. 벤치 전 `OldAiAgent`를 현재 HEAD로 sed 재동결(베이스라인 정합). 결과는 `%TEMP%/tichu_heuristic_strength.txt` 증분 기록. ⚠️ HeuristicBench는 **롤아웃 축만** 측정(라이브 EV 필터 축은 직접 못 잼).
- **신규 .cs 임포트**: `refresh_unity`만으론 임포트 누락 → `execute_code`로 `AssetDatabase.ImportAsset` + `Refresh(ForceUpdate)` 후 컴파일 확인.
- **아티팩트 재발행**: 대시보드/허브는 **같은 파일 재발행 → 같은 URL 유지**. 이 대화가 원 생성 대화가 아니면 Artifact 툴에 기존 `url`을 전달해야 같은 URL로 간다.

---

## File Structure

| 파일 | 책임 | Task |
|---|---|---|
| `티츄_관리대시보드.html` (수정) | 플레이테스트 탭 `CL` 배열에 R1-b 부채 항목 + §5.3 스모크 + R1-a 성능 섹션 추가, 재발행 | 1 |
| Unity 빌드 설정 (검증만) | Android Build Support·Development Build·minSdk 26·방향잠금·FpsOverlay Debug 게이트 확인 | 2 |
| `Assets/**/*.cs` + core 미러 + `HeuristicStrengthBench` (조건부) | 플레이테스트에서 버그/강도 발견 시에만 수정 — 사전 예측 불가 | 3 |
| `티츄_R1_판정결과.html` (신규) | DoD 4게이트 판정표 채운 R1 결과 리포트 | 4 |
| `catalog.json` + `티츄_Master_허브.html` (수정) | 신규 R1 문서 항목 추가, 허브 재발행 | 4 |
| 메모리 (`tichu-r1-release-verification`·`tichu-phase1-design`·`MEMORY.md`) | Phase 1 종료·R1 결과·R2 조건 반영 | 4 |

각 Task는 독립적으로 리뷰 가능한 산출물로 끝난다. Task 1은 사람 플레이테스트를 언블록하는 선행 조건, Task 2·3은 사람 게이트, Task 4는 종료 반영이다.

---

### Task 1: 통합 부채 체크리스트 확장 (에이전트 실행 가능)

기존 대시보드 플레이테스트 탭의 `CL` 배열이 R1-b 부채 대부분(③′·①·#4·#4c·콜헤드)을 이미 커버한다. **누락분만 surgical 추가**한다: C1 교환핀·구조보존리드·§5.3 일반 스모크·§4.3 S23 성능. 그 뒤 같은 URL로 재발행한다.

**Files:**
- Modify: `티츄_관리대시보드.html` — `const CL = [...]` 배열 (섹션 A~D 정의부, 대략 323–340행)

**Interfaces:**
- Consumes: 기존 항목 스키마 `{id:"X#", t:"제목", d:"부연"}`, 섹션 스키마 `{sec:"X", title:"...", badge:"...", hl:bool, items:[...]}`. 렌더러 `crender()`·결과복사 `ccopy()`·상태 `cs`(CKEY localStorage)는 항목 수에 무관하게 동작하므로 배열만 늘리면 된다.
- Produces: 확장된 `CL`(섹션 A~F). 사람이 이 체크리스트로 Task 3 플레이테스트를 전수 관찰한다.

- [ ] **Step 1: 현재 `CL` 배열의 정확한 위치·형태 확인**

Read `티츄_관리대시보드.html`의 `const CL = [` 시작 행부터 배열 종료 `];`까지. 섹션 B(아웃·낭비), 마지막 섹션 D(연출) 뒤 배열 닫힘 위치를 확정한다.

- [ ] **Step 2: 섹션 B에 부채 항목 2개 추가**

섹션 B의 `items` 배열 끝(B5 다음)에 추가:

```js
   {id:"B6",t:"교환핀 — 넘긴 카드 좌석을 아는 듯 플레이 (C1)",d:"간접 관찰: 내가 준 카드를 낼 상대·타이밍을 AI가 예측하는 듯한가"},
   {id:"B7",t:"구조보존 리드 — 족보 깨는 싱글 리드 회피 (07-15/D3)",d:"스트레이트/트리플/연속페어를 깨는 헛싱글을 리드하지 않는가"}
```

- [ ] **Step 3: 섹션 D 뒤에 §5.3 일반 스모크 섹션(E) 추가**

배열의 섹션 D 객체 뒤(닫는 `]` 앞)에 콤마로 이어 추가:

```js
 {sec:"E",title:"일반 스모크 — 출시 품질",badge:"출시 스모크",hl:false,items:[
   {id:"E1",t:"4난이도(쉬움/보통/어려움/전문가) 각각 시작·정상 동작",d:""},
   {id:"E2",t:"풀매치(1000점) 크래시·멈춤 없이 완주",d:""},
   {id:"E3",t:"모든 룰 인터랙션 정상",d:"폭탄·용·개·마작소원·티츄/큰티츄·1-2 피니시 UI"},
   {id:"E4",t:"D5.1 사운드 — 버튼 클릭음·볼륨 슬라이더·백그라운드 음소거",d:""},
   {id:"E5",t:"가로 세이프에어리어(노치/펀치홀 미가림)·방향 잠금",d:""}]}
```

- [ ] **Step 4: 섹션 E 뒤에 §4.3 S23 성능 스모크 섹션(F) 추가**

섹션 E 객체 뒤에 콤마로 이어 추가:

```js
 {sec:"F",title:"S23 성능 스모크 — 실기 (R1-a)",badge:"실기 S23",hl:true,items:[
   {id:"F1",t:"풀매치 크래시 0으로 완주",d:""},
   {id:"F2",t:"전 구간 FpsOverlay 녹색(≥55)·적색 스파이크 없음",d:""},
   {id:"F3",t:"DoTween 애니(플레이팝·차례펄스·결과팝) 프레임드랍 체감 없음",d:""},
   {id:"F4",t:"입력(손패·버튼)→화면 반응 지연 체감 없음",d:""},
   {id:"F5",t:"여러 라운드 연속 GC 스파이크 끊김 없음 (D3 풀링)",d:""}]}
```

- [ ] **Step 5: JS 구문·항목 수 정적 검증**

Run: `grep -o 'id:"[A-F][0-9]"' 티츄_관리대시보드.html | wc -l`
Expected: 기존 14개(A1-4,B1-5,C1,D1-4) + 신규 12개(B6,B7,E1-5,F1-5) = **26**. 배열 콤마·괄호 짝이 맞는지 육안 확인(마지막 항목 뒤 콤마 없음, 각 섹션 사이 콤마 있음).

- [ ] **Step 6: 렌더 확인 후 같은 URL로 재발행**

Artifact 툴로 재발행. 이 대화는 대시보드 원 생성 대화가 아니므로 **기존 URL을 반드시 전달**:
```
Artifact(file_path="티츄_관리대시보드.html",
         url="https://claude.ai/code/artifact/0705b47b-a8ad-4faf-b5ff-ffd5e0ede984",
         favicon=(기존 유지), description=(기존 유지))
```
재발행 후 플레이테스트 탭에서 섹션 A~F가 모두 뜨고 체크 토글·결과복사가 동작하는지 확인(가능하면 브라우저, 아니면 소스 구조로).

- [ ] **Step 7: 커밋**

```bash
git add 티츄_관리대시보드.html
git commit -m "docs(dashboard): R1-b 부채 체크리스트 확장 — 교환핀·구조보존리드·일반스모크·S23 성능 섹션"
```

---

### Task 2: R1-a — S23 성능 스모크 게이트 (사용자 실행 · 에이전트 지원)

APK 빌드·설치·측정은 **사용자가 S23 실기로 실행**한다. 에이전트는 빌드 전 설정을 검증해 첫 관문(Android Build Support)에서 막히지 않게 돕는다.

**Files:**
- 검증만 (코드 변경 없음이 정상): `ProjectSettings`(minSdk·방향), `FpsOverlay`(Debug 게이트).

**Interfaces:**
- Consumes: 완성된 빌드 코드(main 머지분) + Task 1 대시보드 섹션 F.
- Produces: §4.3 통과 여부. Task 4 DoD#2 판정 입력.

- [ ] **Step 1: (에이전트) 빌드 설정 사전 검증**

UnityMCP로 확인(변경 아님, 리포트만):
- minSdk 26 유지 여부.
- 방향 잠금(가로) PlayerSettings 반영 여부.
- `FpsOverlay`가 `Debug.isDebugBuild` 게이트인지(Development Build에서만 뜨는지).
문제 있으면 사용자에게 보고 후 결정. 없으면 다음 단계 안내.

- [ ] **Step 2: (사용자) Android Build Support 설치 확인**

Unity Hub → Installs → 6000.3.17f1 → Add Modules → **Android Build Support (SDK & NDK Tools, OpenJDK)** 체크 확인. 미설치면 여기서 설치(수 분~십수 분). *에이전트 대신 사용자가 수행 — 실기 첫 관문.*

- [ ] **Step 3: (사용자) 플랫폼 전환**

Build Settings(또는 Build Profiles) → Platform = **Android**로 Switch Platform. 첫 전환은 에셋 reimport로 수 분 소요.

- [ ] **Step 4: (사용자) Development Build → Build And Run**

Development Build 체크 → S23 USB 연결(개발자 옵션·USB 디버깅 ON) → **Build And Run**.

- [ ] **Step 5: (사용자) FpsOverlay 관찰 + §4.3 판정**

딜링·교환·플레이 전 구간에서 대시보드 섹션 F(F1~F5) 전 항목을 관찰·체크:
- [ ] F1 풀매치 크래시 0 완주
- [ ] F2 FpsOverlay 상시 녹색(≥55)·적색 스파이크 0
- [ ] F3 DoTween 애니 프레임드랍 체감 0
- [ ] F4 입력→반응 지연 체감 0
- [ ] F5 라운드 연속 GC 스파이크 없음

Expected(Pass): 5개 전부 체크. 하나라도 Fail이면 병목 구간·재현 조건을 기록해 에이전트에 전달(성능 픽스는 R1 범위 내 버그로 처리).

---

### Task 3: R1-b — Expert 플레이테스트 1패스 + 발견 분류·처리 (사용자 게이트 · 에이전트 수정)

사용자가 확장된 대시보드로 S23 Expert 집중 플레이테스트 1패스를 돌려 부채를 전수 관찰한다. 발견이 나오면 에이전트가 §5.4 결정 트리로 분류·처리한다.

**Files:**
- 조건부 수정(발견 시에만): `Assets/**/*.cs`(라이브 로직) + `core/**` 미러 + `HeuristicStrengthBench`. 사전 특정 불가.

**Interfaces:**
- Consumes: Task 1 대시보드 섹션 A~C(부채) + E(스모크).
- Produces: 각 부채 항목의 관찰 판정(좋음/이상함/미확인) + 처리 결과. Task 4 R1-b 게이트 입력.

- [ ] **Step 1: (사용자) Expert 플레이테스트 1패스**

Expert 난이도로 최소 1~2 풀매치. 대시보드 섹션 A~C·E 항목을 라이브에서 관찰:
- A(콜헤드): Grand 더 자주·이상한 Small 감소 회귀 없는가 (재확인)
- B1 확실승자 리드: `{10,A}` 유형에서 **A를 먼저** 리드하고 나가는가
- B2 봉황 보존: 봉황을 싱글로 헛쓰지 않고 자연 승수로 밟는가
- B6 교환핀 / B7 구조보존리드
- C1 라지티츄: 파트너 티츄 콜 시 최고카드 교환·안 밟기·안 먼저나감·살리기(#4c)
- E1~E5 일반 스모크
결과복사 버튼으로 판정 요약을 에이전트에 전달.

- [ ] **Step 2: (에이전트) 발견을 §5.4로 분류**

각 "이상함" 발견을 분류:
- **버그/연출 결함** → Step 3(TDD 수정).
- **AI 강도 관련(리드/셰딩/밟기 오버라이드)** → Step 4(격리벤치 먼저).
- **롤아웃 천장 한계**(예: AI가 인간 라지티츄 응징 못 함) → 연구티어 defer, 명시적 수용(수정 안 함).

분류 근거를 사용자에게 1줄씩 보고하고 처리 순서를 합의.

- [ ] **Step 3: (에이전트, 버그일 때) TDD 수정**

EditMode 재현 테스트 작성 → 실패 확인 → 최소 수정 → 통과 확인. 클래스 필터로만 실행:
Run: `run_tests(test_names=["...AiAgentTests"])` (PlayMode 정지 후). Assets 변경 시 core 미러 동기화. 신규 .cs면 `execute_code`로 ImportAsset+Refresh.

- [ ] **Step 4: (에이전트, 강도일 때) 격리벤치 먼저**

`OldAiAgent`를 현재 HEAD로 sed 재동결 → `HeuristicStrengthBench` 페어드 4000R:
Run: `dotnet test core/tests/.../Tichu.Core.Tests.csproj --filter Name=New_vs_Old_heuristic_mirrored` (`[Explicit]` 임시 제거).
Expected: 결과가 `%TEMP%/tichu_heuristic_strength.txt`에 기록. **페어드(쌍평균) 마진 ≥0이면 채택, <0이면 되돌림·파킹.** ⚠️ per-round CI는 과대추정 — 페어드로 판정. 라이브 배선까지 하면 PimcBench는 비정보일 수 있어 플레이테스트가 최종검증.

- [ ] **Step 5: (에이전트) 처리 결과 기록**

각 발견의 최종 처분(수정/채택/파킹/수용)과 근거를 Task 4 결과 문서에 넣을 수 있게 목록화. 수정분은 커밋:
```bash
git add <변경파일>
git commit -m "fix(ai): <발견 항목> — <처리·근거>"
```

---

### Task 4: R1-c — Phase 1 공식 종료 (에이전트 실행 가능 + 사용자 판정)

Task 2·3 결과로 DoD 4게이트를 판정하고, 종료 산출물을 만든다.

**Files:**
- Create: `티츄_R1_판정결과.html`
- Modify: `티츄_관리대시보드.html`(현황 탭 Phase 1 CLOSED) · `catalog.json` · `티츄_Master_허브.html` · 메모리 3종

**Interfaces:**
- Consumes: Task 2(DoD#2)·Task 3(R1-b) 판정.
- Produces: Phase 1 공식 종료 선언 + R2 착수 조건.

- [ ] **Step 1: (사용자+에이전트) DoD 4게이트 판정**

§6.1 표를 실제 결과로 채운다:

| 게이트 | 통과 조건 | 판정 |
|---|---|---|
| 완주 (DoD#1) | 풀매치 크래시 0·룰 인터랙션 정상 (E2/E3) | ☐ Pass / ☐ Fail |
| 성능 (DoD#2) | S23 60fps·병목 0 (F1~F5, 저사양 미대응) | ☐ Pass / ☐ Fail |
| 부채 검증 (R1-b) | 섹션 A~C 전수 관찰·발견 처리/수용 | ☐ Pass / ☐ Fail |
| 손맛 (DoD#3) | "한 판 더" 의향 합격 | ☐ Pass / ☐ Fail |

- [ ] **Step 2: (에이전트) R1 판정 결과 리포트 작성**

`티츄_R1_판정결과.html` 생성 — 4게이트 판정표(채움) + Task 3 발견·처분 목록 + 성능 스모크 요약. 프로젝트 HTML 스타일(펠트그린/라이트·다크) 준수. 전 항목 Pass면 **Phase 1 공식 종료 선언** 문구 포함.

- [ ] **Step 3: (에이전트) 대시보드 현황 탭 갱신·재발행**

`티츄_관리대시보드.html` 현황 탭에 "Phase 1 CLOSED · R1 결과" 반영. 같은 URL 재발행(`url=0705b47b-...`).

- [ ] **Step 4: (에이전트) 허브 catalog 갱신·재발행**

`catalog.json`에 신규 문서(`티츄_R1_판정결과.html`, R1 spec md, 이 plan) 항목 추가 + `work_area_index`에 "출시/R1" 영역 반영 → `티츄_Master_허브.html`에 재주입 → `url=02f047e7-...` 재발행.

- [ ] **Step 5: (에이전트) 메모리 갱신**

`tichu-r1-release-verification`: R1 결과·Phase 1 종료·R2 조건. `tichu-phase1-design`: Phase 1 CLOSED. `MEMORY.md` 포인터 훅 갱신.

- [ ] **Step 6: (에이전트) 종료 커밋 + 머지 승인 요청**

```bash
git add 티츄_R1_판정결과.html 티츄_관리대시보드.html catalog.json 티츄_Master_허브.html docs/superpowers/plans/2026-07-31-r1-release-verification.md
git commit -m "docs(r1): R1 판정 결과 + Phase 1 공식 종료 반영"
```
그 뒤 사용자에게 `feat/r1-release-verification` → main `--no-ff` 머지 승인 요청. **머지·푸시는 사용자 승인 후에만.**

---

## Self-Review

**Spec coverage:**
- §4 R1-a 성능(§4.3 5기준) → Task 1 섹션 F + Task 2 ✅
- §4.1 DoD#2 재정의(저사양 미대응) → Global Constraints + Task 4 판정표 ✅
- §5.2 부채 7항목(③′·①·#4·#4c·C1교환핀·07-15/D3·콜헤드) → 기존 CL(A/B1/B2/C1) + Task 1 신규(B6/B7) + Task 3 관찰 ✅
- §5.3 일반 스모크(4난이도·풀매치·사운드·세이프에어리어) → Task 1 섹션 E ✅
- §5.4 발견 분류·처리(버그/강도/천장) → Task 3 Step 2~4 ✅
- §6.1 DoD 4게이트 판정표 → Task 4 Step 1 ✅
- §6.2/§7 종료 산출물(결과 문서·대시보드·메모리·커밋) → Task 4 ✅
- §8 IP(Tichu Master)·저사양 → Global Constraints(R1 코드 검증과 독립, 기록만) ✅

**Placeholder scan:** 조건부 Task 3의 수정 대상 파일은 "사전 특정 불가"로 명시(플레이테스트 발견에 의존 — 진짜 미지수이지 placeholder 아님). 그 외 code/data 스텝은 실제 삽입 내용·명령·기대값 포함.

**Type consistency:** 항목 스키마 `{id,t,d}`·섹션 스키마 `{sec,title,badge,hl,items}`는 기존 렌더러와 동일. 신규 id는 기존과 충돌 없음(B6/B7/E*/F*). 아티팩트 URL(대시보드 0705b47b, 허브 02f047e7)은 메모리와 일치.

---

## Execution Handoff

⚠️ **이 계획은 절반이 사람 게이트다** — Task 2(S23 빌드·측정)·Task 3(Expert 플레이테스트)는 사용자가 실기로 실행하고, 에이전트는 Task 1(대시보드 확장)·Task 3의 분류·수정·격리벤치·Task 4(종료 반영)를 담당한다. 표준 subagent/inline 선택지는 **에이전트 실행분(Task 1·3수정·4)에만** 적용된다.

권장 순서:
1. **지금 바로 (에이전트):** Task 1 — 대시보드 확장·재발행. 사람 플레이테스트를 언블록.
2. **사용자:** Task 2 빌드·성능 스모크 → Task 3 Expert 플레이테스트.
3. **발견 오면 (에이전트):** Task 3 분류·수정·격리벤치.
4. **마무리 (에이전트):** Task 4 종료 반영 + 머지 승인 요청.
