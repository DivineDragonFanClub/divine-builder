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
        private const string ShareAddressablesPrefix = "Assets/Share/Addressables/";
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
            public bool IdentityFromFolders;
            public string NameBodyType;
            public string NameId;
            public string NameVariant;

            // True when the folders and the file name each name a different body (Id or
            // body type). Variant-only drift is not a conflict - the rename fix covers it.
            public bool NameConflictsWithFolders =>
                IdentityFromFolders && ParsedFromFileName &&
                (!string.Equals(NameId, Id, StringComparison.OrdinalIgnoreCase) ||
                 !string.Equals(NameBodyType, BodyType, StringComparison.OrdinalIgnoreCase));
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
            bool parsedFromFileName = match.Success;
            string nameBodyType = parsedFromFileName ? match.Groups["BodyType"].Value : null;
            string nameId = parsedFromFileName ? match.Groups["Id"].Value : null;
            string nameVariant = parsedFromFileName ? match.Groups["Variant"].Value : null;

            // The folder tree is the source of truth for the body's identity: dropping a
            // prefab into <bodyType>/<Id>/<Variant>/ is deliberate, while a stale file name
            // is what duplicating an existing variant leaves behind. The file name only
            // defines identity when the folders can't (path too shallow to have a variant
            // level, or 'Prefabs' sitting where the variant folder should be).
            string bodyType;
            string id;
            string variant;
            bool identityFromFolders = IsBodyTypeSegment(segments[0]) &&
                !string.Equals(segments[2], "Prefabs", StringComparison.OrdinalIgnoreCase);

            if (identityFromFolders)
            {
                bodyType = segments[0];
                id = segments[1];
                variant = segments[2];
            }
            else if (parsedFromFileName)
            {
                bodyType = nameBodyType;
                id = nameId;
                variant = nameVariant;
            }
            else
            {
                return false;
            }

            if (!IsBodyTypeSegment(bodyType))
                return false;

            string expectedDirectory = GetExpectedDirectoryPath(bodyType, id, variant);
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
                CanonicalFileName = expectedFileName,
                IdentityFromFolders = identityFromFolders,
                NameBodyType = nameBodyType,
                NameId = nameId,
                NameVariant = nameVariant
            };

            return true;
        }

        internal static string GetExpectedDirectoryPath(string bodyType, string id, string variant)
        {
            return $"{AddressablesRoot}/{bodyType}/{id}/{variant}/Prefabs";
        }

        private static bool IsBodyTypeSegment(string value)
        {
            return string.Equals(value, "uBody", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "oBody", StringComparison.OrdinalIgnoreCase);
        }

        internal static bool IsInExpectedLocation(BodyInfo info)
        {
            if (info == null) return false;
            var directoryMatches = string.Equals(info.DirectoryPath, info.ExpectedDirectoryPath, StringComparison.OrdinalIgnoreCase);
            var nameMatches = string.Equals(info.FileName, info.ExpectedFileName, StringComparison.OrdinalIgnoreCase);
            return directoryMatches && nameMatches;
        }

        internal static bool IsInExpectedDirectory(BodyInfo info)
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

        // Rename/move primitives for the judgment-call issue actions, where the human
        // already picked the resolution. EnsureCanonicalLocation stays the autofix path.
        internal static bool RenamePrefab(string assetPath, string targetFileName)
        {
            string error = AssetDatabase.RenameAsset(assetPath, Path.GetFileNameWithoutExtension(targetFileName));
            if (!string.IsNullOrEmpty(error))
            {
                UnityEngine.Debug.LogError($"Failed to rename prefab '{assetPath}': {error}");
                return false;
            }

            AssetDatabase.SaveAssets();
            return true;
        }

        internal static bool MovePrefab(string assetPath, string targetDirectory)
        {
            string targetPath = $"{targetDirectory}/{Path.GetFileName(assetPath)}";
            if (string.Equals(assetPath, targetPath, StringComparison.OrdinalIgnoreCase))
                return true;

            if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(targetPath) != null)
            {
                UnityEngine.Debug.LogWarning($"BodyPathUtility: Cannot move prefab to '{targetPath}' because another asset already exists there.");
                return false;
            }

            EnsureDirectoryExists(targetDirectory);
            string error = AssetDatabase.MoveAsset(assetPath, targetPath);
            if (!string.IsNullOrEmpty(error))
            {
                UnityEngine.Debug.LogError($"Failed to move prefab '{assetPath}': {error}");
                return false;
            }

            AssetDatabase.SaveAssets();
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
