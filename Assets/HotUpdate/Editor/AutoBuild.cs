using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 编译完成后自动检查 CDN 清单：少了 big / map1 / map2 就自动构建并同步首包。
/// 目的：开发者不需要手动点任何菜单，热更资源始终与代码保持一致。
/// </summary>
[InitializeOnLoad]
public static class AutoBuild
{
    static string CdnDir => BundleBuilder.OutDir;
    static readonly string[] Required = { "data", "map1", "map2" };

    static AutoBuild()
    {
        AllowInsecureHttp();
        EditorApplication.update += Tick;      // 定时兜底：不依赖「什么时候编译」
    }

    static double _nextTick;

    /// <summary>每 5 秒检查一次，缺 bundle 就构建；齐全后彻底停下</summary>
    static void Tick()
    {
        if (_done) return;
        if (EditorApplication.timeSinceStartup < _nextTick) return;
        _nextTick = EditorApplication.timeSinceStartup + 5;
        Check();
    }

    static readonly string[] DataAssets =
    {
        "Assets/SO/GameData.asset",
        "Assets/SO/Map1.asset", "Assets/SO/Map2.asset", "Assets/SO/Map3.asset", "Assets/SO/Map4.asset",
        "Assets/SO/Map5.asset", "Assets/SO/Map6.asset", "Assets/SO/Map7.asset",
    };

    /// <summary>
    /// 用 Unity 的 AssetImporter 再确认一次 bundle 标签，不依赖外部改 .meta 是否能被导入。
    /// 关卡表打不进 data bundle，客户端就只能永远用首包里那份。
    /// </summary>
    static bool EnsureDataBundleTag()
    {
        bool changed = false;
        foreach (string p in DataAssets)
        {
            AssetImporter imp = AssetImporter.GetAtPath(p);
            if (imp == null || imp.assetBundleName == "data") continue;
            imp.assetBundleName = "data";
            changed = true;
        }
        if (changed)
        {
            AssetDatabase.SaveAssets();
            Debug.Log("[HotUpdate] 已把 GameData / Map1~7 标记到 data bundle");
        }
        return changed;
    }

    /// <summary>
    /// 本地 CDN 是 http://，Unity 2022 默认禁止明文请求，会抛 Insecure connection not allowed。
    /// 这里直接把 Player Settings 的 Allow downloads over HTTP 设为 Always allowed。
    /// </summary>
    static void AllowInsecureHttp()
    {
        if (PlayerSettings.insecureHttpOption == InsecureHttpOption.AlwaysAllowed) return;
        PlayerSettings.insecureHttpOption = InsecureHttpOption.AlwaysAllowed;
        Debug.Log("[HotUpdate] 已把 Player Settings / Allow downloads over HTTP 设为 Always allowed（本机 CDN 是 http）");
    }

    static bool _tried;      // 每次脚本重编译只尝试构建一次，避免失败时无限重试
    static bool _hooked;     // 是否已经挂过「退出 Play」回调
    static bool _done;       // 两个平台都齐全后彻底停下

    static void Check()
    {
        // Play 中不能构建：bundle 文件句柄被占用会 IOException。等退出 Play 再补。
        if (EditorApplication.isPlayingOrWillChangePlaymode) { HookPlayMode(); return; }

        // 编辑器 Play 用 Win64、真机用当前激活平台，两边都必须齐全
        string root = Path.GetDirectoryName(CdnDir);
        if (Complete(Path.Combine(root, "Win64")) && Complete(CdnDir))
        {
            _done = true;
            return;
        }

        if (_tried) return;
        _tried = true;

        EnsureDataBundleTag();
        Debug.Log("[HotUpdate] CDN 缺少 bundle（data / map1 / map2）→ 自动构建并同步首包…");
        AssetDatabase.Refresh();
        BundleBuilder.BuildAndSyncQuiet();
    }

    static bool Complete(string dir)
    {
        string path = Path.Combine(dir, "manifest.json");
        if (!File.Exists(path)) return false;
        try { return HasAll(JsonUtility.FromJson<ManifestData>(File.ReadAllText(path))); }
        catch { return false; }
    }

    /// <summary>在 Play 里编译的脚本没法立刻构建，等退出 Play 后补一次</summary>
    static void HookPlayMode()
    {
        if (_hooked) return;
        _hooked = true;
        EditorApplication.playModeStateChanged += s =>
        {
            if (s != PlayModeStateChange.EnteredEditMode) return;
            _hooked = false;
            _tried = false;
            EditorApplication.delayCall += Check;
        };
    }

    static bool HasAll(ManifestData m)
    {
        if (m == null || m.bundles == null) return false;

        foreach (string name in Required)
        {
            bool found = false;
            foreach (BundleInfo b in m.bundles)
                if (b.name == name) { found = true; break; }
            if (!found) return false;
        }
        return true;
    }

    [MenuItem("HotUpdate/③ 重新自动构建（清标记）")]
    static void Reset()
    {
        _tried = false;
        _hooked = false;
        Check();
    }
}
