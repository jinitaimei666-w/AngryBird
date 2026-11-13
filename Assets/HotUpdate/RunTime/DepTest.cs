using UnityEngine;

public class DepTest : MonoBehaviour
{
    public bool loadDependencyFirst = false;   // ← Inspector 里的实验开关

    void Start()
    {
        string root = Application.streamingAssetsPath + "/bundles/";

        AssetBundle dep = null;
        if (loadDependencyFirst)
        {
            dep = AssetBundle.LoadFromFile(root + "ui");
            Debug.Log("[Dep] 先加载依赖 ui：" + (dep != null ? "成功" : "失败"));
        }

        AssetBundle pb = AssetBundle.LoadFromFile(root + "prefabs");
        if (pb == null) { Debug.LogError("[Dep] prefabs 加载失败，先点 ② 同步首包"); return; }

        GameObject go = pb.LoadAsset<GameObject>("ceshi2");
        if (go == null) { Debug.LogError("[Dep] bundle 里没有 ceshi2"); return; }

        Instantiate(go, GameObject.Find("Canvas").transform, false);
        Debug.Log("[Dep] 已实例化 ceshi2，依赖 ui " + (dep != null ? "已加载" : "【未加载】"));
    }
}