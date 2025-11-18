using System;
using System.IO;
using UnityEngine;
using Newtonsoft.Json;
using System.Collections.Generic;
using PhantasmaPhoenix.Unity.Core.Logging;
using PhantasmaPhoenix.RPC.Models;

public static class Cache
{
    public enum FileType
    {
        JSON,
        PNG
    }
    
    private static string FolderPath;
    private static string ImageFolderPath;
    private static string FilePath;
    private static bool ForceCacheUsage = false;

    public static void Init(string folderName, bool forceCacheUsage = false)
    {
        FolderPath = Path.Combine(Application.persistentDataPath, folderName);
        if (!Directory.Exists(FolderPath))
            Directory.CreateDirectory(FolderPath);

        ImageFolderPath = Path.Combine(FolderPath, "image");
        if (!Directory.Exists(ImageFolderPath))
            Directory.CreateDirectory(ImageFolderPath);

        FilePath = Path.Combine(FolderPath, "cache.json");
        ForceCacheUsage = forceCacheUsage;
    }

    public static void Clear()
    {
        System.IO.DirectoryInfo directoryInfo = new DirectoryInfo(FolderPath);

        foreach (FileInfo file in directoryInfo.GetFiles())
        {
            file.Delete();
        }
        foreach (DirectoryInfo dir in directoryInfo.GetDirectories())
        {
            dir.Delete(true);
        }

        Directory.CreateDirectory(ImageFolderPath);
    }

    public class CacheInfo
    {
        public string cacheId;
        public DateTime timestamp;
        public string size;
        public string walletName;
    }

    private static void UpdateRegistry(string CacheId, DateTime Timestamp, int Size, string WalletName)
    {
        List<CacheInfo> cacheInfos = new();

        if (File.Exists(FilePath))
        {
            try
            {
                cacheInfos = JsonConvert.DeserializeObject<List<CacheInfo>>(File.ReadAllText(FilePath));
            }
            catch
            {
                Log.Write("Cache is corrupted, probably old version");
            }
        }

        int index = cacheInfos.FindIndex(x => x.cacheId == CacheId);
        if (index != -1)
        {
            cacheInfos[index].timestamp = Timestamp;
            cacheInfos[index].size = Size.ToString();
            cacheInfos[index].walletName = WalletName;
        }
        else
        {
            cacheInfos.Add(new CacheInfo
            {
                cacheId = CacheId,
                timestamp = Timestamp,
                size = Size.ToString(),
                walletName = WalletName
            });
        }

        File.WriteAllText(FilePath, JsonConvert.SerializeObject(cacheInfos, Formatting.Indented));
    }

    private static Nullable<DateTime> GetRegistryTimestamp(string CacheId)
    {
        if (!File.Exists(FilePath))
        {
            return null;
        }


        List<CacheInfo> cacheInfos = new();
        try
        {
            cacheInfos = JsonConvert.DeserializeObject<List<CacheInfo>>(File.ReadAllText(FilePath));
        }
        catch
        {
            Log.Write("Cache is corrupted, probably old version");
        }

        int index = cacheInfos.FindIndex(x => x.cacheId == CacheId);
        if (index != -1)
        {
            return cacheInfos[index].timestamp;
        }

        return null;
    }

    private static string GetFilePath(string CacheId, FileType FileType)
    {
        if(FileType == FileType.PNG)
            return Path.Combine(ImageFolderPath, "cache." + CacheId + "." + FileType.ToString().ToLower());
        else
            return Path.Combine(FolderPath, "cache." + CacheId + "." + FileType.ToString().ToLower());
    }

    private static string GetFilePathIfCacheIsValid(string CacheId, FileType FileType, int CacheLifetimeInMinutes, string WalletAddress = "")
    {
        if (!String.IsNullOrEmpty(WalletAddress))
            CacheId = CacheId + "." + WalletAddress;

        string filePath = GetFilePath(CacheId, FileType);

        if (!ForceCacheUsage && CacheLifetimeInMinutes > 0)
        {
            Nullable<DateTime> _timestamp = GetRegistryTimestamp(CacheId);

            if (_timestamp == null)
            {
                return null;
            }

            DateTime _timestamp_nn = (DateTime)_timestamp;

            if (_timestamp_nn.AddMinutes(CacheLifetimeInMinutes) < DateTime.UtcNow)
            {
                // Cash is outdated.
                return null;
            }
        }

        if (!File.Exists(filePath))
        {
            return null;
        }

        return filePath;
    }

    public static void Add(string CacheId, FileType FileType, string CacheContents, string WalletAddress = "", string WalletName = "")
    {
        if (!String.IsNullOrEmpty(WalletAddress))
            CacheId = CacheId + "." + WalletAddress;

        string filePath = GetFilePath(CacheId, FileType);

        File.WriteAllText(filePath, CacheContents);

        UpdateRegistry(CacheId, DateTime.Now, System.Text.ASCIIEncoding.ASCII.GetByteCount(CacheContents), WalletName);
    }

