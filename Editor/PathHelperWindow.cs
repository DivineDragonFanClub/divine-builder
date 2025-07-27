using UnityEditor;
using UnityEngine;
using System.IO;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;

namespace DivineDragon
{
    public class PathHelperWindow : EditorWindow
    {
        private enum PathMode
        {
            DressModel,
            BodyModel
        }

        private PathMode currentMode = PathMode.DressModel;
        private GameObject selectedPrefab;
        private string modelName = "";
        private string characterId = "";
        
        private string previewFilename = "";
        private string previewTargetPath = "";
        private string previewAddressablePath = "";
        private bool isValid = false;
        private string validationMessage = "";
        private bool showTechnicalDetails = false;
        private bool showSuccess = false;

        [MenuItem("Divine Dragon/Path Helper", false, 1020)]
        public static void ShowWindow()
        {
            var window = GetWindow<PathHelperWindow>("Path Helper");
            window.minSize = new Vector2(550, 400);
            window.CheckForSelectedPrefab();
            window.UpdatePreview();
        }

        private void OnEnable()
        {
            CheckForSelectedPrefab();
            UpdatePreview();
        }

        private void OnFocus()
        {
            CheckForSelectedPrefab();
            UpdatePreview();
        }

        private void OnSelectionChange()
        {
            CheckForSelectedPrefab();
            Repaint();
        }

        private void CheckForSelectedPrefab()
        {
            if (Selection.activeObject is GameObject go)
            {
                string path = AssetDatabase.GetAssetPath(go);
                if (!string.IsNullOrEmpty(path) && path.EndsWith(".prefab"))
                {
                    selectedPrefab = go;
                    TryExtractModelName();
                }
            }
        }

        private void TryExtractModelName()
        {
            if (selectedPrefab == null) return;
            
            string prefabName = selectedPrefab.name;
            if (prefabName.StartsWith("uBody_"))
            {
                currentMode = PathMode.DressModel;
                string[] parts = prefabName.Split('_');
                if (parts.Length >= 2)
                {
                    modelName = parts[1];
                    if (parts.Length >= 3)
                    {
                        characterId = parts[2];
                    }
                }
            }
            else if (prefabName.StartsWith("oBody_"))
            {
                currentMode = PathMode.BodyModel;
                string[] parts = prefabName.Split('_');
                if (parts.Length >= 2)
                {
                    modelName = parts[1];
                    if (parts.Length >= 3)
                    {
                        characterId = parts[2];
                    }
                }
            }
        }

