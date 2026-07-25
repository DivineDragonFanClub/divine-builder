using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEngine.SceneManagement;
using UnityEditor.SceneManagement;
using Object = UnityEngine.Object;

namespace DivineDragon.PreFlightCheck.Rules
{
    public class PrefabOverridesInScenesRule : BuildRule
    {
        public override string Name => "Prefab Overrides in Scenes";
        public override string Description => "Checks for prefab instances with unapplied overrides in the open scenes";
        // Info, not a blocking error: leaving overrides unapplied is a legitimate choice
        // (the built bundle ships the prefab asset anyway). Apply/Revert are offered as
        // manual actions for when you do want to reconcile them.
        public override IssueSeverity DefaultSeverity => IssueSeverity.Info;
        public override bool CanAutoFix => false; // Can't auto-fix as it requires user decision on what to apply

        private HashSet<string> addressablePrefabPaths;

        public PrefabOverridesInScenesRule()
        {
            // Cache all addressable prefab paths for faster lookup
            CacheAddressablePrefabPaths();
        }

        private void CacheAddressablePrefabPaths()
        {
            addressablePrefabPaths = new HashSet<string>();
            var settings = UnityEditor.AddressableAssets.AddressableAssetSettingsDefaultObject.Settings;
            
            if (settings == null) return;

            foreach (var group in settings.groups)
            {
                if (group == null) continue;
                
                foreach (var entry in group.entries)
                {
                    if (entry == null) continue;
                    
                    string assetPath = AssetDatabase.GUIDToAssetPath(entry.guid);
                    if (assetPath.EndsWith(".prefab"))
                    {
                        addressablePrefabPaths.Add(assetPath);
                    }
                }
            }
        }

        public override bool AppliesTo(string assetPath, Object asset)
        {
            // This rule runs once for the entire check, not per asset
            // We'll use a special marker to run it once
            return assetPath == "SCENE_CHECK" && asset == null;
        }

        public override List<BuildIssue> Validate(string assetPath, Object asset)
        {
            var issues = new List<BuildIssue>();

            if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return issues;
            }

            // Only look at scenes the user already has loaded. Checks run automatically
            // now, and opening other scenes from here (OpenSceneMode.Single) silently
            // discards unsaved work in the current scene - validation must never touch
            // editor state. Unapplied overrides only matter in the scene being worked
            // on anyway; the built bundle ships the prefab asset, not the instance.
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded || !IsProjectScene(scene.path))
                {
                    continue;
                }

                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    // Check all prefab instances in the hierarchy
                    CheckPrefabOverrides(root, scene.path, issues);
                }
            }

            return issues;
        }

        private bool IsProjectScene(string scenePath)
        {
            return scenePath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase);
        }

        private void CheckPrefabOverrides(GameObject gameObject, string scenePath, List<BuildIssue> issues)
        {
            // Check if this is a prefab instance root (not a child of another prefab instance)
            if (PrefabUtility.IsPartOfPrefabInstance(gameObject) && 
                PrefabUtility.IsOutermostPrefabInstanceRoot(gameObject))
            {
                // Get the prefab asset path
                GameObject prefabAsset = PrefabUtility.GetCorrespondingObjectFromSource(gameObject);
                if (prefabAsset != null)
                {
                    string prefabPath = AssetDatabase.GetAssetPath(prefabAsset);
                    
                    // Check if this prefab is in our addressables
                    if (addressablePrefabPaths.Contains(prefabPath))
                    {
                        // Check if there are any overrides on the entire prefab instance
                        bool hasOverrides = PrefabUtility.HasPrefabInstanceAnyOverrides(gameObject, false);

                        if (hasOverrides)
                        {
                            string sceneName = System.IO.Path.GetFileNameWithoutExtension(scenePath);
                            var instance = gameObject;
                            var issue = new BuildIssue(
                                scenePath,
                                $"Prefab '{gameObject.name}' in scene '{sceneName}' has unapplied overrides. " +
                                $"Apply them to the prefab if you want them reflected in the built bundles.",
                                gameObject,
                                DefaultSeverity,
                                this
                            )
                            {
                                Targets = new List<IssueTarget>
                                {
                                    new IssueTarget(instance.name, instance,
                                        tooltip: "Select this prefab instance in the scene"),
                                },
                                // Applying vs reverting is a human decision, so these are
                                // per-issue buttons and deliberately not an AutoFix.
                                Actions = new List<IssueAction>
                                {
                                    new IssueAction("Apply", () => ApplyOverrides(instance),
                                        "Apply all overrides on this instance to the prefab - same as Overrides ▸ Apply All. Undoable."),
                                    new IssueAction("Revert", () => RevertOverrides(instance),
                                        "Discard all overrides on this instance - same as Overrides ▸ Revert All. Undoable."),
                                }
                            };
                            issues.Add(issue);
                        }
                    }
                }
            }

            // Recursively check children only if this is not a prefab instance
            // (to avoid checking inside prefab instances)
            if (!PrefabUtility.IsPartOfPrefabInstance(gameObject))
            {
                foreach (Transform child in gameObject.transform)
                {
                    CheckPrefabOverrides(child.gameObject, scenePath, issues);
                }
            }
        }

        public override bool AutoFix(BuildIssue issue)
        {
            // Can't auto-fix as applying overrides requires user decision
            return false;
        }

        private static bool ApplyOverrides(GameObject instance)
        {
            if (instance == null)
            {
                Debug.LogWarning("Preflight: that prefab instance is no longer around. Hit Refresh and try again.");
                return false;
            }

            PrefabUtility.ApplyPrefabInstance(instance, InteractionMode.UserAction);
            return true;
        }

        private static bool RevertOverrides(GameObject instance)
        {
            if (instance == null)
            {
                Debug.LogWarning("Preflight: that prefab instance is no longer around. Hit Refresh and try again.");
                return false;
            }

            PrefabUtility.RevertPrefabInstance(instance, InteractionMode.UserAction);
            return true;
        }
    }
}
