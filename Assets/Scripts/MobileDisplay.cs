using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 手机端显示适配，全程代码生效，不用在三个场景里改 Canvas。
/// 1) 锁定横屏（禁止竖屏）
/// 2) UI 从「固定像素」改成「按 1920×1080 等比缩放」（以高度为准）
/// 3) 结算遮罩 CenterBg 铺满全屏
/// </summary>
public static class MobileDisplay
{
    static readonly Vector2 RefResolution = new Vector2(1920f, 1080f);

    /// <summary>每个场景的可视宽度（世界单位），按场景原始正交尺寸 × 2 × 16/9 推出来</summary>
    static readonly System.Collections.Generic.Dictionary<string, float> _designWidth =
        new System.Collections.Generic.Dictionary<string, float>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void LockLandscape()
    {
        Screen.orientation = ScreenOrientation.AutoRotation;
        Screen.autorotateToPortrait = false;
        Screen.autorotateToPortraitUpsideDown = false;
        Screen.autorotateToLandscapeLeft = true;
        Screen.autorotateToLandscapeRight = true;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
        Apply(SceneManager.GetActiveScene().name);
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        Apply(scene.name);
    }

    static void Apply(string sceneName)
    {
        // ── ① 所有 Canvas 改成等比缩放
        foreach (CanvasScaler scaler in Object.FindObjectsOfType<CanvasScaler>(true))
        {
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = RefResolution;
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 1f;   // 以高度为准：1080 高不变，宽屏只是多露两侧背景
        }

        // ── ② 相机锁「设计宽度」：每个场景的美术只按 16:9 铺满，
        //      屏幕更宽时不能再放开视野，否则两侧会露出相机底色。
        //      设计宽度 = 场景原始正交尺寸 × 2 × 16/9，每个场景各算一次。
        if (!_designWidth.ContainsKey(sceneName))
        {
            foreach (Camera cam in Object.FindObjectsOfType<Camera>(true))
            {
                if (!cam.orthographic) continue;
                _designWidth[sceneName] = 2f * cam.orthographicSize * (16f / 9f);
                break;
            }
        }
        if (_designWidth.TryGetValue(sceneName, out float designWidth))
        {
            foreach (Camera cam in Object.FindObjectsOfType<Camera>(true))
            {
                if (!cam.orthographic) continue;
                cam.orthographicSize = designWidth / (2f * cam.aspect);
            }
        }

        // ── ④ 相机清屏色改成天空蓝：背景图之间若差 1px，露出来的是天蓝色而不是刺眼的白线
        foreach (Camera cam in Object.FindObjectsOfType<Camera>(true))
        {
            if (cam.clearFlags != CameraClearFlags.SolidColor) continue;
            cam.backgroundColor = new Color(0.69f, 0.86f, 0.95f);
        }

        // ── ③ 全屏化的 UI：
        //      CenterBg 原本是 665×1999 的竖条，铺满全屏才像遮罩；
        //      PausePanel 原本是「整屏锚点 + 2486×1500 的 sizeDelta」，
        //      实际矩形比画布还宽 1243，里面的左侧暂停菜单会被顶到屏幕外。
        foreach (RectTransform rt in Object.FindObjectsOfType<RectTransform>(true))
        {
            if (rt.name != "CenterBg" && rt.name != "PausePanel") continue;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        // ── ⑤ 启动图（蓝色 V2 / 正在加载）不要显示。
        //      按贴图名判断（精灵名是子图名，不含文件名），并把屏幕上所有 UI 图片打出来方便核对。
        foreach (Image img in Object.FindObjectsOfType<Image>(true))
        {
            Sprite sp = img.sprite;
            string tex = sp != null && sp.texture != null ? sp.texture.name : "null";
            Debug.Log($"[UI] Image {img.name} sprite={(sp != null ? sp.name : "null")} texture={tex} active={img.gameObject.activeInHierarchy}");
            if (tex.IndexOf("SPLASH", System.StringComparison.OrdinalIgnoreCase) >= 0)
                img.gameObject.SetActive(false);
        }
        foreach (RawImage raw in Object.FindObjectsOfType<RawImage>(true))
        {
            string tex = raw.texture != null ? raw.texture.name : "null";
            Debug.Log($"[UI] RawImage {raw.name} texture={tex} active={raw.gameObject.activeInHierarchy}");
            if (tex.IndexOf("SPLASH", System.StringComparison.OrdinalIgnoreCase) >= 0)
                raw.gameObject.SetActive(false);
        }
        foreach (SpriteRenderer sp in Object.FindObjectsOfType<SpriteRenderer>(true))
        {
            string tex = sp.sprite != null && sp.sprite.texture != null ? sp.sprite.texture.name : "null";
            Debug.Log($"[UI] SpriteRenderer {sp.name} sprite={(sp.sprite != null ? sp.sprite.name : "null")} texture={tex} active={sp.gameObject.activeInHierarchy}");
        }
    }
}
