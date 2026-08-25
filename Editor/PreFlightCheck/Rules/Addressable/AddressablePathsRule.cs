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

        public override string Name => "Addressable Path Validation";

        public override string Description => "Ensures addressable names are derived from the asset's current path - stale addresses left behind by renames or moves point the game at the wrong asset";

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
                    string entryAssetPath = AssetDatabase.GUIDToAssetPath(entry.guid);
                    string expected = GetExpectedAddress(entryAssetPath, address);
                    if (string.Equals(address, expected, StringComparison.Ordinal))
                        continue;

                    string message = IsUnderAddressablesRoot(entryAssetPath)
                        ? $"Addressable path '{address}' does not match its asset's location and the game will look for '{expected}'. " +
                          "Addresses keep their old value when a file is renamed or moved, so this entry points at the wrong name."
                        : $"Addressable path '{address}' contains the '{PREFIX_TO_REMOVE}' prefix or a file extension. It should be cleaned up for proper Engage compatibility.";

                    var entryAsset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(entryAssetPath);
                    issues.Add(new BuildIssue(
                        entryAssetPath,
                        message,
                        entryAsset,
                        DefaultSeverity,
                        this,
                        null  // Can't pass AddressableAssetEntry as it's not a UnityEngine.Object
                    ));
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
                        string newAddress = GetExpectedAddress(entryAssetPath, originalAddress);

                        if (newAddress != originalAddress)
                        {
                            entry.address = newAddress;
                            EditorUtility.SetDirty(addressableSettings);
                            Debug.Log($"Fixed addressable path: '{originalAddress}' -> '{newAddress}'");
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        private static bool IsUnderAddressablesRoot(string assetPath)
        {
            return !string.IsNullOrEmpty(assetPath) && assetPath.StartsWith(PREFIX_TO_REMOVE, StringComparison.Ordinal);
        }

        /// <summary>
        /// The address an entry is supposed to carry. For assets under the addressables
        /// root it is derived from the asset's current path (prefix stripped), which also
        /// catches addresses gone stale after a rename or move - Unity freezes the address
        /// at mark time and never updates it. Anything outside the root has no path
        /// convention, so only the prefix/extension cleanup applies to its address.
        /// </summary>
        private string GetExpectedAddress(string assetPath, string currentAddress)
        {
            if (IsUnderAddressablesRoot(assetPath))
            {
                return StripConfiguredExtension(assetPath.Substring(PREFIX_TO_REMOVE.Length));
            }

            string cleaned = currentAddress;
            if (cleaned.StartsWith(PREFIX_TO_REMOVE))
            {
                cleaned = cleaned.Substring(PREFIX_TO_REMOVE.Length);
            }

            return StripConfiguredExtension(cleaned);
        }

        // Engage strips only the configured extensions from addresses and keeps every
        // other one (.controller, .mat, .txt, ...) - see the vanilla bundles, e.g.
        // unitanims/template/uac_template.controller.bundle next to the extensionless
        // anim and prefab bundles.
        private string StripConfiguredExtension(string value)
        {
            if (config == null || config.extensionsToRemove == null)
                return value;

            foreach (var ext in config.extensionsToRemove)
            {
                if (!string.IsNullOrEmpty(ext) && value.EndsWith(ext))
                {
                    return value.Substring(0, value.Length - ext.Length);
                }
            }

            return value;
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
            EditorGUILayout.HelpBox($"Always removes the '{PREFIX_TO_REMOVE}' prefix from addressable paths. Only the listed extensions are stripped - Engage keeps every other extension in the address (.controller, .mat, .txt and so on), so don't list those.", MessageType.Info);
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