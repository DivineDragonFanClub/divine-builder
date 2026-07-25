using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using DivineDragon.PreFlightCheck;

namespace DivineDragon.PreFlightCheck.Rules
{
    internal static class ModelAutofixMenu
    {
        private readonly struct PrefabSelection
        {
            public PrefabSelection(string assetPath, GameObject prefab)
            {
                AssetPath = assetPath;
                Prefab = prefab;
            }

            public string AssetPath { get; }
            public GameObject Prefab { get; }
        }

        [MenuItem("Divine Dragon/Utilities/Models/Fix oBody Avatars", false, 2010)]
        private static void FixOBodyAvatars()
        {
            RunSelectedAutofix(() => new OBodyAvatarRule(), "Fix oBody Avatars");
        }

        [MenuItem("Divine Dragon/Utilities/Models/Fix oBody Avatars", true, 2010)]
        private static bool FixOBodyAvatarsValidate()
        {
            return HasApplicableSelection(() => new OBodyAvatarRule());
        }

        [MenuItem("Divine Dragon/Utilities/Models/Enable Update When Offscreen", false, 2011)]
        private static void EnableUpdateWhenOffscreen()
        {
            RunSelectedAutofix(() => new SkinnedMeshRendererRule(), "Enable Update When Offscreen");
        }

        [MenuItem("Divine Dragon/Utilities/Models/Enable Update When Offscreen", true, 2011)]
        private static bool EnableUpdateWhenOffscreenValidate()
        {
            return HasApplicableSelection(() => new SkinnedMeshRendererRule());
        }

        private static void RunSelectedAutofix(Func<BuildRule> ruleFactory, string operationName)
        {
            var selections = GetSelectedPrefabs().ToList();
            if (selections.Count == 0)
            {
                EditorUtility.DisplayDialog(operationName, "Select one or more prefabs to run this fix.", "OK");
                return;
            }

            var rule = ruleFactory();
            int attempted = 0;
            int succeeded = 0;
            bool anyChanges = false;

            foreach (var selection in selections)
            {
                if (selection.Prefab == null)
                    continue;

                if (!rule.AppliesTo(selection.AssetPath, selection.Prefab))
                    continue;

                var issues = rule.Validate(selection.AssetPath, selection.Prefab);
                var fixableIssues = issues.Where(i => i.Rule.CanAutoFix).ToList();
                if (fixableIssues.Count == 0)
                    continue;

                attempted++;

                bool fixedThisPrefab = false;
                foreach (var issue in fixableIssues)
                {
                    if (rule.AutoFix(issue))
                    {
                        fixedThisPrefab = true;
                        break;
                    }
                }

                if (fixedThisPrefab)
                {
                    succeeded++;
                    anyChanges = true;
                    Debug.Log($"[{operationName}] Fixed prefab: {selection.AssetPath}");
                }
                else
                {
                    Debug.LogWarning($"[{operationName}] Could not fix prefab: {selection.AssetPath}. Check the asset setup and try again.");
                }
            }

            if (anyChanges)
            {
                AssetDatabase.Refresh();
            }

            string message;
            if (attempted == 0)
            {
                message = "No applicable prefabs were found in the current selection.";
            }
            else if (succeeded == 0)
            {
                message = "The fix could not be applied to the selected prefabs. See the Console for details.";
            }
            else if (succeeded == attempted)
            {
                message = $"Successfully fixed {succeeded} prefab(s).";
            }
            else
            {
                message = $"Partially completed: fixed {succeeded} of {attempted} applicable prefab(s).";
            }

            EditorUtility.DisplayDialog(operationName, message, "OK");
        }

        private static bool HasApplicableSelection(Func<BuildRule> ruleFactory)
        {
            var rule = ruleFactory();

            foreach (var selection in GetSelectedPrefabs())
            {
                if (selection.Prefab == null)
                    continue;

                if (rule.AppliesTo(selection.AssetPath, selection.Prefab))
                {
                    var issues = rule.Validate(selection.AssetPath, selection.Prefab);
                    if (issues.Any(i => i.Rule.CanAutoFix))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static IEnumerable<PrefabSelection> GetSelectedPrefabs()
        {
            var processedPaths = new HashSet<string>();

            foreach (var obj in Selection.objects)
            {
                if (obj == null)
                    continue;

                GameObject candidate = null;

                if (obj is GameObject go)
                {
                    candidate = go;
                }
                else if (obj is Component component)
                {
                    candidate = component.gameObject;
                }

                if (candidate == null)
                    continue;

                string assetPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(candidate);
                if (string.IsNullOrEmpty(assetPath))
                {
                    assetPath = AssetDatabase.GetAssetPath(candidate);
                }

                if (string.IsNullOrEmpty(assetPath) || !assetPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!processedPaths.Add(assetPath))
                    continue;

                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
                if (prefab != null)
                {
                    yield return new PrefabSelection(assetPath, prefab);
                }
            }
        }
    }
}
