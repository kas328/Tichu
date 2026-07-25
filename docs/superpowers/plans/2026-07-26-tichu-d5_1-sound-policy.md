# D5.1 사운드 폴리시 (A1~A3) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 메뉴 버튼 클릭 SFX, 설정 볼륨 패널(BGM/효과음 2슬라이더), 앱 백그라운드 음소거를 추가한다.

**Architecture:** 볼륨은 정적 단일 진실원 `VolumeSettings`(PlayerPrefs 영속)에 저장하고 메뉴 BGM(App 스코프)·테이블 SFX(Table 씬)가 스코프를 넘어 읽는다. 버튼음은 메뉴용 `IAudioService`로 재생, 백그라운드 음소거는 순수 정책 `AudioLifecycle` + 얇은 `AppLifecycleAudio` MonoBehaviour가 `AudioListener.pause`를 토글한다.

**Tech Stack:** Unity 6000.3.17f1, uGUI, R3(기존), NUnit EditMode, Unity MCP(컴파일/테스트).

## Global Constraints

- 신규 `asmdef` 0. 전부 `Tichu.Presentation` 어셈블리 내부(네임스페이스 `Tichu.Presentation.Audio` / `.Shell`).
- **core/ 미러 불필요** — 전부 Presentation 전용(PIMC/오라클 레이어 무관).
- **오라클 무침범**: `AsyncGameDriver`/`onApply`/게임 로직 미변경(오라클 동기==비동기 회귀 0).
- **no-op 폴백 보존**: 뱅크 부재 → `NoOpAudioService`, 클립 미할당 → 무음.
- `VolumeSettings` 기본값 = **BGM 0.5 / SFX 1.0**(현재 동작과 동일 — 미조작 시 무변화).
- `SfxId`는 **enum 끝에만 추가**(직렬화 int id 보존, `AudioBank.asset` 불변).
- 신규 `.cs`는 `execute_code`로 `AssetDatabase.ImportAsset(path, ForceUpdate)` + `Refresh(ForceUpdate)` 후 컴파일(메모리 지침 — `refresh_unity`만으론 임포트 누락).
- `run_tests` 전 **PlayMode 정지 필수**(`manage_editor action="stop"`). 어셈블리 단위 `Tichu.Presentation.Tests` 실행(2~3초 안정). `Tichu.Core.Tests` 전체 실행 금지.

## File Structure

| 파일 | 책임 |
|---|---|
| `Presentation/Audio/VolumeSettings.cs` (신규) | BGM/SFX 볼륨 정적 진실원 · PlayerPrefs 영속 · Changed 이벤트 |
| `Presentation/Shell/AudioLifecycle.cs` (신규) | 순수 정책: 포커스 → 정지 여부 |
| `Presentation/Shell/AppLifecycleAudio.cs` (신규) | MonoBehaviour: OnApplicationPause/Focus → AudioListener.pause |
| `Presentation/Audio/SfxMap.cs` (편집) | `SfxId`에 `ButtonClick` 추가 |
| `Presentation/Audio/UnityAudioService.cs` (편집) | PlayOneShot 볼륨 = `VolumeSettings.Sfx` |
| `Presentation/Resources/AudioBank.asset` (편집) | `ButtonClick → ui_button_simple_click_01` 매핑 |
| `Presentation/Shell/Views/MenuShellView.cs` (편집) | `AddSlider(...)` 빌더 |
| `Presentation/Shell/MenuShellPresenter.cs` (편집) | 메뉴 오디오·버튼음·슬라이더·BGM 전파·라이프사이클 GO |
| `Presentation/Tests/VolumeSettingsTests.cs` (신규) | 클램프/영속/이벤트 |
| `Presentation/Tests/AudioLifecycleTests.cs` (신규) | 순수 정책 |

