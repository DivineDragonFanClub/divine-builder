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

            bool inExpectedLocation = AccPathUtility.IsInExpectedLocation(info);

            if (!info.IsFileNameCanonical)
            {
                issues.Add(new BuildIssue(
                    assetPath,
                    $"Accessory prefab filename should be '{info.CanonicalFileName}'.",
                    asset,
                    IssueSeverity.Error,
                    this));
            }

            if (!inExpectedLocation)
            {
                issues.Add(new BuildIssue(
                    assetPath,
                    $"Accessory prefab should reside at '{info.ExpectedAssetPath}'.",
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

            if (!inExpectedLocation)
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

            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings != null)
            {
                string guid = AssetDatabase.AssetPathToGUID(assetPath);
                var entry = settings.FindAssetEntry(guid);

                if (entry == null)
                {
                    issues.Add(new BuildIssue(
                        assetPath,
                        $"Prefab is not addressable. Expected address: '{info.ExpectedAddressablePath}'.",
                        asset,
                        IssueSeverity.Error,
                        this));
                }
                else if (!string.Equals(entry.address, info.ExpectedAddressablePath))
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
