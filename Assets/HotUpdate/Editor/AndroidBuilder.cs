using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// 一键打 Android 安装包：切平台 → 打 Android bundle → 同步首包 → BuildPlayer 出 APK。
/// 平台切换会让 Unity 重新导入资源，域重载后会自动继续（用 SessionState 记住断点）。
/// </summary>
[InitializeOnLoad]
public static class AndroidBuilder
{
    const string PendingKey = "HotUpdate_AndroidBuildPending";

    static AndroidBuilder()
    {
        if (SessionState.GetBool(PendingKey, false))
            EditorApplication.update += WaitSwitch;
    }

    [MenuItem("HotUpdate/⑤ 打包 Android 安装包")]
    public static void BuildApk()
    {
        if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
        {
            if (!EditorUtility.DisplayDialog("切换到 Android",
                "需要先切换到 Android 平台，Unity 会重新导入全部资源（可能几分钟）。\n切换完成后会自动继续打包。",
                "继续", "取消"))
                return;

            SessionState.SetBool(PendingKey, true);
            EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);
            EditorApplication.update += WaitSwitch;
            return;
        }

        DoBuild();
    }

    static void WaitSwitch()
    {
        if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android) return;
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;

        EditorApplication.update -= WaitSwitch;
        SessionState.SetBool(PendingKey, false);
        DoBuild();
    }

    static void DoBuild()
    {
        // 包名没填过就给一个，否则打包会失败
        if (string.IsNullOrEmpty(PlayerSettings.applicationIdentifier))
            PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Android, "com.defaultcompany.angrybird");

        // 要的是能装的 .apk，不是上架用的 .aab
        EditorUserBuildSettings.buildAppBundle = false;
        EditorUserBuildSettings.exportAsGoogleAndroidProject = false;

        // 1) Android bundle → C:\GameCDN\Android
        BundleBuilder.BuildToCdn();
        // 2) 同步到 StreamingAssets/bundles/Android（APK 内首包）
        BundleBuilder.SyncStreaming();

        // 3) 场景列表取 Build Settings 里勾选的
        var list = new System.Collections.Generic.List<string>();
        foreach (var s in EditorBuildSettings.scenes)
            if (s.enabled) list.Add(s.path);

        if (list.Count == 0)
        {
            Debug.LogError("[HotUpdate] Build Settings 里没有启用任何场景，无法打包");
            return;
        }

        string outDir = Path.GetFullPath(Path.Combine(Application.dataPath, "../Builds"));
        Directory.CreateDirectory(outDir);
        string apk = Path.Combine(outDir, "AngryBird.apk");

        Debug.Log("[HotUpdate] 开始打包 Android：" + apk);
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = list.ToArray(),
            locationPathName = apk,
            target = BuildTarget.Android,
            options = BuildOptions.None,
        });

        if (report.summary.result == BuildResult.Succeeded)
        {
            Debug.Log($"[HotUpdate] APK 打包成功：{apk}（{report.summary.totalSize / 1024 / 1024} MB）");
            EditorUtility.RevealInFinder(apk);
        }
        else
        {
            Debug.LogError("[HotUpdate] APK 打包失败：" + report.summary.result +
                           "，看 Console 第一条红字");
        }
    }
}