**테스트 정책(프로젝트 관례 준수):** 로직 유닛(`VolumeSettings`·`AudioLifecycle`)은 EditMode TDD. 프레젠터/뷰 배선(버튼음·슬라이더·BGM 전파·라이프사이클)은 기존 메뉴 셸 전례대로 **PlayMode 육안**(자동 테스트 없음 — 브리틀한 uGUI EditMode 하니스 회피).

---

### Task 1: VolumeSettings (볼륨 상태·영속)

**Files:**
- Create: `Assets/_Project/Presentation/Audio/VolumeSettings.cs`
- Test: `Assets/_Project/Presentation/Tests/VolumeSettingsTests.cs`

**Interfaces:**
- Produces: `static class VolumeSettings { event System.Action Changed; float Bgm{get;}; float Sfx{get;}; void Load(); void SetBgm(float); void SetSfx(float); }` — `Bgm/Sfx`는 `private set`, 변경은 `SetBgm/SetSfx`(클램프+저장+Changed)로만.

- [ ] **Step 1: 실패 테스트 작성**

`Assets/_Project/Presentation/Tests/VolumeSettingsTests.cs`:
```csharp
using NUnit.Framework;
using Tichu.Presentation.Audio;
using UnityEngine;

namespace Tichu.Presentation.Tests
{
    public class VolumeSettingsTests
    {
        [TearDown]
        public void Cleanup()
        {
            PlayerPrefs.DeleteKey("vol.bgm");
            PlayerPrefs.DeleteKey("vol.sfx");
            VolumeSettings.Load();   // 다음 테스트 격리: 기본값 복원
        }

        [Test]
        public void SetBgm_clamps_to_unit_range()
        {
            VolumeSettings.SetBgm(-0.5f);
            Assert.AreEqual(0f, VolumeSettings.Bgm);
            VolumeSettings.SetBgm(2f);
            Assert.AreEqual(1f, VolumeSettings.Bgm);
        }

        [Test]
        public void SetSfx_persists_across_reload()
        {
            VolumeSettings.SetSfx(0.3f);
            VolumeSettings.Load();
            Assert.AreEqual(0.3f, VolumeSettings.Sfx, 1e-4f);
        }

        [Test]
        public void SetBgm_fires_Changed_only_on_new_value()
        {
            int hits = 0;
            System.Action h = () => hits++;
            VolumeSettings.Changed += h;
            try
            {
                VolumeSettings.SetBgm(0.42f);
                Assert.AreEqual(1, hits);
                VolumeSettings.SetBgm(0.42f);   // 동일 값 → 발화 없음
                Assert.AreEqual(1, hits);
            }
            finally { VolumeSettings.Changed -= h; }
        }
    }
}
```

- [ ] **Step 2: 테스트 실패 확인**

`execute_code`로 `VolumeSettingsTests.cs` 임포트 → `read_console`로 컴파일 에러(`VolumeSettings` 미정의) 확인.
Expected: CS0103/CS0246 (VolumeSettings does not exist).

- [ ] **Step 3: 최소 구현**

`Assets/_Project/Presentation/Audio/VolumeSettings.cs`:
```csharp
using UnityEngine;

namespace Tichu.Presentation.Audio
{
    /// <summary>
    /// BGM/효과음 볼륨의 단일 진실원(정적·앱 전역). MenuBgm·SfxMap과 동일 컨벤션.
    /// PlayerPrefs 영속 + Changed 이벤트(슬라이더 변경 → BGM 소스 실시간 반영).
    /// 볼륨은 스코프를 넘는 앱 전역 상태(PlayerPrefs 자체가 전역)라 정적으로 둔다.
    /// </summary>
    public static class VolumeSettings
    {
        const string KeyBgm = "vol.bgm";
        const string KeySfx = "vol.sfx";
        const float DefaultBgm = 0.5f;
        const float DefaultSfx = 1f;

        public static event System.Action Changed;

        public static float Bgm { get; private set; } = DefaultBgm;
        public static float Sfx { get; private set; } = DefaultSfx;

        public static void Load()
        {
            Bgm = PlayerPrefs.GetFloat(KeyBgm, DefaultBgm);
            Sfx = PlayerPrefs.GetFloat(KeySfx, DefaultSfx);
        }

        public static void SetBgm(float v)
        {
            v = Mathf.Clamp01(v);
            if (v == Bgm) return;
            Bgm = v;
            PlayerPrefs.SetFloat(KeyBgm, v);
            PlayerPrefs.Save();
            Changed?.Invoke();
        }

        public static void SetSfx(float v)
        {
            v = Mathf.Clamp01(v);
            if (v == Sfx) return;
            Sfx = v;
            PlayerPrefs.SetFloat(KeySfx, v);
            PlayerPrefs.Save();
            Changed?.Invoke();
        }
    }
}
```

