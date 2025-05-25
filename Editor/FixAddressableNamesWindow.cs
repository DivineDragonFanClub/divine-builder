using UnityEditor;
using UnityEngine;
using System.Collections.Generic;

namespace DivineDragon
{
    public class FixAddressableNamesWindow : EditorWindow
    {
        private FixAddressableNamesSettings settings;
        private SerializedObject serializedSettings;
        private SerializedProperty extensionsProperty;

        private class FixedNameInfo
        {
            public string original;
            public string fixedName;
            public string removedPrefix;
            public string removedExtension;
        }
        private List<FixedNameInfo> cleanedNames = new List<FixedNameInfo>();
        private bool showDoneMessage = false;

        [MenuItem("Divine Dragon/Fix Addressable Names Tool", false, 1010)]
        public static void ShowWindow()
        {
            GetWindow<FixAddressableNamesWindow>("Fix Addressable Names");
        }

        private void OnEnable()
        {
            settings = FindOrCreateSettings();
            if (settings != null)
            {
                serializedSettings = new SerializedObject(settings);
                extensionsProperty = serializedSettings.FindProperty("extensionsToRemove");
            }
        }

        private void OnGUI()
        {   
            EditorGUILayout.HelpBox("This tool helps prepare addressable names for use in Engage by 'fixing' them: it removes the 'Assets/Share/Addressables/' prefix and the listed file extensions. This ensures the bundles will work correctly with Engage.", MessageType.Info);
            if (settings == null)
            {
                EditorGUILayout.HelpBox("Settings asset not found or failed to create.", MessageType.Error);
                return;
            }

            serializedSettings.Update();
            EditorGUILayout.PropertyField(extensionsProperty, true);
            serializedSettings.ApplyModifiedProperties();

            GUILayout.Space(10);
            if (GUILayout.Button("Fix Addressable Names"))
            {
                FixAddressablePaths();
                showDoneMessage = true;
            }

            if (showDoneMessage)
            {
                EditorGUILayout.Space();
                EditorGUILayout.HelpBox("Done Fixing!", MessageType.Info);
                if (cleanedNames.Count > 0)
                {
                    EditorGUILayout.LabelField("Fixed Names:");
                    var richStyle = new GUIStyle(EditorStyles.label) { richText = true };
                    foreach (var info in cleanedNames)
                    {
                        string display = string.Empty;
                        string orig = info.original;
                        string prefix = info.removedPrefix;
                        string ext = info.removedExtension;
                        string main = orig;
                        if (!string.IsNullOrEmpty(prefix) && orig.StartsWith(prefix))
                        {
                            main = main.Substring(prefix.Length);
                            display += $"<color=red>{prefix}</color>";
                        }
                        if (!string.IsNullOrEmpty(ext) && main.EndsWith(ext))
                        {
                            string mainNoExt = main.Substring(0, main.Length - ext.Length);
                            display += mainNoExt + $"<color=red>{ext}</color>";
                        }
                        else
                        {
                            display += main;
                        }
                        display += $"  →  <b>{info.fixedName}</b>";
                        EditorGUILayout.LabelField(display, richStyle);
                    }
                }
                else
                {
                    EditorGUILayout.LabelField("No addressable names needed fixing.");
                }
            }
        }

        private void FixAddressablePaths()
        {
            cleanedNames.Clear();
            var addressableSettings = UnityEditor.AddressableAssets.AddressableAssetSettingsDefaultObject.Settings;
            if (addressableSettings == null)
            {
                Debug.LogError("AddressableAssetSettings not found.");
                return;
            }
            foreach (var group in addressableSettings.groups)
            {
                if (group == null || group.HasSchema<UnityEditor.AddressableAssets.Settings.GroupSchemas.PlayerDataGroupSchema>())
                    continue;

                foreach (var entry in group.entries)
                {
                    if (entry == null)
                        continue;

                    string original = entry.address;
                    string removedPrefix = null;
                    string removedExtension = null;
                    bool changed = false;
                    // if entry has the Assets/Share/Addressables/ prefix, remove it
                    if (entry.address.StartsWith("Assets/Share/Addressables/"))
                    {
                        removedPrefix = "Assets/Share/Addressables/";
                        entry.address = entry.address.Substring(removedPrefix.Length);
                        changed = true;
                    }
                    // Remove user-defined file extensions from addresses
                    var extensionsToRemove = settings.extensionsToRemove;
                    foreach (var extension in extensionsToRemove)
                    {
                        if (entry.address.EndsWith(extension))
                        {
                            removedExtension = extension;
                            entry.address = entry.address.Substring(0, entry.address.Length - extension.Length);
                            changed = true;
                            break;
                        }
                    }
                    if (changed)
                    {
                        cleanedNames.Add(new FixedNameInfo
                        {
                            original = original,
                            fixedName = entry.address,
                            removedPrefix = removedPrefix,
                            removedExtension = removedExtension
                        });
                    }
                }
            }
            Debug.Log("Addressable names fixed.");
        }

        private FixAddressableNamesSettings FindOrCreateSettings()
        {
            var found = Resources.Load<FixAddressableNamesSettings>("FixAddressableNamesSettings");
            if (found != null)
                return found;

            // Create the asset in Resources if not found
            var asset = ScriptableObject.CreateInstance<FixAddressableNamesSettings>();
            #if UNITY_EDITOR
            string resourcesPath = "Assets/Resources";
            if (!AssetDatabase.IsValidFolder(resourcesPath))
                AssetDatabase.CreateFolder("Assets", "Resources");
            string assetPath = resourcesPath + "/FixAddressableNamesSettings.asset";
            AssetDatabase.CreateAsset(asset, assetPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            #endif
            return asset;
        }
    }
}