    public static void AddTexture(string CacheId, Texture2D CacheContents, string WalletAddress = "", string WalletName = "")
    {
        if (!String.IsNullOrEmpty(WalletAddress))
            CacheId = CacheId + "." + WalletAddress;

        string filePath = GetFilePath(CacheId, FileType.PNG);

        var imageSizeLimit = 256;
        if (CacheContents.width > imageSizeLimit)
        {
            TextureScaler.scale(CacheContents, imageSizeLimit, (int)((double)imageSizeLimit / CacheContents.width * CacheContents.height));
        }
        else if (CacheContents.height > imageSizeLimit)
        {
            TextureScaler.scale(CacheContents, (int)((double)imageSizeLimit / CacheContents.height * CacheContents.width), imageSizeLimit);
        }

        byte[] bytes = CacheContents.EncodeToPNG();
        File.WriteAllBytes(filePath, bytes);

        UpdateRegistry(CacheId, DateTime.Now, bytes.Length, WalletName);
    }
    public static void ClearTexture(string CacheId, string WalletAddress = "", string WalletName = "")
    {
        if (!String.IsNullOrEmpty(WalletAddress))
            CacheId = CacheId + "." + WalletAddress;

        string filePath = GetFilePath(CacheId, FileType.PNG);

        File.Delete(filePath);

        UpdateRegistry(CacheId, DateTime.Now, 0, WalletName);
    }

    public static void SaveTokenDatas(string CacheId, FileType FileType, TokenDataResult[] CacheContents, string WalletAddress = "", string WalletName = "")
    {
        if (!String.IsNullOrEmpty(WalletAddress))
            CacheId = CacheId + "." + WalletAddress;

        string filePath = GetFilePath(CacheId, FileType);

        var serializedCacheContents = JsonConvert.SerializeObject(CacheContents, Formatting.Indented);

        File.WriteAllText(filePath, serializedCacheContents);

        UpdateRegistry(CacheId, DateTime.Now, System.Text.ASCIIEncoding.ASCII.GetByteCount(serializedCacheContents), WalletName);
    }

    public static void ClearDataNode(string CacheId, FileType FileType, string WalletAddress = "", string WalletName = "")
    {
        if (!String.IsNullOrEmpty(WalletAddress))
            CacheId = CacheId + "." + WalletAddress;

        string filePath = GetFilePath(CacheId, FileType);

        File.Delete(filePath);

        UpdateRegistry(CacheId, DateTime.Now, 0, WalletName);
    }

    public static string GetAsString(string CacheId, FileType FileType, int CacheLifetimeInMinutes, string WalletAddress = "")
    {
        if (!String.IsNullOrEmpty(WalletAddress))
            CacheId = CacheId + "." + WalletAddress;

        var filePath = GetFilePathIfCacheIsValid(CacheId, FileType, CacheLifetimeInMinutes);

        if (String.IsNullOrEmpty(filePath))
            return null;

        return File.ReadAllText(filePath);
    }

    public static byte[] GetAsByteArray(string CacheId, FileType FileType, int CacheLifetimeInMinutes, string WalletAddress = "")
    {
        if (!String.IsNullOrEmpty(WalletAddress))
            CacheId = CacheId + "." + WalletAddress;

        var filePath = GetFilePathIfCacheIsValid(CacheId, FileType, CacheLifetimeInMinutes);

        if (String.IsNullOrEmpty(filePath))
            return null;

        return File.ReadAllBytes(filePath);
    }

    public static Texture2D GetTexture(string CacheId, int CacheLifetimeInMinutes, string WalletAddress = "")
    {
        var bytes = GetAsByteArray(CacheId, FileType.PNG, CacheLifetimeInMinutes, WalletAddress);

        if (bytes == null)
            return null;

        var texture = new Texture2D(2, 2);
        texture.LoadImage(bytes);
        return texture;
    }

    public static TokenDataResult[] GetTokenCache(string CacheId, FileType FileType, int CacheLifetimeInMinutes, string WalletAddress = "")
    {
        var cacheContents = GetAsString(CacheId, FileType, CacheLifetimeInMinutes, WalletAddress);

        if (String.IsNullOrEmpty(cacheContents))
            return null;

        TokenDataResult[] cache = new TokenDataResult[]{};
        try
        {
            cache = JsonConvert.DeserializeObject<TokenDataResult[]>(cacheContents);
        }
        catch
        {
            Log.Write("Cache is corrupted, probably old version");
        }

        if(cache == null)
        {
            cache = new TokenDataResult[]{};
        }

        return cache;
    }
    public static TokenDataResult? FindTokenData(TokenDataResult[] cache, string id)
    {
        if(cache == null)
        {
            return null;
        }

        foreach (var cachedToken in cache)
        {
            if (cachedToken.Id == id)
            {
                return cachedToken;
            }
        }

        return null;
    }
}
