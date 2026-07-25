# D5.1 사운드 폴리시 (A1~A3) — 설계

- **날짜:** 2026-07-26
- **범위:** Phase 1 잔여(D5.1) 손맛 보강 중 사운드 3종 — 버튼 클릭 SFX(A1), 설정 볼륨 패널(A2), 앱 백그라운드 음소거(A3).
- **비범위:** 인게임 일시정지(Pause) 메뉴, BGM 트랙 교체, 실 사운드 최종 청취 확정 — 모두 별도 D5.1 이월/실기 검증 항목으로 유지.
- **브랜치:** `feat/p1d-d5_1-sound-policy`

## 배경 (현재 구조)

두 오디오 서브시스템이 스코프별로 분리되어 있다.

- **메뉴 오디오 (App 스코프):** `MenuShellPresenter`가 BGM `AudioSource`를 소유(볼륨 `0.5` 하드코딩, `music_fun_funky_whistle_groove_loop` 루프). 메뉴 버튼(`MenuShellView.AddButton`)에는 **클릭 SFX가 없다**. `Settings` 화면에는 "뒤로" 버튼만 있고 주석에 "볼륨 슬라이더는 D5".
- **테이블 SFX (Table 씬):** `RuntimeTableView`가 `UnityAudioService`(`AudioSource[6]`)를 생성해 `AudioBank`로 `PlaySfx(SfxId)`. 볼륨은 `Vol=1f` 고정, 마스터/뮤트 개념 없음.

`IAudioService.PlaySfx(SfxId)`가 유일 멤버이며 주석에 "BGM/볼륨은 D5.1에서 멤버 추가(비파괴적)".

## 결정 사항 (사용자 승인)

1. **볼륨 축:** BGM · 효과음 **2개 분리** 슬라이더.
2. **버튼 클릭 사운드:** 기존 `ui_button_simple_click_01` 재사용(최종 청취·교체는 실기 검증 때).
3. **백그라운드 음소거:** 전체 음소거(`AudioListener.pause`, BGM+SFX 일괄, 복귀 자동복원).
4. **볼륨 패널 위치:** 메뉴 설정 화면만(인게임 Pause는 이월).

## 설계

### 유닛 1 — `VolumeSettings` (공유 볼륨 상태·영속)

**무엇:** BGM/SFX 볼륨의 단일 진실원. **어떻게 쓰나:** 정적 프로퍼티 읽기/쓰기, 변경 시 이벤트. **의존:** `UnityEngine.PlayerPrefs`, `Mathf`.

`MenuBgm`·`SfxMap`·`SafeAreaMath`와 동일한 **정적 클래스**. 볼륨은 본질적으로 앱 전역 상태(PlayerPrefs 자체가 전역)이고, BGM 소스(App 스코프)와 SFX 보이스(Table 씬)가 스코프를 넘어 읽어야 하므로 정적이 가장 마찰이 적다. (대안: DI 싱글톤 — 크로스-씬 접근에 컨테이너 조회가 필요해 더 복잡. 기존 정적 정책 클래스들과의 일관성을 택함.)

```csharp
namespace Tichu.Presentation.Audio
public static class VolumeSettings
{
    const string KeyBgm = "vol.bgm";
    const string KeySfx = "vol.sfx";
    const float DefaultBgm = 0.5f;   // 현재 하드코딩 값 유지
    const float DefaultSfx = 1.0f;

    public static event System.Action Changed;

    public static float Bgm { get; private set; } = DefaultBgm;
    public static float Sfx { get; private set; } = DefaultSfx;

    public static void Load();                 // PlayerPrefs 로드(부재 시 기본값)
    public static void SetBgm(float v);        // [0,1] 클램프 + 저장 + Changed
    public static void SetSfx(float v);        // [0,1] 클램프 + 저장 + Changed
}
```

