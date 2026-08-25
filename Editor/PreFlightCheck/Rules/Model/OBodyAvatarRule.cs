using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;

namespace DivineDragon.PreFlightCheck.Rules
{
    public class OBodyAvatarRule : BuildRule
    {
        public override string Name => "oBody Avatar Check";
        public override string Description => "Ensures all oBody prefabs have an Avatar component for proper functionality";
        public override IssueSeverity DefaultSeverity => IssueSeverity.Error;
        public override bool CanAutoFix => true;

        public override bool AppliesTo(string assetPath, Object asset)
        {
            // Check if it's an oBody prefab
            if (!assetPath.EndsWith(".prefab"))
                return false;
            
            // Check if the path or name indicates it's an oBody
            if (assetPath.Contains("/oBody/") || assetPath.Contains("oBody_"))
            {
                return asset is GameObject;
            }
            
            return false;
        }

        public override List<BuildIssue> Validate(string assetPath, Object asset)
        {
            var issues = new List<BuildIssue>();
            
            if (asset is GameObject prefab)
            {
                // Check if the prefab has an Animator component
                var animator = prefab.GetComponent<Animator>();
                
                if (animator == null)
                {
                    issues.Add(new BuildIssue(
                        assetPath,
                        $"oBody prefab '{prefab.name}' is missing an Animator component. " +
                        $"oBody prefabs require an Animator component with a configured Avatar to function correctly.",
                        prefab,
                        DefaultSeverity,
                        this
                    ));
                }
                else if (animator.avatar == null)
                {
                    issues.Add(new BuildIssue(
                        assetPath,
                        $"oBody prefab '{prefab.name}' has an Animator component but no Avatar assigned. " +
                        $"Attempting to auto-configure the humanoid avatar from the source model.",
                        prefab,
                        DefaultSeverity,
                        this,
                        animator
                    ));
                }
            }
            
            return issues;
        }

        public override bool AutoFix(BuildIssue issue)
        {
            if (!(issue.Asset is GameObject prefabAsset))
                return false;

            string prefabPath = AssetDatabase.GetAssetPath(prefabAsset);
            if (string.IsNullOrEmpty(prefabPath))
                return false;

            var prefabRoot = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                var animator = prefabRoot.GetComponent<Animator>();
                if (animator == null)
                    return false;

                // If the avatar already exists the issue is resolved.
                if (animator.avatar != null && animator.avatar.isHuman)
                    return true;

                string modelPath = TryGetModelAssetPath(prefabRoot);
                if (string.IsNullOrEmpty(modelPath))
                    return false;

                var modelImporter = AssetImporter.GetAtPath(modelPath) as ModelImporter;
                if (modelImporter == null)
                    return false;

                bool importerModified = false;
                if (modelImporter.animationType != ModelImporterAnimationType.Human)
                {
                    modelImporter.animationType = ModelImporterAnimationType.Human;
                    importerModified = true;
                }

                if (modelImporter.avatarSetup != ModelImporterAvatarSetup.CreateFromThisModel)
                {
                    modelImporter.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                    importerModified = true;
                }

                if (importerModified)
                {
                    modelImporter.SaveAndReimport();
                }

                var avatar = FindHumanoidAvatar(modelPath);
                if (avatar == null)
                    return false;

                animator.avatar = avatar;
                EditorUtility.SetDirty(animator);
                PrefabUtility.SaveAsPrefabAsset(prefabRoot, prefabPath);
                return true;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(prefabRoot);
            }
        }

        private static string TryGetModelAssetPath(GameObject prefabRoot)
        {
            var skinnedMesh = prefabRoot.GetComponentInChildren<SkinnedMeshRenderer>(true);
            if (skinnedMesh?.sharedMesh == null)
                return null;

            string meshPath = AssetDatabase.GetAssetPath(skinnedMesh.sharedMesh);
            if (string.IsNullOrEmpty(meshPath))
                return null;

            // Mesh sub-assets live inside the FBX; we need the FBX path.
            if (System.IO.Path.GetExtension(meshPath).Equals(".fbx", System.StringComparison.OrdinalIgnoreCase))
            {
                return meshPath;
            }

            return null;
        }

        private static Avatar FindHumanoidAvatar(string modelPath)
        {
            return AssetDatabase.LoadAllAssetsAtPath(modelPath)
                .OfType<Avatar>()
                .FirstOrDefault(avatar => avatar != null && avatar.isHuman);
        }
    }
}
