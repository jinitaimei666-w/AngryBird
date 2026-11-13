using UnityEditor;
using UnityEngine;

public static class BundleTagTool
{
    [MenuItem("HotUpdate/工具·打标签 ui")] static void TagUi() { Tag("ui"); }
    //[MenuItem("HotUpdate/工具/打标签 ui")] static void TagUi() { Tag("ui"); }
    [MenuItem("HotUpdate/工具/打标签 prefabs")] static void TagPrefabs() { Tag("prefabs"); }
    [MenuItem("HotUpdate/工具/打标签 big")] static void TagBig() { Tag("big"); }   // ← 新增 }
    [MenuItem("HotUpdate/工具·清除选中标签")] static void ClearTag() { Tag(""); }
    [MenuItem("HotUpdate/工具·查看选中标签")]
    static void PrintTag()
    {
        if (Selection.objects.Length == 0) { Debug.LogWarning("[Tag] 先在 Project 里选中资源"); return; }
        foreach (var o in Selection.objects)
        {
            var path = AssetDatabase.GetAssetPath(o);
            var imp = AssetImporter.GetAtPath(path);
            Debug.Log("[Tag] " + path + " → \"" + (imp == null ? "<无 importer>" : imp.assetBundleName) + "\"");
        }
    }

    static void Tag(string name)
    {
        if (Selection.objects.Length == 0) { Debug.LogWarning("[Tag] 先在 Project 里选中资源"); return; }
        foreach (var o in Selection.objects)
        {
            var path = AssetDatabase.GetAssetPath(o);
            if (string.IsNullOrEmpty(path)) continue;                 // 场景对象没有资产路径，跳过
            var imp = AssetImporter.GetAtPath(path);
            if (imp == null) { Debug.LogError("[Tag] 拿不到 importer: " + path); continue; }
            imp.assetBundleName = name;
            imp.SaveAndReimport();
            Debug.Log("[Tag] " + path + " → \"" + imp.assetBundleName + "\"");
        }
    }
}