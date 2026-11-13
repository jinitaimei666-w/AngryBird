using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class AimLine : MonoBehaviour
{
    private Bird bird;

    [Header("Parameters")]
    public int dotCount = 12;        // 圆点数量
    public float timeStep = 0.08f;   // 每点时间间隔（决定点间距）
    public float dotRadius = 0.4f;  // 每个圆点的显示半径
    public Color dotColor = new Color(0.5f, 0.5f, 0.5f, 0.8f); // 灰色略透明

    // 用一个坐标系装这些圆点（便于整体清理）
    private Transform dotRoot;
    private Sprite circleSprite;

    private void Awake()
    {
        bird = GetComponent<Bird>();
        circleSprite = CreateCircleSprite();

        // 存放所有圆点的父物体
        dotRoot = new GameObject("AimDots").transform;
        for (int i = 0; i < dotCount; i++)
        {
            GameObject dot = new GameObject("Dot" + i);
            dot.transform.SetParent(dotRoot);
            var sr = dot.AddComponent<SpriteRenderer>();
            sr.sprite = circleSprite;
            sr.color = dotColor;
            sr.sortingOrder = 10;      // 保证在精灵上层可见
        }
        dotRoot.gameObject.SetActive(false);
    }

    private void Update()
    {
        if (bird.state == BirdState.BeforeShoot && bird.isMouseDown)
        {
            dotRoot.gameObject.SetActive(true);
            Draw();
        }
        else
        {
            dotRoot.gameObject.SetActive(false);
        }
    }

    private void Draw()
    {
        // 发射方向 = 弹弓中心 → 鸟当前位置
        Vector2 launchDir = (SlingShot.Instance.getCenterPosition() - transform.position).normalized;
        float speed = bird.flySpeed;
        Vector2 g = Physics2D.gravity;
        Vector2 start = transform.position;

        for (int i = 0; i < dotCount; i++)
        {
            float t = i * timeStep;
            Vector2 pos = start + launchDir * speed * t + 0.5f * g * (t * t);
            dotRoot.GetChild(i).localScale = Vector3.one * (dotRadius * 2f);
            dotRoot.GetChild(i).position = pos;
        }
    }

    // 代码生成一个圆形贴图，避免依赖外部素材
    private Sprite CreateCircleSprite()
    {
        int size = 32;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        float center = (size - 1f) / 2f;
        float radius = center;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dist = Mathf.Sqrt((x - center) * (x - center) + (y - center) * (y - center));
                Color col = dist <= radius ? Color.white : Color.clear;
                // 边缘简单抗锯齿
                if (dist > radius - 1.5f) col.a = Mathf.Clamp01(radius - dist);
                tex.SetPixel(x, y, col);
            }
        }
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
    }
}