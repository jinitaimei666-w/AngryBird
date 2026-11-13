using System;

[Serializable]
public class BundleInfo
{
    public string name;
    public string hash;           // 决定"要不要下"（内容 hash）
    public string md5;            // 决定"下完对不对"（文件 md5）
    public long size;
    public string[] dependencies; // ← 新增：依赖的 bundle 名（含传递依赖）
}

[Serializable]
public class ManifestData
{
    public string version;
    public string appVersion;
    public BundleInfo[] bundles;
}