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
        
        private enum ValidationState
        {
            Ready,                      // Ready to move and set addressable
            NeedsAddressableOnly,       // File in correct location, just needs addressable update
            WillReplace,                // Will replace existing file at target
            AddressableConflict,        // Addressable path already used by another asset
            AlreadyCorrect,            // Everything is already correct
            Invalid                    // Missing required inputs
        }

        private PathMode currentMode = PathMode.DressModel;
        private GameObject selectedPrefab;
        private string modelName = "";
        private string characterId = "";
        
        private string previewFilename = "";
        private string previewTargetPath = "";
        private string previewAddressablePath = "";
        private ValidationState validationState = ValidationState.Invalid;
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
            ParseModelInput(prefabName, out PathMode? detectedMode, out string extractedModel, out string extractedCharId);
            
            if (detectedMode.HasValue)
            {
                currentMode = detectedMode.Value;
                modelName = extractedModel;
                characterId = extractedCharId;
            }
        }
        
        private void ParseModelInput(string input, out PathMode? detectedMode, out string extractedModelName, out string extractedCharId)
        {
            detectedMode = null;
            extractedModelName = input;
            extractedCharId = "";
            
            if (string.IsNullOrEmpty(input)) return;
            
            // Check for uBody_ or oBody_ prefix
            bool hasUBodyPrefix = input.StartsWith("uBody_");
            bool hasOBodyPrefix = input.StartsWith("oBody_");
            
            if (hasUBodyPrefix || hasOBodyPrefix)
            {
                detectedMode = hasUBodyPrefix ? PathMode.DressModel : PathMode.BodyModel;
                
                // Remove the prefix
                string withoutPrefix = input.Substring(6); // "uBody_" or "oBody_" is 6 chars
                
                // Check for character ID suffix pattern (_c### at the end)
                var match = System.Text.RegularExpressions.Regex.Match(withoutPrefix, @"^(.+?)(?:_(c\d{3}))?$");
                if (match.Success)
                {
                    extractedModelName = match.Groups[1].Value;
                    if (match.Groups[2].Success)
                    {
                        extractedCharId = match.Groups[2].Value;
                    }
                }
                else
                {
                    extractedModelName = withoutPrefix;
                }
            }
            else
            {
                // No prefix found, check if there's a character ID suffix
                var match = System.Text.RegularExpressions.Regex.Match(input, @"^(.+?)(?:_(c\d{3}))?$");
                if (match.Success)
                {
                    extractedModelName = match.Groups[1].Value;
                    if (match.Groups[2].Success)
                    {
                        extractedCharId = match.Groups[2].Value;
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
            
            string previousModelName = modelName;
            modelName = EditorGUILayout.TextField("Model Name", modelName);
            
            // Smart parsing when model name changes
            if (modelName != previousModelName && !string.IsNullOrEmpty(modelName))
            {
                ParseModelInput(modelName, out PathMode? detectedMode, out string extractedModel, out string extractedCharId);
                
                if (detectedMode.HasValue)
                {
                    // Auto-switch mode if a prefix was detected
                    currentMode = detectedMode.Value;
                    modelName = extractedModel;
                    
                    // Only update character ID if one was found
                    if (!string.IsNullOrEmpty(extractedCharId))
                    {
                        characterId = extractedCharId;
                    }
                }
                else if (!string.IsNullOrEmpty(extractedCharId))
                {
                    // Even without prefix, if we found a character ID, extract it
                    modelName = extractedModel;
                    characterId = extractedCharId;
                }
            }
            
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
                if (validationState == ValidationState.AddressableConflict || validationState == ValidationState.AlreadyCorrect)
                    messageType = MessageType.Warning;
                else if (validationState == ValidationState.WillReplace)
                    messageType = MessageType.Warning;
                else if (validationState == ValidationState.Invalid)
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
                bool canExecute = validationState == ValidationState.Ready || 
                                 validationState == ValidationState.NeedsAddressableOnly || 
                                 validationState == ValidationState.WillReplace;
                
                EditorGUI.BeginDisabledGroup(!canExecute);
                string buttonText = validationState == ValidationState.NeedsAddressableOnly ? "Update Addressable" : "Execute";
                if (GUILayout.Button(buttonText, GUILayout.Height(30)))
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
            validationState = ValidationState.Invalid;
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
            
            // Check addressable status
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            bool addressableConflict = false;
            string conflictingAssetPath = "";
            string currentAddressablePath = "";
            bool hasAddressableEntry = false;
            
            if (settings != null)
            {
                // Check if current prefab has an addressable entry
                string currentGuid = AssetDatabase.AssetPathToGUID(currentPath);
                foreach (var group in settings.groups)
                {
                    var currentEntry = group.GetAssetEntry(currentGuid);
                    if (currentEntry != null)
                    {
                        currentAddressablePath = currentEntry.address;
                        hasAddressableEntry = true;
                        break;
                    }
                }
                
                // Check if the desired addressable path is taken by another asset
                foreach (var group in settings.groups)
                {
                    foreach (var entry in group.entries)
                    {
                        if (entry.address == previewAddressablePath)
                        {
                            string assetPath = AssetDatabase.GUIDToAssetPath(entry.guid);
                            if (assetPath != currentPath && assetPath != targetFullPath)
                            {
                                addressableConflict = true;
                                conflictingAssetPath = assetPath;
                                break;
                            }
                        }
                    }
                    if (addressableConflict) break;
                }
            }
            
            // Determine validation state
            bool isInCorrectLocation = (currentPath == targetFullPath);
            bool hasCorrectAddressable = hasAddressableEntry && (currentAddressablePath == previewAddressablePath);
            
            if (addressableConflict)
            {
                validationState = ValidationState.AddressableConflict;
                validationMessage = $"Warning: Addressable path already used by: {Path.GetFileName(conflictingAssetPath)}";
            }
            else if (isInCorrectLocation && hasCorrectAddressable)
            {
                validationState = ValidationState.AlreadyCorrect;
                validationMessage = "This prefab is already in the correct location with correct addressable path";
            }
            else if (isInCorrectLocation && !hasCorrectAddressable)
            {
                validationState = ValidationState.NeedsAddressableOnly;
                if (!hasAddressableEntry)
                {
                    validationMessage = "Prefab is in correct location but needs addressable path set";
                }
                else
                {
                    validationMessage = $"Prefab is in correct location but addressable path needs updating from:\n{currentAddressablePath}";
                }
            }
            else if (fileExists)
            {
                validationState = ValidationState.WillReplace;
                validationMessage = "Warning: A prefab already exists at the target location. It will be replaced.";
            }
            else
            {
                validationState = ValidationState.Ready;
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
            bool canExecute = validationState == ValidationState.Ready || 
                            validationState == ValidationState.NeedsAddressableOnly || 
                            validationState == ValidationState.WillReplace;
                            
            if (!canExecute || selectedPrefab == null) return;
            
            try
            {
                string currentPath = AssetDatabase.GetAssetPath(selectedPrefab);
                string targetFullPath = previewTargetPath + previewFilename;
                
                // Handle different validation states
                if (validationState == ValidationState.NeedsAddressableOnly)
                {
                    // Just update the addressable path, no file movement needed
                    UpdateAddressable(currentPath, previewAddressablePath);
                    
                    AssetDatabase.SaveAssets();
                    AssetDatabase.Refresh();
                    
                    // Show success
                    showSuccess = true;
                    
                    // Clear the form
                    selectedPrefab = null;
                    modelName = "";
                    characterId = "";
                    UpdatePreview();
                    
                    // Force window repaint to show success button
                    Repaint();
                    return;
                }
                
                // Create directory structure if it doesn't exist
                CreateDirectoryStructure(previewTargetPath);
                
                // Handle replacement case
                if (validationState == ValidationState.WillReplace)
                {
                    // Show confirmation dialog
                    bool shouldReplace = EditorUtility.DisplayDialog(
                        "Replace Existing Prefab?",
                        $"A prefab already exists at:\n{targetFullPath}\n\nDo you want to replace it?",
                        "Replace",
                        "Cancel"
                    );
                    
                    if (!shouldReplace)
                    {
                        return;
                    }
                    
                    // Delete the existing prefab first
                    AssetDatabase.DeleteAsset(targetFullPath);
                }
                
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