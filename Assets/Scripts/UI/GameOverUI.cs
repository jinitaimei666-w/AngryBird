using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class GameOverUI : MonoBehaviour
{
    private Animator anim;
    private int starCount = 0;
    public GameObject failPig;
    public StarUI starUI1;
    public StarUI starUI2;
    public StarUI starUI3;
    public GameObject nextButton;

    private void Awake()
    {
        anim = GetComponent<Animator>();
        if (nextButton != null)
        {
            nextButton.SetActive(false);
        }
    }

    public void Show(int sartCount)
    {
        gameObject.SetActive(true);          // 确保结算界面先显示出来
        anim.SetTrigger("isShow");
        this.starCount = sartCount;
        if (nextButton != null)
        {
            nextButton.SetActive(starCount >= 1);
        }
    }

    public void ShowStar()
    {
        if (starCount == 0)
        {
            failPig.SetActive(true);
        }
        if (starCount >= 1)
        {
            starUI1.Show();
        }
        if (starCount >= 2)
        {
            starUI2.Show();
        }
        if (starCount >= 3)
        {
            starUI3.Show();
        }
    }

    // ===== 核心修复：点按钮 → 先强制隐藏结算界面 → 再切场景 =====
    public void OnRestartButtonClick()
    {
        gameObject.SetActive(false);
        GameManager.Instance.RestartLevel();
    }

    public void OnLevelListButtonClick()
    {
        gameObject.SetActive(false);
        GameManager.Instance.LevelList();
    }

    public void OnNextButtonClick()
    {
        gameObject.SetActive(false);
        GameManager.Instance.NextLevel();
    }
}