- [ ] **Step 4: 테스트 통과 확인**

`execute_code`로 `VolumeSettings.cs` 임포트 → `read_console` 컴파일 0에러 → `manage_editor action="stop"` → `run_tests` assembly_names=`["Tichu.Presentation.Tests"]` + `get_test_job` wait.
Expected: `VolumeSettingsTests` 3/3 PASS, 회귀 0.

- [ ] **Step 5: 커밋**

```bash
git add Assets/_Project/Presentation/Audio/VolumeSettings.cs Assets/_Project/Presentation/Tests/VolumeSettingsTests.cs
git commit -m "feat(sound): VolumeSettings 볼륨 상태·PlayerPrefs 영속(A2 토대)"
```

---

### Task 2: AudioLifecycle (백그라운드 음소거, A3)

**Files:**
- Create: `Assets/_Project/Presentation/Shell/AudioLifecycle.cs`
- Create: `Assets/_Project/Presentation/Shell/AppLifecycleAudio.cs`
- Test: `Assets/_Project/Presentation/Tests/AudioLifecycleTests.cs`

**Interfaces:**
- Produces: `static class AudioLifecycle { bool ShouldPause(bool focused); }` · `class AppLifecycleAudio : MonoBehaviour`(부트 시 GO로 생성됨).

- [ ] **Step 1: 실패 테스트 작성**

`Assets/_Project/Presentation/Tests/AudioLifecycleTests.cs`:
```csharp
using NUnit.Framework;
using Tichu.Presentation.Shell;

namespace Tichu.Presentation.Tests
{
    /// <summary>앱 포커스 → 오디오 정지 여부(순수)의 EditMode 검증.</summary>
    public class AudioLifecycleTests
    {
        [Test] public void Pauses_when_unfocused() => Assert.IsTrue(AudioLifecycle.ShouldPause(false));
        [Test] public void Resumes_when_focused() => Assert.IsFalse(AudioLifecycle.ShouldPause(true));
    }
}
```

- [ ] **Step 2: 테스트 실패 확인**

임포트 후 `read_console`: CS0103/CS0246 (AudioLifecycle 미정의).

- [ ] **Step 3: 최소 구현**

`Assets/_Project/Presentation/Shell/AudioLifecycle.cs`:
```csharp
namespace Tichu.Presentation.Shell
{
    /// <summary>앱 포커스/일시정지 → 오디오 정지 여부(순수). MenuBgm.PlaysIn 미러.</summary>
    public static class AudioLifecycle
    {
        public static bool ShouldPause(bool focused) => !focused;
    }
}
```

`Assets/_Project/Presentation/Shell/AppLifecycleAudio.cs`:
```csharp
using UnityEngine;

namespace Tichu.Presentation.Shell
{
    /// <summary>
    /// 앱 백그라운드/포커스 아웃 시 전역 오디오 정지(AudioListener.pause), 복귀 시 복원.
    /// App-수명 GO(MenuShellPresenter가 부트 시 생성). 순수 판정은 AudioLifecycle.
    /// </summary>
    public sealed class AppLifecycleAudio : MonoBehaviour
    {
        void OnApplicationPause(bool paused) => AudioListener.pause = paused;
        void OnApplicationFocus(bool focus) => AudioListener.pause = AudioLifecycle.ShouldPause(focus);
    }
}
```

