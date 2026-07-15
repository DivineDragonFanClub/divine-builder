using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using AssetsTools.NET;
using AssetsTools.NET.Extra;
using UnityEngine;

namespace DivineDragon.Patcher
{
    public class BundlePatcher
    {
        private readonly string _gameCodeAsm;

        public BundlePatcher(string gameCodeAsm = "DivineDragon.GameCode")
        {
            _gameCodeAsm = gameCodeAsm;
        }

        private const int ClassShader = 48;
        private const int ClassMaterial = 21;
        private const int ClassAnimationClip = 74;
        private const int ClassAnimatorController = 91;
        private const int ClassMonoBehaviour = 114;
        private const int ClassMonoScript = 115;
        private const int ClassAssetBundle = 142;
        private const int ClassAnimatorOverrideController = 221;

        public PatchResult Run(
            string cacheJsonPath,
            string uacCacheJsonPath,
            string settingsJsonPath,
            string outDir,
            IProgress<PatchProgress> progress,
            CancellationToken ct)
        {
            var result = new PatchResult();
            GameCache game = GameCache.Load(cacheJsonPath, uacCacheJsonPath);

            string projectRoot = GetProjectRoot(settingsJsonPath);
            if (!Directory.Exists(projectRoot))
                throw new DirectoryNotFoundException(
                    $"Could not find the built bundles at '{projectRoot}'. Is your build target correct?");

            var bundlePaths = Directory
                .EnumerateFiles(projectRoot, "*.bundle", SearchOption.AllDirectories)
                .ToList();

            var am = new AssetsManager();
            LoadOptionalClassDatabase(am, cacheJsonPath);

            try
            {
                var localCache = BuildLocalCache(bundlePaths, projectRoot, progress, ct, result);
                if (result.Cancelled) return result;

                var uacThisBuild = BuildUacCache(am, bundlePaths, projectRoot, progress, ct, result);
                if (result.Cancelled) return result;

                PatchBundles(am, bundlePaths, projectRoot, outDir, localCache, uacThisBuild, game, progress, ct, result);
            }
            finally
            {
                am.UnloadAll(true);
            }

            return result;
        }

        private static string GetProjectRoot(string settingsJsonPath)
        {
            string json = File.ReadAllText(settingsJsonPath);
            var settings = JsonUtility.FromJson<UnitySettings>(json);
            if (settings == null || string.IsNullOrEmpty(settings.m_buildTarget))
                throw new Exception("settings.json has no m_buildTarget. Is this the right file?");
            string parent = Path.GetDirectoryName(Path.GetFullPath(settingsJsonPath));
            return Path.Combine(parent, settings.m_buildTarget);
        }

        [Serializable]
        private class UnitySettings
        {
            public string m_buildTarget;
        }

        private void LoadOptionalClassDatabase(AssetsManager am, string cacheJsonPath)
        {
            string dir = Path.GetDirectoryName(cacheJsonPath);
            string tpk = Path.Combine(dir, "classdata.tpk");
            if (File.Exists(tpk))
                am.LoadClassPackage(tpk);
        }

        private string InternalIdFor(string projectRoot, string bundlePath)
        {
            string full = Path.GetFullPath(bundlePath);
            string root = Path.GetFullPath(projectRoot);
            string rel = full.Substring(root.Length).TrimStart('/', '\\').Replace('\\', '/');
            return Addr.RuntimePath + "/" + rel;
        }

        private Dictionary<string, string> BuildLocalCache(
            List<string> bundlePaths, string projectRoot,
            IProgress<PatchProgress> progress, CancellationToken ct, PatchResult result)
        {
            var localCache = new Dictionary<string, string>();
            int i = 0;
            foreach (var path in bundlePaths)
            {
                if (ct.IsCancellationRequested) { result.Cancelled = true; return localCache; }
                i++;
                Report(progress, "scan", i, bundlePaths.Count, Path.GetFileName(path));

                try
                {
                    var bun = new AssetBundleFile();
                    using (var reader = new AssetsFileReader(File.OpenRead(path)))
                    {
                        bun.Read(reader);
                        string cab = FirstAssetsFileName(bun);
                        if (cab != null)
                            localCache[cab] = InternalIdFor(projectRoot, path);
                        bun.Close();
                    }
                }
                catch (Exception e)
                {
                    result.Errors.Add(new BuildError
                    {
                        BundlePath = path,
                        Kind = BuildErrorKind.BundleIo,
                        Detail = $"could not read CAB: {e.Message}",
                    });
                }
            }
            return localCache;
        }

        private static string FirstAssetsFileName(AssetBundleFile bun)
        {
            foreach (var dir in bun.BlockAndDirInfo.DirectoryInfos)
            {
                if (dir.Name != null && dir.Name.StartsWith("CAB-") && !dir.Name.Contains("."))
                    return dir.Name;
            }
            return null;
        }

