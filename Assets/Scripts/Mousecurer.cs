using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using System.Collections.Generic;

public class MouseCursor : MonoBehaviour
{
    public Texture2D openTex;            // 松开爪原图
    public Texture2D gripTex;            // 握紧爪原图
    public string gameSceneName = "3-GameScene";
    public Vector2 cursorSize = new Vector2(100f, 100f);

    private Image cursorImage;
    private Sprite openSprite, gripSprite;

    void Start()
    {
        DontDestroyOnLoad(gameObject);   // 常驻：切场景脚本不销毁，光标状态能一直控制

        CreateCursorImage();             // 代码自动建爪子 UI，不用手动搭

        openSprite = MakeSprite(openTex);
        gripSprite = MakeSprite(gripTex);
        cursorImage.sprite = openSprite;
    }

    void Update()
    {
        bool inLevel = SceneManager.GetActiveScene().name == gameSceneName;

        if (!inLevel)
        {
            Cursor.visible = true;       // 关卡外：显示系统光标
            cursorImage.enabled = false;
            return;
        }

        Cursor.visible = false;          // 关卡内：隐藏系统光标
        cursorImage.enabled = true;
        cursorImage.sprite = Input.GetMouseButton(0) ? gripSprite : openSprite;
        cursorImage.rectTransform.position = Input.mousePosition;
    }

    private void CreateCursorImage()
    {
        GameObject canvasGO = new GameObject("CursorCanvas");
        canvasGO.transform.SetParent(transform);
        Canvas canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 999;                       // 最顶层，不被其他 UI 盖住
        canvasGO.AddComponent<CanvasScaler>();

        GameObject imgGO = new GameObject("CursorImage");
        imgGO.transform.SetParent(canvasGO.transform, false);
        cursorImage = imgGO.AddComponent<Image>();
        cursorImage.raycastTarget = false;               // 不挡按钮点击
        cursorImage.rectTransform.sizeDelta = cursorSize;
    }

    // 先缩到 128 再抠底 + 批量读像素 → 不再卡顿
    private Sprite MakeSprite(Texture2D src)
    {
        if (src == null) return null;
        Texture2D small = ResizeTexture(src, 128, 128);
        RemoveBackground(small);
        return Sprite.Create(small, new Rect(0, 0, small.width, small.height), new Vector2(0.5f, 0.5f));
    }

    private Texture2D ResizeTexture(Texture2D src, int w, int h)
    {
        RenderTexture rt = RenderTexture.GetTemporary(w, h);
        Graphics.Blit(src, rt);
        RenderTexture.active = rt;
        Texture2D result = new Texture2D(w, h, TextureFormat.RGBA32, false);
        result.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        result.Apply();
        RenderTexture.active = null;
        RenderTexture.ReleaseTemporary(rt);
        return result;
    }

    // flood fill 抠底（GetPixels 批量读取，速度快）
    private void RemoveBackground(Texture2D tex)
    {
        int w = tex.width, h = tex.height;
        Color[] pixels = tex.GetPixels();
        Color bg = pixels[0];
        bool[] visited = new bool[w * h];
        Queue<int> queue = new Queue<int>();
        queue.Enqueue(0); queue.Enqueue(w - 1); queue.Enqueue(w * (h - 1)); queue.Enqueue(w * h - 1);
        List<int> toClear = new List<int>();

        while (queue.Count > 0)
        {
            int idx = queue.Dequeue();
            if (visited[idx]) continue;
            visited[idx] = true;
            Color c = pixels[idx];
            if (Mathf.Abs(c.r - bg.r) < 0.1f && Mathf.Abs(c.g - bg.g) < 0.1f && Mathf.Abs(c.b - bg.b) < 0.1f)
            {
                toClear.Add(idx);
                if (idx % w > 0) queue.Enqueue(idx - 1);
                if (idx % w < w - 1) queue.Enqueue(idx + 1);
                if (idx >= w) queue.Enqueue(idx - w);
                if (idx < w * h - w) queue.Enqueue(idx + w);
            }
        }
        if (toClear.Count < w * h * 0.9f)
        {
            foreach (int idx in toClear)
            {
                Color c = pixels[idx];
                c.a = 0f;
                pixels[idx] = c;
            }
        }
        tex.SetPixels(pixels);
        tex.Apply();
    }
}