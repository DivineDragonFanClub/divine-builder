using UnityEditor;
using UnityEngine;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using System.IO;

namespace DivineDragon
{
    public class ReplaceAddressableWindow : EditorWindow
    {
        private enum ValidationState
        {
            Invalid,
            Ready,
            SameAsset,
            OriginalNotAddressable,
            ReplacementIsAddressable
        }

        private GameObject originalPrefab;
        private GameObject replacementPrefab;

        private ValidationState validationState = ValidationState.Invalid;
        private string validationMessage = "";

        private string originalPath = "";
        private string originalAddressablePath = "";
        private string originalGroupName = "";
        private bool showSuccess = false;

        [MenuItem("Divine Dragon/Replace Addressable", false, 1030)]
        public static void ShowWindow()
        {
            var window = GetWindow<ReplaceAddressableWindow>("Replace Addressable");
            window.minSize = new Vector2(500, 350);
            window.LoadSessionState();
        }

        private void OnEnable()
        {
            LoadSessionState();
        }

        private void OnDisable()
        {
            SaveSessionState();
        }

        private void LoadSessionState()
        {
            string originalPath = SessionState.GetString("ReplaceAddressable_OriginalPath", "");
            string replacementPath = SessionState.GetString("ReplaceAddressable_ReplacementPath", "");

            if (!string.IsNullOrEmpty(originalPath))
            {
                originalPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(originalPath);
            }

            if (!string.IsNullOrEmpty(replacementPath))
            {
                replacementPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(replacementPath);
            }

            if (originalPrefab != null || replacementPrefab != null)
            {
                UpdateValidation();
            }
        }

        private void SaveSessionState()
        {
            string originalPath = originalPrefab ? AssetDatabase.GetAssetPath(originalPrefab) : "";
            string replacementPath = replacementPrefab ? AssetDatabase.GetAssetPath(replacementPrefab) : "";

            SessionState.SetString("ReplaceAddressable_OriginalPath", originalPath);
            SessionState.SetString("ReplaceAddressable_ReplacementPath", replacementPath);
        }

        private void OnGUI()
        {
            EditorGUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Space(5);

            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(5);
            EditorGUILayout.BeginVertical();

            EditorGUILayout.HelpBox("Replace an addressable prefab with another prefab, preserving the original's name and addressable path. This operation cannot be undone.", MessageType.Info);

            GUILayout.Space(10);

            EditorGUILayout.LabelField("Prefabs", EditorStyles.boldLabel);
            GUILayout.Space(3);

            EditorGUI.BeginChangeCheck();

            originalPrefab = (GameObject)EditorGUILayout.ObjectField(
                new GUIContent("Original Addressable", "The addressable prefab to be replaced"),
                originalPrefab, typeof(GameObject), false);

            GUILayout.Space(3);

            replacementPrefab = (GameObject)EditorGUILayout.ObjectField(
                new GUIContent("Replacement Prefab", "The prefab that will replace the original"),
                replacementPrefab, typeof(GameObject), false);

            if (EditorGUI.EndChangeCheck())
            {
                showSuccess = false;
                UpdateValidation();
                SaveSessionState();
            }

            GUILayout.Space(10);

            if (!string.IsNullOrEmpty(originalAddressablePath))
            {
                EditorGUILayout.LabelField("Addressable Info", EditorStyles.boldLabel);
                GUILayout.Space(3);

                EditorGUI.BeginDisabledGroup(true);
                EditorGUILayout.TextField("Addressable Path", originalAddressablePath);
                EditorGUI.EndDisabledGroup();

                GUILayout.Space(10);
            }

            GUILayout.Space(8);

            if (!string.IsNullOrEmpty(validationMessage))
            {
                MessageType messageType = MessageType.Info;
                if (validationState == ValidationState.Invalid ||
                    validationState == ValidationState.SameAsset ||
                    validationState == ValidationState.OriginalNotAddressable)
                {
                    messageType = MessageType.Warning;
                }
                else if (validationState == ValidationState.ReplacementIsAddressable)
                {
                    messageType = MessageType.Info;
                }

                EditorGUILayout.HelpBox(validationMessage, messageType);
            }

            GUILayout.Space(10);

            if (showSuccess)
            {
                var originalColor = GUI.backgroundColor;
                GUI.backgroundColor = Color.green;
                EditorGUI.BeginDisabledGroup(true);
                GUILayout.Button("Success! Ready for next iteration", GUILayout.Height(30));
                EditorGUI.EndDisabledGroup();
                GUI.backgroundColor = originalColor;

                EditorGUILayout.HelpBox("The replaced prefab has been moved to the 'Original' field, ready for another replacement.", MessageType.Info);
            }
            else
            {
                bool canExecute = validationState == ValidationState.Ready ||
                                 validationState == ValidationState.ReplacementIsAddressable;

                EditorGUI.BeginDisabledGroup(!canExecute);
                if (GUILayout.Button("Execute Replace", GUILayout.Height(30)))
                {
                    ExecuteReplace();
                }
                EditorGUI.EndDisabledGroup();
            }

            EditorGUILayout.EndVertical();
            GUILayout.Space(5);
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(5);
            EditorGUILayout.EndVertical();
        }

