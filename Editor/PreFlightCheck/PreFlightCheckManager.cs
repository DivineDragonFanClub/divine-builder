using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;
using Stopwatch = System.Diagnostics.Stopwatch;
#if UNITY_2021_2_OR_NEWER
using PrefabStageUtility = UnityEditor.SceneManagement.PrefabStageUtility;
#else
using PrefabStageUtility = UnityEditor.Experimental.SceneManagement.PrefabStageUtility;
#endif

namespace DivineDragon.PreFlightCheck
{
    public static class PreFlightCheckManager
    {
        private const int MaxIterations = 10;

        /// <summary>Seconds the most recent completed check run took. 0 until a run completes.</summary>
        public static double LastRunSeconds { get; private set; }

        /// <summary>Addressable assets the most recent completed check run validated.</summary>
        public static int LastRunAssetCount { get; private set; }

        /// <summary>
        /// Fires after every completed check run with the fresh issue list. Guarded runs
        /// (play mode, no rules, no addressable settings) don't fire it.
        /// </summary>
        public static event Action<List<BuildIssue>> ChecksCompleted;

        // Markers for rules that validate something project-wide instead of a single addressable.
        private static readonly string[] SpecialCheckMarkers = { "SCENE_CHECK", "ADDRESSABLE_PATH_CHECK" };

        /// <summary>
        /// Runs every enabled rule and reports what it finds. Never mutates assets;
        /// fixing happens explicitly through <see cref="AutoFixAll"/>.
        /// </summary>
        public static List<BuildIssue> RunAllChecks()
        {
            // Timed so we can judge whether validation is cheap enough to run continuously.
            // Guarded early returns below don't count as a run and leave the stats untouched.
            var stopwatch = Stopwatch.StartNew();

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
            int assetsChecked = 0;

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

                    assetsChecked++;

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

            LastRunSeconds = stopwatch.Elapsed.TotalSeconds;
            LastRunAssetCount = assetsChecked;

            // No log line here: checks run automatically while a window is watching, and the
            // results (including the run cost) are shown in the UI instead.
            ChecksCompleted?.Invoke(allIssues);
            return allIssues;
        }

        public static string FormatDuration(double seconds)
        {
            return seconds >= 1 ? $"{seconds:0.0} s" : $"{seconds * 1000:0} ms";
        }

        /// <summary>
        /// Whether there are unsaved edits that saving would actually reveal to the checks:
        /// a dirty prefab open in prefab mode (checks read the saved asset, not the stage),
        /// or a dirty open scene. Material and other asset edits are read live, so they don't
        /// count here - a plain re-check already sees them.
        /// </summary>
        public static bool HasSavableEdits()
        {
            if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
                return false;

            var stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage != null && stage.scene.isDirty)
                return true;

            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                if (SceneManager.GetSceneAt(i).isDirty)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Flushes unsaved editor state to disk so the checks see it: the prefab open in
        /// prefab mode, open scenes, and dirty assets. Checks read saved asset state, so
        /// prefab-mode edits are otherwise invisible until saved. No-op in play mode.
        /// Returns true if anything was written.
        /// </summary>
        public static bool SavePendingEdits()
        {
            if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
                return false;

            bool savedAnything = false;

            // A prefab being edited in prefab mode isn't written to its asset until saved,
            // and SaveAssets/SaveOpenScenes don't touch it - persist it explicitly.
            try
            {
                var stage = PrefabStageUtility.GetCurrentPrefabStage();
                if (stage != null && stage.scene.isDirty)
                {
                    PrefabUtility.SaveAsPrefabAsset(stage.prefabContentsRoot, stage.assetPath);
                    savedAnything = true;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Divine Builder: couldn't save the open prefab before checking: {ex.Message}");
            }

            if (EditorSceneManager.SaveOpenScenes())
                savedAnything = true;

            AssetDatabase.SaveAssets();
            return savedAnything;
        }

        // The three build-time behaviors, counted for badges and summaries: blocking issues
        // cancel the build, attention issues ride along as warnings, fixable ones are
        // cleared by Autofix. Info is none of those.
        public struct TierCounts
        {
            public int Blocking;      // error severity, no autofix
            public int Attention;     // warning severity, no autofix
            public int Fixable;       // anything the rule can autofix
            public int FixableErrors; // the subset of Fixable that blocks when Autofix is off
            public int Info;
        }

        public static TierCounts CountTiers(List<BuildIssue> issues)
        {
            var counts = new TierCounts();
            foreach (var issue in issues)
            {
                if (issue.Rule.CanAutoFix)
                {
                    counts.Fixable++;
                    if (issue.Severity == IssueSeverity.Error)
                        counts.FixableErrors++;
                }
                else if (issue.Severity == IssueSeverity.Error)
                    counts.Blocking++;
                else if (issue.Severity == IssueSeverity.Warning)
                    counts.Attention++;
                else
                    counts.Info++;
            }
            return counts;
        }

        /// <summary>
        /// How many issues the build gate would refuse over, given the Autofix setting:
        /// fixable errors stop being blockers when Autofix will repair them first.
        /// </summary>
        public static int WillBlockCount(TierCounts counts, bool autofixEnabled)
        {
            return counts.Blocking + (autofixEnabled ? 0 : counts.FixableErrors);
        }

        public static string SummarizeTiers(TierCounts counts, bool autofixEnabled)
        {
            int willBlock = WillBlockCount(counts, autofixEnabled);
            var parts = new List<string>();

            if (willBlock > 0)
            {
                parts.Add($"{willBlock} fatal issue{(willBlock == 1 ? "" : "s")}");
                if (!autofixEnabled && counts.FixableErrors > 0)
                {
                    parts.Add(counts.FixableErrors == willBlock
                        ? "enable Autofix to fix them"
                        : $"enable Autofix to fix {counts.FixableErrors} of them");
                }
            }

            if (counts.Attention > 0)
            {
                parts.Add($"{counts.Attention} issue{(counts.Attention == 1 ? "" : "s")} " +
                          $"need{(counts.Attention == 1 ? "s" : "")} attention");
            }

            if (autofixEnabled)
            {
                if (counts.Fixable > 0)
                    parts.Add($"{counts.Fixable} issue{(counts.Fixable == 1 ? "" : "s")} will be autofixed on build");
            }
            else
            {
                // Fixable errors were already covered by the "enable Autofix" hint above.
                int fixableWarnings = counts.Fixable - counts.FixableErrors;
                if (fixableWarnings > 0)
                    parts.Add($"{fixableWarnings} issue{(fixableWarnings == 1 ? "" : "s")} can be autofixed");
            }

            if (counts.Info > 0)
                parts.Add($"{counts.Info} info");

            return parts.Count == 0 ? "No issues found" : string.Join(" · ", parts);
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
