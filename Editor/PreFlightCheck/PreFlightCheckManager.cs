using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.AddressableAssets;
using Object = UnityEngine.Object;

namespace DivineDragon.PreFlightCheck
{
    public static class PreFlightCheckManager
    {
        private const int MaxIterations = 10;

        // Markers for rules that validate something project-wide instead of a single addressable.
        private static readonly string[] SpecialCheckMarkers = { "SCENE_CHECK", "ADDRESSABLE_PATH_CHECK" };

        /// <summary>
        /// Runs every enabled rule and reports what it finds. Never mutates assets;
        /// fixing happens explicitly through <see cref="AutoFixAll"/>.
        /// </summary>
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
                Debug.LogWarning("No pre-flight rules are registered and enabled. Skipping checks.");
                return new List<BuildIssue>();
            }

            var settings = AddressableAssetSettingsDefaultObject.Settings;

            if (settings == null)
            {
                Debug.LogError("Addressable settings not found. Cannot run pre-flight checks.");
                return new List<BuildIssue>();
            }

            var allIssues = new List<BuildIssue>();

            // Run the per-asset rules against every addressable entry.
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

                    foreach (var rule in activeRules)
                    {
                        if (!rule.AppliesTo(assetPath, asset))
                            continue;

                        try
                        {
                            allIssues.AddRange(rule.Validate(assetPath, asset));
                        }
                        catch (Exception ex)
                        {
                            Debug.LogError($"Pre-flight rule '{rule.Name}' threw an exception while validating '{assetPath}': {ex}");
                        }
                    }
                }
            }

            // Run the project-wide rules once each.
            foreach (var rule in activeRules)
            {
                foreach (var marker in SpecialCheckMarkers)
                {
                    if (!rule.AppliesTo(marker, null))
                        continue;

                    try
                    {
                        allIssues.AddRange(rule.Validate(marker, null));
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"Pre-flight rule '{rule.Name}' threw an exception during {marker}: {ex}");
                    }
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
    }
}
