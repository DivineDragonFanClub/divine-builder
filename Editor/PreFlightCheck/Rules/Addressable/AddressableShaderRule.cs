using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using Object = UnityEngine.Object;

namespace DivineDragon.PreFlightCheck.Rules
{
    public class AddressableShaderRule : BuildRule
    {
        public override string Name => "Addressable Shader Check";
        public override string Description => "Ensures materials on addressable assets use addressable shaders - built-in Unity shaders and materials don't exist in Engage";
        public override IssueSeverity DefaultSeverity => IssueSeverity.Error;
        public override bool CanAutoFix => false;

        private readonly HashSet<Shader> addressableShaders;
        private readonly HashSet<string> processedEntries;

        public AddressableShaderRule()
        {
            addressableShaders = new HashSet<Shader>();
            processedEntries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            CacheAddressableShaders();
        }

        private void CacheAddressableShaders()
        {
            addressableShaders.Clear();
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            
            if (settings == null) return;

            foreach (var group in settings.groups)
            {
                if (group == null) continue;
                
                foreach (var entry in group.entries)
                {
                    if (entry == null) continue;
                    
                    string assetPath = AssetDatabase.GUIDToAssetPath(entry.guid);
                    if (string.IsNullOrEmpty(assetPath)) continue;
                    
                    // Check if it's a shader file
                    if (assetPath.EndsWith(".shader", StringComparison.OrdinalIgnoreCase))
                    {
                        Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(assetPath);
                        if (shader != null)
                        {
                            addressableShaders.Add(shader);
                        }
                    }
                }
            }
        }

        public override bool AppliesTo(string assetPath, Object asset)
        {
            if (assetPath == "SCENE_CHECK")
                return false;

            return !string.IsNullOrEmpty(assetPath);
        }

        public override List<BuildIssue> Validate(string assetPath, Object asset)
        {
            var issues = new List<BuildIssue>();
            if (!processedEntries.Add(assetPath))
            {
                return issues;
            }

            var owningAsset = asset ?? AssetDatabase.LoadAssetAtPath<Object>(assetPath);
            var checkedMaterials = new HashSet<Material>();

            // 1. Project materials referenced anywhere in the dependency graph. Walked by
            //    hand instead of GetDependencies(recursive: true) so the walk can stop at
            //    model files: a model importer in legacy external-materials mode wires
            //    name-searched project materials into its import result, which the actual
            //    prefab may not use anywhere. Those don't ship with the addressable, so
            //    edges through models are not followed.
            var visitedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var pending = new Queue<string>();
            visitedPaths.Add(assetPath);
            pending.Enqueue(assetPath);

            while (pending.Count > 0)
            {
                string current = pending.Dequeue();

                if (current.EndsWith(".mat", StringComparison.OrdinalIgnoreCase))
                {
                    var material = AssetDatabase.LoadAssetAtPath<Material>(current);
                    if (material != null && checkedMaterials.Add(material) && !IsShaderAllowed(material.shader))
                    {
                        issues.Add(MakeIssue(assetPath, owningAsset,
                            $"material '{material.name}' ({current})", material.shader, material));
                    }
                }

                if (IsModelFile(current))
                    continue;

                string[] directDependencies;
                try
                {
                    directDependencies = AssetDatabase.GetDependencies(current, false);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"Failed to gather dependencies for '{current}': {ex.Message}");
                    continue;
                }

                foreach (var dependencyPath in directDependencies)
                {
                    if (string.IsNullOrEmpty(dependencyPath))
                        continue;
                    if (visitedPaths.Add(dependencyPath))
                    {
                        pending.Enqueue(dependencyPath);
                    }
                }
            }

            // 2. Materials assigned directly on renderers. Built-in and inline materials
            //    (Default-Diffuse and friends) live in unity_builtin_extra, never show up
            //    as a .mat dependency. This is also the only place materials a model
            //    really contributes are checked, since step 1 stops at model files.
            if (owningAsset is GameObject go)
            {
                foreach (var renderer in go.GetComponentsInChildren<Renderer>(true))
                {
                    foreach (var material in renderer.sharedMaterials)
                    {
                        if (material == null || !checkedMaterials.Add(material))
                            continue;
                        if (IsShaderAllowed(material.shader))
                            continue;

                        issues.Add(MakeIssue(assetPath, owningAsset,
                            $"material '{material.name}' on renderer '{renderer.name}'", material.shader, renderer));
                    }
                }
            }

            return issues;
        }

        private BuildIssue MakeIssue(string assetPath, Object owningAsset, string where, Shader shader,
            Object specificComponent)
        {
            string shaderName = shader != null ? shader.name : "a missing shader";
            string shaderPath = shader != null ? AssetDatabase.GetAssetPath(shader) : null;

            string message =
                $"Addressable asset '{assetPath}' uses {where}, whose shader ({shaderName}) is not addressable. " +
                "Use an addressable shader instead.";

            if (!string.IsNullOrEmpty(shaderPath))
            {
                message += $" Shader asset path: {shaderPath}.";
            }

            return new BuildIssue(assetPath, message, owningAsset, DefaultSeverity, this, specificComponent);
        }

        public override bool AutoFix(BuildIssue issue)
        {
            return false;
        }

        private bool IsShaderAllowed(Shader shader)
        {
            if (shader == null)
                return false;

            return addressableShaders.Contains(shader);
        }

        // Formats handled by Unity's ModelImporter. Their import graph is deliberately
        // not followed - see step 1 in Validate.
        private static readonly HashSet<string> ModelExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".fbx", ".dae", ".obj", ".blend", ".3ds", ".dxf", ".ma", ".mb", ".max", ".c4d"
        };

        private static bool IsModelFile(string path)
        {
            string extension = System.IO.Path.GetExtension(path);
            return !string.IsNullOrEmpty(extension) && ModelExtensions.Contains(extension);
        }
    }
}
