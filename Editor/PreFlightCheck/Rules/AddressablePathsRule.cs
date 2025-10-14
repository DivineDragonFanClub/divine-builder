using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

namespace DivineDragon.PreFlightCheck.Rules
{
    public class AddressablePathsRule : BuildRule
    {
        private const string PREFIX_TO_REMOVE = "Assets/Share/Addressables/";

        // Configuration
        [Serializable]
        public class Config
        {
            public List<string> extensionsToRemove = new List<string> { ".anim", ".overrideController", ".prefab" };
        }

        private Config config;
        private bool configExpanded = false;

        public override string Name => "Addressable Path Validation";

        public override string Description => "Ensures addressable names follow proper conventions (no Assets/Share/Addressables/ prefix or file extensions)";

        public override IssueSeverity DefaultSeverity => IssueSeverity.Warning;

        public override bool CanAutoFix => true;

        public override bool HasConfiguration => true;

        public AddressablePathsRule()
        {
            LoadConfiguration();
        }

        public override bool AppliesTo(string assetPath, UnityEngine.Object asset)
        {
            // Special marker for addressable-wide checks
            return assetPath == "ADDRESSABLE_PATH_CHECK";
        }

        public override List<BuildIssue> Validate(string assetPath, UnityEngine.Object asset)
        {
            var issues = new List<BuildIssue>();

            var addressableSettings = AddressableAssetSettingsDefaultObject.Settings;
            if (addressableSettings == null)
            {
                return issues;
            }

            foreach (var group in addressableSettings.groups)
            {
                if (group == null || group.HasSchema<UnityEditor.AddressableAssets.Settings.GroupSchemas.PlayerDataGroupSchema>())
                    continue;

                foreach (var entry in group.entries)
                {
                    if (entry == null)
                        continue;

                    string address = entry.address;
                    bool hasIssue = false;
                    string issueDescription = "";

                    // Check for prefix
                    if (address.StartsWith(PREFIX_TO_REMOVE))
                    {
                        hasIssue = true;
                        issueDescription = $"Address contains '{PREFIX_TO_REMOVE}' prefix";
                    }

                    // Check for extensions
                    if (config != null && config.extensionsToRemove != null)
                    {
                        foreach (var ext in config.extensionsToRemove)
                        {
                            if (address.EndsWith(ext))
                            {
                                hasIssue = true;
                                if (!string.IsNullOrEmpty(issueDescription))
                                    issueDescription += " and ";
                                issueDescription += $"ends with '{ext}'";
                                break;
                            }
                        }
                    }

                    if (hasIssue)
                    {
                        string entryAssetPath = AssetDatabase.GUIDToAssetPath(entry.guid);
                        var entryAsset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(entryAssetPath);

                        issues.Add(new BuildIssue(
                            entryAssetPath,
                            $"Addressable path '{address}' {issueDescription}. It should be cleaned up for proper Engage compatibility.",
                            entryAsset,
                            DefaultSeverity,
                            this,
                            null  // Can't pass AddressableAssetEntry as it's not a UnityEngine.Object
                        ));
                    }
                }
            }

            return issues;
        }

        public override bool AutoFix(BuildIssue issue)
        {
            var addressableSettings = AddressableAssetSettingsDefaultObject.Settings;
            if (addressableSettings == null)
                return false;

            // Find the entry that needs fixing
            foreach (var group in addressableSettings.groups)
            {
                if (group == null)
                    continue;

                foreach (var entry in group.entries)
                {
                    if (entry == null)
                        continue;

                    string entryAssetPath = AssetDatabase.GUIDToAssetPath(entry.guid);
                    if (entryAssetPath == issue.AssetPath)
                    {
                        string originalAddress = entry.address;
                        string newAddress = originalAddress;

                        // Remove prefix
                        if (newAddress.StartsWith(PREFIX_TO_REMOVE))
                        {
                            newAddress = newAddress.Substring(PREFIX_TO_REMOVE.Length);
                        }

                        // Remove extensions
                        if (config != null && config.extensionsToRemove != null)
                        {
                            foreach (var ext in config.extensionsToRemove)
                            {
                                if (newAddress.EndsWith(ext))
                                {
                                    newAddress = newAddress.Substring(0, newAddress.Length - ext.Length);
                                    break;
                                }
                            }
                        }

                        if (newAddress != originalAddress)
                        {
                            entry.address = newAddress;
                            EditorUtility.SetDirty(addressableSettings);
                            Debug.Log($"Fixed addressable path: '{originalAddress}' → '{newAddress}'");
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        public override void DrawConfiguration()
        {
            if (config == null)
            {
                config = new Config();
            }

            EditorGUI.indentLevel++;

            EditorGUILayout.BeginVertical(GUI.skin.box);

            EditorGUILayout.LabelField("Configuration", EditorStyles.boldLabel);
            EditorGUILayout.Space(5);

            // Info about prefix removal
            EditorGUILayout.HelpBox($"Always removes '{PREFIX_TO_REMOVE}' prefix from addressable paths", MessageType.Info);
            EditorGUILayout.Space(5);

            // Extensions to remove
            EditorGUILayout.LabelField("Extensions to Remove:", EditorStyles.label);

            EditorGUI.BeginChangeCheck();

            for (int i = 0; i < config.extensionsToRemove.Count; i++)
            {
                EditorGUILayout.BeginHorizontal();
                config.extensionsToRemove[i] = EditorGUILayout.TextField(config.extensionsToRemove[i]);
                if (GUILayout.Button("Remove", GUILayout.Width(60)))
                {
                    config.extensionsToRemove.RemoveAt(i);
                    i--;
                }
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Add Extension", GUILayout.Width(100)))
            {
                config.extensionsToRemove.Add("");
            }
            EditorGUILayout.EndHorizontal();

            if (EditorGUI.EndChangeCheck())
            {
                SaveConfiguration();
            }

            EditorGUILayout.EndVertical();

            EditorGUI.indentLevel--;
        }

        public override void LoadConfiguration()
        {
            // Load from FixAddressableNamesSettings if it exists
            var settings = FixAddressableNamesSettings.Instance;
            if (settings != null)
            {
                config = new Config
                {
                    extensionsToRemove = new List<string>(settings.extensionsToRemove)
                };
            }
            else
            {
                // Use default config
                config = new Config();
            }
        }

        public override void SaveConfiguration()
        {
            // Save to FixAddressableNamesSettings
            var settings = FixAddressableNamesSettings.Instance;
            if (settings == null)
            {
                // Create settings if it doesn't exist
                settings = ScriptableObject.CreateInstance<FixAddressableNamesSettings>();
                string resourcesPath = "Assets/Resources";
                if (!AssetDatabase.IsValidFolder(resourcesPath))
                    AssetDatabase.CreateFolder("Assets", "Resources");
                string assetPath = resourcesPath + "/FixAddressableNamesSettings.asset";
                AssetDatabase.CreateAsset(settings, assetPath);
            }

            if (settings != null && config != null)
            {
                settings.extensionsToRemove = new List<string>(config.extensionsToRemove);
                EditorUtility.SetDirty(settings);
                AssetDatabase.SaveAssets();
            }
        }
    }
}