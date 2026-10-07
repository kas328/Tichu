using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// 실기 검증용 개발 APK 빌드(Development Build · 디버그 키 서명).
/// 친구방(NetJoin)·FPS 오버레이는 개발 빌드에서만 켜진다. 릴리스는 ReleaseBuild.BuildReleaseAab.
/// 배치: -executeMethod DevBuild.BuildDevApk
/// </summary>
public static class DevBuild
{
    [MenuItem("Tichu/Build Dev APK")]
    public static void BuildDevApk()
    {
        if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
            throw new Exception("활성 빌드 타겟이 Android 가 아니다. 플랫폼을 먼저 전환할 것.");

        var outDir = Path.Combine(Directory.GetCurrentDirectory(), "Build");
        Directory.CreateDirectory(outDir);
        var outPath = Path.Combine(outDir, "TichuMaster-dev-arm64.apk");

        bool wasBundle = EditorUserBuildSettings.buildAppBundle;
        try
        {
            EditorUserBuildSettings.buildAppBundle = false;   // APK 로 직접 설치하기 위해

            var scenes = new System.Collections.Generic.List<string>();
            foreach (var s in EditorBuildSettings.scenes)
                if (s.enabled) scenes.Add(s.path);

            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes.ToArray(),
                locationPathName = outPath,
                target = BuildTarget.Android,
                options = BuildOptions.Development,
            });
            if (report.summary.result != BuildResult.Succeeded)
                throw new Exception("빌드 실패: " + report.summary.result);

            Debug.Log("[DevBuild] 성공 " + outPath);
        }
        finally
        {
            EditorUserBuildSettings.buildAppBundle = wasBundle;
        }
    }
}
