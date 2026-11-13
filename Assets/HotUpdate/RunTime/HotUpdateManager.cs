using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

public class HotUpdateManager : MonoBehaviour
{
    /// <summary>CDN 地址。编辑器走 127.0.0.1；真机必须用局域网 IP（127.0.0.1 在手机上指手机自己）。</summary>
    public static string RemoteBase
    {
        get
        {
#if UNITY_EDITOR
            return "http://127.0.0.1:8000";
#else
            return "http://10.85.253.103:8000";
#endif
        }
    }
    public const string BundleName = "ui";

    /// <summary>CDN 平台目录。编辑器 Play 固定用 Win64：bundle 格式必须和运行进程一致。</summary>
    public static string Platform
    {
        get
        {
#if UNITY_EDITOR
            return "Win64";
#elif UNITY_ANDROID
            return "Android";
#elif UNITY_IOS
            return "iOS";
#else
            return "Win64";
#endif
        }
    }

    const int KeepSnapshots = 3;      // 【3.2c】只保留最近几份良好快照

    public static HotUpdateManager Instance { get; private set; }

    bool _ran;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);      // 关卡场景切换后仍要能取 bundle
    }

    public string CurrentVersion { get; private set; } = "0.0.0";
    public ManifestData Applied { get; private set; }

    readonly Dictionary<string, AssetBundle> _loaded = new Dictionary<string, AssetBundle>();

    string HotfixRoot => Path.Combine(Application.persistentDataPath, "hotfix");
    string AppliedManifest => Path.Combine(HotfixRoot, "manifest.json");
    string VersionFile => Path.Combine(HotfixRoot, "current.txt");
    string LocalManifest => Path.Combine(FirstPackRoot, "manifest.json");

    /// <summary>首包解压目录。Android 的 StreamingAssets 在 APK 内部，必须解压出来才能 LoadFromFile。</summary>
    string FirstPackRoot => Path.Combine(Application.persistentDataPath, "firstpack", Platform);
    string SnapshotDir => Path.Combine(HotfixRoot, "manifests");
    string StampFile => Path.Combine(FirstPackRoot, "firstpack.stamp");
    string BootFlag => Path.Combine(HotfixRoot, "boot.flag");
    string BundlesDir => Path.Combine(HotfixRoot, "bundles");

    string BundleDir(BundleInfo b) => Path.Combine(BundlesDir, b.hash);

    /// <summary>整个进程只跑一次更新流程；StartScene 跑过之后，关卡场景再调直接返回。</summary>
    public IEnumerator EnsureUpdated(Action<string> log)
    {
        if (_ran) yield break;
        _ran = true;

        // 提示层挂在更新流程本身：不管哪个场景触发更新，玩家都能看到进度
        UpdateNotice.Show("正在检查更新…");
        bool updated = false;
        yield return Run(msg =>
        {
            if (msg != null && msg.Contains("已更新到")) updated = true;
            UpdateNotice.Set(msg);
            if (log != null) log(msg);
        });

        if (updated)
        {
            UpdateNotice.Set("更新完成，即将进入游戏…");
            yield return new WaitForSeconds(1f);
        }
        UpdateNotice.Hide();
    }

    public IEnumerator Run(Action<string> log)
    {
        Directory.CreateDirectory(HotfixRoot);
        yield return EnsureFirstPack(log);                 // Android：先把首包从 APK 解压到可写目录
        CurrentVersion = ReadCurrent();

        // ── 【3.2b】启动熔断
        if (File.Exists(BootFlag))
        {
            string failed = File.ReadAllText(BootFlag).Trim();
            log?.Invoke("[HotUpdate] 上次启动未完成（" + failed + "）→ 开始回滚");
            Applied = Rollback(failed, log);
            CurrentVersion = ReadCurrent();
        }

        // ── 【3.2c】清理无用内容（下载前、无句柄占用）
        Cleanup(log);

        // ── ② 本地清单
        ManifestData local = Applied;
        if (local == null)
        {
            string localPath = File.Exists(AppliedManifest) ? AppliedManifest : LocalManifest;
            if (File.Exists(localPath))
            {
                local = JsonUtility.FromJson<ManifestData>(File.ReadAllText(localPath));
                log?.Invoke("[HotUpdate] 本地清单（" + local.version + "）：" + localPath);
            }
            else log?.Invoke("[HotUpdate] 无本地清单，按首包运行");
        }
        else log?.Invoke("[HotUpdate] 使用回滚后的清单：" + local.version);
        Applied = local;

        // ── ② 拉远端清单
        ManifestData remote = null;
        {
            UnityWebRequest req = UnityWebRequest.Get(RemoteBase + "/" + Platform + "/manifest.json");
            req.timeout = 8;      // 服务器不可达时别无限等，超时按离线处理

            UnityWebRequestAsyncOperation op = null;
            string sendErr = null;
            try { op = req.SendWebRequest(); }   // http 被 Player Settings 禁掉时这里会直接抛异常
            catch (System.Exception e) { sendErr = e.Message; }

            if (op == null)
            {
                req.Dispose();
                log?.Invoke("[HotUpdate] 清单请求被拒绝：" + sendErr + " → 离线模式，沿用本地版本 " + CurrentVersion);
                yield break;
            }

            yield return op;

            bool ok = req.result == UnityWebRequest.Result.Success;
            string err = req.error;
            string body = ok ? req.downloadHandler.text : null;
            req.Dispose();

            if (!ok || string.IsNullOrEmpty(body))
            {
                log?.Invoke("[HotUpdate] 清单失败：" + err + " → 离线模式，沿用本地版本 " + CurrentVersion);
                yield break;
            }

            remote = JsonUtility.FromJson<ManifestData>(body);
            log?.Invoke("[HotUpdate] 远端清单：version=" + remote.version);
        }

        // ── 【3.2b】黑名单
        if (File.Exists(Path.Combine(SnapshotDir, remote.version + ".bad.json")))
        {
            log?.Invoke("[HotUpdate] 远端 " + remote.version + " 已被标记为坏版本 → 跳过更新");
            yield break;
        }

        // ── ③ 比对
        List<BundleInfo> todo = new List<BundleInfo>();
        HashSet<string> seen = new HashSet<string>();
        foreach (BundleInfo rb in remote.bundles) Collect(rb, local, remote, todo, seen, log);

        if (todo.Count == 0) log?.Invoke("[HotUpdate] 内容已是最新：" + remote.version);

        // ── ④ 下载（断点续传）+ 校验
        for (int i = 0; i < todo.Count; i++)
        {
            BundleInfo b = todo[i];
            Directory.CreateDirectory(BundleDir(b));
            string finalPath = Path.Combine(BundleDir(b), b.name);

            if (File.Exists(finalPath) && Md5(finalPath) == b.md5)
            {
                log?.Invoke("[HotUpdate] 复用已存在的 " + b.name + "（同 hash 已在本地）");
                continue;
            }

            string tmp = finalPath + ".tmp";

            // 【3.1】断点 = .tmp 里已经有的字节数
            long offset = 0;
            if (File.Exists(tmp))
            {
                long have = new FileInfo(tmp).Length;
                if (b.size > 0 && have <= b.size) offset = have;
                else File.Delete(tmp);                       // 长度不可信（远端换了内容 / 脏文件）→ 重下
            }

            if (offset > 0 && offset < b.size)
                log?.Invoke("[HotUpdate] 续传 " + b.name + "：已有 " + Size(offset) + " / " + Size(b.size) +
                            " → 从第 " + offset + " 字节继续");
            else if (b.size > 0 && offset == b.size)
                log?.Invoke("[HotUpdate] 断点已完整，直接校验 " + b.name);
            else
                log?.Invoke("[HotUpdate] 下载 " + b.name + " (" + (i + 1) + "/" + todo.Count + ")  " + b.size + " B");

            if (b.size <= 0 || offset < b.size)
            {
                // 最多两次：第一次带 Range 续传；若发现服务端其实给了整包，再从头下一次
                for (int attempt = 0; attempt < 2; attempt++)
                {
                    DownloadResult r = null;
                    yield return DownloadTo(RemoteBase + "/" + Platform + "/" + b.name, tmp, offset, b.size, log,
                                            res => r = res);

                    if (!r.ok)
                    {
                        // 关键：不删 .tmp —— 它就是下次启动的断点
                        log?.Invoke("[HotUpdate] 下载中断：" + r.error + "（已保留断点 " + Size(r.total) +
                                    "，下次启动从这里续）→ 沿用旧版本 " + CurrentVersion);
                        yield break;
                    }

                    // 续传成功 = 断点 + 本次收到的 == 清单里的 size。
                    // 不等说明服务端忽略了 Range（回了整包），断点后面的内容已被污染 → 从头重下
                    if (offset > 0 && b.size > 0 && r.total != b.size)
                    {
                        log?.Invoke("[HotUpdate] 服务端未按 Range 响应（" + Size(r.total) + " ≠ " + Size(b.size) +
                                    "），改为整包重下 " + b.name);
                        File.Delete(tmp);
                        offset = 0;
                        continue;
                    }
                    break;
                }
            }

            if (Md5(tmp) != b.md5)
            {
                log?.Invoke("[HotUpdate] md5 校验失败，丢弃 " + b.name + " 的断点");
                File.Delete(tmp);
                yield break;
            }
            if (File.Exists(finalPath)) File.Delete(finalPath);
            File.Move(tmp, finalPath);
        }

        // ── ⑤ 提交
        Directory.CreateDirectory(SnapshotDir);
        File.WriteAllText(Path.Combine(SnapshotDir, remote.version + ".json"), JsonUtility.ToJson(remote));
        File.WriteAllText(AppliedManifest, JsonUtility.ToJson(remote));
        File.WriteAllText(VersionFile, remote.version);
        File.WriteAllText(BootFlag, remote.version);
        CurrentVersion = remote.version;
        Applied = remote;
        log?.Invoke("[HotUpdate] 已更新到 " + remote.version);
    }

    // 【3.1】带 Range 的下载：从 offset 起写入 tmp，失败时保留已下内容作为断点
    IEnumerator DownloadTo(string url, string tmp, long offset, long total,
                           Action<string> log, Action<DownloadResult> done)
    {
        DownloadResult r = new DownloadResult();
        AppendHandler h = new AppendHandler(tmp, offset);
        using (UnityWebRequest req = UnityWebRequest.Get(url))
        {
            req.downloadHandler = h;
            // 只有桌面/移动端能设 Range 头；WebGL 下浏览器不允许，服务端也会忽略 → 走 200 兜底
            if (offset > 0) req.SetRequestHeader("Range", "bytes=" + offset + "-");
            req.timeout = 60;   // 卡住的连接要能失败，否则断点续传永远等不到结束

            UnityWebRequestAsyncOperation op = null;
            string sendErr = null;
            try { op = req.SendWebRequest(); }
            catch (System.Exception e) { sendErr = e.Message; }

            if (op == null)
            {
                h.CloseFile();
                r.error = sendErr;
                done?.Invoke(r);
                yield break;
            }

            int lastPct = -10;
            while (!op.isDone)
            {
                long sum = h.Total;
                int pct = total > 0 ? (int)(sum * 100 / total) : -1;
                if (pct >= 0 && pct / 10 != lastPct / 10)
                {
                    lastPct = pct;
                    log?.Invoke("[HotUpdate] … " + pct.ToString().PadLeft(3) + "%  " + Size(sum) + " / " + Size(total));
                }
                yield return null;
            }

            h.CloseFile();                       // 无论成功失败都要落盘并释放句柄
            r.total = h.Total;
            r.ok = req.result == UnityWebRequest.Result.Success && string.IsNullOrEmpty(h.Error);
            r.error = !string.IsNullOrEmpty(h.Error) ? h.Error : req.error;
        }
        done?.Invoke(r);
    }

    class DownloadResult
    {
        public bool ok;
        public long total;          // 本次结束后 .tmp 的实际字节数（断点 + 本次收到的）
        public string error;
    }

    /// <summary>把收到的数据写到文件 offset 之后（续传时就是追加）。</summary>
    class AppendHandler : DownloadHandlerScript
    {
        FileStream _fs;
        readonly long _offset;
        long _received;

        public long Total => _offset + _received;
        public string Error { get; private set; }

        public AppendHandler(string path, long offset) : base(new byte[64 * 1024])
        {
            _offset = offset;
            _fs = new FileStream(path, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Read);
            if (offset > 0) _fs.Seek(offset, SeekOrigin.Begin);
            else _fs.SetLength(0);
        }

        protected override bool ReceiveData(byte[] data, int dataLength)
        {
            if (data == null || dataLength == 0) return true;
            try
            {
                _fs.Write(data, 0, dataLength);
                _received += dataLength;
                return true;
            }
            catch (Exception e)
            {
                Error = e.Message;
                return false;
            }
        }

        protected override void CompleteContent()
        {
            CloseFile();
        }

        /// <summary>DownloadHandler 没有可重写的 Dispose，句柄由调用方显式关闭（成功/失败都要关）</summary>
        public void CloseFile()
        {
            if (_fs == null) return;
            try { _fs.Flush(); _fs.Dispose(); } catch { }
            _fs = null;
        }
    }

    static string Size(long bytes)
    {
        if (bytes >= 1024 * 1024) return (bytes / 1024.0 / 1024.0).ToString("0.0") + " MB";
        if (bytes >= 1024) return (bytes / 1024.0).ToString("0.0") + " KB";
        return bytes + " B";
    }

    // ── 首包解压
    // Android 的 StreamingAssets 在 APK 内部（jar:file://），既不能 File.ReadAllText，
    // 也不能 AssetBundle.LoadFromFile。所以首次启动时先解压到 persistentDataPath。
    IEnumerator EnsureFirstPack(Action<string> log)
    {
        Directory.CreateDirectory(FirstPackRoot);

        string srcRoot = Path.Combine(Application.streamingAssetsPath, "bundles", Platform);
        string srcManifest = Path.Combine(srcRoot, "manifest.json");

        string json = null;
        yield return ReadText(srcManifest, s => json = s);

        if (string.IsNullOrEmpty(json))
        {
            log?.Invoke("[HotUpdate] 首包清单缺失（" + srcManifest + "），只能走热更");
            yield break;
        }

        ManifestData pack = null;
        try { pack = JsonUtility.FromJson<ManifestData>(json); } catch { }

        // 首包内容变了（装了新 APK）→ 必须丢掉旧的热更状态，否则旧的 hotfix/manifest.json
        // 会让客户端继续按旧 hash 加载旧 bundle，表现为「明明换了资源，装上还是老样子」。
        string stamp = Md5Text(json);
        // 老版本升级上来的机器还没有指纹文件（但已经有首包）→ 也要重置一次
        bool stale = File.Exists(StampFile)
            ? File.ReadAllText(StampFile).Trim() != stamp
            : File.Exists(LocalManifest);
        if (stale)
        {
            log?.Invoke("[HotUpdate] 检测到首包更新，清理旧热更状态");
            DropHotfixState();
        }

        // 已经解压过且文件大小都对得上就不再重复拷贝
        bool ready = !stale && File.Exists(LocalManifest);
        if (ready && pack != null && pack.bundles != null)
        {
            foreach (BundleInfo b in pack.bundles)
            {
                string f = Path.Combine(FirstPackRoot, b.name);
                if (!File.Exists(f) || new FileInfo(f).Length != b.size) { ready = false; break; }
            }
        }

        if (!ready)
        {
            log?.Invoke("[HotUpdate] 解压首包 → " + FirstPackRoot);
            if (pack != null && pack.bundles != null)
            {
                foreach (BundleInfo b in pack.bundles)
                    yield return CopyFile(Path.Combine(srcRoot, b.name), Path.Combine(FirstPackRoot, b.name));
            }
        }

        File.WriteAllText(LocalManifest, json);
        File.WriteAllText(StampFile, stamp);
    }

    /// <summary>首包换新时清掉旧的「已生效」状态，让加载回落到新首包（bundle 是 hash 命名的，留着无害）</summary>
    void DropHotfixState()
    {
        Applied = null;
        CurrentVersion = "0.0.0";
        _loaded.Clear();
        TryDelete(AppliedManifest);
        TryDelete(VersionFile);
        TryDelete(BootFlag);
        try { if (Directory.Exists(SnapshotDir)) Directory.Delete(SnapshotDir, true); } catch { }
    }

    static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    static string Md5Text(string text)
    {
        using (MD5 md5 = MD5.Create())
        {
            byte[] hash = md5.ComputeHash(Encoding.UTF8.GetBytes(text));
            StringBuilder sb = new StringBuilder();
            foreach (byte b in hash) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }
    }

    IEnumerator ReadText(string path, Action<string> done)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        using (UnityWebRequest req = UnityWebRequest.Get(path))
        {
            yield return req.SendWebRequest();
            done?.Invoke(req.result == UnityWebRequest.Result.Success ? req.downloadHandler.text : null);
        }
