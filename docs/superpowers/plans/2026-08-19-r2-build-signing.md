# R2 빌드/서명 구현 계획

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 서명된 릴리스 AAB를 재현 가능하게 만들어 Google Play 비공개 테스트 트랙에 업로드 수용시킨다.

**Architecture:** Unity 에디터 스크립트 1개(`ReleaseBuild.cs`)가 릴리스 빌드의 단일 진입점이다. 프로젝트 결정(targetSdk 36 · AAB · versionCode)은 `ProjectSettings.asset`에 영구 저장·커밋하고, 키스토어 경로·비밀번호는 환경변수로만 주입해 빌드 후 `finally`로 원복한다. 산출물 검증은 Unity 번들 도구(`bundletool`·`jarsigner`)로 실측하고, 최종 판정은 Play 업로드 수용에 맡긴다.

**Tech Stack:** Unity 6000.3.17f1 · IL2CPP · Android Gradle · 번들 OpenJDK(`keytool`/`jarsigner`) · `bundletool-all-1.17.2.jar` · `aapt2` 36.0.0

**설계 스펙:** `docs/superpowers/specs/2026-08-19-r2-build-signing-design.md` (커밋 `1ec3791`)

## Global Constraints

- Unity `6000.3.17f1`. 에디터 경로 `E:/6000.3.17f1/Editor/Unity.exe`.
- Android SDK/JDK는 **Unity 번들만 사용한다.** 별도 설치 금지.
  - `AP` = `E:/6000.3.17f1/Editor/Data/PlaybackEngines/AndroidPlayer`
  - `keytool` = `AP/OpenJDK/bin/keytool.exe` · `jarsigner` = `AP/OpenJDK/bin/jarsigner.exe` · `java` = `AP/OpenJDK/bin/java.exe`
  - `bundletool` = `AP/Tools/bundletool-all-1.17.2.jar` · `adb` = `AP/SDK/platform-tools/adb.exe`
- `targetSdkVersion` = **36** (`AndroidSdkVersions.AndroidApiLevel36`). `minSdkVersion` **26 유지**.
- ARM64 단독 · IL2CPP · `stripEngineCode: 1` — **기존 값 변경 금지**.
- `productName` = `Tichu Master` · 패키지명 = `com.kas328.tichumaster` — **변경 금지**(Play 첫 업로드 후 영구 고정).
- `bundleVersion` = `1.0.0`. `versionCode`는 빌드마다 +1(재사용 불가).
- **asmdef를 추가하지 않는다.** `Editor` 폴더 자동 컴파일을 쓴다.
- **키스토어 파일과 비밀번호는 저장소에 절대 들어가지 않는다.** 커밋 전 `git status`로 확인한다.
- 브랜치 `feat/r2-build-signing`. 커밋 메시지는 한국어.

## 사전 조건 — Task 1 시작 전에 워킹트리를 정리한다

Task 4 Step 7은 `git diff ProjectSettings/ProjectSettings.asset`으로 **키스토어 경로가 커밋에 새는지**를 판정한다. 그 파일에 무관한 수정분이 남아 있으면 이 판정이 오염된다.

계획 작성 시점의 워킹트리:

- `ProjectSettings/ProjectSettings.asset` — `preloadedAssets`가 비워진 상태. Unity를 열고 닫을 때마다 재발하는 에디터 자동 재직렬화분이며 무해하다(R1에서 같은 성격의 변경을 `5f445a6`으로 수용한 전례가 있다).
- `티츄_현황브리핑_2026-08-19.html` — 미추적 보고서.
- `.utmp/` — 미추적 임시 디렉터리. 건드리지 않는다.

Task 1 시작 전에 앞의 두 건을 커밋해 트리를 비운다. `.utmp/`는 그대로 둔다.

```
git add ProjectSettings/ProjectSettings.asset 티츄_현황브리핑_2026-08-19.html
git commit -m "chore(r2): R2 착수 전 워킹트리 정리 — 에디터 재직렬화분 수용 + 현황 브리핑"
```