- [ ] **Step 4: 테스트 통과 확인**

두 파일 임포트 → 컴파일 0에러 → PlayMode stop → `run_tests` `Tichu.Presentation.Tests`.
Expected: `AudioLifecycleTests` 2/2 PASS.

- [ ] **Step 5: 커밋**

```bash
git add Assets/_Project/Presentation/Shell/AudioLifecycle.cs Assets/_Project/Presentation/Shell/AppLifecycleAudio.cs Assets/_Project/Presentation/Tests/AudioLifecycleTests.cs
git commit -m "feat(sound): 앱 백그라운드 음소거 — AudioLifecycle 순수 정책 + AppLifecycleAudio(A3)"
```

---

### Task 3: 버튼 SFX 배관 (SfxId + AudioBank + SFX 볼륨, A1 하부)

**Files:**
- Modify: `Assets/_Project/Presentation/Audio/SfxMap.cs:8`
- Modify: `Assets/_Project/Presentation/Resources/AudioBank.asset:29` (id 7 뒤에 추가)
- Modify: `Assets/_Project/Presentation/Audio/UnityAudioService.cs:12,35`

**Interfaces:**
- Consumes: `VolumeSettings.Sfx`(Task 1).
- Produces: `SfxId.ButtonClick`(값 8) · `AudioBank.Clip(SfxId.ButtonClick)` → ui_button 클립.

- [ ] **Step 1: SfxId에 ButtonClick 추가**

`SfxMap.cs:8` 교체:
```csharp
    public enum SfxId { None = 0, CardPlay, Pass, Bomb, GiveDragon, TichuCall, GrandTichuCall, RoundEnd, ButtonClick }
```

- [ ] **Step 2: AudioBank.asset에 매핑 추가**

`AudioBank.asset`의 `- id: 7` 블록(29행) 뒤에 추가(들여쓰기 2칸 유지):
```yaml
  - id: 8
    clip: {fileID: 8300000, guid: 5114974f6f86b5b4aa3a48e17518314c, type: 3}
```
(guid `5114974f...` = `ui_button_simple_click_01.wav` = 현 CardPlay 클립, 공유 무방.)

- [ ] **Step 3: UnityAudioService SFX 볼륨 적용**

`UnityAudioService.cs`에서 `private const float Vol = 1f;`(12행) 삭제하고, `PlaySfx`(35행)의 재생 줄 교체:
```csharp
            _voices[_next++ % _voices.Length].PlayOneShot(clip, VolumeSettings.Sfx);
```
(같은 네임스페이스 `Tichu.Presentation.Audio`라 using 불필요.)

- [ ] **Step 4: 컴파일 + 회귀 확인**

세 파일 임포트(`AudioBank.asset`은 `AssetDatabase.ImportAsset`로 재임포트) → `read_console` 0에러 → PlayMode stop → `run_tests` `Tichu.Presentation.Tests`.
Expected: 전체 PASS. 특히 `AudioWiringTests`는 스파이 서비스라 볼륨 변경 무관·그린. `SfxMap`은 미변경이라 `SfxMapTests` 그린. `VolumeSettings.Sfx` 기본 1.0 → 기존 재생 볼륨과 동일.

- [ ] **Step 5: 커밋**

```bash
git add Assets/_Project/Presentation/Audio/SfxMap.cs Assets/_Project/Presentation/Resources/AudioBank.asset Assets/_Project/Presentation/Audio/UnityAudioService.cs
git commit -m "feat(sound): SfxId.ButtonClick + AudioBank 매핑 + SFX 볼륨 연동(A1 배관)"
```

---

### Task 4: 메뉴 배선 — 슬라이더·버튼음·BGM 전파·라이프사이클 (A1·A2·A3 통합)

**Files:**
- Modify: `Assets/_Project/Presentation/Shell/Views/MenuShellView.cs` (`AddSlider` 추가)
- Modify: `Assets/_Project/Presentation/Shell/MenuShellPresenter.cs` (배선)

