using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;

namespace DivineDragon.PreFlightCheck
{
    public static class PreFlightCheckManager
    {
        private static readonly List<BuildRule> Rules = new List<BuildRule>
        {
            new SkinnedMeshRendererRule(),
            new PrefabOverridesInScenesRule(),
            new AddressableShaderRule(),
            new OBodyAvatarRule()
        };

        public static List<BuildIssue> RunAllChecks()
        {
            var allIssues = new List<BuildIssue>();
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            
            if (settings == null)
            {
                Debug.LogError("Addressable settings not found. Cannot run pre-flight checks.");
                return allIssues;
            }

            // Iterate through all addressable groups and entries
            foreach (var group in settings.groups)
            {
                if (group == null) continue;
                
                foreach (var entry in group.entries)
                {
                    if (entry == null) continue;
                    
                    string assetPath = AssetDatabase.GUIDToAssetPath(entry.guid);
                    if (string.IsNullOrEmpty(assetPath)) continue;
                    
                    Object asset = AssetDatabase.LoadAssetAtPath<Object>(assetPath);
                    if (asset == null) continue;
                    
                    // Run each rule against this asset
                    foreach (var rule in Rules)
                    {
                        if (rule.AppliesTo(assetPath, asset))
                        {
                            var issues = rule.Validate(assetPath, asset);
                            allIssues.AddRange(issues);
                        }
                    }
                }
            }
            
            // Run special checks that don't iterate through addressables (like scene checks)
            foreach (var rule in Rules)
            {
                if (rule.AppliesTo("SCENE_CHECK", null))
                {
                    var issues = rule.Validate("SCENE_CHECK", null);
                    allIssues.AddRange(issues);
                }
            }
            
            Debug.Log($"Pre-flight check complete. Found {allIssues.Count} issues.");
            return allIssues;
        }

        public static bool HasErrors(List<BuildIssue> issues)
        {
            return issues.Any(issue => issue.Severity == IssueSeverity.Error);
        }

        public static bool HasWarnings(List<BuildIssue> issues)
        {
            return issues.Any(issue => issue.Severity == IssueSeverity.Warning);
        }

        public static int AutoFixAll(List<BuildIssue> issues)
        {
            int fixedCount = 0;
            var fixableIssues = issues.Where(issue => issue.Rule.CanAutoFix).ToList();
            
            // Group by asset to avoid fixing the same asset multiple times
            var issuesByAsset = fixableIssues.GroupBy(issue => issue.AssetPath);
            
            foreach (var assetGroup in issuesByAsset)
            {
                // Get the first issue for this asset (they should all have the same asset reference)
                var firstIssue = assetGroup.First();
                
                // Try to fix all issues for this asset at once
                if (firstIssue.Rule.AutoFix(firstIssue))
                {
                    fixedCount += assetGroup.Count();
                }
            }
            
            if (fixedCount > 0)
            {
                AssetDatabase.Refresh();
            }
            
            return fixedCount;
        }
    }
}