- `Load()`는 App 부트 1회(`MenuShellPresenter.Start`)에서 호출.
- `SetBgm/SetSfx`: `Mathf.Clamp01` → 값 변경 시에만 `PlayerPrefs.SetFloat` + `PlayerPrefs.Save` + `Changed?.Invoke()`.
- **테스트(EditMode):** 클램프(음수→0, >1→1), 라운드트립(Set→Load 후 동일), `Changed` 발화. PlayerPrefs는 EditMode에서 동작하며, 테스트는 SetUp/TearDown에서 해당 키를 `DeleteKey`로 정리해 오염 방지.

### 유닛 2 — 버튼 클릭 SFX (A1)

- `SfxId` enum **끝에** `ButtonClick` 추가(`RoundEnd` 뒤 = 8 → 기존 항목의 직렬화 int id 보존, `AudioBank.asset` 매핑 불변). **`SfxMap`은 불변** — 버튼음은 `GameAction` 파생이 아니라 UI에서 직접 호출한다.
- `Resources/AudioBank.asset`에 `ButtonClick → ui_button_simple_click_01` 매핑 1줄 추가(CardPlay와 클립 공유 무방).
- `MenuShellPresenter`가 메뉴용 `IAudioService`를 App-수명으로 소유:
  - `Start`에서 `Resources.Load<AudioBank>("AudioBank")` → 있으면 `new UnityAudioService(bank, sfxVoices: 2)`, 없으면 `NoOpAudioService`(기존 폴백 컨벤션).
  - `WireButtons`에서 각 `onClick`을 헬퍼로 감싼다: `Wire(state, label, action)` → `_view.AddButton(state, label, () => { _menuAudio.PlaySfx(SfxId.ButtonClick); action(); })`. **View는 오디오를 모른다**(프레젠터가 배선).
- **테스트:** 스파이 `IAudioService` 주입 seam으로 버튼 클릭 시 `ButtonClick` 발화 검증(기존 `AudioWiringTests`의 스파이 패턴 재사용).

### 유닛 3 — 설정 볼륨 패널 (A2)

- `MenuShellView`에 슬라이더 빌더 추가:
  ```csharp
  public Slider AddSlider(ScreenState panel, string label, float initial, Action<float> onChange);
  ```
  버튼과 동일한 코드-빌드 패턴으로 배경 트랙 + Fill Area/Fill + Handle Slide Area/Handle을 구성한 uGUI `Slider`(min 0, max 1)를 패널 content(VerticalLayoutGroup)에 추가한다. 라벨은 슬라이더 위 텍스트. `onValueChanged`에 `onChange` 배선.
- `MenuShellPresenter`가 Settings 화면에 슬라이더 2개를 "뒤로" 위에 배치(배선은 `WireButtons`에서 "뒤로"보다 먼저 추가):
  - BGM: `AddSlider(Settings, "음악", VolumeSettings.Bgm, VolumeSettings.SetBgm)`
  - 효과음: `AddSlider(Settings, "효과음", VolumeSettings.Sfx, VolumeSettings.SetSfx)`
- **전파:**
  - BGM 소스: `SetupBgm`에서 초기 볼륨을 `VolumeSettings.Bgm`으로(하드코딩 0.5 제거). `Start`에서 `VolumeSettings.Changed` 구독 → `if (_bgm != null) _bgm.volume = VolumeSettings.Bgm`(실시간).
  - SFX: `UnityAudioService.PlaySfx`가 `PlayOneShot(clip, VolumeSettings.Sfx)`로 매 재생 시 반영(구독 불필요). 이 한 줄 변경으로 테이블 SFX + 메뉴 버튼음 모두 효과음 슬라이더를 따른다.
- **테스트:** 슬라이더 `onChange` 호출 → `VolumeSettings` 갱신 + PlayerPrefs 저장. (Slider 시각은 PlayMode 육안.)

### 유닛 4 — 앱 백그라운드 음소거 (A3)

