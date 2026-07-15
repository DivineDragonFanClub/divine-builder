using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;

namespace DivineDragon.Patcher
{
    [Serializable]
    public class CacheEntry
    {
        public string cab;
        public string internal_id;
        public long path_id;
    }

    [Serializable]
    internal class CacheEntryArray
    {
        public CacheEntry[] items;
    }

    public static class Addr
    {
        public const string RuntimePath = "{UnityEngine.AddressableAssets.Addressables.RuntimePath}";
    }

    public class GameCache
    {
        public readonly Dictionary<string, CacheEntry> ByInternalId;
        public readonly Dictionary<string, long> UacByName;

        private GameCache(Dictionary<string, CacheEntry> byInternalId, Dictionary<string, long> uacByName)
        {
            ByInternalId = byInternalId;
            UacByName = uacByName;
        }

        public static GameCache Load(string cacheJsonPath, string uacCacheJsonPath)
        {
            var byInternalId = LoadCache(cacheJsonPath);
            var uac = LoadUacCache(uacCacheJsonPath);
            return new GameCache(byInternalId, uac);
        }

        private static Dictionary<string, CacheEntry> LoadCache(string path)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException($"Divine Builder cache is missing: {path}");

            string json = File.ReadAllText(path);
            var wrapped = JsonUtility.FromJson<CacheEntryArray>("{\"items\":" + json + "}");
            var entries = wrapped != null && wrapped.items != null ? wrapped.items : Array.Empty<CacheEntry>();

            var map = new Dictionary<string, CacheEntry>(entries.Length);
            foreach (var e in entries)
            {
                if (e == null || string.IsNullOrEmpty(e.internal_id))
                    continue;
                if (!map.ContainsKey(e.internal_id))
                    map.Add(e.internal_id, e);
            }
            return map;
        }

        private static readonly Regex UacPair = new Regex(
            "\"((?:[^\"\\\\]|\\\\.)*)\"\\s*:\\s*(-?\\d+)", RegexOptions.Compiled);

        private static Dictionary<string, long> LoadUacCache(string path)
        {
            var map = new Dictionary<string, long>();
            if (!File.Exists(path))
                return map;

            string json = File.ReadAllText(path);
            foreach (Match m in UacPair.Matches(json))
            {
                string name = Regex.Unescape(m.Groups[1].Value);
                if (long.TryParse(m.Groups[2].Value, out long id))
                    map[name] = id;
            }
            return map;
        }
    }

    public struct ExternalSource
    {
        public bool IsLibrary;
        public string Cab;

        public static ExternalSource Library => new ExternalSource { IsLibrary = true, Cab = null };
        public static ExternalSource Archive(string cab) => new ExternalSource { IsLibrary = false, Cab = cab };
    }
}
