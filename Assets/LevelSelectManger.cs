using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public class LevelSelectManger : MonoBehaviour
{
    public static LevelSelectManger Instance { get; private set; }
    public GameSO gameSO;
    public MapLevelUI mapLevelUI;
    private void Awake()
    {
        Instance = this;
    }
    // Start is called before the first frame update
    void Start()
    {
        StartCoroutine(Init());
    }

    // 关卡表在 bundle 里，要先跑完热更、再从 bundle 读
    private IEnumerator Init()
    {
        // 热更的提示层由 HotUpdateManager 统一弹，这里只管读表
        yield return GameDataService.Load(gameSO);

        gameSO = GameDataService.Data;
        if (gameSO == null) yield break;
        mapLevelUI.ShowMapList(gameSO.mapArray);
    }

    public void SetSelectedMap(int mapID)
    {
        gameSO.selectMapID = mapID;
    }
    public void SetSelectedLevel(int levelID)
    {
        gameSO.selectedLevelID = levelID;
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex+1);
    }
    public int[] GetSelectedMap()
    {
        return gameSO.mapArray[gameSO.selectMapID - 1].starNumberOfLevel;
    }
}