- **순수 정책** `Shell/AudioLifecycle.cs` (`MenuBgm.PlaysIn` 미러, UnityEngine 무의존):
  ```csharp
  public static class AudioLifecycle { public static bool ShouldPause(bool focused) => !focused; }
  ```
- **얇은 MonoBehaviour** `Shell/AppLifecycleAudio.cs`:
  ```csharp
  void OnApplicationPause(bool paused) { AudioListener.pause = paused; }
  void OnApplicationFocus(bool focus) { AudioListener.pause = AudioLifecycle.ShouldPause(focus); }
  ```
  `AudioListener.pause`는 전역이라 BGM+SFX를 일괄 정지하고 복귀 시 자동 복원한다. App-수명 GO로 부트 시 생성(`MenuShellPresenter.Start`에서 `new GameObject("AppLifecycleAudio").AddComponent<AppLifecycleAudio>()`, `Dispose`에서 파괴). App 스코프는 항상 로드되므로 인게임 포함 전 구간에서 동작.
- **테스트(EditMode):** `AudioLifecycle.ShouldPause(true)==false`, `ShouldPause(false)==true`. (Unity 콜백 자체는 육안.)

## 파일 변경 요약

| 신규 | 편집 |
|---|---|
| `Presentation/Audio/VolumeSettings.cs` | `Presentation/Audio/SfxMap.cs` (enum `+ButtonClick`) |
| `Presentation/Shell/AudioLifecycle.cs` (순수) | `Presentation/Audio/UnityAudioService.cs` (SFX 볼륨) |
| `Presentation/Shell/AppLifecycleAudio.cs` (MB) | `Presentation/Shell/MenuShellPresenter.cs` (배선) |
| `Presentation/Tests/VolumeSettingsTests.cs` | `Presentation/Shell/Views/MenuShellView.cs` (`+AddSlider`) |
| `Presentation/Tests/AudioLifecycleTests.cs` | `Presentation/Resources/AudioBank.asset` (`+1` 매핑) |
| (메뉴 배선 테스트는 기존 `AudioWiringTests` 확장 가능) | |

## 불변식·회귀 가드

- **오라클 무침범:** 전부 셸/오디오 레이어. `AsyncGameDriver`의 `onApply` 경로·게임 로직 불변 → 오라클(동기==비동기) 회귀 0.
- **no-op 폴백 보존:** 뱅크 부재 → `NoOpAudioService`, 클립 미할당 → 무음. `VolumeSettings` 기본값(BGM 0.5/SFX 1.0)은 현재 동작과 동일 → 미변경 시 비트 동일에 준함.
- **`asmdef` 신규 0.** 코어 미러 불필요(전부 Presentation 전용).

## 검증 워크플로

1. 신규 `.cs`는 `execute_code`로 `AssetDatabase.ImportAsset(path, ForceUpdate)` + `Refresh(ForceUpdate)` 후 컴파일(메모리 지침).
2. `read_console` types=[error] filter "error CS"로 컴파일 확인.
3. `run_tests` 전 PlayMode 정지 필수 → EditMode `Tichu.Presentation.Tests` 어셈블리 실행(그린).
4. PlayMode 육안: 메뉴 버튼 클릭음 · 설정 슬라이더 조작(BGM 실시간·SFX 다음 재생) · 앱 포커스 아웃 시 뮤트/복귀.

## TDD 태스크 순서(예정)

1. `VolumeSettings` (순수 상태·영속) — RED→GREEN.
2. `AudioLifecycle` 순수 정책 + `AppLifecycleAudio` MonoBehaviour.
3. `SfxId.ButtonClick` + `AudioBank` 매핑 + `UnityAudioService` SFX 볼륨.
4. `MenuShellView.AddSlider` + `MenuShellPresenter` 배선(버튼음·슬라이더·BGM 전파·라이프사이클 GO).
5. 통합 검증(컴파일·EditMode·PlayMode 육안) → `--no-ff` 머지.
