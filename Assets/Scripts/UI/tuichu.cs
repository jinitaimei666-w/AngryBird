using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class QuitButton : MonoBehaviour
{
    public void OnQuitClick()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;  // 编辑器里点测试用
#else
        Application.Quit();                              // 打包后真正退出游戏
#endif
    }
}