using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;

namespace DivineDragon.PreFlightCheck
{
    public class AddressableShaderRule : BuildRule
    {
        public override string Name => "Addressable Shader Check";
        public override string Description => "Ensures materials on SkinnedMeshRenderers use shaders that are addressable";
        public override IssueSeverity DefaultSeverity => IssueSeverity.Error;
        public override bool CanAutoFix => false;

        private HashSet<Shader> addressableShaders;

        public AddressableShaderRule()
        {
            CacheAddressableShaders();
        }

        private void CacheAddressableShaders()
        {
            addressableShaders = new HashSet<Shader>();
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            
            if (settings == null) return;

            foreach (var group in settings.groups)
            {
                if (group == null) continue;
                
                foreach (var entry in group.entries)
                {
                    if (entry == null) continue;
                    
                    string assetPath = AssetDatabase.GUIDToAssetPath(entry.guid);
                    
                    // Check if it's a shader file
                    if (assetPath.EndsWith(".shader"))
                    {
                        Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(assetPath);
                        if (shader != null)
                        {
                            addressableShaders.Add(shader);
                        }
                    }
                }
            }
            
            Debug.Log($"Found {addressableShaders.Count} addressable shaders");
        }

        public override bool AppliesTo(string assetPath, Object asset)
        {
            // Check prefabs that have SkinnedMeshRenderers
            if (!assetPath.EndsWith(".prefab"))
                return false;
            
            if (asset is GameObject prefab)
            {
                return prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length > 0;
            }
            
            return false;
        }

        public override List<BuildIssue> Validate(string assetPath, Object asset)
        {
            var issues = new List<BuildIssue>();
            
            if (asset is GameObject prefab)
            {
                var skinnedMeshRenderers = prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                
                foreach (var renderer in skinnedMeshRenderers)
                {
                    if (renderer.sharedMaterials != null)
                    {
                        for (int i = 0; i < renderer.sharedMaterials.Length; i++)
                        {
                            Material material = renderer.sharedMaterials[i];
                            if (material == null) continue;
                            
                            Shader shader = material.shader;
                            if (shader == null) continue;
                            
                            // Check if the shader is addressable
                            if (!addressableShaders.Contains(shader))
                            {
                                var issue = new BuildIssue(
                                    assetPath,
                                    $"Material '{material.name}' on SkinnedMeshRenderer '{renderer.name}' uses shader '{shader.name}' which is not addressable. " +
                                    "This will cause a build error. " +
                                    $"Make sure to use a shader from the game and mark the shader as addressable.",
                                    prefab,
                                    DefaultSeverity,
                                    this,
                                    renderer // Pass the specific component
                                );
                                issues.Add(issue);
                            }
                        }
                    }
                }
            }
            
            return issues;
        }

        public override bool AutoFix(BuildIssue issue)
        {
            // Can't auto-fix - requires making shaders addressable which is a project setup decision
            return false;
        }
    }
}