using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public class PauseUI : MonoBehaviour
{
    private Animator anim;
    private RectTransform panel;        // PausePanel/Panel：左边那条竖着的菜单
    private RectTransform canvasRT;

    void Start()
    {
        anim = GetComponent<Animator>();
        // 暂停时 Time.timeScale = 0，Animator 默认会跟着缩放时间一起停住，
        // 弹窗动画根本不播，菜单会停在场景里存的老坐标上。
        if (anim != null) anim.updateMode = AnimatorUpdateMode.UnscaledTime;

        panel = transform.Find("PausePanel/Panel") as RectTransform;
        Canvas c = GetComponentInParent<Canvas>();
        canvasRT = c != null ? c.rootCanvas.transform as RectTransform : null;
    }

    /// <summary>
    /// 动画里存的 anchoredPosition 是旧分辨率下摆的，播完会落在屏幕中间偏左。
    /// 这里每帧（LateUpdate 晚于 Animator）把菜单按画布真实左边缘重新摆一次：
    /// 菜单左沿贴屏幕左边，只留 8 个单位的缝。
    /// </summary>
    void LateUpdate()
    {
        if (panel == null || canvasRT == null) return;
        if (!panel.gameObject.activeInHierarchy) return;

        Vector3 screenLeft = canvasRT.TransformPoint(new Vector3(canvasRT.rect.xMin, 0f, 0f));
        float localLeft = panel.parent.InverseTransformPoint(screenLeft).x;
        float x = localLeft + panel.rect.width * 0.5f + 8f;

        Vector3 p = panel.localPosition;
        if (Mathf.Abs(p.x - x) < 0.5f) return;          // 已经在位上就别天天写
        panel.localPosition = new Vector3(x, p.y, p.z);
    }

    // 场景切换完成时自动清理，防止暂停界面残留到选关界面
    void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name != "3-GameScene")
        {
            Time.timeScale = 1f;              // 恢复时间，防止选关界面被冻结
            if (anim != null) anim.SetBool("isShow", false);   // 播隐藏动画关闭暂停界面
        }
    }

    public void OnPauseButtonClick()
    {
        Time.timeScale = 0;
        anim.SetBool("isShow", true);
    }

    public void OnContinueButtonClick()
    {
        Time.timeScale = 1;
        anim.SetBool("isShow", false);
    }

    // 核心修复：恢复时间 + 先关闭暂停界面 + 再切场景
    public void OnRestartButtonClick()
    {
        Time.timeScale = 1;              // 暂停时是0，不恢复新场景会被冻结
        anim.SetBool("isShow", false);   // 先播关闭动画，暂停界面收起来
        GameManager.Instance.RestartLevel();
    }

    public void OnLevelListButtonClick()
    {
        Time.timeScale = 1;
        anim.SetBool("isShow", false);
        GameManager.Instance.LevelList();
    }
}