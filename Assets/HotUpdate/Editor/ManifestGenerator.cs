using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;

public static class ManifestGenerator
{
    // 注意：BundleInfo / ManifestData 是全局类型，定义在 HotUpdateTypes.cs（运行时可共用）

    /// <summary>生成清单。version 自动推导：内容变了补丁号 +1，内容没变保持原版</summary>
    public static void Generate(string outDir, string appVersion = "1.0.0")
    {
        // 清单 bundle 的名字 = 输出目录名（BuildAssetBundles 用目录名做清单名）：
        // Win64 → Win64，Android → Android
        var manifestPath = Path.Combine(outDir, Path.GetFileName(outDir.TrimEnd('/', '\\')));
        var mb = AssetBundle.LoadFromFile(manifestPath);
        if (mb == null) throw new Exception("找不到清单 bundle: " + manifestPath);
        var manifest = mb.LoadAsset<AssetBundleManifest>("AssetBundleManifest");

        var all = manifest.GetAllAssetBundles();
        var list = new BundleInfo[all.Length];
        for (int i = 0; i < all.Length; i++)
        {
            var file = Path.Combine(outDir, all[i]);
            list[i] = new BundleInfo
            {
                name = all[i],
                hash = manifest.GetAssetBundleHash(all[i]).ToString(),
                md5 = Md5(file),
                size = new FileInfo(file).Length,
                dependencies = manifest.GetAllDependencies(all[i]),
            };
        }
        mb.Unload(true);

        // 【3.3】读上一份已发布清单，判定内容有没有变
        var prevPath = Path.Combine(outDir, "manifest.json");
        ManifestData prev = File.Exists(prevPath)
            ? JsonUtility.FromJson<ManifestData>(File.ReadAllText(prevPath)) : null;

        bool changed = !SameContent(prev, list);
        string version = NextVersion(prev != null ? prev.version : null, changed);

        var data = new ManifestData { version = version, appVersion = appVersion, bundles = list };
        File.WriteAllText(prevPath, JsonUtility.ToJson(data, true), new UTF8Encoding(false));

        string tag = prev == null
            ? "首次发布"
            : changed ? ("内容有变化，" + prev.version + " → " + version)
                      : ("内容无变化，保持 " + version);
        Debug.Log("[HotUpdate] 清单生成：" + list.Length + " 个 bundle，version=" + version + "（" + tag + "）");
    }

    // 只比 name / hash / dependencies —— size、md5 都是 hash 的派生物，版本号管不着
    static bool SameContent(ManifestData prev, BundleInfo[] cur)
    {
        if (prev == null || prev.bundles == null || prev.bundles.Length != cur.Length) return false;
        var map = new Dictionary<string, BundleInfo>();
        foreach (var b in prev.bundles) map[b.name] = b;

        foreach (var b in cur)
        {
            BundleInfo o;
            if (!map.TryGetValue(b.name, out o)) return false;      // 新增了 bundle
            if (o.hash != b.hash) return false;                     // 内容变了
            if (Join(o.dependencies) != Join(b.dependencies)) return false;   // 依赖关系变了
        }
        return true;
    }

    static string Join(string[] arr)
    {
        return arr == null ? "" : string.Join("|", arr.OrderBy(x => x));
    }

    // 内容变了：补丁号 +1；内容没变：原样返回
    static string NextVersion(string prev, bool changed)
    {
        if (string.IsNullOrEmpty(prev)) return "1.0.0";
        if (!changed) return prev;

        string[] parts = prev.Split('.');
        int patch;
        if (parts.Length == 3 && int.TryParse(parts[2], out patch))
            return parts[0] + "." + parts[1] + "." + (patch + 1);
        return prev + ".1";                     // 非常规版本号的兜底
    }

    static string Md5(string path)
    {
        using (var md5 = MD5.Create())
        using (var fs = File.OpenRead(path))
        {
            var bytes = md5.ComputeHash(fs);
            var sb = new StringBuilder();
            foreach (var b in bytes) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }
    }
}