**Interfaces:**
- Consumes: `VolumeSettings`(Task 1) · `AppLifecycleAudio`(Task 2) · `SfxId.ButtonClick`·`UnityAudioService`·`NoOpAudioService`·`AudioBank`(Task 3).
- Produces: `MenuShellView.AddSlider(ScreenState, string, float, Action<float>) → Slider`.

이 태스크는 프레젠터/뷰 배선이라 자동 테스트 대신 **컴파일 + PlayMode 육안**으로 검증한다(메뉴 셸 전례).

- [ ] **Step 1: MenuShellView.AddSlider 추가**

`MenuShellView.cs`의 `AddButton`(52행) 메서드 뒤에 추가:
```csharp
        /// <summary>패널에 라벨 + 0~1 슬라이더를 추가하고 값 변경을 배선한다(코드 빌드).</summary>
        public Slider AddSlider(ScreenState panel, string label, float initial, Action<float> onChange)
        {
            var root = _buttonRoots[panel];

            var lbl = NewText($"Lbl_{label}", root, label, 30);
            var lle = lbl.gameObject.AddComponent<LayoutElement>();
            lle.preferredWidth = 460; lle.preferredHeight = 44;

            var go = new GameObject($"Slider_{label}", typeof(RectTransform), typeof(Slider));
            go.transform.SetParent(root, false);
            var sle = go.AddComponent<LayoutElement>();
            sle.preferredWidth = 460; sle.preferredHeight = 40;

            var bg = new GameObject("Background", typeof(RectTransform), typeof(Image));
            bg.transform.SetParent(go.transform, false);
            StretchFull((RectTransform)bg.transform);
            bg.GetComponent<Image>().color = new Color(0.20f, 0.24f, 0.34f, 1f);

            var fillArea = new GameObject("Fill Area", typeof(RectTransform));
            fillArea.transform.SetParent(go.transform, false);
            StretchFull((RectTransform)fillArea.transform);
            var fill = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            fill.transform.SetParent(fillArea.transform, false);
            var fillRt = (RectTransform)fill.transform;
            fillRt.anchorMin = new Vector2(0f, 0f); fillRt.anchorMax = new Vector2(0f, 1f);
            fillRt.pivot = new Vector2(0f, 0.5f); fillRt.sizeDelta = new Vector2(10f, 0f);
            fill.GetComponent<Image>().color = new Color(0.16f, 0.62f, 0.44f, 1f);

            var handleArea = new GameObject("Handle Slide Area", typeof(RectTransform));
            handleArea.transform.SetParent(go.transform, false);
            StretchFull((RectTransform)handleArea.transform);
            var handle = new GameObject("Handle", typeof(RectTransform), typeof(Image));
            handle.transform.SetParent(handleArea.transform, false);
            var hRt = (RectTransform)handle.transform;
            hRt.sizeDelta = new Vector2(30f, 40f);
            handle.GetComponent<Image>().color = new Color(0.92f, 0.94f, 0.98f, 1f);

            var slider = go.GetComponent<Slider>();
            slider.fillRect = fillRt;
            slider.handleRect = hRt;
            slider.targetGraphic = handle.GetComponent<Image>();
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = 0f; slider.maxValue = 1f;
            slider.value = initial;
            slider.onValueChanged.AddListener(v => onChange(v));
            return slider;
        }
```

- [ ] **Step 2: MenuShellPresenter 배선**

`MenuShellPresenter.cs` 상단에 `using Tichu.Presentation.Audio;` 추가.

필드 추가(`AudioSource _bgm;` 옆):
```csharp
        IAudioService _menuAudio;               // 메뉴 버튼 SFX(App-수명). 뱅크 부재 → NoOp.
        AppLifecycleAudio _lifecycle;           // 앱 백그라운드 음소거 훅 호스트.
```