#else
        done?.Invoke(File.Exists(path) ? File.ReadAllText(path) : null);
        yield break;
#endif
    }

    IEnumerator CopyFile(string src, string dst)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        using (UnityWebRequest req = UnityWebRequest.Get(src))
        {
            yield return req.SendWebRequest();
            if (req.result == UnityWebRequest.Result.Success) File.WriteAllBytes(dst, req.downloadHandler.data);
            else Debug.LogError("[HotUpdate] 首包解压失败：" + src + " → " + req.error);
        }
#else
        try { File.Copy(src, dst, true); }
        catch (Exception e) { Debug.LogError("[HotUpdate] 首包复制失败：" + e.Message); }
        yield break;
#endif
    }


    public void MarkBootOk()
    {
        if (File.Exists(BootFlag)) File.Delete(BootFlag);
        Debug.Log("[HotUpdate] 启动完成，版本 " + CurrentVersion);
    }

    // 【3.2c】按引用关系回收
    void Cleanup(Action<string> log)
    {
        if (!Directory.Exists(SnapshotDir)) return;

        // 1) 收集良好快照，按版本降序，只保留最近 KeepSnapshots 份（当前生效版永不裁）
        List<string> good = new List<string>();
        foreach (string f in Directory.GetFiles(SnapshotDir, "*.json"))
            if (!f.EndsWith(".bad.json", StringComparison.OrdinalIgnoreCase)) good.Add(f);

        good.Sort((a, b) => CompareVersion(
            Path.GetFileNameWithoutExtension(b), Path.GetFileNameWithoutExtension(a)));

        for (int i = good.Count - 1; i >= KeepSnapshots; i--)
        {
            string v = Path.GetFileNameWithoutExtension(good[i]);
            if (v == CurrentVersion) continue;                     // 生效版本绝不裁
            log?.Invoke("[HotUpdate] 清理旧快照 " + v);
            try { File.Delete(good[i]); } catch { }
            good.RemoveAt(i);
        }

        // 2) 存活 hash = 良好快照 ∪ 首包清单（.bad.json 不算）
        HashSet<string> alive = new HashSet<string>();
        AddHashes(alive, LocalManifest);
        foreach (string f in good) AddHashes(alive, f);

        if (alive.Count == 0) return;                              // 什么都不认识就别乱删

        // 3) 删掉没人引用的 hash 目录
        if (!Directory.Exists(BundlesDir)) return;
        long freed = 0;
        foreach (string dir in Directory.GetDirectories(BundlesDir))
        {
            string hash = Path.GetFileName(dir);
            if (alive.Contains(hash)) continue;

            long size = DirSize(dir);
            try
            {
                Directory.Delete(dir, true);
                freed += size;
                log?.Invoke("[HotUpdate] 回收无用内容 " + hash.Substring(0, 8) + "…（" +
                            (size / 1024.0).ToString("0.0") + " KB）");
            }
            catch (Exception e)
            {
                log?.Invoke("[HotUpdate] 回收失败 " + hash.Substring(0, 8) + "：" + e.Message);
            }
        }
        if (freed > 0)
            log?.Invoke("[HotUpdate] 清理完成，释放 " + (freed / 1024.0).ToString("0.0") + " KB");
    }

    static void AddHashes(HashSet<string> set, string manifestPath)
    {
        if (!File.Exists(manifestPath)) return;
        ManifestData m = null;
        try { m = JsonUtility.FromJson<ManifestData>(File.ReadAllText(manifestPath)); } catch { return; }
        if (m == null || m.bundles == null) return;
        foreach (BundleInfo b in m.bundles)
            if (!string.IsNullOrEmpty(b.hash)) set.Add(b.hash);
    }

    static long DirSize(string dir)
    {
        long total = 0;
        foreach (string f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
        {
            try { total += new FileInfo(f).Length; } catch { }
        }
        return total;
    }

    ManifestData Rollback(string failedVersion, Action<string> log)
    {
        if (!string.IsNullOrEmpty(failedVersion))
        {
            string src = Path.Combine(SnapshotDir, failedVersion + ".json");
            string bad = Path.Combine(SnapshotDir, failedVersion + ".bad.json");
            if (File.Exists(src))
            {
                if (File.Exists(bad)) File.Delete(bad);
                File.Move(src, bad);
                log?.Invoke("[HotUpdate] 版本 " + failedVersion + " 已标记为坏版本");
            }
        }

        ManifestData best = null;
        string bestVersion = null;
        if (Directory.Exists(SnapshotDir))
        {
            foreach (string f in Directory.GetFiles(SnapshotDir, "*.json"))
            {
                if (f.EndsWith(".bad.json", StringComparison.OrdinalIgnoreCase)) continue;
                string v = Path.GetFileNameWithoutExtension(f);
                if (v == failedVersion) continue;
                if (CompareVersion(v, failedVersion) >= 0) continue;

                ManifestData m = null;
                try { m = JsonUtility.FromJson<ManifestData>(File.ReadAllText(f)); } catch { continue; }
                if (m == null || m.bundles == null) continue;
                if (best == null || CompareVersion(v, bestVersion) > 0) { best = m; bestVersion = v; }
            }
        }

        if (best != null)
        {
            File.WriteAllText(AppliedManifest, JsonUtility.ToJson(best));
            File.WriteAllText(VersionFile, best.version);
            log?.Invoke("[HotUpdate] 已回滚到 " + best.version);
            return best;
        }

        if (File.Exists(VersionFile)) File.Delete(VersionFile);
        if (File.Exists(AppliedManifest)) File.Delete(AppliedManifest);
        log?.Invoke("[HotUpdate] 无可回滚版本 → 退回首包");
        return null;
    }

    static int CompareVersion(string a, string b)
    {
        int[] pa = ParseVersion(a), pb = ParseVersion(b);
        for (int i = 0; i < 3; i++)
            if (pa[i] != pb[i]) return pa[i] > pb[i] ? 1 : -1;
        return 0;
    }

    static int[] ParseVersion(string v)
    {
        int[] r = new int[3];
        if (string.IsNullOrEmpty(v)) return r;
        string[] p = v.Split('.');
        for (int i = 0; i < 3 && i < p.Length; i++) int.TryParse(p[i], out r[i]);
        return r;
    }

    void Collect(BundleInfo rb, ManifestData local, ManifestData remote,
                 List<BundleInfo> todo, HashSet<string> seen, Action<string> log)
    {
        if (!seen.Add(rb.name)) return;
        BundleInfo lb = Find(local, rb.name);
        if (lb == null) { todo.Add(rb); log?.Invoke("[HotUpdate] 新增 " + rb.name); }
        else if (lb.hash != rb.hash)
        {
            todo.Add(rb);
            log?.Invoke("[HotUpdate] 需要更新 " + rb.name + "（" + lb.hash.Substring(0, 8) + " → " + rb.hash.Substring(0, 8) + "）");
        }

        if (rb.dependencies == null) return;
        foreach (string dep in rb.dependencies)
        {
            BundleInfo rd = Find(remote, dep);
            if (rd == null) { log?.Invoke("[HotUpdate] 警告：远端清单里没有依赖 " + dep); continue; }
            Collect(rd, local, remote, todo, seen, log);
        }
    }

    static BundleInfo Find(ManifestData m, string name)
    {
        if (m == null || m.bundles == null) return null;
        foreach (BundleInfo b in m.bundles) if (b.name == name) return b;
        return null;
    }

    public AssetBundle LoadBundle() { return LoadBundle(BundleName); }

    public AssetBundle LoadBundle(string name)
    {
        AssetBundle cached;
        if (_loaded.TryGetValue(name, out cached) && cached != null) return cached;

        EnsureApplied();
        BundleInfo info = Find(Applied, name);

        if (info != null && info.dependencies != null)
            foreach (string dep in info.dependencies) LoadBundle(dep);

        if (info != null)
        {
            string hp = Path.Combine(BundleDir(info), name);
            if (File.Exists(hp))
            {
                AssetBundle hb = AssetBundle.LoadFromFile(hp);
                if (hb != null)
                {
                    Debug.Log("[HotUpdate] 从 hotfix/" + info.hash.Substring(0, 8) + " 加载 " + name);
                    _loaded[name] = hb;
                    return hb;
                }
            }
        }

        string sp = Path.Combine(FirstPackRoot, name);
        AssetBundle sb = AssetBundle.LoadFromFile(sp);
        if (sb == null) Debug.LogError("[HotUpdate] 首包也没有 " + name);
        else Debug.Log("[HotUpdate] 从首包加载 " + name);
        _loaded[name] = sb;
        return sb;
    }

    void EnsureApplied()
    {
        if (Applied != null) return;
        string p = File.Exists(AppliedManifest) ? AppliedManifest : LocalManifest;
        if (File.Exists(p)) Applied = JsonUtility.FromJson<ManifestData>(File.ReadAllText(p));
    }

    string ReadCurrent()
    {
        if (!File.Exists(VersionFile)) return "0.0.0";
        string v = File.ReadAllText(VersionFile).Trim();
        return string.IsNullOrEmpty(v) ? "0.0.0" : v;
    }

    static string Md5(string path)
    {
        using (MD5 md5 = MD5.Create())
        using (FileStream fs = File.OpenRead(path))
        {
            byte[] bytes = md5.ComputeHash(fs);
            StringBuilder sb = new StringBuilder();
            foreach (byte b in bytes) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }
    }
}