        private void OnGUI()
        {
            // Add padding around the entire window
            EditorGUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Space(5);
            
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(5);
            EditorGUILayout.BeginVertical();
            
            EditorGUILayout.HelpBox("Path Helper organizes prefabs into the correct folder structure and assigns addressable paths.", MessageType.Info);
            
            GUILayout.Space(10);
            
            EditorGUILayout.LabelField("Model Type", EditorStyles.boldLabel);
            GUILayout.Space(3);
            EditorGUI.BeginChangeCheck();
            currentMode = (PathMode)EditorGUILayout.EnumPopup(currentMode);
            if (EditorGUI.EndChangeCheck())
            {
                showSuccess = false;
                UpdatePreview();
            }
            
            // Mode-specific info
            switch (currentMode)
            {
                case PathMode.DressModel:
                    EditorGUILayout.HelpBox("Used in zoomed-in combat scenes", MessageType.None);
                    break;
                case PathMode.BodyModel:
                    EditorGUILayout.HelpBox("Used on the battle map", MessageType.None);
                    break;
            }
            
            GUILayout.Space(10);
            EditorGUILayout.LabelField("Input", EditorStyles.boldLabel);
            GUILayout.Space(3);
            
            EditorGUI.BeginChangeCheck();
            
            // Record undo for object field
            Undo.RecordObject(this, "Path Helper Change");
            selectedPrefab = (GameObject)EditorGUILayout.ObjectField("Prefab", selectedPrefab, typeof(GameObject), false);
            GUILayout.Space(3);
            
            modelName = EditorGUILayout.TextField("Model Name", modelName);
            GUILayout.Space(3);
            
            // Character ID with placeholder and tooltip
            var charIdContent = new GUIContent("Character ID", "Use c000 if you aren't sure what to put here");
            EditorGUILayout.BeginHorizontal();
            characterId = EditorGUILayout.TextField(charIdContent, characterId);
            
            // Show placeholder text when field is empty
            if (string.IsNullOrEmpty(characterId) && Event.current.type == EventType.Repaint)
            {
                var rect = GUILayoutUtility.GetLastRect();
                var placeholderStyle = new GUIStyle(GUI.skin.label);
                placeholderStyle.normal.textColor = new Color(0.5f, 0.5f, 0.5f, 0.7f);
                placeholderStyle.fontSize = EditorStyles.textField.fontSize;
                
                // Adjust rect to position text inside the text field
                rect.x += EditorGUIUtility.labelWidth + 4;
                rect.width -= EditorGUIUtility.labelWidth + 4;
                
                GUI.Label(rect, "c000", placeholderStyle);
            }
            EditorGUILayout.EndHorizontal();
            
            if (EditorGUI.EndChangeCheck())
            {
                showSuccess = false;
                UpdatePreview();
            }
            
            // AssetTable Reference section
            if (!string.IsNullOrEmpty(modelName))
            {
                GUILayout.Space(10);
                EditorGUILayout.LabelField("AssetTable Reference", EditorStyles.boldLabel);
                GUILayout.Space(3);
                
                string assetTableName = previewFilename.Replace(".prefab", "");
                EditorGUILayout.LabelField("Use this name in the AssetTable to refer to the asset:");
                
                string fieldName = currentMode == PathMode.DressModel ? "DressModel" : "BodyModel";
                EditorGUILayout.BeginHorizontal();
                EditorGUI.BeginDisabledGroup(true);
                var boldStyle = new GUIStyle(EditorStyles.textField) { fontStyle = FontStyle.Bold };
                EditorGUILayout.TextField("", $"{fieldName}=\"{assetTableName}\"", boldStyle);
                EditorGUI.EndDisabledGroup();
                if (GUILayout.Button("Copy Name", GUILayout.Width(90)))
                {
                    GUIUtility.systemCopyBuffer = assetTableName;
                    ShowNotification(new GUIContent("Copied to clipboard!"));
                }
                EditorGUILayout.EndHorizontal();
            }
            
            GUILayout.Space(10);
            
            // Technical Details foldout
            showTechnicalDetails = EditorGUILayout.Foldout(showTechnicalDetails, "Technical Details", true);
            if (showTechnicalDetails)
            {
                EditorGUI.indentLevel++;
                EditorGUI.BeginDisabledGroup(true);
                EditorGUILayout.TextField("Filename", previewFilename);
                EditorGUILayout.TextField("Target Path", previewTargetPath);
                EditorGUILayout.TextField("Addressable Path", previewAddressablePath);
                EditorGUI.EndDisabledGroup();
                EditorGUI.indentLevel--;
            }
            
            GUILayout.Space(8);
            
            if (!string.IsNullOrEmpty(validationMessage))
            {
                MessageType messageType = MessageType.Info;
                if (validationMessage.StartsWith("Warning:"))
                    messageType = MessageType.Warning;
                else if (!isValid)
                    messageType = MessageType.Warning;
                
                EditorGUILayout.HelpBox(validationMessage, messageType);
            }
            
            GUILayout.Space(10);
            
            // Execute button with success state
            if (showSuccess)
            {
                var originalColor = GUI.backgroundColor;
                GUI.backgroundColor = Color.green;
                EditorGUI.BeginDisabledGroup(true);
                GUILayout.Button("Success!", GUILayout.Height(30));
                EditorGUI.EndDisabledGroup();
                GUI.backgroundColor = originalColor;
            }
            else
            {
                EditorGUI.BeginDisabledGroup(!isValid);
                if (GUILayout.Button("Execute", GUILayout.Height(30)))
                {
                    Execute();
                }
                EditorGUI.EndDisabledGroup();
            }
            
            EditorGUILayout.EndVertical();
            GUILayout.Space(5);
            EditorGUILayout.EndHorizontal();
            
            GUILayout.Space(5);
            EditorGUILayout.EndVertical();
        }

        private void UpdatePreview()
        {
            isValid = false;
            validationMessage = "";
            
            if (selectedPrefab == null)
            {
                validationMessage = "Please select a prefab";
                ClearPreview();
                return;
            }
            
            if (string.IsNullOrEmpty(modelName))
            {
                validationMessage = "Please enter a model name";
                ClearPreview();
                return;
            }
            
            switch (currentMode)
            {
                case PathMode.DressModel:
                    GenerateDressModelPaths();
                    break;
                case PathMode.BodyModel:
                    GenerateBodyModelPaths();
                    break;
            }
            
            // Check if target file already exists
            string targetFullPath = previewTargetPath + previewFilename;
            bool fileExists = File.Exists(targetFullPath);
            string currentPath = AssetDatabase.GetAssetPath(selectedPrefab);
            
            // Check if addressable path is already taken
            bool addressableExists = false;
            string existingAssetPath = "";
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings != null)
            {
                foreach (var group in settings.groups)
                {
                    foreach (var entry in group.entries)
                    {
                        if (entry.address == previewAddressablePath)
                        {
                            existingAssetPath = AssetDatabase.GUIDToAssetPath(entry.guid);
                            if (existingAssetPath != currentPath && existingAssetPath != targetFullPath)
                            {
                                addressableExists = true;
                                break;
                            }
                        }
                    }
                    if (addressableExists) break;
                }
            }
            
