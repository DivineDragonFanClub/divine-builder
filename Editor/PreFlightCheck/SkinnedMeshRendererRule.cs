using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace DivineDragon.PreFlightCheck
{
    public class SkinnedMeshRendererRule : BuildRule
    {
        public override string Name => "SkinnedMeshRenderer Update When Offscreen";
        public override string Description => "Ensures all SkinnedMeshRenderers in uBody prefabs have 'Update When Offscreen' enabled";
        public override IssueSeverity DefaultSeverity => IssueSeverity.Warning;
        public override bool CanAutoFix => true;

        public override bool AppliesTo(string assetPath, Object asset)
        {
            // Only check prefabs that are in a uBody path
            if (!assetPath.Contains("uBody") || !assetPath.EndsWith(".prefab"))
                return false;
            
            return asset is GameObject;
        }

        public override List<BuildIssue> Validate(string assetPath, Object asset)
        {
            var issues = new List<BuildIssue>();
            
            if (asset is GameObject prefab)
            {
                // Get all SkinnedMeshRenderers in the prefab and its children
                var skinnedMeshRenderers = prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                
                foreach (var renderer in skinnedMeshRenderers)
                {
                    if (renderer.updateWhenOffscreen == false)
                    {
                        var issue = new BuildIssue(
                            assetPath,
                            $"SkinnedMeshRenderer on '{renderer.name}' does not have 'Update When Offscreen' enabled",
                            prefab,
                            DefaultSeverity,
                            this,
                            renderer // Pass the specific component
                        );
                        issues.Add(issue);
                    }
                }
            }
            
            return issues;
        }

        public override bool AutoFix(BuildIssue issue)
        {
            if (issue.Asset is GameObject prefab)
            {
                // Load the prefab for editing
                string assetPath = AssetDatabase.GetAssetPath(prefab);
                GameObject prefabRoot = PrefabUtility.LoadPrefabContents(assetPath);
                
                try
                {
                    bool modified = false;
                    var skinnedMeshRenderers = prefabRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                    
                    foreach (var renderer in skinnedMeshRenderers)
                    {
                        if (renderer.updateWhenOffscreen == false)
                        {
                            renderer.updateWhenOffscreen = true;
                            modified = true;
                        }
                    }
                    
                    if (modified)
                    {
                        PrefabUtility.SaveAsPrefabAsset(prefabRoot, assetPath);
                        Debug.Log($"Fixed SkinnedMeshRenderer settings in: {assetPath}");
                    }
                    
                    return true;
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(prefabRoot);
                }
            }
            
            return false;
        }
    }
}