        private void UpdateValidation()
        {
            validationState = ValidationState.Invalid;
            validationMessage = "";
            originalAddressablePath = "";
            originalGroupName = "";
            originalPath = "";

            if (originalPrefab == null)
            {
                validationMessage = "Please select an original addressable prefab";
                return;
            }

            // Get original path and addressable info even if no replacement is selected yet
            originalPath = AssetDatabase.GetAssetPath(originalPrefab);

            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings != null)
            {
                string originalGuid = AssetDatabase.AssetPathToGUID(originalPath);
                foreach (var group in settings.groups)
                {
                    var entry = group.GetAssetEntry(originalGuid);
                    if (entry != null)
                    {
                        originalAddressablePath = entry.address;
                        originalGroupName = group.name;
                        break;
                    }
                }
            }

            if (replacementPrefab == null)
            {
                validationMessage = "Please select a replacement prefab";
                return;
            }

            string replacementPath = AssetDatabase.GetAssetPath(replacementPrefab);

            if (originalPath == replacementPath)
            {
                validationState = ValidationState.SameAsset;
                validationMessage = "Original and replacement are the same asset";
                return;
            }

            if (settings == null)
            {
                validationMessage = "Addressable settings not found";
                return;
            }

            // Check if original is addressable (already populated above)
            if (string.IsNullOrEmpty(originalAddressablePath))
            {
                validationState = ValidationState.OriginalNotAddressable;
                validationMessage = "Original prefab is not an addressable asset";
                return;
            }

            string replacementGuid = AssetDatabase.AssetPathToGUID(replacementPath);
            bool replacementIsAddressable = false;

            foreach (var group in settings.groups)
            {
                if (group.GetAssetEntry(replacementGuid) != null)
                {
                    replacementIsAddressable = true;
                    break;
                }
            }

            if (replacementIsAddressable)
            {
                validationState = ValidationState.ReplacementIsAddressable;
                validationMessage = "Warning: Replacement is already addressable. Its addressable entry will be removed.";
            }
            else
            {
                validationState = ValidationState.Ready;
                validationMessage = "Ready to replace addressable";
            }
        }

        private void ExecuteReplace()
        {
            if (validationState != ValidationState.Ready &&
                validationState != ValidationState.ReplacementIsAddressable)
            {
                return;
            }

            bool shouldProceed = EditorUtility.DisplayDialog(
                "Replace Addressable?",
                $"This will replace:\n{originalPrefab.name}\n\nWith:\n{replacementPrefab.name}\n\n" +
                $"The original will be deleted and cannot be recovered.\n\n" +
                $"Are you sure you want to proceed?",
                "Replace",
                "Cancel"
            );

            if (!shouldProceed)
            {
                return;
            }

            try
            {
                var settings = AddressableAssetSettingsDefaultObject.Settings;
                if (settings == null)
                {
                    EditorUtility.DisplayDialog("Error", "Addressable settings not found", "OK");
                    return;
                }

                string originalGuid = AssetDatabase.AssetPathToGUID(originalPath);
                string replacementPath = AssetDatabase.GetAssetPath(replacementPrefab);
                string replacementGuid = AssetDatabase.AssetPathToGUID(replacementPath);

                AddressableAssetEntry originalEntry = null;
                AddressableAssetGroup originalGroup = null;

                foreach (var group in settings.groups)
                {
                    var entry = group.GetAssetEntry(originalGuid);
                    if (entry != null)
                    {
                        originalEntry = entry;
                        originalGroup = group;
                        break;
                    }
                }

                if (originalEntry == null || originalGroup == null)
                {
                    EditorUtility.DisplayDialog("Error", "Could not find original addressable entry", "OK");
                    return;
                }

                string addressablePath = originalEntry.address;
                var labels = originalEntry.labels;

                if (validationState == ValidationState.ReplacementIsAddressable)
                {
                    foreach (var group in settings.groups)
                    {
                        var replacementEntry = group.GetAssetEntry(replacementGuid);
                        if (replacementEntry != null)
                        {
                            group.RemoveAssetEntry(replacementEntry);
                            break;
                        }
                    }
                }

                originalGroup.RemoveAssetEntry(originalEntry);

                if (!AssetDatabase.DeleteAsset(originalPath))
                {
                    EditorUtility.DisplayDialog("Error", "Failed to delete original prefab", "OK");
                    return;
                }

                string error = AssetDatabase.MoveAsset(replacementPath, originalPath);
                if (!string.IsNullOrEmpty(error))
                {
                    EditorUtility.DisplayDialog("Error", $"Failed to move replacement: {error}", "OK");
                    return;
                }

                string movedGuid = AssetDatabase.AssetPathToGUID(originalPath);
                var newEntry = settings.CreateOrMoveEntry(movedGuid, originalGroup, false, false);
                if (newEntry != null)
                {
                    newEntry.address = addressablePath;

                    foreach (var label in labels)
                    {
                        newEntry.SetLabel(label, true, false);
                    }

                    settings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryMoved, new object[] { newEntry }, true);
                }

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                GameObject movedPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(originalPath);
                if (movedPrefab != null)
                {
                    Selection.activeObject = movedPrefab;
                    EditorGUIUtility.PingObject(movedPrefab);
                }

                showSuccess = true;

                // Shift the replaced item to original slot for next iteration
                originalPrefab = movedPrefab;
                replacementPrefab = null;

                UpdateValidation();
                SaveSessionState();
                Repaint();
            }
            catch (System.Exception e)
            {
                EditorUtility.DisplayDialog("Error", $"An error occurred: {e.Message}", "OK");
                Debug.LogError(e);
            }
        }
    }
}