ctor에 선택 주입 파라미터 추가(테스트/헤드리스 seam, RuntimeTableView 전례):
```csharp
        public MenuShellPresenter(AppFlowMachine flow, MatchSettings settings, IAudioService menuAudio = null)
        {
            _flow = flow;
            _settings = settings;
            _menuAudio = menuAudio;
        }
```

`Start()` 교체:
```csharp
        public void Start()
        {
            VolumeSettings.Load();
            _view = new MenuShellView();
            ResolveAudio();
            _lifecycle = new GameObject("AppLifecycleAudio").AddComponent<AppLifecycleAudio>();
            WireButtons();
            SetupBgm();
            VolumeSettings.Changed += OnVolumeChanged;
            _sub = _flow.State.Subscribe(Show);
        }

        // 메뉴 버튼 SFX 서비스 해결(미주입 시 뱅크 로드, 부재면 NoOp).
        void ResolveAudio()
        {
            if (_menuAudio != null) return;
            var bank = Resources.Load<AudioBank>("AudioBank");
            _menuAudio = bank != null ? new UnityAudioService(bank, 2) : (IAudioService)new NoOpAudioService();
        }

        void OnVolumeChanged()
        {
            if (_bgm != null) _bgm.volume = VolumeSettings.Bgm;
        }
```

`SetupBgm()`의 볼륨 줄(47행) 교체:
```csharp
            _bgm.volume = VolumeSettings.Bgm;
```

`WireButtons()`: 모든 `_view.AddButton(...)` 호출을 `Wire(...)`로 바꾸고(버튼음 래핑), Settings에 슬라이더 2개를 "뒤로" 앞에 추가. 메서드 전체 교체:
```csharp
        void WireButtons()
        {
            Wire(ScreenState.Intro,      "시작하기",  () => _flow.Send(AppFlowEvent.IntroFinished));
            Wire(ScreenState.MainHub,    "게임 시작", () => _flow.Send(AppFlowEvent.OpenModeSelect));
            Wire(ScreenState.MainHub,    "게임 방법", () => _flow.Send(AppFlowEvent.OpenHowTo));
            Wire(ScreenState.MainHub,    "설정",      () => _flow.Send(AppFlowEvent.OpenSettings));
            Wire(ScreenState.ModeSelect, "AI 대전",   () => _flow.Send(AppFlowEvent.OpenDifficultySelect));
            Wire(ScreenState.ModeSelect, "랭킹",      () => { _flow.Send(AppFlowEvent.SelectRankingStub);    ShowToast("랭킹은 Phase 3에서 제공됩니다"); });
            Wire(ScreenState.ModeSelect, "친구방",    () => { _flow.Send(AppFlowEvent.SelectFriendRoomStub); ShowToast("친구방은 Phase 3에서 제공됩니다"); });
            Wire(ScreenState.ModeSelect, "뒤로",      () => _flow.Send(AppFlowEvent.Back));
            Wire(ScreenState.DifficultySelect, "쉬움",   () => StartAt(Difficulty.Easy));
            Wire(ScreenState.DifficultySelect, "보통",   () => StartAt(Difficulty.Normal));
            Wire(ScreenState.DifficultySelect, "어려움", () => StartAt(Difficulty.Hard));
            Wire(ScreenState.DifficultySelect, "전문가", () => StartAt(Difficulty.Expert));
            Wire(ScreenState.DifficultySelect, "뒤로",   () => _flow.Send(AppFlowEvent.Back));
            Wire(ScreenState.HowTo,      "뒤로",      () => _flow.Send(AppFlowEvent.Back));
            _view.AddSlider(ScreenState.Settings, "음악",   VolumeSettings.Bgm, VolumeSettings.SetBgm);
            _view.AddSlider(ScreenState.Settings, "효과음", VolumeSettings.Sfx, VolumeSettings.SetSfx);
            Wire(ScreenState.Settings,   "뒤로",      () => _flow.Send(AppFlowEvent.Back));
        }

        // 버튼 클릭 시 클릭음을 낸 뒤 액션 실행(View는 오디오 무지 유지).
        void Wire(ScreenState panel, string label, System.Action action)
            => _view.AddButton(panel, label, () => { _menuAudio.PlaySfx(SfxId.ButtonClick); action(); });
```

