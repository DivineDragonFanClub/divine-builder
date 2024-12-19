using System;
using System.Collections.Generic;
using System.Linq;
using Sherbert.Framework.Generic;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

namespace Code.Combat
{
    [CreateAssetMenu(fileName = "CostumeInfo", menuName = "Divine Dragon/CostumeInfo",
        order = 1)]
    public class Costume : ScriptableObject
    {
        public string Name;
        public GameObject CostumePrefab;

        [HideInInspector] public Gender gender;

        [HideInInspector] public ModelType modelType;

        [HideInInspector] [SerializeField] public SerializableDictionary<string, CostumeShopStrings> LocalizedStrings;
    }

    [Serializable]
    public class CostumeShopStrings
    {
        public string Description;
        public string Title;
    }

    public enum Gender
    {
        Male,
        Female
    }

    public enum ModelType
    {
        uBody,
        oBody
    }

    [CustomEditor(typeof(Costume))]
    public class CostumeEditor : UnityEditor.Editor
    {
        private readonly string[] costumeModelType = { "uBody", "oBody" };

        private readonly string[] genderTypes = { "Male", "Female" };

        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();
            var costume = target as Costume;
            var currentCostumeIndex = costume.modelType == ModelType.uBody ? 0 : 1;
            var costumeModelSelectedIndex =
                EditorGUILayout.Popup("Model Type", currentCostumeIndex, costumeModelType);
            var selectedModelType = costumeModelSelectedIndex == 0 ? ModelType.uBody : ModelType.oBody;
            costume.modelType = selectedModelType;

            var currentGenderIndex = costume.gender == Gender.Female ? 1 : 0;
            var genderSelectedIndex = EditorGUILayout.Popup("Gender", currentGenderIndex, genderTypes);
            var selectedGender = genderSelectedIndex == 0 ? Gender.Male : Gender.Female;
            costume.gender = selectedGender;

            if (GUILayout.Button("Setup Costume"))
            {
                var folderName = constructFolderName(costume.Name, selectedModelType, selectedGender);
                var prefabName = constructPrefabName(costume.Name, selectedModelType, selectedGender);

                var (folderPath, address) = ConstructPaths(folderName, selectedModelType);

                makeFoldersAsNeeded(folderPath);

                var prefabPath = string.Join("/", folderPath) + "/" + prefabName + ".prefab";

                if (AssetDatabase.GetAssetPath(costume.CostumePrefab).Equals(prefabPath))
                {
                    Debug.Log("Prefab already exists at path, nothing to delete or move.");
                }
                else
                {
                    // Delete what's currently in the addressable folder - there's no way this can be the same prefab
                    // since it's not at the same path
                    // Another possibility is there is no prefab at the addressable folder - in which case this will do nothing
                    AssetDatabase.DeleteAsset(prefabPath);

                    // Move our new prefab to the addressable folder
                    AssetDatabase.MoveAsset(AssetDatabase.GetAssetPath(costume.CostumePrefab),
                        prefabPath);
                }

                var prefabGUID = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(costume.CostumePrefab));
                var settings = AddressableAssetSettingsDefaultObject.Settings;
                var entry = settings.CreateOrMoveEntry(prefabGUID, settings.FindGroup("fe"));
                entry.address = $"{string.Join("/", address)}/{prefabName}";
                settings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryMoved, entry, true);
                AssetDatabase.SaveAssets();

                var costumeModArgs = new ModFileMaker.CostumeModArgs(prefabName, costume);
                var list = new List<ModFileMaker.CostumeModArgs> { costumeModArgs };
                ModFileMaker.MakeShopFile(list);
                ModFileMaker.MakeAssetTableFile(list);
                ModFileMaker.MakeItemFile(list);
                ModFileMaker.MakeAccessoriesFile(list);
            }
        }

        private static (List<string> folderPath, List<string> address) ConstructPaths(string constructedName,
            ModelType modelType)
        {
            var modelString = modelType == ModelType.uBody ? "uBody" : "oBody";

            var address = new List<string>
            {
                "Unit",
                "Model",
                modelString,
                constructedName,
                "c000",
                "Prefabs"
            };

            var folderPath = address.ToList();
            folderPath.InsertRange(0, new[]
            {
                "Assets",
                "Share",
                "Addressables"
            });

            return (folderPath, address);
        }

        private string constructPrefabName(string name, ModelType modelType, Gender gender)
        {
            // uBody_Pyr0AF_c000.prefab
            var modelString = modelType == ModelType.uBody ? "uBody" : "oBody";
            var genderString = gender == Gender.Male ? "m" : "f";
            return $"{modelString}_{name}0a{genderString}_c000";
        }

        private string constructFolderName(string name, ModelType modelType, Gender gender)
        {
            var genderString = gender == Gender.Male ? "m" : "f";
            return $"{name}0a{genderString}";
        }

        private void makeFoldersAsNeeded(List<string> path)
        {
            var currentPath = path[0];
            foreach (var folder in path.Skip(1))
            {
                var prePath = currentPath;
                currentPath += $"/{folder}";
                if (!AssetDatabase.IsValidFolder(currentPath))
                {
                    AssetDatabase.CreateFolder(prePath, folder);
                    Debug.Log($"Created folder {folder} at {currentPath}");
                }
            }
        }
    }
}