이후 `git status --porcelain`에 `.utmp/`만 남아야 한다.

## File Structure

| 파일 | 책임 |
|---|---|
| `.gitignore` (수정) | 키스토어 확장자 차단 |
| `Assets/_Project/Editor/ReleaseBuild.cs` (생성) | 릴리스 AAB 빌드 단일 진입점. 설정 적용·시크릿 주입·원복 |
| `ProjectSettings/ProjectSettings.asset` (수정) | `bundleVersion 1.0.0` 1회 확정. targetSdk·versionCode는 스크립트가 갱신 |

**사람만 할 수 있는 작업(Task 2·5·6)은 코드 변경이 없다.** 해당 태스크는 지시서이며 커밋을 만들지 않는다.

---

### Task 1: 시크릿 차단 (.gitignore)

**Files:**
- Modify: `.gitignore`

**Interfaces:**
- Consumes: 없음
- Produces: `*.jks`·`*.keystore`·`*.p12`·`keystore.properties`가 git에서 무시되는 상태. Task 2가 이에 의존한다.

**이 태스크는 Task 2보다 반드시 먼저 끝나야 한다.** 키를 만든 뒤에 무시 규칙을 넣으면 이미 스테이징된 키가 커밋될 수 있다.

- [ ] **Step 1: 현재는 차단되지 않음을 확인 (RED)**

Run: `git check-ignore -v tichumaster-upload.jks ; echo "exit=$?"`

Expected: 출력 없음 + `exit=1` (= 무시 규칙 없음)

- [ ] **Step 2: `.gitignore`에 규칙 추가**

`# Builds` 블록 바로 위에 다음 블록을 삽입한다.

```gitignore
# Signing secrets — 절대 커밋 금지
*.jks
*.keystore
*.p12
keystore.properties
```

- [ ] **Step 3: 차단되는지 확인 (GREEN)**

Run:
```
git check-ignore -v tichumaster-upload.jks ; echo "exit=$?"
git check-ignore -v foo.keystore ; echo "exit=$?"
```

Expected: 두 명령 모두 `.gitignore:<줄번호>:*.jks` 형태로 매칭 출력 + `exit=0`

- [ ] **Step 4: 커밋**

Run:
```
git add .gitignore
git commit -m "chore(r2): 키스토어 확장자를 git 에서 차단"
```

커밋 본문(한국어)에 "키 생성 전에 선행해야 하는 조치. 키를 만든 뒤 규칙을 넣으면 이미 스테이징된 키가 커밋될 수 있다"를 포함하고 `Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>` 트레일러를 붙인다.

---

### Task 2: 업로드 키스토어 생성·백업·복원 검증 〔사용자 실행〕

**Files:** 없음 (저장소 밖 파일만 생성)

**Interfaces:**
- Consumes: Task 1의 무시 규칙
- Produces: 키스토어 파일 + 환경변수 4개 `TICHU_KEYSTORE_PATH` · `TICHU_KEYSTORE_PASS` · `TICHU_KEY_ALIAS` · `TICHU_KEY_PASS`. Task 3·4·5가 이에 의존한다.

**비밀번호를 다루므로 사용자가 직접 실행한다.** 비밀번호를 대화에 붙여넣지 말 것.

- [ ] **Step 1: 저장소 밖에 키 디렉터리 생성**

```powershell
New-Item -ItemType Directory -Force "$env:USERPROFILE\.keys"
```

- [ ] **Step 2: 업로드 키스토어 생성**

`keytool`이 저장소 비밀번호·키 비밀번호·소유자 정보를 대화식으로 묻는다.

```powershell
& "E:\6000.3.17f1\Editor\Data\PlaybackEngines\AndroidPlayer\OpenJDK\bin\keytool.exe" -genkeypair -v -keystore "$env:USERPROFILE\.keys\tichumaster-upload.jks" -alias tichumaster -keyalg RSA -keysize 2048 -validity 10000
```

