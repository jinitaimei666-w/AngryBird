using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 在 Unity 编辑器进程内跑一个极简 HTTP 服务（支持 Range / 206），
/// 把 C:\GameCDN 暴露成 http://127.0.0.1:8000。
/// 好处：不需要额外开终端跑 python，编辑器开着 CDN 就在线。
/// </summary>
[InitializeOnLoad]
public static class LocalCdnServer
{
    const int Port = 8000;
    const string Root = @"C:\GameCDN";

    static TcpListener _listener;
    static Thread _thread;
    static volatile bool _running;

    static LocalCdnServer()
    {
        AssemblyReloadEvents.beforeAssemblyReload += Stop;   // 域重载前释放端口
        EditorApplication.quitting += Stop;
        EditorApplication.delayCall += Start;
    }

    [MenuItem("HotUpdate/④ 启动/停止本地 CDN 服务")]
    static void Toggle()
    {
        if (_running) { Stop(); Debug.Log("[HotUpdate] 本地 CDN 已停止"); }
        else Start();
    }

    static void Start()
    {
        if (_running) return;
        try
        {
            _listener = new TcpListener(IPAddress.Any, Port);   // Any：手机能通过局域网 IP 访问
            _listener.Start();
            _running = true;
            _thread = new Thread(Loop) { IsBackground = true, Name = "LocalCdnServer" };
            _thread.Start();
            Debug.Log($"[HotUpdate] 本地 CDN 已启动：http://127.0.0.1:{Port} → {Root}");
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[HotUpdate] 本地 CDN 启动失败（{Port} 端口可能被占用）：{e.Message}");
        }
    }

    static void Stop()
    {
        _running = false;
        try { _listener?.Stop(); } catch { }
        _listener = null;
    }

    static void Loop()
    {
        while (_running)
        {
            try
            {
                using (TcpClient c = _listener.AcceptTcpClient())
                using (NetworkStream s = c.GetStream())
                {
                    Handle(s);
                }
            }
            catch
            {
                if (_running) Thread.Sleep(30);   // 客户端中途断开（Play 被 Stop）属正常
            }
        }
    }

    static void Handle(NetworkStream s)
    {
        // ── 读请求头（直到空行）
        var head = new StringBuilder();
        var one = new byte[1];
        while (!head.ToString().EndsWith("\r\n\r\n"))
        {
            if (s.Read(one, 0, 1) <= 0) return;
            head.Append((char)one[0]);
            if (head.Length > 8192) return;
        }

        string[] lines = head.ToString().Split(new[] { "\r\n" }, StringSplitOptions.None);
        string[] first = lines[0].Split(' ');
        if (first.Length < 2) return;

        string method = first[0];
        string path = Uri.UnescapeDataString(first[1].Split('?')[0]).TrimStart('/');

        long start = 0, end = -1;
        foreach (string line in lines)
        {
            if (!line.StartsWith("Range:", StringComparison.OrdinalIgnoreCase)) continue;
            string v = line.Substring(6).Trim();
            if (v.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase)) v = v.Substring(6);
            string[] p = v.Split('-');
            long.TryParse(p[0], out start);
            if (p.Length > 1 && p[1].Length > 0) long.TryParse(p[1], out end);
        }

        string fsPath = Path.Combine(Root, path.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(fsPath)) { WriteHead(s, "404 Not Found", 0, null); return; }

        long total = new FileInfo(fsPath).Length;
        if (start < 0 || start >= total) { WriteHead(s, "416 Range Not Satisfiable", 0, $"bytes */{total}"); return; }
        if (end < 0 || end >= total) end = total - 1;

        bool partial = start > 0 || end < total - 1;
        long len = end - start + 1;

        WriteHead(s, partial ? "206 Partial Content" : "200 OK", len,
                  partial ? $"bytes {start}-{end}/{total}" : null);

        if (method == "HEAD") return;

        using (FileStream f = File.OpenRead(fsPath))
        {
            f.Seek(start, SeekOrigin.Begin);
            var buf = new byte[64 * 1024];
            long remain = len;
            while (remain > 0)
            {
                int n = f.Read(buf, 0, (int)Math.Min(buf.Length, remain));
                if (n <= 0) break;
                s.Write(buf, 0, n);
                remain -= n;
            }
        }
    }

    static void WriteHead(NetworkStream s, string status, long len, string contentRange)
    {
        var sb = new StringBuilder();
        sb.Append("HTTP/1.1 ").Append(status).Append("\r\n");
        sb.Append("Accept-Ranges: bytes\r\n");
        sb.Append("Content-Length: ").Append(len).Append("\r\n");
        if (contentRange != null) sb.Append("Content-Range: ").Append(contentRange).Append("\r\n");
        sb.Append("Content-Type: application/octet-stream\r\n");
        sb.Append("Connection: close\r\n\r\n");
        byte[] bytes = Encoding.ASCII.GetBytes(sb.ToString());
        s.Write(bytes, 0, bytes.Length);
    }
}
