using System;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UIElements;

namespace DivineDragon
{
    [FilePath("Assets/Editor/DivineSettings.settings", FilePathAttribute.Location.ProjectFolder)]
    public class DivineDragonSettingsScriptableObject : ScriptableSingleton<DivineDragonSettingsScriptableObject>
    {
        [SerializeField]
        string bundleOutputPath;

        [SerializeField] private bool openAfterBuildCheckbox;
        
        public String getBundleOutputPath()
        {
            return bundleOutputPath;
        }
        
        public void setOpenAfterBuild(bool openAfterBuild)
        {
            openAfterBuildCheckbox = openAfterBuild;
            Save(true);
        }
        
        public bool getOpenAfterBuild()
        {
            return openAfterBuildCheckbox;
        }
        public void setBundleOutputPath(string path)
        {
            bundleOutputPath = path;
            Save(true);
        }
    }

    /// <summary>
    /// Copied from https://www.kodeco.com/6452218-uielements-tutorial-for-unity-getting-started?page=2
    /// </summary>
    public class SettingsWindow: EditorWindow
    {
        private TextField bundleOutputPathField;
        
        [MenuItem("Divine Dragon/Divine Dragon Window")]
        public static void ShowSettings()
        {
            SettingsWindow wnd = GetWindow<SettingsWindow>();
            wnd.titleContent = new GUIContent("Divine Dragon Window");
        }
        
        public void OnEnable()
        {
            // 3
            // Each editor window contains a root VisualElement object
            VisualElement root = rootVisualElement;

            // 5
            // Import UXML
            var visualTree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>
                ("Packages/com.divinedragon.builder/Editor/DivineWindow.uxml");
            VisualElement divineWindow = visualTree.CloneTree();
            root.Add(divineWindow);
            
            InitializeBundleOutputField(divineWindow);
            InitializeBrowseButton(divineWindow);
            InitializeOpenOutputButton(divineWindow);
            InitializeBuildButton(divineWindow);
            InitializeOpenAfterBuildCheckbox(divineWindow);

        }

        private void InitializeOpenAfterBuildCheckbox(VisualElement divineWindow)
        {
            Toggle openAfterBuildCheckbox = divineWindow.Q<Toggle>("openAfterBuildCheckbox");
            openAfterBuildCheckbox.value = DivineDragonSettingsScriptableObject.instance.getOpenAfterBuild();
            openAfterBuildCheckbox.RegisterValueChangedCallback(evt =>
            {
                DivineDragonSettingsScriptableObject.instance.setOpenAfterBuild(evt.newValue);
            });
        }

        private void InitializeBundleOutputField(VisualElement divineWindow)
        {
            TextField bundleOutputField = divineWindow.Q<TextField>("BundleOutputField");
            
            bundleOutputPathField = bundleOutputField;
            bundleOutputField.value = DivineDragonSettingsScriptableObject.instance.getBundleOutputPath();
            
            // reflect edits of the field back to the scriptable object
            bundleOutputField.RegisterValueChangedCallback(evt =>
            {
                DivineDragonSettingsScriptableObject.instance.setBundleOutputPath(evt.newValue);
            });
        }
        
        private void InitializeBrowseButton(VisualElement divineWindow)
        {
            Button browseButton = divineWindow.Q<Button>("BrowseOutputPath");
            
            browseButton.clickable.clicked += () =>
            {
                var outputPath = EditorUtility.OpenFolderPanel("Choose a folder to save the output to", "", "");
                if (string.IsNullOrEmpty(outputPath))
                {
                    Debug.Log("no path to output?");
                    return;
                }
                DivineDragonSettingsScriptableObject.instance.setBundleOutputPath(outputPath);
                bundleOutputPathField.value = outputPath;
                Debug.Log("Set the output path to " + DivineDragonSettingsScriptableObject.instance.getBundleOutputPath());
            };
        }
        
        private void InitializeOpenOutputButton(VisualElement divineWindow)
        {
            Button openOutputButton = divineWindow.Q<Button>("OpenOutputPath");
            
            openOutputButton.clickable.clicked += () =>
            {
                EditorUtility.OpenWithDefaultApp(DivineDragonSettingsScriptableObject.instance.getBundleOutputPath());
            };
        }
        
        private void InitializeBuildButton(VisualElement divineWindow)
        {
            Button buildButton = divineWindow.Q<Button>("BuildAddressablesDivine");
            
            buildButton.clickable.clicked += () =>
            {
                if (String.IsNullOrEmpty(DivineDragonSettingsScriptableObject.instance.getBundleOutputPath()))
                {
                    EditorUtility.DisplayDialog("Error", "No output path set - please set one in this window", "OK");
                    return;
                }
                Build.BuildAddressableContent();
            };
        }

    }
}