        private Dictionary<long, string> BuildUacCache(
            AssetsManager am, List<string> bundlePaths, string projectRoot,
            IProgress<PatchProgress> progress, CancellationToken ct, PatchResult result)
        {
            var uac = new Dictionary<long, string>();
            int i = 0;
            foreach (var path in bundlePaths)
            {
                if (ct.IsCancellationRequested) { result.Cancelled = true; return uac; }
                i++;
                Report(progress, "uac", i, bundlePaths.Count, Path.GetFileName(path));

                BundleFileInstance bun = null;
                try
                {
                    bun = am.LoadBundleFile(path, true);
                    foreach (var afi in AssetsFilesIn(am, bun))
                    {
                        if (!HasUacTemplate(am, afi))
                            continue;
                        foreach (var info in afi.file.AssetInfos)
                        {
                            if (info.TypeId != ClassAnimationClip)
                                continue;
                            var bf = am.GetBaseField(afi, info, AssetReadFlags.None);
                            string name = bf["m_Name"].AsString;
                            if (!string.IsNullOrEmpty(name))
                                uac[info.PathId] = name;
                        }
                    }
                }
                catch (Exception e)
                {
                    result.Errors.Add(new BuildError
                    {
                        BundlePath = path,
                        Kind = BuildErrorKind.BundleIo,
                        Detail = $"could not scan for UAC template: {e.Message}",
                    });
                }
                finally
                {
                    if (bun != null) am.UnloadBundleFile(bun);
                }
            }
            return uac;
        }

        private bool HasUacTemplate(AssetsManager am, AssetsFileInstance afi)
        {
            foreach (var info in afi.file.AssetInfos)
            {
                if (info.TypeId != ClassAnimatorController)
                    continue;
                var bf = am.GetBaseField(afi, info, AssetReadFlags.None);
                if (bf["m_Name"].AsString == "UAC_Template")
                    return true;
            }
            return false;
        }

        private void PatchBundles(
            AssetsManager am, List<string> bundlePaths, string projectRoot, string outDir,
            Dictionary<string, string> localCache, Dictionary<long, string> uacThisBuild,
            GameCache game, IProgress<PatchProgress> progress, CancellationToken ct, PatchResult result)
        {
            int i = 0;
            foreach (var path in bundlePaths)
            {
                if (ct.IsCancellationRequested) { result.Cancelled = true; return; }
                i++;
                Report(progress, "patch", i, bundlePaths.Count, Path.GetFileName(path));

                BundleFileInstance bun = null;
                try
                {
                    bun = am.LoadBundleFile(path, true);

                    if (BundleHasShader(am, bun))
                    {
                        result.Skipped++;
                        continue;
                    }

                    bool anyError = ProcessBundle(am, bun, path, projectRoot, localCache, uacThisBuild, game, result);
                    if (anyError)
                    {
                        result.Skipped++;
                        continue;
                    }

                    WriteBundle(bun, projectRoot, outDir, path);
                    result.Patched++;
                }
                catch (Exception e)
                {
                    result.Errors.Add(new BuildError
                    {
                        BundlePath = path,
                        Kind = BuildErrorKind.BundleIo,
                        Detail = $"failed to patch: {e.Message}",
                    });
                }
                finally
                {
                    if (bun != null) am.UnloadBundleFile(bun);
                }
            }
        }

        private bool BundleHasShader(AssetsManager am, BundleFileInstance bun)
        {
            foreach (var afi in AssetsFilesIn(am, bun))
                foreach (var info in afi.file.AssetInfos)
                    if (info.TypeId == ClassShader)
                        return true;
            return false;
        }

