using System;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;

namespace DivineDragon.PreFlightCheck.Rules
{
    internal static class AccPathUtility
    {
        private const string AddressablesRoot = "Assets/Share/Addressables/Item/Acc";
        private const string AddressablesRelativeRoot = "Item/Acc";
        private const string ShareAddressablesPrefix = "Assets/Share/Addressables/";
        private static readonly Regex FileNameRegex = new Regex(@"^(?<AccType>uAcc|oAcc)_(?<Locator>[^_]+)_(?<Id>.+)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // Valid locators based on game build analysis
        private static readonly string[] ValidLocators = { "bangle", "eff", "event", "gift", "head", "hip", "ring", "shield", "spine2" };

        internal sealed class AccInfo
        {
            public string AccType;  // "uAcc" or "oAcc"
            public string Locator;
            public string Id;
            public string AssetPath;
            public string DirectoryPath;
            public string FileName;
            public string ExpectedDirectoryPath;
            public string ExpectedFileName;
            public string ExpectedAssetPath;
            public string ExpectedAddressablePath;
            public bool ParsedFromFileName;
            public string[] PathSegments;
            public bool IsFileNameCanonical;
            public string CanonicalFileName;
        }

        internal static bool TryGetInfo(string assetPath, out AccInfo info)
        {
            info = null;

            if (string.IsNullOrEmpty(assetPath) || !assetPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                return false;

            assetPath = assetPath.Replace('\\', '/');
            if (!assetPath.StartsWith(AddressablesRoot + "/", StringComparison.OrdinalIgnoreCase))
                return false;

            // Must be in uAcc or oAcc subfolder
            if (!assetPath.Contains("/uAcc/") && !assetPath.Contains("/oAcc/"))
                return false;

            string relative = assetPath.Substring(AddressablesRoot.Length + 1);
            var segments = relative.Split('/');

            // Expected: <accType>/<locator>/<id>/Prefabs/<filename>.prefab
            // segments: [0]=uAcc|oAcc, [1]=locator, [2]=id, [3]=Prefabs, [4]=filename.prefab
            if (segments.Length < 5)
                return false;

            string accType = segments[0];
            if (!string.Equals(accType, "uAcc", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(accType, "oAcc", StringComparison.OrdinalIgnoreCase))
                return false;

            // Normalize case
            accType = accType.ToLowerInvariant() == "uacc" ? "uAcc" : "oAcc";

            if (!string.Equals(segments[3], "Prefabs", StringComparison.OrdinalIgnoreCase))
                return false;

            string fileName = Path.GetFileNameWithoutExtension(assetPath);
            var match = FileNameRegex.Match(fileName);
            string locator;
            string id;
            bool parsedFromFileName = match.Success;

            if (match.Success)
            {
                locator = match.Groups["Locator"].Value;
                id = match.Groups["Id"].Value;
            }
            else
            {
                // Parse from directory structure
                locator = segments[1];
                id = segments[2];
            }

            // Validate locator
            bool validLocator = false;
            foreach (var valid in ValidLocators)
            {
                if (string.Equals(locator, valid, StringComparison.OrdinalIgnoreCase))
                {
                    validLocator = true;
                    locator = valid; // normalize case
                    break;
                }
            }

            if (!validLocator)
                return false;

            string expectedDirectory = $"{AddressablesRoot}/{accType}/{locator}/{id}/Prefabs";
            string expectedFileName = $"{accType}_{locator}_{id}.prefab";

            info = new AccInfo
            {
                AccType = accType,
                Locator = locator,
                Id = id,
                AssetPath = assetPath,
                DirectoryPath = Path.GetDirectoryName(assetPath)?.Replace('\\', '/'),
                FileName = Path.GetFileName(assetPath),
                ExpectedDirectoryPath = expectedDirectory,
                ExpectedFileName = expectedFileName,
                ExpectedAssetPath = $"{expectedDirectory}/{expectedFileName}",
                ExpectedAddressablePath = $"{AddressablesRelativeRoot}/{accType}/{locator}/{id}/Prefabs/{accType}_{locator}_{id}",
                ParsedFromFileName = parsedFromFileName,
                PathSegments = segments,
                IsFileNameCanonical = string.Equals(Path.GetFileName(assetPath), expectedFileName, StringComparison.OrdinalIgnoreCase),
                CanonicalFileName = expectedFileName
            };

            return true;
        }

        internal static bool IsInExpectedLocation(AccInfo info)
        {
            if (info == null) return false;
            var directoryMatches = string.Equals(info.DirectoryPath, info.ExpectedDirectoryPath, StringComparison.OrdinalIgnoreCase);
            var nameMatches = string.Equals(info.FileName, info.ExpectedFileName, StringComparison.OrdinalIgnoreCase);
            return directoryMatches && nameMatches;
        }

        internal static bool IsInExpectedDirectory(AccInfo info)
        {
            if (info == null) return false;
            return string.Equals(info.DirectoryPath, info.ExpectedDirectoryPath, StringComparison.OrdinalIgnoreCase);
        }

        // True when the address is just an auto-derived form of the asset path: the raw
        // path Unity assigns when an asset is marked addressable, or that path with the
        // 'Assets/Share/Addressables/' prefix and/or the file extension stripped.
        internal static bool IsAddressDerivedFromPath(string address, string assetPath)
        {
            if (string.IsNullOrEmpty(address) || string.IsNullOrEmpty(assetPath))
                return false;

            assetPath = assetPath.Replace('\\', '/');
            if (AddressMatchesPathForm(address, assetPath))
                return true;

            return assetPath.StartsWith(ShareAddressablesPrefix, StringComparison.OrdinalIgnoreCase)
                   && AddressMatchesPathForm(address, assetPath.Substring(ShareAddressablesPrefix.Length));
        }

        private static bool AddressMatchesPathForm(string address, string path)
        {
            if (string.Equals(address, path, StringComparison.OrdinalIgnoreCase))
                return true;

            string extension = Path.GetExtension(path);
            if (string.IsNullOrEmpty(extension))
                return false;

            string withoutExtension = path.Substring(0, path.Length - extension.Length);
            return string.Equals(address, withoutExtension, StringComparison.OrdinalIgnoreCase);
        }

        internal static bool EnsureCanonicalLocation(AccInfo info)
        {
            if (info == null)
                return false;

            if (IsInExpectedLocation(info))
                return true;

            EnsureDirectoryExists(info.ExpectedDirectoryPath);

            var existingAsset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(info.ExpectedAssetPath);
            if (existingAsset != null && AssetDatabase.GetAssetPath(existingAsset) != info.AssetPath)
            {
                UnityEngine.Debug.LogWarning($"AccPathUtility: Cannot move prefab to '{info.ExpectedAssetPath}' because another asset already exists there.");
                return false;
            }

            if (!string.Equals(info.FileName, info.ExpectedFileName, StringComparison.OrdinalIgnoreCase))
            {
                var renameResult = AssetDatabase.RenameAsset(info.AssetPath, Path.GetFileNameWithoutExtension(info.ExpectedFileName));
                if (!string.IsNullOrEmpty(renameResult))
                {
                    UnityEngine.Debug.LogError($"Failed to rename prefab to canonical filename: {renameResult}");
                    return false;
                }

                var directory = info.DirectoryPath ?? string.Empty;
                var renamedPath = string.IsNullOrEmpty(directory)
                    ? info.ExpectedFileName
                    : $"{directory}/{info.ExpectedFileName}";
                UpdateInfoFromAssetPath(info, renamedPath);
            }

            if (!string.Equals(info.DirectoryPath, info.ExpectedDirectoryPath, StringComparison.OrdinalIgnoreCase))
            {
                string error = AssetDatabase.MoveAsset(info.AssetPath, info.ExpectedAssetPath);
                if (!string.IsNullOrEmpty(error))
                {
                    UnityEngine.Debug.LogError($"Failed to move prefab to canonical location: {error}");
                    return false;
                }
                UpdateInfoFromAssetPath(info, info.ExpectedAssetPath);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return true;
        }

        internal static string[] GetPrefabPathsInCanonicalFolder(AccInfo info)
        {
            if (info == null)
                return Array.Empty<string>();

            string folder = info.ExpectedDirectoryPath;
            if (!AssetDatabase.IsValidFolder(folder))
                return Array.Empty<string>();

            var guids = AssetDatabase.FindAssets("t:Prefab", new[] { folder });
            if (guids == null || guids.Length == 0)
                return Array.Empty<string>();

            var results = new string[guids.Length];
            for (int i = 0; i < guids.Length; i++)
            {
                results[i] = AssetDatabase.GUIDToAssetPath(guids[i]);
            }

            return results;
        }

        internal static void EnsureDirectoryExists(string directory)
        {
            if (string.IsNullOrEmpty(directory))
                return;

            directory = directory.Replace('\\', '/');
            if (AssetDatabase.IsValidFolder(directory))
                return;

            string[] parts = directory.Split('/');
            if (parts.Length == 0)
                return;

            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = $"{current}/{parts[i]}";
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[i]);
                }
                current = next;
            }
        }

        private static void UpdateInfoFromAssetPath(AccInfo info, string assetPath)
        {
            if (info == null || string.IsNullOrEmpty(assetPath))
                return;

            assetPath = assetPath.Replace('\\', '/');
            info.AssetPath = assetPath;
            info.DirectoryPath = Path.GetDirectoryName(assetPath)?.Replace('\\', '/');
            info.FileName = Path.GetFileName(assetPath);
            info.IsFileNameCanonical = string.Equals(info.FileName, info.ExpectedFileName, StringComparison.OrdinalIgnoreCase);

            if (assetPath.StartsWith(AddressablesRoot + "/", StringComparison.OrdinalIgnoreCase))
            {
                string relative = assetPath.Substring(AddressablesRoot.Length + 1);
                info.PathSegments = relative.Split('/');
            }
        }
    }
}
