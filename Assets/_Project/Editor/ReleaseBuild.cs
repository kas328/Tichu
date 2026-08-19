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