            // Determine validation status
            if (currentPath == targetFullPath)
            {
                validationMessage = "This prefab is already in the correct location";
                isValid = false;
            }
            else if (addressableExists)
            {
                validationMessage = $"Warning: Addressable path already used by: {Path.GetFileName(existingAssetPath)}";
                isValid = false; // Don't allow duplicate addressable paths
            }
            else if (fileExists)
            {
                validationMessage = "Warning: A prefab already exists at the target location. It will be replaced.";
                isValid = true; // Still allow execution, but with warning
            }
            else
            {
                isValid = true;
                validationMessage = "Ready to organize prefab";
            }
        }

        private void GenerateDressModelPaths()
        {
            string charId = string.IsNullOrEmpty(characterId) ? "c000" : characterId;
            previewFilename = $"uBody_{modelName}_{charId}.prefab";
            previewTargetPath = $"Assets/Share/Addressables/Unit/Model/uBody/{modelName}/{charId}/Prefabs/";
            previewAddressablePath = $"Unit/Model/uBody/{modelName}/{charId}/Prefabs/uBody_{modelName}_{charId}";
        }

        private void GenerateBodyModelPaths()
        {
            string charId = string.IsNullOrEmpty(characterId) ? "c000" : characterId;
            previewFilename = $"oBody_{modelName}_{charId}.prefab";
            previewTargetPath = $"Assets/Share/Addressables/Unit/Model/oBody/{modelName}/{charId}/Prefabs/";
            previewAddressablePath = $"Unit/Model/oBody/{modelName}/{charId}/Prefabs/oBody_{modelName}_{charId}";
        }

        private void ClearPreview()
        {
            previewFilename = "";
            previewTargetPath = "";
            previewAddressablePath = "";
        }

        private void Execute()
        {
            if (!isValid || selectedPrefab == null) return;
            
            try
            {
                string currentPath = AssetDatabase.GetAssetPath(selectedPrefab);
                string targetFullPath = previewTargetPath + previewFilename;
                
                // Create directory structure if it doesn't exist
                CreateDirectoryStructure(previewTargetPath);
                
                // Move and rename the prefab
                string error = AssetDatabase.MoveAsset(currentPath, targetFullPath);
                if (!string.IsNullOrEmpty(error))
                {
                    EditorUtility.DisplayDialog("Error", $"Failed to move prefab: {error}", "OK");
                    return;
                }
                
                // Update addressable
                UpdateAddressable(targetFullPath, previewAddressablePath);
                
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                
                // Reselect the prefab at its new location
                GameObject movedPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(targetFullPath);
                if (movedPrefab != null)
                {
                    Selection.activeObject = movedPrefab;
                    EditorGUIUtility.PingObject(movedPrefab);
                    
                    // Force focus to project window
                    EditorUtility.FocusProjectWindow();
                }
                else
                {
                    Debug.LogWarning($"Could not find prefab at new location: {targetFullPath}");
                }
                
                // Show success state
                showSuccess = true;
                
                // Clear the form
                selectedPrefab = null;
                modelName = "";
                characterId = "";
                UpdatePreview();
                
                // Force window repaint to show success button
                Repaint();
            }
            catch (System.Exception e)
            {
                EditorUtility.DisplayDialog("Error", $"An error occurred: {e.Message}", "OK");
                Debug.LogError(e);
            }
        }

        private void CreateDirectoryStructure(string path)
        {
            string[] folders = path.Split('/');
            string currentPath = folders[0];
            
            for (int i = 1; i < folders.Length; i++)
            {
                string folder = folders[i];
                if (string.IsNullOrEmpty(folder)) continue;
                
                string nextPath = currentPath + "/" + folder;
                if (!AssetDatabase.IsValidFolder(nextPath))
                {
                    AssetDatabase.CreateFolder(currentPath, folder);
                }
                currentPath = nextPath;
            }
        }

        private void UpdateAddressable(string assetPath, string addressablePath)
        {
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
            {
                Debug.LogError("Addressable settings not found");
                return;
            }
            
            // Find the "fe" group
            AddressableAssetGroup targetGroup = null;
            foreach (var group in settings.groups)
            {
                if (group.name == "fe")
                {
                    targetGroup = group;
                    break;
                }
            }
            
            if (targetGroup == null)
            {
                Debug.LogError("Could not find 'fe' addressable group");
                return;
            }
            
            // Get the asset GUID
            string guid = AssetDatabase.AssetPathToGUID(assetPath);
            
            // Create or move the entry
            var entry = settings.CreateOrMoveEntry(guid, targetGroup, false, false);
            if (entry != null)
            {
                entry.address = addressablePath;
                settings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryMoved, new object[] { entry }, true);
            }
        }
    }
}