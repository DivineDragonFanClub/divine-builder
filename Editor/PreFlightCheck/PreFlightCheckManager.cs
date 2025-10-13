using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using Object = UnityEngine.Object;

namespace DivineDragon.PreFlightCheck
{
    public static class PreFlightCheckManager
    {
        public static List<BuildIssue> RunAllChecks()
        {
            var activeRules = PreFlightRuleRegistry.CreateActiveRules();
            
            if (activeRules.Count == 0)
            {
                Debug.LogWarning("No pre-flight rules have been registered. Skipping checks.");
                return new List<BuildIssue>();
            }

            var allIssues = new List<BuildIssue>();
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            
            if (settings == null)
            {
                Debug.LogError("Addressable settings not found. Cannot run pre-flight checks.");
                return allIssues;
            }

            bool autoFixApplied = false;

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
                    foreach (var activeRule in activeRules)
                    {
                        var rule = activeRule.Rule;

                        if (rule.AppliesTo(assetPath, asset))
                        {
                            var issuesForRule = rule.Validate(assetPath, asset);

                            if (issuesForRule.Count > 0 && activeRule.AutoApply)
                            {
                                if (AttemptAutoApply(rule, issuesForRule))
                                {
                                    autoFixApplied = true;
                                    asset = AssetDatabase.LoadAssetAtPath<Object>(assetPath);
                                    issuesForRule = asset != null
                                        ? rule.Validate(assetPath, asset)
                                        : new List<BuildIssue>();
                                }
                            }
                            
                            allIssues.AddRange(issuesForRule);
                        }
                    }
                }
            }
            
            // Run special checks that don't iterate through addressables (like scene checks)
            foreach (var activeRule in activeRules)
            {
                var rule = activeRule.Rule;
                
                if (rule.AppliesTo("SCENE_CHECK", null))
                {
                    var issuesForRule = rule.Validate("SCENE_CHECK", null);

                    if (issuesForRule.Count > 0 && activeRule.AutoApply)
                    {
                        if (AttemptAutoApply(rule, issuesForRule))
                        {
                            autoFixApplied = true;
                            issuesForRule = rule.Validate("SCENE_CHECK", null);
                        }
                    }
                    
                    allIssues.AddRange(issuesForRule);
                }
            }
            
            if (autoFixApplied)
            {
                AssetDatabase.Refresh();
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

        private static bool AttemptAutoApply(BuildRule rule, List<BuildIssue> issues)
        {
            if (!rule.CanAutoFix || issues.Count == 0)
                return false;

            bool fixedSomething = false;

            foreach (var issue in issues)
            {
                try
                {
                    if (rule.AutoFix(issue))
                    {
                        fixedSomething = true;
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"Auto-apply failed for rule '{rule.Name}': {ex.Message}");
                }
            }

            return fixedSomething;
        }
    }
}
