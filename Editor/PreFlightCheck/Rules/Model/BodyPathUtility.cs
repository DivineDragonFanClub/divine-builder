using System;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;

namespace DivineDragon.PreFlightCheck.Rules
{
    internal static class BodyPathUtility
    {
        private const string AddressablesRoot = "Assets/Share/Addressables/Unit/Model";
        private const string AddressablesRelativeRoot = "Unit/Model";
        private static readonly Regex FileNameRegex = new Regex(@"^(?<BodyType>[ou]Body)_(?<Id>[^_]+)_(?<Variant>[^_]+)$", RegexOptions.Compiled);

        internal sealed class BodyInfo
        {
            public string BodyType;
            public string Id;
            public string Variant;
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

        internal static bool TryGetInfo(string assetPath, out BodyInfo info)
        {
            info = null;

            if (string.IsNullOrEmpty(assetPath) || !assetPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                return false;

            assetPath = assetPath.Replace('\\', '/');
            if (!assetPath.StartsWith(AddressablesRoot + "/", StringComparison.OrdinalIgnoreCase))
                return false;

            string relative = assetPath.Substring(AddressablesRoot.Length + 1);
            var segments = relative.Split('/');
            if (segments.Length < 4)
                return false;

            string fileName = Path.GetFileNameWithoutExtension(assetPath);
            var match = FileNameRegex.Match(fileName);
            string bodyType;
            string id;
            string variant;
            bool parsedFromFileName = match.Success;

            if (match.Success)
            {
                bodyType = match.Groups["BodyType"].Value;
                id = match.Groups["Id"].Value;
                variant = match.Groups["Variant"].Value;
            }
            else
            {
                bodyType = segments[0];
                id = segments[1];
                variant = segments[2];

                if (!string.Equals(segments[3], "Prefabs", StringComparison.OrdinalIgnoreCase))
                    return false;
            }

            if (!string.Equals(bodyType, "uBody", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(bodyType, "oBody", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string expectedDirectory = $"{AddressablesRoot}/{bodyType}/{id}/{variant}/Prefabs";
            string expectedFileName = $"{bodyType}_{id}_{variant}.prefab";

            info = new BodyInfo
            {
                BodyType = bodyType,
                Id = id,
                Variant = variant,
                AssetPath = assetPath,
                DirectoryPath = Path.GetDirectoryName(assetPath)?.Replace('\\', '/'),
                FileName = Path.GetFileName(assetPath),
                ExpectedDirectoryPath = expectedDirectory,
                ExpectedFileName = expectedFileName,
                ExpectedAssetPath = $"{expectedDirectory}/{expectedFileName}",
                ExpectedAddressablePath = $"{AddressablesRelativeRoot}/{bodyType}/{id}/{variant}/Prefabs/{bodyType}_{id}_{variant}",
                ParsedFromFileName = parsedFromFileName,
                PathSegments = segments,
                IsFileNameCanonical = string.Equals(Path.GetFileName(assetPath), expectedFileName, StringComparison.OrdinalIgnoreCase),
                CanonicalFileName = expectedFileName
            };

            return true;
        }

        internal static bool IsInExpectedLocation(BodyInfo info)
        {
            if (info == null) return false;
            var directoryMatches = string.Equals(info.DirectoryPath, info.ExpectedDirectoryPath, StringComparison.OrdinalIgnoreCase);
            var nameMatches = string.Equals(info.FileName, info.ExpectedFileName, StringComparison.OrdinalIgnoreCase);
            return directoryMatches && nameMatches;
        }

        internal static bool EnsureCanonicalLocation(BodyInfo info)
        {
            if (info == null)
                return false;

            if (IsInExpectedLocation(info))
                return true;

            EnsureDirectoryExists(info.ExpectedDirectoryPath);

            var existingAsset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(info.ExpectedAssetPath);
            if (existingAsset != null && AssetDatabase.GetAssetPath(existingAsset) != info.AssetPath)
            {
                UnityEngine.Debug.LogWarning($"BodyPathUtility: Cannot move prefab to '{info.ExpectedAssetPath}' because another asset already exists there.");
                return false;
            }

            if (!string.Equals(info.FileName, info.ExpectedFileName, System.StringComparison.OrdinalIgnoreCase))
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

            if (!string.Equals(info.DirectoryPath, info.ExpectedDirectoryPath, System.StringComparison.OrdinalIgnoreCase))
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

        internal static string[] GetPrefabPathsInCanonicalFolder(BodyInfo info)
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

        private static void UpdateInfoFromAssetPath(BodyInfo info, string assetPath)
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
