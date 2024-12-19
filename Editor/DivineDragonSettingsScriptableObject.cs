using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DivineDragon
{
    [FilePath("Assets/Editor/DivineSettings.settings", FilePathAttribute.Location.ProjectFolder)]
    public class DivineDragonSettingsScriptableObject : ScriptableSingleton<DivineDragonSettingsScriptableObject>
    {
        // Path to our mods
        public string engageModsPath = "engage/mods";

        [SerializeField] string sdPath;

        [SerializeField] string modPath;

        [SerializeField] private bool openAfterBuildCheckbox;

        public void setOpenAfterBuild(bool openAfterBuild)
        {
            openAfterBuildCheckbox = openAfterBuild;
            Save(true);
        }

        public bool getOpenAfterBuild()
        {
            return openAfterBuildCheckbox;
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
        private TextField sdPathField;
        private TextField modPathField;
        private Label buildStatusLabel;
        private Color cobaltBlue = new Color(0.0f, 0.28f, 0.67f, 1.0f);

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

            InitializeOpenAfterBuildCheckbox(divineWindow);
            InitializeBrowseSDButton(divineWindow);
            InitializeSDCardField(divineWindow);
            InitializeBrowseModButton(divineWindow);
            InitializeModField(divineWindow);
            InitializeAutodetectRyujinxButton(divineWindow);
            InitializeBuildButton(divineWindow);
            InitializeBuildStatusLabel(divineWindow);
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
                if (!Directory.Exists(Path.Combine(outputPath,
                        DivineDragonSettingsScriptableObject.instance.engageModsPath)))
                {
                    bool createEngageMods = EditorUtility.DisplayDialog("Create engage/mods?",
                        "The engage/mods folder does not exist in the chosen path. Would you like to create it?",
                        "Yes", "No");
                    if (createEngageMods)
                    {
                        Directory.CreateDirectory(Path.Combine(outputPath,
                            DivineDragonSettingsScriptableObject.instance.engageModsPath));
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

        private Button buildButton;
        private void InitializeBuildButton(VisualElement divineWindow)
        {
            buildButton = divineWindow.Q<Button>("BuildAddressablesDivine");

            buildButton.SetEnabled(!String.IsNullOrEmpty(DivineDragonSettingsScriptableObject.instance.getModPath()));

            buildButton.clickable.clicked += () =>
            {
                Build.BuildAddressableContent();
                buildStatusLabel.text = "Build complete at " + DateTime.Now + " ✔ (See debug logs for details)";
                buildStatusLabel.style.color = Color.green;
            };

            modPathField.RegisterValueChangedCallback(evt =>
            {
                buildButton.SetEnabled(!String.IsNullOrEmpty(evt.newValue));
            });
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
                    // Check if the standard Ryujinx path exists
                    var ryujinxSd = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Ryujinx\\sdcard");

                    if (Directory.Exists(ryujinxSd))
                    {
                        DivineDragonSettingsScriptableObject.instance.setSDCardPath(ryujinxSd);
                        sdPathField.value = ryujinxSd;
                        Debug.Log("Set the SD path to " +
                                  DivineDragonSettingsScriptableObject.instance.getSDCardPath());
                        // Show a unity popup that we've set the path
                        EditorUtility.DisplayDialog("Success",
                            "Set the SD path to " + DivineDragonSettingsScriptableObject.instance.getSDCardPath(),
                            "OK");
                    }
                    else
                    {
                        Debug.Log("Ryujinx path not found");
                        EditorUtility.DisplayDialog("Error",
                            "Ryujinx path not found - did you modify your setup somehow?", "OK");
                    }
                }

                if (platform == RuntimePlatform.OSXEditor)
                {
                    // Check if the standard Ryujinx path exists
                    var ryujinxSd = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                        "Library/Application Support/Ryujinx/sdcard");
                    if (Directory.Exists(ryujinxSd))
                    {
                        DivineDragonSettingsScriptableObject.instance.setSDCardPath(ryujinxSd);
                        sdPathField.value = ryujinxSd;
                        Debug.Log("Set the SD path to " +
                                  DivineDragonSettingsScriptableObject.instance.getSDCardPath());
                        // Show a unity popup that we've set the path
                        EditorUtility.DisplayDialog("Success",
                            "Set the SD path to " + DivineDragonSettingsScriptableObject.instance.getSDCardPath(),
                            "OK");
                    }
                    else
                    {
                        Debug.Log("Ryujinx path not found");
                        EditorUtility.DisplayDialog("Error",
                            "Ryujinx path not found - did you modify your setup somehow?", "OK");
                    }
                }

                if (platform == RuntimePlatform.LinuxEditor)
                {
                    Debug.Log("sorry linux users");
                }
            };
        }

        private void InitializeBuildStatusLabel(VisualElement divineWindow)
        {
            buildStatusLabel = divineWindow.Q<Label>("buildStatusLabel");

            // Initial update
            UpdateBuildStatus();

            // Register callbacks
            sdPathField.RegisterValueChangedCallback(evt => UpdateBuildStatus());
            modPathField.RegisterValueChangedCallback(evt => UpdateBuildStatus());
        }

        private void UpdateBuildStatus()
        {
            SetFieldBorderColorAndWidth(sdPathField, Color.clear, 1);
            SetFieldBorderColorAndWidth(modPathField, Color.clear, 1);
            SetFieldBorderColorAndWidth(buildButton, Color.clear, 1);

            if (string.IsNullOrEmpty(sdPathField.value))
            {
                buildStatusLabel.text = "Please set the SD path.";
                buildStatusLabel.style.color = Color.green;
                SetFieldBorderColorAndWidth(sdPathField, cobaltBlue, 1);
            }
            else if (string.IsNullOrEmpty(modPathField.value))
            {
                buildStatusLabel.text = "Please set the mod path.";
                buildStatusLabel.style.color = Color.green;
                SetFieldBorderColorAndWidth(modPathField, cobaltBlue, 1);
            }
            else
            {
                buildStatusLabel.text = "Ready to build.";
                buildStatusLabel.style.color = Color.green;
                SetFieldBorderColorAndWidth(buildButton, cobaltBlue, 1);
                
            }
        }

        private void SetFieldBorderColorAndWidth(VisualElement element, Color color, float width)
        {
            element.style.borderTopColor = color;
            element.style.borderBottomColor = color;
            element.style.borderLeftColor = color;
            element.style.borderRightColor = color;
            element.style.borderBottomWidth = width;
            element.style.borderTopWidth = width;
            element.style.borderLeftWidth = width;
            element.style.borderRightWidth = width;
        }
    }
}