using System.IO;
using UnityEditor;
using UnityEngine;

public static class BundleBuilder
{
    const string CdnRoot = @"C:\GameCDN";

    /// <summary>输出目录跟着当前激活平台走：CDN/Android、CDN/Win64 …</summary>
    public static string PlatformDir
    {
        get
        {
            switch (EditorUserBuildSettings.activeBuildTarget)
            {
                case BuildTarget.Android: return "Android";
                case BuildTarget.iOS: return "iOS";
                default: return "Win64";
            }
        }
    }

    static BuildTarget Target
    {
        get
        {
            switch (EditorUserBuildSettings.activeBuildTarget)
            {
                case BuildTarget.Android: return BuildTarget.Android;
                case BuildTarget.iOS: return BuildTarget.iOS;
                default: return BuildTarget.StandaloneWindows64;
            }
        }
    }

    public static string OutDir => Path.Combine(CdnRoot, PlatformDir);

    static string NameOf(BuildTarget t)
    {
        if (t == BuildTarget.Android) return "Android";
        if (t == BuildTarget.iOS) return "iOS";
        return "Win64";
    }

    /// <summary>
    /// 当前激活平台之外，永远额外构建一份 Win64：
    /// 编辑器 Play 只能加载 Win64 的 bundle，切到 Android 出包时编辑器的热更测试也不能断。
    /// </summary>
    static BuildTarget[] Targets
    {
        get
        {
            BuildTarget t = Target;
            return t == BuildTarget.StandaloneWindows64
                ? new[] { t }
                : new[] { t, BuildTarget.StandaloneWindows64 };
        }
    }

    [MenuItem("HotUpdate/① 构建到 CDN")]
    public static void BuildToCdn() { Build(true); }

    [MenuItem("HotUpdate/② 构建并同步首包")]
    public static void BuildToStreamingAssets() { Build(true); SyncStreaming(true); }

    /// <summary>自动流程用：不弹资源管理器，构建完顺带把首包同步好。</summary>
    public static void BuildAndSyncQuiet() { Build(false); SyncStreaming(false); }

    static void Build(bool reveal)
    {
        foreach (BuildTarget t in Targets)
        {
            string outDir = Path.Combine(CdnRoot, NameOf(t));
            Directory.CreateDirectory(outDir);
            BuildPipeline.BuildAssetBundles(
                outDir,
                BuildAssetBundleOptions.ChunkBasedCompression |
                BuildAssetBundleOptions.ForceRebuildAssetBundle,
                t);

            ManifestGenerator.Generate(outDir);             // 版本号由内容 hash 推导
            Debug.Log($"[HotUpdate] 构建完成（{NameOf(t)}）→ {outDir}");
        }
        if (reveal) EditorUtility.RevealInFinder(OutDir);
    }

    public static void SyncStreaming()
    {
        SyncStreaming(true);
    }

    static void SyncStreaming(bool log)
    {
        foreach (BuildTarget t in Targets)
        {
            string name = NameOf(t);
            string src = Path.Combine(CdnRoot, name);
            string dst = Path.Combine(Application.dataPath, "StreamingAssets/bundles", name);
            if (!Directory.Exists(src)) continue;

            if (Directory.Exists(dst)) Directory.Delete(dst, true);
            Directory.CreateDirectory(dst);
            foreach (var f in Directory.GetFiles(src))
                File.Copy(f, Path.Combine(dst, Path.GetFileName(f)), true);

            if (log) Debug.Log($"[HotUpdate] 首包资源已同步（{name}）→ {dst}");
        }

        AssetDatabase.Refresh();
    }
}
