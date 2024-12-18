using System;
using System.IO;
using System.Net.Mime;
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
        // Path to our mods
        public string engageModsPath = "engage/mods";
            
        [SerializeField]
        string bundleOutputPath;

        [SerializeField] 
        string sdPath;

        [SerializeField] 
        string modPath;

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
        
        public void setSDCardPath(string path)
        {
            sdPath = path;
            Save(true);
        }
        
        public string getSDCardPath()
        {
            return sdPath;
        }
        
        public void setModPath(string path)
        {
            modPath = path;
            Save(true);
        }
        
        public string getModPath()
        {
            return modPath;
        }
    }

    /// <summary>
    /// Copied from https://www.kodeco.com/6452218-uielements-tutorial-for-unity-getting-started?page=2
    /// </summary>
    public class SettingsWindow : EditorWindow
    {
        private TextField bundleOutputPathField;
        private TextField sdPathField;
        private TextField modPathField;

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
            InitializeBrowseSDButton(divineWindow);
            InitializeSDCardField(divineWindow);
            InitializeBrowseModButton(divineWindow);
            InitializeModField(divineWindow);
            InitializeAutodetectRyujinxButton(divineWindow);
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
                Debug.Log("Set the output path to " +
                          DivineDragonSettingsScriptableObject.instance.getBundleOutputPath());
            };
        }

        private void InitializeSDCardField(VisualElement divineWindow)
        {
            TextField mySDCardField = divineWindow.Q<TextField>("SDCardField");

            sdPathField = mySDCardField;
            sdPathField.value = DivineDragonSettingsScriptableObject.instance.getSDCardPath();

            // reflect edits of the field back to the scriptable object
            sdPathField.RegisterValueChangedCallback(evt =>
            {
                DivineDragonSettingsScriptableObject.instance.setSDCardPath(evt.newValue);
            });
        }

        private void InitializeModField(VisualElement divineWindow)
        {
            TextField myModPathField = divineWindow.Q<TextField>("modPathField");

            modPathField = myModPathField;
            modPathField.value = DivineDragonSettingsScriptableObject.instance.getModPath();

            // reflect edits of the field back to the scriptable object
            modPathField.RegisterValueChangedCallback(evt =>
            {
                DivineDragonSettingsScriptableObject.instance.setModPath(evt.newValue);
            });
        }

        private void InitializeBrowseSDButton(VisualElement divineWindow)
        {
            Button browseButton = divineWindow.Q<Button>("BrowseSDPath");

            browseButton.clickable.clicked += () =>
            {
                var outputPath = EditorUtility.OpenFolderPanel("Choose your Ryujinx/console's sdcard path",
                    DivineDragonSettingsScriptableObject.instance.getSDCardPath(), "");
                if (string.IsNullOrEmpty(outputPath))
                {
                    Debug.Log("No SD card path was chosen");
                    return;
                }
                
                // Check if engage/mods already exists in the sd card path
                // If not, prompt the user if they would like it to be made
                if (!Directory.Exists(Path.Combine(outputPath, DivineDragonSettingsScriptableObject.instance.engageModsPath)))
                {
                    bool createEngageMods = EditorUtility.DisplayDialog("Create engage/mods?",
                        "The engage/mods folder does not exist in the chosen path. Would you like to create it?",
                        "Yes", "No");
                    if (createEngageMods)
                    {
                        Directory.CreateDirectory(Path.Combine(outputPath, DivineDragonSettingsScriptableObject.instance.engageModsPath));
                    }
                    else
                    {
                        Debug.Log("No engage/mods folder was created");
                    }
                }
                
                DivineDragonSettingsScriptableObject.instance.setSDCardPath(outputPath);
                sdPathField.value = outputPath;
                Debug.Log("Set the SD path to " + DivineDragonSettingsScriptableObject.instance.getSDCardPath());
            };
        }

        private void InitializeBrowseModButton(VisualElement divineWindow)
        {
            Button browseButton = divineWindow.Q<Button>("BrowseModPath");

            browseButton.clickable.clicked += () =>
            {
                var outputPath = EditorUtility.OpenFolderPanel("Choose your mod path",
                    !string.IsNullOrEmpty(DivineDragonSettingsScriptableObject.instance.getModPath())
                        ? DivineDragonSettingsScriptableObject.instance.getModPath()
                        : DivineDragonSettingsScriptableObject.instance.getSDCardPath() + '/' +
                          DivineDragonSettingsScriptableObject.instance.engageModsPath, "");
                if (string.IsNullOrEmpty(outputPath))
                {
                    Debug.Log("No mod path was chosen");
                    return;
                }

                DivineDragonSettingsScriptableObject.instance.setModPath(outputPath);
                modPathField.value = outputPath;
                Debug.Log("Set the mod path to " + DivineDragonSettingsScriptableObject.instance.getModPath());
            };
        }


        private void InitializeOpenOutputButton(VisualElement divineWindow)
        {
            Button openOutputButton = divineWindow.Q<Button>("OpenOutputPath");

            openOutputButton.clickable.clicked += () =>
            {
                EditorUtility.OpenWithDefaultApp(
                    DivineDragonSettingsScriptableObject.instance.getBundleOutputPath());
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

        private void InitializeAutodetectRyujinxButton(VisualElement divineWindow)
        {
            Button autodetectRyujinxButton = divineWindow.Q<Button>("AutodetectRyujinx");

            autodetectRyujinxButton.clickable.clicked += () =>
            {
                // Check if we're on Windows or Mac (sorry linux users)
                var platform = Application.platform;
                if (platform == RuntimePlatform.WindowsEditor)
                {
                    Debug.Log("handle windows");
                }
                if (platform == RuntimePlatform.OSXEditor)
                {
                    // Check if the standard Ryujinx path exists
                    var ryujinxSd = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library/Application Support/Ryujinx/sdcard");
                    if (Directory.Exists(ryujinxSd))
                    {
                        DivineDragonSettingsScriptableObject.instance.setSDCardPath(ryujinxSd);
                        sdPathField.value = ryujinxSd;
                        Debug.Log("Set the SD path to " + DivineDragonSettingsScriptableObject.instance.getSDCardPath());
                        // Show a unity popup that we've set the path
                        EditorUtility.DisplayDialog("Success", "Set the SD path to " + DivineDragonSettingsScriptableObject.instance.getSDCardPath(), "OK");
                    }
                    else
                    {
                        Debug.Log("Ryujinx path not found");
                        EditorUtility.DisplayDialog("Error", "Ryujinx path not found - did you modify your setup somehow?", "OK");
                    }
                }

                if (platform == RuntimePlatform.LinuxEditor)
                {
                    Debug.Log("sorry linux users");
                }
            };

        }
    }
}
