using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 按 Level1 的搭建约定批量生成关卡（猪 + 木/冰/石结构），难度逐关递增。
/// 结构改为紧凑的官方样式：立柱 1.6 高、梁 2.017 长，层高 1.968，
/// 柱底压在 BgDown 顶面 y=-11.14，首层横梁 y=-9.36，屋内猪 y=-10.63；
/// 桌子（Desktop）会被删掉，房子只摆在弹弓右侧的位置。
/// Map1 的 2/4/5/6/7 用手写配方，其余（含 Map2）按等级自动生成。
/// </summary>
public static class LevelContentBuilder
{
    const string Map1Dir = "Assets/Resources/Map1";
    const string Map2Dir = "Assets/Resources/Map2";
    const string Map1Bundle = "map1";
    const string Map2Bundle = "map2";

    const string Wood = "Assets/Prefabs/destructible/BigWood1.prefab";
    const string Ice = "Assets/Prefabs/destructible/Bigice1.prefab";
    const string Stone = "Assets/Prefabs/destructible/longshitou1.prefab";
    const string PigSmall = "Assets/Prefabs/destructible/Pig1.prefab";
    const string PigMid = "Assets/Prefabs/destructible/Pig2_1.prefab";
    const string PigBig = "Assets/Prefabs/destructible/Pig3_1.prefab";

    // ---------- 几何（关卡根节点坐标，数值取自 Level1 的实测碰撞体） ----------
    // 地面：BgDown 的 BoxCollider2D，中心 -13.5087、半高 2.3674 → 顶面 -11.1413
    const float GroundTop = -11.14f;
    const float PostLen = 1.6f;               // 立柱高度（约 1.55 只猪，官方那种矮墩墩的格子）
    const float PlankTh = 0.368f;             // 木料厚度（0.187 × 1.97）
    const float PostX = 0.78f;                // 立柱半间距，中间刚好塞下一只猪
    const float PigHalf = 0.515f;             // Pig1 半径（0.78 × 1.3219 / 2）

    const float PillarY = GroundTop + PostLen * 0.5f;                 // -10.34 柱底贴地
    const float BeamY = GroundTop + PostLen + PlankTh * 0.5f;         // -9.356 梁压在柱顶
    const float PigY = GroundTop + PigHalf;                           // -10.625
    const float PigOnBeamOffset = PlankTh * 0.5f + PigHalf;           // 0.699
    const float FloorPitch = PostLen + PlankTh;                       // 1.968 上下两层梁的间距
    const float UpPillarOffset = PostLen * 0.5f + PlankTh * 0.5f;     // 0.984 上层柱心相对本层梁心
    const float PillarOffset = PostX;
    const float StackedBeamY = BeamY + PlankTh;                       // 叠在屋梁上，不会悬空掉落

    // 默认值只是兜底，真正的位置在 Generate 里按模板的 Desktop（桌子）自动取
    static float[] Houses = { -17.17f, -11.5f, -5.81f };

    [MenuItem("HotUpdate/⑥ 生成全部关卡")]
    public static void BuildAll()
    {
        _theme2 = null;
        int n1 = GenerateMap(Map1Dir, Map1Bundle, true, false);
        int n2 = GenerateMap(Map2Dir, Map2Bundle, false, true);

        UnlockAll(Map1Dir);
        UnlockAll(Map2Dir);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[LevelContent] 全部关卡生成完毕：Map1 {n1} 关，Map2 {n2} 关");
    }