- `-validity 10000` 약 27년. Play는 2033-10-22 이후까지 유효한 키를 요구하므로 충분하다.
- 별칭은 `tichumaster`로 고정한다(환경변수 `TICHU_KEY_ALIAS`와 일치해야 한다).
- 저장소 비밀번호와 키 비밀번호를 같게 두어도 무방하다(업로드 키 용도).

- [ ] **Step 3: 생성 확인**

```powershell
& "E:\6000.3.17f1\Editor\Data\PlaybackEngines\AndroidPlayer\OpenJDK\bin\keytool.exe" -list -keystore "$env:USERPROFILE\.keys\tichumaster-upload.jks"
```

Expected: `tichumaster, <날짜>, PrivateKeyEntry,` 와 SHA-256 지문 출력.

- [ ] **Step 4: 백업 (스펙 §6.1의 3요건)**

1. **서로 다른 물리적 위치 2곳**에 복사한다. 최소 한 곳은 이 PC 외부(USB 등 오프라인 매체 또는 클라우드).
2. **비밀번호는 키스토어와 분리 보관**한다. 같은 폴더에 메모로 두면 백업이 아니라 단일 실패점이 하나 더 생기는 것이다.
3. 백업 파일명에 날짜를 남긴다: `tichumaster-upload-2026-08-19.jks`

- [ ] **Step 5: 복원 테스트 — 백업본으로 실제 서명이 되는지 확인**

"열어보지 않은 백업은 백업이 아니다." 백업본을 임시 경로로 되가져와 실제 서명을 시도한다.

```powershell
$AP = "E:\6000.3.17f1\Editor\Data\PlaybackEngines\AndroidPlayer"
$tmp = "$env:TEMP\r2-restore-test"
New-Item -ItemType Directory -Force $tmp | Out-Null
Copy-Item "<백업본 경로>\tichumaster-upload-2026-08-19.jks" "$tmp\restored.jks"
"restore test" | Out-File "$tmp\payload.txt"
Compress-Archive -Path "$tmp\payload.txt" -DestinationPath "$tmp\dummy.zip" -Force
& "$AP\OpenJDK\bin\jarsigner.exe" -keystore "$tmp\restored.jks" "$tmp\dummy.zip" tichumaster
& "$AP\OpenJDK\bin\jarsigner.exe" -verify -verbose "$tmp\dummy.zip"
```

Expected: 마지막 명령이 `jar verified.` 출력.

- [ ] **Step 6: 임시 파일 정리**

```powershell
Remove-Item -Recurse -Force "$env:TEMP\r2-restore-test"
```

- [ ] **Step 7: 환경변수 4개를 사용자 범위로 등록**

사용자 범위로 등록하면 이후 실행되는 프로세스(Unity 포함)가 값을 상속하므로, 빌드할 때마다 비밀번호를 다시 입력하거나 대화에 노출할 필요가 없다.

```powershell
[Environment]::SetEnvironmentVariable('TICHU_KEYSTORE_PATH', "$env:USERPROFILE\.keys\tichumaster-upload.jks", 'User')
[Environment]::SetEnvironmentVariable('TICHU_KEY_ALIAS', 'tichumaster', 'User')
[Environment]::SetEnvironmentVariable('TICHU_KEYSTORE_PASS', '<저장소 비밀번호>', 'User')
[Environment]::SetEnvironmentVariable('TICHU_KEY_PASS', '<키 비밀번호>', 'User')
```

**트레이드오프:** 사용자 범위 환경변수는 레지스트리에 평문으로 저장된다. 1인 개발 PC에서는 통상 수용하는 수준이나, 이 PC를 공유한다면 대신 빌드 직전에 세션 변수(`$env:TICHU_KEYSTORE_PASS = '...'`)로만 설정하고 그 세션에서 빌드를 실행할 것.

