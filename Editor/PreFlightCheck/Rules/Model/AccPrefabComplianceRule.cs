using System.Collections.Generic;
using System.IO;
using DivineDragon.PreFlightCheck;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;
using Object = UnityEngine.Object;

namespace DivineDragon.PreFlightCheck.Rules
{
    public class AccPrefabComplianceRule : BuildRule
    {
        public override string Name => "Accessory Prefab Compliance";
        public override string Description => "Ensures uAcc/oAcc prefabs follow the canonical naming: <accType>_<locator>_<id>.prefab";
        public override IssueSeverity DefaultSeverity => IssueSeverity.Error;
        public override bool CanAutoFix => true;

        public override bool AppliesTo(string assetPath, Object asset)
        {
            if (string.IsNullOrEmpty(assetPath) || !assetPath.EndsWith(".prefab"))
                return false;

            if (!assetPath.StartsWith("Assets/Share/Addressables/"))
                return false;

            return assetPath.Contains("/Item/Acc/uAcc/") || assetPath.Contains("/Item/Acc/oAcc/");
        }

        public override List<BuildIssue> Validate(string assetPath, Object asset)
        {
            var issues = new List<BuildIssue>();

            if (!AccPathUtility.TryGetInfo(assetPath, out var info))
            {
                issues.Add(new BuildIssue(
                    assetPath,
                    "Accessory prefab name or location does not match the required pattern 'Assets/Share/Addressables/Item/Acc/<accType>/<locator>/<id>/Prefabs/<accType>_<locator>_<id>.prefab'.",
                    asset,
                    IssueSeverity.Error,
                    this));
                return issues;
            }

            bool inExpectedDirectory = AccPathUtility.IsInExpectedDirectory(info);
            bool needsRelocation = !inExpectedDirectory || !info.IsFileNameCanonical;

            var settings = AddressableAssetSettingsDefaultObject.Settings;
            AddressableAssetEntry entry = null;
            if (settings != null)
            {
                string guid = AssetDatabase.AssetPathToGUID(assetPath);
                entry = settings.FindAssetEntry(guid);
            }

            // An address that is just the auto-derived form of the current wrong path is a
            // consequence of the misnaming, so it rides along on the path issue below instead
            // of getting a row of its own. Missing or hand-edited addresses stay separate.
            bool addressMatches = entry != null && string.Equals(entry.address, info.ExpectedAddressablePath);
            bool addressFollowsPath = entry != null && !addressMatches && needsRelocation
                && AccPathUtility.IsAddressDerivedFromPath(entry.address, assetPath);
            string addressNote = addressFollowsPath
                ? $" The addressable address is derived from this path and will be corrected to '{info.ExpectedAddressablePath}' by the same fix."
                : string.Empty;

            if (!info.IsFileNameCanonical)
            {
                issues.Add(new BuildIssue(
                    assetPath,
                    $"Accessory prefab filename should be '{info.CanonicalFileName}'.{addressNote}",
                    asset,
                    IssueSeverity.Error,
                    this));
                addressNote = string.Empty;
            }

            if (!inExpectedDirectory)
            {
                issues.Add(new BuildIssue(
                    assetPath,
                    $"Accessory prefab should reside in folder '{info.ExpectedDirectoryPath}'.{addressNote}",
                    asset,
                    IssueSeverity.Error,
                    this));
            }

            var prefabPaths = AccPathUtility.GetPrefabPathsInCanonicalFolder(info);
            bool isCanonicalAsset = string.Equals(
                assetPath,
                info.ExpectedAssetPath,
                System.StringComparison.OrdinalIgnoreCase);

            foreach (var otherPath in prefabPaths)
            {
                if (string.Equals(otherPath, assetPath, System.StringComparison.OrdinalIgnoreCase))
                    continue;
                if (string.Equals(otherPath, info.ExpectedAssetPath, System.StringComparison.OrdinalIgnoreCase) && isCanonicalAsset)
                    continue;

                var duplicateAsset = AssetDatabase.LoadAssetAtPath<Object>(otherPath);
                string duplicateName = Path.GetFileName(otherPath);
                issues.Add(new BuildIssue(
                    otherPath,
                    $"Found additional prefab '{duplicateName}' in {info.AccType}/{info.Locator}/{info.Id}/Prefabs. Only one prefab should exist for this accessory.",
                    duplicateAsset,
                    IssueSeverity.Error,
                    this));
            }

            if (needsRelocation)
            {
                foreach (var otherPath in prefabPaths)
                {
                    if (string.Equals(otherPath, assetPath, System.StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (string.Equals(otherPath, info.ExpectedAssetPath, System.StringComparison.OrdinalIgnoreCase) && isCanonicalAsset)
                        continue;

                    issues.Add(new BuildIssue(
                        assetPath,
                        $"Cannot move accessory prefab to canonical location because '{Path.GetFileName(otherPath)}' already exists there. Remove or relocate duplicates first.",
                        asset,
                        IssueSeverity.Error,
                        this));
                    break;
                }
            }

            if (settings != null)
            {
                if (entry == null)
                {
                    issues.Add(new BuildIssue(
                        assetPath,
                        $"Prefab is not addressable. Expected address: '{info.ExpectedAddressablePath}'.",
                        asset,
                        IssueSeverity.Error,
                        this));
                }
                else if (!addressMatches && !addressFollowsPath)
                {
                    issues.Add(new BuildIssue(
                        assetPath,
                        $"Addressable path mismatch. Current: '{entry.address}', Expected: '{info.ExpectedAddressablePath}'.",
                        asset,
                        IssueSeverity.Error,
                        this));
                }
            }

            return issues;
        }

        public override bool AutoFix(BuildIssue issue)
        {
            if (issue.Asset == null)
                return false;

            string assetPath = AssetDatabase.GetAssetPath(issue.Asset);
            if (!AccPathUtility.TryGetInfo(assetPath, out var info))
                return false;

            var siblingPrefabs = AccPathUtility.GetPrefabPathsInCanonicalFolder(info);
            foreach (var sibling in siblingPrefabs)
            {
                if (string.Equals(sibling, info.ExpectedAssetPath, System.StringComparison.OrdinalIgnoreCase))
                    continue;
                if (string.Equals(sibling, assetPath, System.StringComparison.OrdinalIgnoreCase))
                    continue;

                Debug.LogWarning($"Accessory Prefab Compliance: Multiple prefabs detected in {info.AccType}/{info.Locator}/{info.Id}/Prefabs. Resolve duplicates before applying fixes.");
                return false;
            }

            if (!AccPathUtility.IsInExpectedLocation(info))
            {
                if (!AccPathUtility.EnsureCanonicalLocation(info))
                    return false;

                assetPath = info.ExpectedAssetPath;
                if (!AccPathUtility.TryGetInfo(assetPath, out info))
                    return false;
            }

            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
                return false;

            string guid = AssetDatabase.AssetPathToGUID(assetPath);
            if (string.IsNullOrEmpty(guid))
                return false;

            var group = FindTargetGroup(settings);
            if (group == null)
            {
                Debug.LogError("Accessory Prefab Compliance: 'fe' group not found.");
                return false;
            }

            var entry = settings.FindAssetEntry(guid) ?? settings.CreateOrMoveEntry(guid, group, false, false);
            if (entry == null)
                return false;

            if (entry.parentGroup != group)
            {
                entry = settings.CreateOrMoveEntry(guid, group, false, false);
            }

            bool changed = false;
            if (entry.address != info.ExpectedAddressablePath)
            {
                entry.address = info.ExpectedAddressablePath;
                changed = true;
            }

            if (changed)
            {
                settings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryModified, entry, true);
                AssetDatabase.SaveAssets();
            }

            return true;
        }

        private static AddressableAssetGroup FindTargetGroup(AddressableAssetSettings settings)
        {
            foreach (var group in settings.groups)
            {
                if (group != null && group.name == "fe")
                    return group;
            }

            return null;
        }
    }
}