    /// <summary>只换背景、不动关卡内容：把 Map2 全部关卡的天空/大地/草换成主题2</summary>
    [MenuItem("HotUpdate/⑦ Map2 换主题2背景")]
    public static void Theme2ForMap2()
    {
        _theme2 = null;
        MapSO map = AssetDatabase.LoadAssetAtPath<MapSO>("Assets/SO/Map2.asset");
        int slots = map != null ? map.starNumberOfLevel.Length : 32;

        int done = 0;
        for (int lv = 1; lv <= slots; lv++)
        {
            string path = $"{Map2Dir}/Level{lv}.prefab";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null) continue;

            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                if (ApplyTheme2(root.transform) > 0)
                {
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                    done++;
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            AssetImporter importer = AssetImporter.GetAtPath(path);
            if (importer != null && importer.assetBundleName != Map2Bundle)
            {
                importer.assetBundleName = Map2Bundle;
                importer.SaveAndReimport();
            }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[LevelContent] Map2 背景已替换为主题2：{done} 关");
    }

    // ================= 按地图批量生成 =================

    /// <summary>生成 MapX 下 Level2 起的全部关卡，返回该地图的关卡槽位数</summary>
    static int GenerateMap(string dir, string bundle, bool handMade, bool theme2)
    {
        string name = Path.GetFileName(dir);                       // Map1 / Map2
        string template = dir + "/Level1.prefab";

        MapSO map = AssetDatabase.LoadAssetAtPath<MapSO>("Assets/SO/" + name + ".asset");
        int slots = map != null ? map.starNumberOfLevel.Length : 8;

        for (int lv = 2; lv <= slots; lv++)
        {
            if (handMade && lv == 3) continue;                     // 手摆的原有关卡，保持不动

            System.Action<Transform> recipe = handMade ? HandMade(lv) : null;
            if (recipe == null)
            {
                int level = lv;                                     // 闭包捕获副本
                recipe = t => Auto(t, level);
            }

            Generate(template, dir, bundle, lv, recipe, AutoBirds(lv), theme2);
        }
        return slots;
    }

    static System.Action<Transform> HandMade(int lv)
    {
        switch (lv)
        {
            case 2: return Lv2;
            case 4: return Lv4;
            case 5: return Lv5;
            case 6: return Lv6;
            case 7: return Lv7;
            default: return null;
        }
    }

    // ================= 手写配方（Map1 2/4/5/6/7） =================

    static void Lv2(Transform p)
    {
        House(p, Houses[0], Wood, PigSmall);
        House(p, Houses[1], Wood, PigSmall);
        House(p, Houses[2], Wood, PigSmall);
        Beam(p, Houses[1], StackedBeamY, Wood);
    }

    static void Lv4(Transform p)
    {
        House(p, Houses[0], Wood, PigSmall);
        House(p, Houses[1], Wood, PigSmall);
        UpperFloor(p, Houses[1], Wood, PigMid);
        House(p, Houses[2], Wood, PigSmall);
    }

    static void Lv5(Transform p)
    {
        House(p, Houses[0], Wood, PigSmall);
        House(p, Houses[1], Ice, PigSmall);
        UpperFloor(p, Houses[1], Ice, PigMid);
        House(p, Houses[2], Wood, PigSmall);
        UpperFloor(p, Houses[2], Ice, PigSmall);
    }

    static void Lv6(Transform p)
    {
        House(p, Houses[0], Wood, PigSmall);
        House(p, Houses[1], Stone, PigSmall);
        UpperFloor(p, Houses[1], Wood, PigMid);
        House(p, Houses[2], Ice, PigSmall);
        UpperFloor(p, Houses[2], Stone, PigBig);
    }

    static void Lv7(Transform p)
    {
        House(p, Houses[0], Stone, PigSmall);
        Beam(p, Houses[0], StackedBeamY, Stone);
        Beam(p, Houses[0], StackedBeamY + 0.4f, Stone);
        House(p, Houses[1], Stone, PigMid);
        UpperFloor(p, Houses[1], Stone, PigBig);
        House(p, Houses[2], Ice, PigSmall);
        UpperFloor(p, Houses[2], Wood, PigMid);
    }

    // ================= 自动配方（8 关往后） =================
    //
    // 参照官方关卡的搭法：一关只用一到两种材质（木 → 冰 → 石），
    // 房子是「两根立柱 + 顶梁」的方框，猪关在屋里，梁上再叠楼层，屋顶上还可以站猪。
    // 每 5 关一个难度档，档内 5 关用 5 种不同的建筑组合，避免每关长得一样。

    static void Auto(Transform p, int lv)
    {
        int tier = Mathf.Clamp((lv - 8) / 5, 0, 4);
        int idx = Mathf.Clamp((lv - 8) % 5, 0, 4);

        string main = tier <= 0 ? Wood : (tier == 1 ? Ice : Stone);
        string sub = tier <= 1 ? Wood : (tier == 2 ? Ice : Stone);
        // 屋里的猪别用大号：柱间净宽只有 1.19（2×0.78-0.368），大猪会把立柱挤开
        string pigS = tier <= 1 ? PigSmall : PigMid;

        // 越往后房子越多：3 → 4 → 5 栋
        int n = 3 + (tier >= 1 ? 1 : 0) + (tier >= 3 ? 1 : 0);

        // 先把结构全部搭好，记下所有能站猪的位置，最后统一分配
        List<Vector3> slots = new List<Vector3>();

        for (int h = 0; h < n; h++)
        {
            float cx = Houses[h];
            string mat = h % 2 == 1 ? sub : main;
            int floors = FloorsFor(idx, h, n);

            Pillar(p, cx - PillarOffset, mat);
            Pillar(p, cx + PillarOffset, mat);
            Beam(p, cx, BeamY, mat);
            slots.Add(new Vector3(cx, PigY, 0f));                       // 屋里

            for (int f = 0; f < floors; f++)
            {
                Floor(p, cx, f, f == 0 ? sub : mat);
                slots.Add(new Vector3(cx, BeamY + FloorPitch * f + PigOnBeamOffset, 0f));
            }
            slots.Add(new Vector3(cx, BeamY + FloorPitch * floors + PigOnBeamOffset, 0f));  // 屋顶
        }

        // 猪的数量单独控制：房子多不等于猪多，最多 5 只，从低处开始占位
        slots.Sort((x, y) => x.y.CompareTo(y.y));
        int want = Mathf.Min(4 + tier, 5);
        for (int i = 0; i < want; i++)
            Spawn(p, pigS, slots[i], PigScale(pigS), 0f);
    }

    /// <summary>同一档内 5 关用 5 种不同的楼层组合</summary>
    static int FloorsFor(int idx, int h, int n)
    {
        switch (idx)
        {
            case 0: return 0;                                   // 全是平房
            case 1: return h % 2;                               // 平房/两层交替
            case 2: return 1;                                   // 全两层
            case 3: return h == n / 2 ? 2 : 1;                  // 中间一栋三层
            default: return h % 2 == 0 ? 2 : 1;                 // 高低交错，最厚
        }
    }

    static int AutoBirds(int lv)
    {
        int tier = Mathf.Clamp((lv - 8) / 5, 0, 4);
        if (tier >= 3) return 2;          // 5 只
        if (tier >= 1) return 1;          // 4 只
        return 0;
    }

    // ================= 结构基本件 =================

    /// <summary>一栋标准房：左右两根竖梁 + 上方一根横梁 + 屋内一只猪</summary>
    static void House(Transform p, float cx, string mat, string pig)
    {
        Pillar(p, cx - PillarOffset, mat);
        Pillar(p, cx + PillarOffset, mat);
        Beam(p, cx, BeamY, mat);
        Spawn(p, pig, new Vector3(cx, PigY, 0f), PigScale(pig), 0f);
    }

    /// <summary>屋梁上加一层：两根竖梁 + 一根梁 + 一只猪（二层）</summary>
    static void UpperFloor(Transform p, float cx, string mat, string pig)
    {
        UpperFloorAt(p, cx, 0, mat, pig);
    }

    /// <summary>在第 f 层梁上加一层（f=0 为二层，f=1 为三层）</summary>
    static void UpperFloorAt(Transform p, float cx, int f, string mat, string pig)
    {
        Floor(p, cx, f, mat);
        Spawn(p, pig, new Vector3(cx, BeamY + FloorPitch * f + PigOnBeamOffset, 0f), PigScale(pig), 0f);
    }

    /// <summary>第 f 层结构（f=0 为二层），只出梁柱不放猪</summary>
    static void Floor(Transform p, float cx, int f, string mat)
    {
        float beamY = BeamY + FloorPitch * f;
        Spawn(p, mat, new Vector3(cx - PillarOffset, beamY + UpPillarOffset, 0f), PostScale, -90f);
        Spawn(p, mat, new Vector3(cx + PillarOffset, beamY + UpPillarOffset, 0f), PostScale, -90f);
        Spawn(p, mat, new Vector3(cx, beamY + FloorPitch, 0f), One, 0f);
    }

    // 横梁：2.017 长 × 0.368 厚；立柱：1.6 高 × 0.368 厚（转 -90° 后长的一边竖起来）
    static Vector3 One => new Vector3(1f, 1.97f, 1f);
    static Vector3 PostScale => new Vector3(0.793f, 1.97f, 1f);

    static void Pillar(Transform p, float x, string mat)
    {
        Spawn(p, mat, new Vector3(x, PillarY, 0f), PostScale, -90f);
    }

    static void Beam(Transform p, float cx, float y, string mat)
    {
        Spawn(p, mat, new Vector3(cx, y, 0f), One, 0f);
    }

    static Vector3 PigScale(string path)
    {
        if (path == PigSmall) return new Vector3(1.32f, 1.31f, 1f);
        if (path == PigMid) return new Vector3(1.15f, 1.12f, 1f);
        return new Vector3(1f, 1f, 1f);
    }

    static int _serial;

    /// <summary>实例化并挂到父节点，保留 prefab 自带的缩放（鸟用这个）</summary>
    static GameObject Load(Transform parent, string path, Vector3 pos, float rotZ)
    {
        GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (asset == null)
        {
            Debug.LogError("[LevelContent] 找不到素材：" + path);
            return null;
        }

        GameObject go = Object.Instantiate(asset);

        // 拆掉 prefab 关联，让关卡像原有关卡一样是「扁平」结构，
        // 避免 map1 包里再挂一层 prefab 依赖。
        GameObject instRoot = PrefabUtility.GetOutermostPrefabInstanceRoot(go);
        if (instRoot != null)
            PrefabUtility.UnpackPrefabInstance(instRoot, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);

        go.name = asset.name + " (" + _serial++ + ")";
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localRotation = Quaternion.Euler(0f, 0f, rotZ);
        return go;
    }

    static void Spawn(Transform parent, string path, Vector3 pos, Vector3 scale, float rotZ)
    {
        GameObject go = Load(parent, path, pos, rotZ);
        if (go != null) go.transform.localScale = scale;
    }

    // ================= 生成单个关卡 =================

    static readonly string[] Stripped =
    {
        // Desktop 是原关卡里的木桌，会把房子架空；新关卡房子直接落地，一并删掉
        "Desktop",
        "Pig", "BigWood1", "SmallWood1", "CubeWood1", "Bigice1", "fangice1",
        "longshitou1", "icesanjiao", "iceshixinsanjiao1", "fangshi1", "sanjiao1",
        "huziPig1", "baoxiang"
    };

    static readonly HashSet<string> blockNames = new HashSet<string>
    {
        "BigWood1", "SmallWood1", "CubeWood1", "Bigice1", "fangice1",
        "longshitou1", "icesanjiao", "iceshixinsanjiao1", "fangshi1", "sanjiao1"
    };

    static void Generate(string template, string dir, string bundle, int level,
                         System.Action<Transform> recipe, int extraBirds, bool theme2 = false)
    {
        _serial = 0;
        string path = $"{dir}/Level{level}.prefab";
        int pigs = 0, blocks = 0, birds = 0;

        GameObject root = PrefabUtility.LoadPrefabContents(template);
        try
        {
            // 原有关卡的木头/猪是挂在关卡根节点下的（Destructible 那个节点只装鸟，
            // 且带 +12.39/+0.33 的偏移）。以前把零件塞进 Destructible，整套房子就被平移出去了，
            // 落点对不上桌子、悬空掉下来摔成一堆，所以这里直接挂在根节点上。
            Transform parent = root.transform;

            // 读模板里的弹弓位置，房子统一摆到它右侧
            float slingX = -15.3f;
            foreach (Transform c in root.transform)
                if (c.name.StartsWith("Slingshot")) { slingX = c.localPosition.x; break; }

            // 结构一律摆在弹弓右侧，且留出一段飞行距离，不要贴着弹弓
            const float step = 5.69f;
            const float gap = 7f;
            Houses = new float[5];
            for (int i = 0; i < Houses.Length; i++)
                Houses[i] = slingX + gap + step * i;

            RemoveExisting(parent);
            recipe(parent);
            AddBirds(root.transform, level, extraBirds);
            if (theme2) ApplyTheme2(root.transform);

            root.name = "Level" + level;
            PrefabUtility.SaveAsPrefabAsset(root, path);

            foreach (Transform t in parent.GetComponentsInChildren<Transform>(true))
            {
                if (t.name.StartsWith("Pig")) pigs++;
                else if (blockNames.Contains(BaseName(t.name))) blocks++;
            }
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                if (t.GetComponent<Bird>() != null) birds++;
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        AssetImporter importer = AssetImporter.GetAtPath(path);
        if (importer != null && importer.assetBundleName != bundle)
        {
            importer.assetBundleName = bundle;
            importer.SaveAndReimport();
        }

        Debug.Log($"[LevelContent] {Path.GetFileName(dir)}/Level{level}：{pigs} 猪 / {blocks} 块 / {birds} 鸟");
    }

    static string BaseName(string name)
    {
        int i = name.IndexOf(" (");
        return i < 0 ? name : name.Substring(0, i);
    }

    static void RemoveExisting(Transform parent)
    {
        List<GameObject> kill = new List<GameObject>();
        foreach (Transform t in parent.GetComponentsInChildren<Transform>(true))
        {
            if (t == parent) continue;
            string bn = BaseName(t.name);
            foreach (string s in Stripped)
            {
                if (t.name.StartsWith(s) || bn == s) { kill.Add(t.gameObject); break; }
            }
        }
        foreach (GameObject go in kill)
        {
            if (go != null) Object.DestroyImmediate(go);   // 父物体被删后子物体会变成 null
        }
    }

    static Transform FindChild(Transform root, string name)
    {
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            if (t.name == name) return t;
        return null;
    }

    /// <summary>在待发射队列最左侧再复制 n 只鸟（关卡越难给越多弹药）</summary>
    // 每一关按关卡号轮换鸟的种类，不再清一色红鸟
    static readonly string[] BirdDeck =
    {
        "Assets/Prefabs/BigBird1.prefab",      // 红鸟
        "Assets/Prefabs/YellowBird.prefab",    // 黄鸟
        "Assets/Prefabs/BlackBird.prefab",     // 黑鸟
        "Assets/Prefabs/BigMouthBird.prefab",  // 大嘴鸟
        "Assets/Prefabs/LanBird.prefab",       // 蓝鸟
    };

    // 后期关卡只用带技能的鸟，保证「鸟少猪多」时也打得动
    static readonly string[] SpecialBirds =
    {
        "Assets/Prefabs/BlackBird.prefab",     // 黑鸟：炸弹
        "Assets/Prefabs/LanBird.prefab",       // 蓝鸟：分裂
        "Assets/Prefabs/BigMouthBird.prefab",  // 大嘴鸟
        "Assets/Prefabs/YellowBird.prefab",    // 黄鸟：加速
    };

    static void AddBirds(Transform root, int lv, int extra)
    {
        List<Transform> birds = new List<Transform>();
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            if (t.GetComponent<Bird>() != null) birds.Add(t);

        if (birds.Count == 0)
        {
            Debug.LogWarning("[LevelContent] 关卡里找不到 Bird，无法配鸟");
            return;
        }

        birds.Sort((a, b) => a.localPosition.x.CompareTo(b.localPosition.x));
        // 最靠右的那只离弹弓最近，以它为基准向左排，否则整队会排到视野外
        Transform near = birds[birds.Count - 1];
        float gap = birds.Count > 1
            ? Mathf.Abs(near.localPosition.x - birds[birds.Count - 2].localPosition.x)
            : 2.06f;

        Vector3 p0 = near.localPosition;
        Transform par = near.parent;
        int total = birds.Count + Mathf.Max(0, extra);

        foreach (Transform b in birds) Object.DestroyImmediate(b.gameObject);

        for (int i = 0; i < total; i++)
        {
            // 用 Load 而不是 Spawn：每种鸟 prefab 自带的缩放不同（1.7~2.6），
            // 统一覆盖会缩成看不见的小点
            // 后期换成以特殊技能鸟为主（黑鸟炸弹 / 蓝鸟分裂 / 大嘴 / 黄鸟），前期保持混合
            int tier = Mathf.Clamp((lv - 8) / 5, 0, 4);
            string[] deck = tier >= 2 ? SpecialBirds : BirdDeck;
            string path = deck[Mathf.Abs(lv + i) % deck.Length];
            Load(par, path, p0 + new Vector3(-gap * i, 0f, 0f), 0f);
        }
    }

    // ================= 主题背景 =================

    const string Theme2Sky = "Assets/Image/THEME_02_THEME_2.png";
    const string Theme2Parallax = "Assets/Image/THEME_02_PARALLAX_2.png";

    static Dictionary<string, Sprite> _theme2;

    /// <summary>
    /// 把主题1的背景（BgUp 天空 / BgDown 大地 / Grass 草）换成主题2对应精灵。
    /// 两套图尺寸不同，按「显示尺寸不变」重算 localScale，关卡布局不用动。
    /// 已经是主题2 的精灵不在映射表里，重复执行也不会变形。
    /// </summary>
    static int ApplyTheme2(Transform root)
    {
        int swapped = 0;
        foreach (SpriteRenderer sr in root.GetComponentsInChildren<SpriteRenderer>(true))
        {
            if (sr.sprite == null) continue;
            Sprite to = Theme2Of(sr.sprite.name);
            if (to == null) continue;

            Vector2 from = SizeOf(sr.sprite);
            Vector2 size = SizeOf(to);
            Vector3 sc = sr.transform.localScale;
            sr.sprite = to;
            if (from.x > 0f && from.y > 0f && size.x > 0f && size.y > 0f)
                sr.transform.localScale = new Vector3(sc.x * from.x / size.x,
                                                     sc.y * from.y / size.y, sc.z);
            swapped++;
        }
        return swapped;
    }

    static Sprite Theme2Of(string oldName)
    {
        if (_theme2 == null)
        {
            _theme2 = new Dictionary<string, Sprite>();
            Collect(Theme2Sky, _theme2);
            Collect(Theme2Parallax, _theme2);
        }
        switch (oldName)
        {
            case "BgUp": return Pick(_theme2, "BgUP_2");
            case "BgDown": return Pick(_theme2, "Bgdown_2");
            case "Grass": return Pick(_theme2, "Grass_2");
            default: return null;
        }
    }

    static void Collect(string path, Dictionary<string, Sprite> into)
    {
        foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(path))
        {
            Sprite s = o as Sprite;
            if (s != null && !into.ContainsKey(s.name)) into[s.name] = s;
        }
    }

    static Sprite Pick(Dictionary<string, Sprite> dict, string name)
    {
        Sprite s;
        if (dict.TryGetValue(name, out s)) return s;
        Debug.LogError("[LevelContent] 主题2素材里找不到精灵：" + name + "（" + Theme2Sky + " / " + Theme2Parallax + "）");
        return null;
    }

    static Vector2 SizeOf(Sprite s)
    {
        return new Vector2(s.rect.width / s.pixelsPerUnit, s.rect.height / s.pixelsPerUnit);
    }

    // ================= 解锁全部关卡 =================

    static void UnlockAll(string dir)
    {
        string name = Path.GetFileName(dir);
        MapSO map = AssetDatabase.LoadAssetAtPath<MapSO>("Assets/SO/" + name + ".asset");
        if (map == null) { Debug.LogError("[LevelContent] 读不到 " + name + " 的地图数据"); return; }

        SerializedObject so = new SerializedObject(map);
        SerializedProperty arr = so.FindProperty("starNumberOfLevel");
        if (arr == null || !arr.isArray) { Debug.LogError("[LevelContent] starNumberOfLevel 字段异常"); return; }

        int changed = 0;
        for (int i = 0; i < arr.arraySize; i++)
        {
            SerializedProperty e = arr.GetArrayElementAtIndex(i);
            if (e.intValue < 0) { e.intValue = 0; changed++; }
        }
        if (changed > 0)
        {
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(map);
            Debug.Log($"[LevelContent] {name} 解锁了 {changed} 关（共 {arr.arraySize} 个槽位）");
        }
    }
}