- [ ] **Step 8: 등록 확인 (값은 출력하지 않는다)**

**새 PowerShell 창을 연 뒤** 실행한다(기존 창은 갱신된 값을 상속하지 않는다).

```powershell
'TICHU_KEYSTORE_PATH','TICHU_KEYSTORE_PASS','TICHU_KEY_ALIAS','TICHU_KEY_PASS' | ForEach-Object { "{0} = {1}" -f $_, $(if ([Environment]::GetEnvironmentVariable($_,'User')) { 'OK' } else { '미설정' }) }
```

Expected: 4줄 모두 `OK`

- [ ] **Step 9: 저장소가 깨끗한지 확인**

Run: `git status --porcelain`

Expected: 키 관련 파일이 목록에 없다.

**커밋 없음** — 저장소에 들어가는 변경이 없다.

---

### Task 3: 릴리스 빌드 스크립트

**Files:**
- Create: `Assets/_Project/Editor/ReleaseBuild.cs`

**Interfaces:**
- Consumes: Task 2의 환경변수 4개
- Produces: `ReleaseBuild.BuildReleaseAab()` — 메뉴 `Tichu/Build Release AAB` 및 배치 모드 `-executeMethod ReleaseBuild.BuildReleaseAab` 진입점. Task 4가 호출한다. 산출물 경로 규칙 `Build/TichuMaster-{bundleVersion}-{versionCode}.aab`.

- [ ] **Step 1: 진입점이 아직 없음을 확인 (RED)**

Run: `ls Assets/_Project/Editor/ReleaseBuild.cs 2>/dev/null ; echo "exit=$?"`

Expected: `exit=2` (파일 없음)

- [ ] **Step 2: 스크립트 작성**

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// 릴리스 AAB 빌드의 단일 진입점.
///
/// 설정을 두 종류로 나눈다.
///  - 영구(커밋): targetSdk 36 · AAB · versionCode -> ProjectSettings 에 남는다.
///  - 일시(커밋 금지): 키스토어 경로·비밀번호·별칭 -> 환경변수로만 받고 finally 에서 원복한다.
///
/// AndroidKeystoreName 은 ProjectSettings.asset 에 직렬화되므로,
/// 원복하지 않으면 로컬 절대경로가 커밋된다.
/// </summary>
public static class ReleaseBuild
{
    const string PathVar = "TICHU_KEYSTORE_PATH";
    const string StorePassVar = "TICHU_KEYSTORE_PASS";
    const string AliasVar = "TICHU_KEY_ALIAS";
    const string KeyPassVar = "TICHU_KEY_PASS";

