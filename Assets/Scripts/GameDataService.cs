using System.Collections;
using UnityEngine;

/// <summary>
/// 关卡表（GameSO）的统一入口：从 bundle "data" 读取，读不到才退回场景里引用的那份。
/// 缓存静态实例，保证选关场景写进去的 selectMapID / selectedLevelID 能被战斗场景读到。
///
/// 同时负责关卡进度的落盘：SO 只是资源，内存里改星数不会写回磁盘，
/// 所以每次通关后把星数存进 PlayerPrefs，下次启动读回来覆盖。
/// </summary>
public static class GameDataService
{
    public const string BundleName = "data";
    public const string AssetName = "GameData";
    const string ProgressKey = "AngryBird.Progress";

    static GameSO _data;
    public static GameSO Data { get { return _data; } }

    /// <param name="fallback">bundle 缺失时使用的 GameSO（场景里挂的那份）</param>
    /// <param name="log">热更日志回调，用来把进度显示到界面上</param>
    public static IEnumerator Load(GameSO fallback, System.Action<string> log = null)
    {
        if (_data != null) yield break;

        HotUpdateManager hu = HotUpdateManager.Instance;
        if (hu == null) hu = new GameObject("HotUpdateManager").AddComponent<HotUpdateManager>();
        yield return hu.EnsureUpdated(msg =>
        {
            Debug.Log(msg);
            if (log != null) log(msg);
        });

        AssetBundle ab = hu.LoadBundle(BundleName);
        if (ab != null)
        {
            GameSO so = ab.LoadAsset<GameSO>(AssetName);
            if (so != null)
            {
                _data = so;
                Debug.Log("[GameData] 从 bundle data 加载关卡表，共 " + so.mapArray.Length + " 张地图");
                ApplyProgress();
                yield break;
            }
        }

        _data = fallback;
        if (_data != null)
        {
            Debug.LogWarning("[GameData] bundle data 缺失，沿用场景里引用的 GameSO");
            ApplyProgress();
        }
        else
        {
            Debug.LogError("[GameData] 关卡表加载失败：bundle 和场景引用都为空");
        }
    }

    // ================= 进度存档 =================

    [System.Serializable]
    class MapProgress
    {
        public int[] levels;
        public int mapStar;
    }

    [System.Serializable]
    class Progress
    {
        public MapProgress[] maps;
    }

    /// <summary>把存档里的星数覆盖到关卡表上（关卡新增时自动只合并到较短的长度）</summary>
    public static void ApplyProgress()
    {
        if (_data == null || _data.mapArray == null) return;

        string json = PlayerPrefs.GetString(ProgressKey, string.Empty);
        if (string.IsNullOrEmpty(json)) return;

        Progress p = null;
        try { p = JsonUtility.FromJson<Progress>(json); }
        catch { return; }
        if (p == null || p.maps == null) return;

        for (int i = 0; i < _data.mapArray.Length && i < p.maps.Length; i++)
        {
            MapSO map = _data.mapArray[i];
            MapProgress mp = p.maps[i];
            if (map == null || mp == null) continue;

            // 关卡表（随资源热更）定义解锁，存档只负责记录玩家打出来的进度，
            // 两者取并集：存档不能把新版本里已解锁的关卡再锁回去。
            if (mp.mapStar > map.starNumberOfMap) map.starNumberOfMap = mp.mapStar;
            if (mp.levels == null || map.starNumberOfLevel == null) continue;

            int n = Mathf.Min(map.starNumberOfLevel.Length, mp.levels.Length);
            for (int j = 0; j < n; j++)
            {
                if (mp.levels[j] > map.starNumberOfLevel[j])
                    map.starNumberOfLevel[j] = mp.levels[j];
            }
        }
        Debug.Log("[GameData] 已恢复关卡进度");
    }

    /// <summary>通关后调用：把当前星数写进 PlayerPrefs</summary>
    public static void SaveProgress()
    {
        if (_data == null || _data.mapArray == null) return;

        Progress p = new Progress { maps = new MapProgress[_data.mapArray.Length] };
        for (int i = 0; i < _data.mapArray.Length; i++)
        {
            MapSO map = _data.mapArray[i];
            if (map == null) continue;
            p.maps[i] = new MapProgress
            {
                levels = map.starNumberOfLevel,
                mapStar = map.starNumberOfMap
            };
        }

        PlayerPrefs.SetString(ProgressKey, JsonUtility.ToJson(p));
        PlayerPrefs.Save();
        Debug.Log("[GameData] 关卡进度已保存");
    }
}