        private bool ProcessBundle(
            AssetsManager am, BundleFileInstance bun, string path, string projectRoot,
            Dictionary<string, string> localCache, Dictionary<long, string> uacThisBuild,
            GameCache game, PatchResult result)
        {
            string relPath = RelativePath(projectRoot, path);
            int errorsBefore = result.Errors.Count;

            var dirInfos = bun.file.BlockAndDirInfo.DirectoryInfos;
            for (int idx = 0; idx < dirInfos.Count; idx++)
            {
                if (!bun.file.IsAssetsFile(idx))
                    continue;

                var afi = am.LoadAssetsFileFromBundle(bun, idx, false);

                if (!afi.file.Metadata.TypeTreeEnabled)
                {
                    result.Errors.Add(new BuildError
                    {
                        BundlePath = relPath,
                        Kind = BuildErrorKind.BundleIo,
                        Detail = "assets file has no type tree, cannot read fields",
                        Hint = "Turn off 'Disable Type Tree' in the Addressables build script, or ship a classdata.tpk.",
                    });
                    continue;
                }

                var deps = afi.file.Metadata.Externals.Select(e => GetExternalSource(e.PathName)).ToList();
                var retargeter = new AssetRetargeter(deps, localCache, game, result.Errors, relPath);

                foreach (var info in afi.file.AssetInfos)
                {
                    if (info.TypeId != ClassAssetBundle && info.TypeId != ClassMaterial &&
                        info.TypeId != ClassAnimationClip && info.TypeId != ClassAnimatorOverrideController &&
                        info.TypeId != ClassMonoBehaviour && info.TypeId != ClassMonoScript)
                        continue;

                    if (info.TypeId == ClassMonoBehaviour)
                    {
                        AssetTypeValueField mb;
                        try { mb = am.GetBaseField(afi, info, AssetReadFlags.None); }
                        catch (Exception e)
                        {
                            result.Warnings.Add($"{relPath}: skipped a MonoBehaviour we couldn't read: {e.Message}");
                            continue;
                        }
                        retargeter.TextMeshPro(mb);
                        info.SetNewData(mb);
                        continue;
                    }

                    var bf = am.GetBaseField(afi, info, AssetReadFlags.None);
                    switch (info.TypeId)
                    {
                        case ClassAssetBundle:
                            retargeter.AssetBundleContainer(bf);
                            break;
                        case ClassMaterial:
                            retargeter.Material(bf);
                            break;
                        case ClassAnimationClip:
                            retargeter.AnimationClip(bf);
                            break;
                        case ClassAnimatorOverrideController:
                            retargeter.AnimatorOverrideController(bf, uacThisBuild);
                            break;
                        case ClassMonoScript:
                            retargeter.MonoScript(bf, _gameCodeAsm);
                            break;
                    }
                    info.SetNewData(bf);
                }

                RewriteExternals(afi, localCache, game, relPath, result);

                AssetRetargeter.SetSwitchPlatform(afi.file);
                dirInfos[idx].SetNewData(afi.file);
            }

            return result.Errors.Count > errorsBefore;
        }

        private void RewriteExternals(
            AssetsFileInstance afi, Dictionary<string, string> localCache,
            GameCache game, string relPath, PatchResult result)
        {
            foreach (var ext in afi.file.Metadata.Externals)
            {
                var src = GetExternalSource(ext.PathName);
                if (src.IsLibrary)
                    continue;

                if (!localCache.TryGetValue(src.Cab, out string internalId))
                {
                    result.Warnings.Add(
                        $"{relPath}: dependency CAB '{src.Cab}' isn't a project Addressable, left as-is. " +
                        "Make sure your materials only depend on shaders from the game.");
                    continue;
                }
                if (!game.ByInternalId.TryGetValue(internalId, out CacheEntry ce))
                {
                    result.Warnings.Add(
                        $"{relPath}: original file '{internalId}' detected, do not place it in the Atmosphere RomFS.");
                    continue;
                }
                ext.PathName = ext.PathName.Replace(src.Cab, ce.cab);
            }
        }

        private void WriteBundle(BundleFileInstance bun, string projectRoot, string outDir, string srcPath)
        {
            string rel = RelativePath(projectRoot, srcPath);
            string outPath = Path.Combine(outDir, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(outPath));

            byte[] uncompressed;
            using (var ms = new MemoryStream())
            {
                using (var w = new AssetsFileWriter(ms))
                    bun.file.Write(w, 0);
                uncompressed = ms.ToArray();
            }

            var repacked = new AssetBundleFile();
            repacked.Read(new AssetsFileReader(new MemoryStream(uncompressed)));
            using (var w = new AssetsFileWriter(File.Open(outPath, FileMode.Create)))
                repacked.Pack(w, AssetBundleCompressionType.LZ4, false, null);
            repacked.Close();
        }

        private IEnumerable<AssetsFileInstance> AssetsFilesIn(AssetsManager am, BundleFileInstance bun)
        {
            var dirInfos = bun.file.BlockAndDirInfo.DirectoryInfos;
            for (int idx = 0; idx < dirInfos.Count; idx++)
            {
                if (bun.file.IsAssetsFile(idx))
                    yield return am.LoadAssetsFileFromBundle(bun, idx, false);
            }
        }

        private static ExternalSource GetExternalSource(string pathName)
        {
            if (string.IsNullOrEmpty(pathName) || pathName.StartsWith("Library"))
                return ExternalSource.Library;
            if (pathName.Length >= 45)
                return ExternalSource.Archive(pathName.Substring(9, 36));
            return ExternalSource.Library;
        }

        private static string RelativePath(string projectRoot, string path)
        {
            string full = Path.GetFullPath(path);
            string root = Path.GetFullPath(projectRoot);
            return full.Substring(root.Length).TrimStart('/', '\\');
        }

        private static void Report(IProgress<PatchProgress> progress, string phase, int current, int total, string name)
        {
            progress?.Report(new PatchProgress { Phase = phase, Current = current, Total = total, BundleName = name });
        }
    }
}
