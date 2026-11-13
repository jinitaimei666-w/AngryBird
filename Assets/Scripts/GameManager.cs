using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }
    public GameSO gameSO;

    public Bird[] birdList;
    private int index = -1;
    private int pigTotalCount;
    private int pigDeadCount;
    private FollowTarget cameraFollowTarget;
    public GameOverUI gameOverUI;

    private void Awake()
    {
        Instance = this;
        pigDeadCount = 0;
    }

    // 关卡现在来自热更 bundle，可能是异步的（要先跑更新流程），所以整套初始化放进协程
    IEnumerator Start()
    {
        yield return LoadSelectedLevel();

        birdList = FindObjectsByType<Bird>(FindObjectsSortMode.None);
        pigTotalCount = (FindObjectsByType<Pig>(FindObjectsSortMode.None)).Length;
        cameraFollowTarget = Camera.main.GetComponent<FollowTarget>();

        if (pigTotalCount == 0)
            Debug.LogError("[GameManager] 当前关卡里一只 Pig 都没有：检查关卡 prefab 的内容，否则打完鸟会直接判负");

        if (birdList.Length == 0)
            Debug.LogError("[GameManager] 场景里没有 Bird：检查 SlingShot / 小鸟是否被放在了关卡 prefab 里而非场景里");

        LoadNextBird();
    }

    private IEnumerator LoadSelectedLevel()
    {
        Time.timeScale = 1;

        // 从 1-StartScene 进来时已经跑过更新，这里会立即返回
        HotUpdateManager hu = HotUpdateManager.Instance;
        if (hu == null) hu = new GameObject("HotUpdateManager").AddComponent<HotUpdateManager>();
        yield return hu.EnsureUpdated(Debug.Log);

        // 关卡表也走 bundle：热更下发的 GameData 优先，场景里引用的那份只做兜底
        yield return GameDataService.Load(gameSO);
        if (GameDataService.Data != null) gameSO = GameDataService.Data;
        if (gameSO == null)
        {
            Debug.LogError("[GameManager] 关卡表为空，无法加载关卡");
            yield break;
        }

        // 直接从 3-GameScene Play 时 SO 里还是 -1，兜底到 Map1/Level1
        if (gameSO.selectMapID < 1) gameSO.selectMapID = 1;
        if (gameSO.selectedLevelID < 1) gameSO.selectedLevelID = 1;

        GameObject levelPrefab = LoadLevel(gameSO.selectMapID, gameSO.selectedLevelID);
        if (levelPrefab == null)
        {
            Debug.LogError($"[GameManager] 找不到关卡 Map{gameSO.selectMapID}/Level{gameSO.selectedLevelID}");
            yield break;
        }

        Instantiate(levelPrefab);
        ResetShot();
    }

    // 鸟还没出手时，障碍物之间的挤压/落地不结算伤害（否则关卡一进场就互相撞碎）。
    // 结构本身靠摆放保持稳定，不做冻结——鸟一发射就正常恢复物理与伤害。
    public static bool ShotStarted { get; private set; }

    static void ResetShot() { ShotStarted = false; }

    public static void ReleaseLevel() { ShotStarted = true; }

    // 热更 bundle 优先，首包 Resources 兜底（迁移期两处都在）
    private GameObject LoadLevel(int mapID, int levelID)
    {
        string assetName = "Level" + levelID;

        HotUpdateManager hu = HotUpdateManager.Instance;
        if (hu != null)
        {
            AssetBundle ab = hu.LoadBundle("map" + mapID);
            if (ab != null)
            {
                GameObject go = ab.LoadAsset<GameObject>(assetName);
                if (go != null)
                {
                    Debug.Log($"[GameManager] 从 bundle map{mapID} 加载 {assetName}");
                    return go;
                }
            }
        }

        GameObject fallback = Resources.Load<GameObject>($"Map{mapID}/{assetName}");
        if (fallback != null) Debug.Log($"[GameManager] 从 Resources 加载 Map{mapID}/{assetName}");
        return fallback;
    }
    public void LoadNextBird()
    {
        index++;
        if (index >= birdList.Length)
        {
            GameOver();
        }
        else
        {
            birdList[index].GoStage(SlingShot.Instance.getCenterPosition());
            cameraFollowTarget.SetTarget(birdList[index].transform);
        }
    }

    public void OnPigDead()
    {
        pigDeadCount++;
        if (pigDeadCount >= pigTotalCount)
        {
            GameOver();
        }
    }

    private void GameOver()
    {
        int starCount = 0;
        float pigDeadPercent = pigTotalCount > 0 ? pigDeadCount * 1f / pigTotalCount : 0f;

        if (pigDeadPercent > 0.99f)
        {
            starCount = 3;
        }
        else if (pigDeadPercent > 0.66f)
        {
            starCount = 2;
        }
        else if (pigDeadPercent > 0.33f)
        {
            starCount = 1;
        }
        gameOverUI.Show(starCount);
        gameSO.UpdateLevel(starCount);
        GameDataService.SaveProgress();   // SO 只是内存对象，必须落盘，否则退出游戏进度清零
    }

    public void RestartLevel()
    {
        Time.timeScale = 1;                          // 恢复时间，防止场景切换后界面被冻结
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    public void LevelList()
    {
        Time.timeScale = 1;
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex - 1);
    }

    // ================= 新增：进入下一关 =================
    public void NextLevel()
    {
        Time.timeScale = 1;

        if (gameSO.selectMapID < 1) gameSO.selectMapID = 1;
        if (gameSO.selectedLevelID < 1) gameSO.selectedLevelID = 1;

        int nextID = gameSO.selectedLevelID + 1;
        if (LoadLevel(gameSO.selectMapID, nextID) == null)
        {
            Debug.LogWarning($"[GameManager] Map{gameSO.selectMapID}/Level{nextID} 不存在，返回关卡列表");
            LevelList();
            return;
        }

        gameSO.selectedLevelID = nextID;
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }
}