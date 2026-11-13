using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 热更提示层：运行时用代码搭一个全屏遮罩 + 文字，不依赖任何场景或预制体。
/// 字体用项目自带的 SIMLI SDF（已移到 Resources/ziti），保证手机上有中文字形。
/// </summary>
public class UpdateNotice : MonoBehaviour
{
    static UpdateNotice _inst;

    GameObject _root;
    TMP_Text _message;

    /// <summary>多久没有新日志就自动收起：网络卡死/异常中断时不能把玩家永远挡在遮罩下</summary>
    const float StallSeconds = 8f;
    float _lastSet;

    /// <summary>显示提示层并更新文案</summary>
    public static void Show(string message)
    {
        if (_inst == null)
        {
            GameObject go = new GameObject("UpdateNotice");
            _inst = go.AddComponent<UpdateNotice>();   // Awake 里建好 UI
        }
        _inst._root.SetActive(true);
        Set(message);
    }

    /// <summary>只改文案</summary>
    public static void Set(string message)
    {
        if (_inst == null || _inst._message == null) return;
        _inst._message.text = message;
        _inst._lastSet = Time.unscaledTime;
    }

    public static void Hide()
    {
        if (_inst == null || _inst._root == null) return;
        _inst._root.SetActive(false);
    }

    void Update()
    {
        if (_root == null || !_root.activeSelf) return;
        if (Time.unscaledTime - _lastSet < StallSeconds) return;
        Hide();   // 更新流程卡住或异常退出时，遮罩不能一直留在屏幕上
    }

    void Awake()
    {
        DontDestroyOnLoad(gameObject);   // 更新可能在启动场景触发，穿过场景切换也不能断

        Canvas canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 32000;

        CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 1f;

        _root = new GameObject("Root", typeof(RectTransform));
        _root.transform.SetParent(transform, false);
        RectTransform rootRt = _root.GetComponent<RectTransform>();
        rootRt.anchorMin = Vector2.zero;
        rootRt.anchorMax = Vector2.one;
        rootRt.offsetMin = Vector2.zero;
        rootRt.offsetMax = Vector2.zero;

        // 暗色遮罩
        GameObject dim = new GameObject("Dim", typeof(RectTransform), typeof(Image));
        dim.transform.SetParent(_root.transform, false);
        RectTransform dimRt = dim.GetComponent<RectTransform>();
        dimRt.anchorMin = Vector2.zero;
        dimRt.anchorMax = Vector2.one;
        dimRt.offsetMin = Vector2.zero;
        dimRt.offsetMax = Vector2.zero;
        Image dimImage = dim.GetComponent<Image>();
        dimImage.color = new Color(0f, 0f, 0f, 0.78f);
        dimImage.raycastTarget = false;   // 只做视觉遮挡，别把底下按钮的点按吞掉

        TMP_FontAsset font = Resources.Load<TMP_FontAsset>("ziti/SIMLI SDF");
        if (font == null) font = TMP_Settings.defaultFontAsset;

        TMP_Text title = MakeText("Title", font, 60, new Vector2(0f, 110f), new Color(1f, 1f, 1f, 0.95f));
        title.text = "正在更新游戏内容";

        _message = MakeText("Message", font, 36, new Vector2(0f, 0f), new Color(0.86f, 0.92f, 1f, 0.95f));
        _message.text = "正在检查更新…";

        TMP_Text hint = MakeText("Hint", font, 28, new Vector2(0f, -120f), new Color(1f, 1f, 1f, 0.6f));
        hint.text = "请保持网络连接，更新完成后自动进入";

        _root.SetActive(false);
    }

    TMP_Text MakeText(string name, TMP_FontAsset font, int size, Vector2 pos, Color color)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(_root.transform, false);

        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = new Vector2(1500f, 220f);

        TextMeshProUGUI text = go.AddComponent<TextMeshProUGUI>();
        if (font != null) text.font = font;
        text.fontSize = size;
        text.alignment = TextAlignmentOptions.Center;
        text.color = color;
        text.raycastTarget = false;
        return text;
    }
}
