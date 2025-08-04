using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEngine.SceneManagement;
using UnityEditor.SceneManagement;

namespace DivineDragon.PreFlightCheck
{
    public class PrefabOverridesInScenesRule : BuildRule
    {
        public override string Name => "Prefab Overrides in Scenes";
        public override string Description => "Checks for prefab instances with unsaved overrides in scenes";
        public override IssueSeverity DefaultSeverity => IssueSeverity.Warning;
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
            
            // Get all scenes in the project
            string[] scenePaths = AssetDatabase.FindAssets("t:Scene")
                .Select(guid => AssetDatabase.GUIDToAssetPath(guid))
                .ToArray();

            foreach (string scenePath in scenePaths)
            {
                // Open each scene
                Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
                
                // Get all root game objects in the scene
                GameObject[] rootObjects = scene.GetRootGameObjects();
                
                foreach (GameObject root in rootObjects)
                {
                    // Check all prefab instances in the hierarchy
                    CheckPrefabOverrides(root, scenePath, issues);
                }
            }

            return issues;
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
                            var issue = new BuildIssue(
                                scenePath,
                                $"Prefab '{gameObject.name}' in scene '{sceneName}' has unsaved overrides. " +
                                $"Remember to apply prefab overrides if you want them reflected in individual bundles.",
                                gameObject,
                                DefaultSeverity,
                                this
                            );
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
    }
}