    [MenuItem("Tichu/Build Release AAB")]
    public static void BuildReleaseAab()
    {
        if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
            throw new Exception("활성 빌드 타겟이 Android 가 아니다. 플랫폼을 먼저 전환할 것.");

        var keystore = ReadRequired(PathVar);
        var storePass = ReadRequired(StorePassVar);
        var alias = ReadRequired(AliasVar);
        var keyPass = ReadRequired(KeyPassVar);

        if (!File.Exists(keystore))
            throw new Exception("키스토어 파일을 찾을 수 없다: " + keystore);

        PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevel36;
        EditorUserBuildSettings.buildAppBundle = true;
        PlayerSettings.Android.bundleVersionCode += 1;

        var versionCode = PlayerSettings.Android.bundleVersionCode;
        var outDir = Path.Combine(Directory.GetCurrentDirectory(), "Build");
        Directory.CreateDirectory(outDir);
        var outPath = Path.Combine(
            outDir,
            string.Format("TichuMaster-{0}-{1}.aab", PlayerSettings.bundleVersion, versionCode));

        try
        {
            PlayerSettings.Android.useCustomKeystore = true;
            PlayerSettings.Android.keystoreName = keystore;
            PlayerSettings.Android.keystorePass = storePass;
            PlayerSettings.Android.keyaliasName = alias;
            PlayerSettings.Android.keyaliasPass = keyPass;

            var options = new BuildPlayerOptions
            {
                scenes = EnabledScenes(),
                locationPathName = outPath,
                target = BuildTarget.Android,
                options = BuildOptions.None,
            };

            var report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result != BuildResult.Succeeded)
                throw new Exception("빌드 실패: " + report.summary.result);

            Debug.Log(string.Format(
                "[ReleaseBuild] 성공 {0} / versionCode={1} / targetSdk=36", outPath, versionCode));
        }
        finally
        {
            // 키 정보를 ProjectSettings 에 남기지 않는다.
            PlayerSettings.Android.keystoreName = string.Empty;
            PlayerSettings.Android.keystorePass = string.Empty;
            PlayerSettings.Android.keyaliasName = string.Empty;
            PlayerSettings.Android.keyaliasPass = string.Empty;
            PlayerSettings.Android.useCustomKeystore = false;
            AssetDatabase.SaveAssets();
        }
    }

    static string ReadRequired(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrEmpty(value))
            throw new Exception(
                "릴리스 빌드 중단 — 환경변수 " + name + " 이 설정되지 않았다. 서명 없는 산출물을 만들지 않는다.");
        return value;
    }

    static string[] EnabledScenes()
    {
        var list = new List<string>();
        foreach (var scene in EditorBuildSettings.scenes)
            if (scene.enabled) list.Add(scene.path);
        if (list.Count == 0)
            throw new Exception("빌드 설정에 활성 씬이 없다.");
        return list.ToArray();
    }
}
```

- [ ] **Step 3: 컴파일 확인 (GREEN)**

Unity 에디터를 **종료한 상태**에서 배치 모드로 컴파일한다.

```powershell
$log = "$env:TEMP\r2-compile.log"
& "E:\6000.3.17f1\Editor\Unity.exe" -quit -batchmode -nographics -projectPath "C:\Users\user\Desktop\Project\Tichu" -buildTarget Android -logFile $log
"exit=$LASTEXITCODE"
Select-String -Path $log -Pattern "error CS|Compilation failed" | Select-Object -First 20
```

Expected: `exit=0`, `error CS` 매칭 0건.

Unity 에디터가 켜져 있으면 배치 모드가 프로젝트 잠금으로 실패한다. 그 경우 에디터에서 스크립트 리컴파일 후 콘솔 에러 0건 확인으로 대체한다.

- [ ] **Step 4: 메뉴 항목이 등록됐는지 확인**

Run: `grep -n "MenuItem\|BuildReleaseAab" Assets/_Project/Editor/ReleaseBuild.cs`

Expected: `[MenuItem("Tichu/Build Release AAB")]` 와 `public static void BuildReleaseAab()` 가 각각 1건.

- [ ] **Step 5: 커밋**

Run:
```
git add Assets/_Project/Editor/ReleaseBuild.cs
git status --porcelain
git commit -m "feat(r2): 릴리스 AAB 빌드 스크립트"
```

커밋 본문에 다음을 포함한다: 영구/일시 설정 분리 이유, `AndroidKeystoreName`이 `.asset`에 직렬화되므로 `finally` 원복이 필요하다는 점, 환경변수 누락 시 빌드를 시작조차 하지 않는 이유. `Co-Authored-By` 트레일러를 붙인다.

---

### Task 4: 릴리스 AAB 빌드 + 산출물 실측

**Files:**
- Modify: `ProjectSettings/ProjectSettings.asset` (`bundleVersion` 1회 확정 + 빌드가 갱신하는 targetSdk·versionCode)

**Interfaces:**
- Consumes: `ReleaseBuild.BuildReleaseAab()` (Task 3), 환경변수 4개 (Task 2)
- Produces: `Build/TichuMaster-1.0.0-2.aab` — Task 5·6이 사용한다.

- [ ] **Step 1: Unity 에디터 종료 확인**

```powershell
Get-Process Unity -ErrorAction SilentlyContinue | Select-Object Id, MainWindowTitle
```

Expected: 출력 없음. 떠 있으면 종료한다(배치 모드가 프로젝트 잠금으로 실패한다).

- [ ] **Step 2: `bundleVersion`을 1.0.0으로 확정**

Unity가 종료된 상태에서만 디스크 편집이 안전하다.

Run:
```
sed -i 's/^  bundleVersion: 0\.1\.0$/  bundleVersion: 1.0.0/' ProjectSettings/ProjectSettings.asset
grep -n "^  bundleVersion:" ProjectSettings/ProjectSettings.asset
```

Expected: `  bundleVersion: 1.0.0`

- [ ] **Step 3: 릴리스 AAB 빌드**

```powershell
$log = "$env:TEMP\r2-build.log"
& "E:\6000.3.17f1\Editor\Unity.exe" -quit -batchmode -nographics -projectPath "C:\Users\user\Desktop\Project\Tichu" -buildTarget Android -executeMethod ReleaseBuild.BuildReleaseAab -logFile $log
"exit=$LASTEXITCODE"
Select-String -Path $log -Pattern "\[ReleaseBuild\]|error CS|BuildFailed|Exception"
```

Expected: `exit=0` 과 `[ReleaseBuild] 성공 ... versionCode=2 ... targetSdk=36`

IL2CPP 릴리스 빌드는 수십 분이 걸릴 수 있다. 백그라운드로 실행하고 로그를 폴링한다. Android 플랫폼이 활성이면 Unity가 ADB 서버를 띄워 대기 호출이 반환되지 않을 수 있으므로, 완료 판정은 로그 파일로 한다.

- [ ] **Step 4: 산출물 존재 확인**

Run: `ls -la Build/*.aab`

Expected: `TichuMaster-1.0.0-2.aab` 존재.

- [ ] **Step 5: targetSdk·versionCode 실측**

```powershell
$AP = "E:\6000.3.17f1\Editor\Data\PlaybackEngines\AndroidPlayer"
$aab = "C:\Users\user\Desktop\Project\Tichu\Build\TichuMaster-1.0.0-2.aab"
& "$AP\OpenJDK\bin\java.exe" -jar "$AP\Tools\bundletool-all-1.17.2.jar" dump manifest --bundle=$aab
```

Expected: 출력 XML에 `android:targetSdkVersion="36"` · `android:minSdkVersion="26"` · `android:versionCode="2"` · `package="com.kas328.tichumaster"`

- [ ] **Step 6: 서명 검증**

```powershell
$AP = "E:\6000.3.17f1\Editor\Data\PlaybackEngines\AndroidPlayer"
& "$AP\OpenJDK\bin\jarsigner.exe" -verify "C:\Users\user\Desktop\Project\Tichu\Build\TichuMaster-1.0.0-2.aab"
```

Expected: `jar verified.`

- [ ] **Step 7: 키 경로가 저장소에 남지 않았는지 확인 (스펙 리스크 2의 방어선)**

Run:
```
git diff ProjectSettings/ProjectSettings.asset | grep -nE "AndroidKeystoreName|keyalias|Keystore"
grep -n "AndroidKeystoreName" ProjectSettings/ProjectSettings.asset
```

Expected: 첫 명령은 `AndroidKeystoreName` 변경 없음. 두 번째는 `  AndroidKeystoreName: ` (값 없음).

**값이 남아 있으면 즉시 중단하고 Task 3의 `finally` 블록을 점검한다.**

- [ ] **Step 8: 커밋**

Run:
```
git add ProjectSettings/ProjectSettings.asset
git status --porcelain
git commit -m "chore(r2): 출시 빌드 설정 확정 — bundleVersion 1.0.0 · targetSdk 36 · AAB"
```

커밋 본문에 targetSdk를 Automatic(0)에서 36으로 고정한 이유(2026-08-31부터 Play 신규 앱 API 36 요구 + 빌드 재현성)를 남긴다.

---

### Task 5: S23 릴리스 스모크 〔사용자 실행〕

**Files:** 없음

**Interfaces:**
- Consumes: `Build/TichuMaster-1.0.0-2.aab` (Task 4)
- Produces: 릴리스 빌드가 실기에서 동작한다는 판정. Task 6의 선행 게이트.

**이 태스크가 IL2CPP 스트리핑 회귀를 잡는 유일한 그물이다.** 개발 빌드는 통과했지만 릴리스 빌드는 처음이며, VContainer·R3의 리플렉션 타입이 잘리면 **런타임에만** 터진다.

- [ ] **Step 1: AAB에서 설치 가능한 APK 생성**

AAB는 기기에 직접 설치할 수 없다. 업로드할 바로 그 산출물에서 universal APK를 만들어 검증한다.

```powershell
$AP = "E:\6000.3.17f1\Editor\Data\PlaybackEngines\AndroidPlayer"
$build = "C:\Users\user\Desktop\Project\Tichu\Build"
& "$AP\OpenJDK\bin\java.exe" -jar "$AP\Tools\bundletool-all-1.17.2.jar" build-apks --bundle="$build\TichuMaster-1.0.0-2.aab" --output="$build\TichuMaster-1.0.0-2.apks" --mode=universal --ks=$env:TICHU_KEYSTORE_PATH --ks-key-alias=$env:TICHU_KEY_ALIAS --ks-pass=pass:$env:TICHU_KEYSTORE_PASS --key-pass=pass:$env:TICHU_KEY_PASS
```

Expected: `.apks` 파일 생성.

- [ ] **Step 2: S23 연결 확인**

USB 디버깅을 켜고 케이블로 연결한 뒤 실행한다.

```powershell
& "E:\6000.3.17f1\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe" devices
```

Expected: 기기 시리얼이 `device` 상태로 1줄 출력.

- [ ] **Step 3: 기존 빌드 제거**

```powershell
& "E:\6000.3.17f1\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe" uninstall com.kas328.tichumaster
```

Expected: `Success` 또는 미설치 시 실패 메시지(무시하고 진행).

- [ ] **Step 4: 릴리스 APK 설치**

```powershell
$AP = "E:\6000.3.17f1\Editor\Data\PlaybackEngines\AndroidPlayer"
& "$AP\OpenJDK\bin\java.exe" -jar "$AP\Tools\bundletool-all-1.17.2.jar" install-apks --apks="C:\Users\user\Desktop\Project\Tichu\Build\TichuMaster-1.0.0-2.apks" --adb="$AP\SDK\platform-tools\adb.exe"
```

- [ ] **Step 5: 스모크 체크리스트 (사람 판정)**

한 판을 끝까지 진행하며 확인한다.

- [ ] 앱이 실행되고 메뉴가 뜬다 (**스트리핑 회귀 1차 관문** — DI 컨테이너가 여기서 터진다)
- [ ] 난이도 선택 → 게임 시작이 된다
- [ ] **한 판을 끝까지 완주한다** (딜링 → 교환 → 플레이 → 결과)
- [ ] 크래시 0
- [ ] **FpsOverlay가 보이지 않는다** (`Debug.isDebugBuild` 게이트가 릴리스에서 꺼지는지 확인)
- [ ] 코너 UI(총점·페이즈·최근 플레이)가 둥근 모서리에 안 잘린다 — 기본 방향 + 자동회전 180° 양쪽
- [ ] 사운드가 나온다(카드·버튼·BGM)

- [ ] **Step 6: 실패 시 대응**

앱이 즉시 종료되거나 특정 화면에서만 죽으면 스트리핑 회귀를 의심한다.

```powershell
& "E:\6000.3.17f1\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe" logcat -d -s Unity:V AndroidRuntime:E > "$env:TEMP\r2-smoke.log"
```

`ExecutionEngineException` · `MissingMethodException` · `TypeLoadException` 이 보이면 `Assets/link.xml`로 해당 어셈블리 보존을 선언하고 Task 4부터 다시 실행한다.

**커밋 없음** (실패 시 후속 수정 커밋은 별도).

---

### Task 6: Play 계정 · 앱 등록 · 비공개 트랙 업로드 〔사용자 실행〕

**Files:** 없음

**Interfaces:**
- Consumes: `Build/TichuMaster-1.0.0-2.aab` (Task 4), Task 5 스모크 통과
- Produces: **R2 완료.** 구글이 AAB를 받아들이면 targetSdk 36 · 16KB 정렬 · 서명 · AAB 형식이 모두 통과된 것이다.

**이 태스크의 트랙 공개는 R3·R4가 끝나야 가능하다.** 스펙 §2대로 App content 선언과 완성된 스토어 등록정보가 선행 조건이다. R2의 완료 기준은 **AAB 업로드가 수용되는 것**까지이며, 트랙 공개는 R3·R4 완료 후다.

- [ ] **Step 1: Google Play 개발자 계정 생성 (개인)**

https://play.google.com/console/signup — 등록비 $25(1회), 본인 확인 필요. 계정 유형은 **개인(personal)** 으로 생성한다(스펙 §2.1 결정).

- [ ] **Step 2: 앱 만들기**

- 앱 이름: `Tichu Master`
- 기본 언어: 한국어
- 앱/게임: **게임**
- 유료/무료: 무료

- [ ] **Step 3: Play App Signing 확인**

앱 등록 시 기본으로 적용된다. 별도 선택 없이 기본 경로를 따른다(스펙 §6.2). 우리가 만든 키는 **업로드 키**다.

- [ ] **Step 4: 비공개 테스트 트랙에 AAB 업로드**

테스트 → 비공개 테스트 → 새 버전 만들기 → `TichuMaster-1.0.0-2.aab` 업로드.

Expected: 업로드가 수용되고 버전 정보에 `대상 SDK 36` · `버전 코드 2` 가 표시된다.

- [ ] **Step 5: 거부 시 대응**

| 거부 사유 | 대응 |
|---|---|
| target API level | Task 4 Step 5에서 36을 실측했으므로 발생하지 않아야 한다. 발생 시 빌드 로그 확인 |
| 16KB 페이지 정렬 | Unity 빌드 설정 재검토(스펙 §7.3). Unity 버전 이슈일 수 있어 릴리스 노트 확인 |
| 서명 문제 | Task 2의 키스토어·별칭 불일치 확인 |
| versionCode 중복 | 재빌드하면 스크립트가 자동 증분한다 |

- [ ] **Step 6: R2 완료 기록**

업로드가 수용되면 R2 DoD 6개가 모두 통과다. 결과를 대시보드·메모리에 반영하고 브랜치를 `main`에 `--no-ff`로 머지한다.

**병행 확인:** 테스터 15~18명 명단이 준비돼 있으면, R3·R4 완료 즉시 트랙을 공개해 14일 시계를 켤 수 있다.

---

## 완료 조건 (스펙 §4 DoD 대조)

| DoD | 태스크 | 검증 |
|---|---|---|
| 1 `.gitignore` 차단 | Task 1 | `git check-ignore` exit=0 |
| 2 키스토어 생성·백업 | Task 2 | 백업본으로 `jar verified.` |
| 3 빌드 스크립트 | Task 3 | 컴파일 0 에러 |
| 4 서명 AAB | Task 4 | `bundletool dump manifest` targetSdk 36 · `jarsigner -verify` |
| 5 S23 스모크 | Task 5 | 완주 · 크래시 0 · FpsOverlay 미표시 |
| 6 업로드 수용 | Task 6 | Play Console 수용 |
