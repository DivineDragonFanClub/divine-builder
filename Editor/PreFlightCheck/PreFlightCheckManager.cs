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
        private const int MaxIterations = 10;

        public static List<BuildIssue> RunAllChecks()
        {
            if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("Pre-flight checks are disabled while the Editor is in play mode.");
                return new List<BuildIssue>();
            }

            var activeRules = PreFlightRuleRegistry.CreateActiveRules();

            if (activeRules.Count == 0)
            {
                Debug.LogWarning("No pre-flight rules have been registered. Skipping checks.");
                return new List<BuildIssue>();
            }

            var settings = AddressableAssetSettingsDefaultObject.Settings;

            if (settings == null)
            {
                Debug.LogError("Addressable settings not found. Cannot run pre-flight checks.");
                return new List<BuildIssue>();
            }

            var allIssues = new List<BuildIssue>();
            int iteration = 0;
            bool autoFixApplied;

            do
            {
                iteration++;
                allIssues.Clear();
                autoFixApplied = false;

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

                            if (!rule.AppliesTo(assetPath, asset))
                                continue;

                            List<BuildIssue> issuesForRule;
                            try
                            {
                                issuesForRule = rule.Validate(assetPath, asset);
                            }
                            catch (System.Exception ex)
                            {
                                Debug.LogError($"Pre-flight rule '{rule.Name}' threw an exception while validating '{assetPath}': {ex}");
                                continue;
                            }

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

                // Run special checks that don't iterate through addressables (like scene checks and addressable path checks)
                foreach (var activeRule in activeRules)
                {
                    var rule = activeRule.Rule;

                    // Check for scene-specific rules
                    if (rule.AppliesTo("SCENE_CHECK", null))
                    {
                        List<BuildIssue> issuesForRule;
                        try
                        {
                            issuesForRule = rule.Validate("SCENE_CHECK", null);
                        }
                        catch (System.Exception ex)
                        {
                            Debug.LogError($"Pre-flight rule '{rule.Name}' threw an exception during scene check: {ex}");
                            continue;
                        }

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

                    // Check for addressable path validation rules
                    if (rule.AppliesTo("ADDRESSABLE_PATH_CHECK", null))
                    {
                        List<BuildIssue> issuesForRule;
                        try
                        {
                            issuesForRule = rule.Validate("ADDRESSABLE_PATH_CHECK", null);
                        }
                        catch (System.Exception ex)
                        {
                            Debug.LogError($"Pre-flight rule '{rule.Name}' threw an exception during addressable path check: {ex}");
                            continue;
                        }

                        if (issuesForRule.Count > 0 && activeRule.AutoApply)
                        {
                            if (AttemptAutoApply(rule, issuesForRule))
                            {
                                autoFixApplied = true;
                                issuesForRule = rule.Validate("ADDRESSABLE_PATH_CHECK", null);
                            }
                        }

                        allIssues.AddRange(issuesForRule);
                    }
                }

                if (autoFixApplied)
                {
                    AssetDatabase.Refresh();

                    if (iteration >= MaxIterations)
                    {
                        Debug.LogWarning($"Pre-flight check reached maximum iterations ({MaxIterations}). " +
                            "There may be conflicting rules causing fixes to cycle.");
                        break;
                    }

                    Debug.Log($"Pre-flight iteration {iteration}: Fixes applied, re-checking...");
                }

            } while (autoFixApplied);

            Debug.Log($"Pre-flight check complete after {iteration} pass(es). Found {allIssues.Count} issues.");
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
            int totalFixedCount = 0;

            for (int iteration = 1; iteration <= MaxIterations; iteration++)
            {
                int fixedThisPass = 0;
                var fixableIssues = issues.Where(issue => issue.Rule.CanAutoFix).ToList();

                if (fixableIssues.Count == 0)
                    break;

                // Group by asset to avoid fixing the same asset multiple times
                var issuesByAsset = fixableIssues.GroupBy(issue => issue.AssetPath);

                foreach (var assetGroup in issuesByAsset)
                {
                    // Get the first issue for this asset (they should all have the same asset reference)
                    var firstIssue = assetGroup.First();

                    // Try to fix all issues for this asset at once
                    if (firstIssue.Rule.AutoFix(firstIssue))
                    {
                        fixedThisPass += assetGroup.Count();
                    }
                }

                totalFixedCount += fixedThisPass;

                if (fixedThisPass == 0)
                    break;

                // Refresh and re-check for cascading issues
                AssetDatabase.Refresh();
                issues = RunAllChecks();

                if (iteration == MaxIterations)
                {
                    Debug.LogWarning($"AutoFixAll reached maximum iterations ({MaxIterations}). " +
                        "There may be conflicting rules causing fixes to cycle.");
                }
                else if (issues.Any(i => i.Rule.CanAutoFix))
                {
                    Debug.Log($"AutoFixAll iteration {iteration}: Fixed {fixedThisPass} issues, found more autofixable issues, continuing...");
                }
            }

            return totalFixedCount;
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
