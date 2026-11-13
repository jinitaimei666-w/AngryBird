using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class DemoUI : MonoBehaviour
{
    public HotUpdateManager hotUpdate;
    public Image target;
    public string assetName = "pic_v1";

    [Tooltip("实验开关：勾选后本次启动不写成功标记，用来模拟启动失败")]
    public bool simulateBootFailure = false;

    IEnumerator Start()
    {
        if (hotUpdate == null) { Debug.LogError("没有 HotUpdateManager"); yield break; }

        yield return hotUpdate.EnsureUpdated(Debug.Log);

        AssetBundle ui = hotUpdate.LoadBundle();
        if (ui == null) { Debug.LogError("[DemoUI] bundle 加载失败"); yield break; }
        Sprite sprite = ui.LoadAsset<Sprite>(assetName);
        if (sprite == null) { Debug.LogError("[DemoUI] asset 名不对：" + assetName); yield break; }
        target.sprite = sprite;
        Debug.Log("[DemoUI] 显示成功：" + assetName);

        AssetBundle pb = hotUpdate.LoadBundle("prefabs");
        GameObject go = pb != null ? pb.LoadAsset<GameObject>("ceshi2") : null;
        if (go == null) { Debug.LogError("[DemoUI] 加载 ceshi2 失败"); yield break; }
        Instantiate(go, target.canvas.transform, false);
        Debug.Log("[DemoUI] 已实例化 ceshi2");

        // 【3.2】走到这里才算"启动成功"
        if (simulateBootFailure)
        {
            Debug.LogWarning("[DemoUI] 模拟启动失败：不写成功标记");
            yield break;
        }
        hotUpdate.MarkBootOk();
    }
}