`Dispose()` 교체:
```csharp
        public void Dispose()
        {
            VolumeSettings.Changed -= OnVolumeChanged;
            _sub?.Dispose();
            if (_bgm != null) UnityEngine.Object.Destroy(_bgm.gameObject);
            if (_lifecycle != null) UnityEngine.Object.Destroy(_lifecycle.gameObject);
        }
```

- [ ] **Step 3: 컴파일 확인**

`MenuShellView.cs`·`MenuShellPresenter.cs` 임포트 → `read_console` types=[error] filter "error CS".
Expected: 0 에러.

- [ ] **Step 4: EditMode 회귀 + PlayMode 육안**

PlayMode stop → `run_tests` `Tichu.Presentation.Tests`.
Expected: 전체 PASS(오라클·AppFlow·Audio 회귀 0).

이어서 PlayMode 육안(`FindFirstObjectByType<AppScope>().Container.Resolve(typeof(AppFlowMachine))` → `Send`로 화면 구동, 메모리 [[unity-mcp-session-reload]]):
- 메뉴 버튼 클릭 시 클릭음.
- MainHub → 설정 → "음악"/"효과음" 슬라이더 2개 표시, 드래그 시 BGM 볼륨 실시간 변화·효과음은 다음 재생부터 반영.
- Alt-Tab(포커스 아웃) → 오디오 뮤트, 복귀 → 복원.
- InGame 진입 시 BGM 정지(기존)·버튼음/슬라이더 회귀 없음.

- [ ] **Step 5: 커밋**

```bash
git add Assets/_Project/Presentation/Shell/Views/MenuShellView.cs Assets/_Project/Presentation/Shell/MenuShellPresenter.cs
git commit -m "feat(sound): 메뉴 버튼음 + 설정 볼륨 슬라이더 + 백그라운드 음소거 배선(A1·A2·A3)"
```

---

### Task 5: 통합 검증

**Files:** 없음(검증만).

- [ ] **Step 1: 전체 EditMode 그린 재확인**

PlayMode stop → `run_tests` `Tichu.Presentation.Tests` 어셈블리.
Expected: 전 스위트 PASS(신규 `VolumeSettingsTests` 3 + `AudioLifecycleTests` 2 포함), 오라클 회귀 0.

- [ ] **Step 2: 최종 PlayMode 통합 육안**

한 판 흐름: 메뉴(버튼음·BGM) → 설정(슬라이더 조작·저장) → AI 대전 진입(BGM 정지) → 인게임 카드/폭탄 SFX가 효과음 슬라이더 볼륨 반영 → 백그라운드 뮤트/복귀.

- [ ] **Step 3: finishing-a-development-branch로 랜딩**

`superpowers:finishing-a-development-branch` — `--no-ff` main 머지 + origin 푸시(사용자 승인). 대시보드 현황 갱신은 후속.

---

## Self-Review

**1. Spec coverage:** A1(버튼음)=Task 3+4, A2(볼륨 패널)=Task 1+4, A3(백그라운드)=Task 2+4. `VolumeSettings`·`AudioLifecycle`·`AppLifecycleAudio`·`AddSlider`·SFX 볼륨·AudioBank 매핑 전부 태스크 존재. 비범위(인게임 Pause·BGM 교체)는 미포함(의도).

**2. Placeholder scan:** 모든 코드 블록 실제 코드. "적절한 에러처리" 류 없음.

**3. Type consistency:** `VolumeSettings.SetBgm/SetSfx`(Task 1) ↔ Task 4 슬라이더 배선 일치. `SfxId.ButtonClick`(Task 3) ↔ Task 4 `Wire` 일치. `AudioLifecycle.ShouldPause`(Task 2) ↔ `AppLifecycleAudio` 일치. `AddSlider` 시그니처(Task 4 정의) ↔ 사용 일치.
