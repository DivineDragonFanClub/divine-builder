using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using DivineDragon.Patcher;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DivineDragon
{
    // Where a build gets delivered. The first three write to a local folder, FTP is scaffolded
    // (fields are saved but not yet wired into the build).
    public enum DeliveryTarget
    {
        Ryujinx,
        Yuzu,
        SdOrUsb,
        Ftp
    }

    // A saved FTP connection. The default one is anonymous on port 5000, which is what sys-ftpd
    // (the console's FTP sysmodule) uses.
    [Serializable]
    public class FtpProfile
    {
        public string name = "sys-ftpd";
        public string host = "";
        public int port = 5000;
        public bool anonymous = true;
        public string user = "";
        public string password = "";
    }

    [FilePath("Assets/Editor/DivineSettings.settings", FilePathAttribute.Location.ProjectFolder)]
    public class DivineDragonSettingsScriptableObject : ScriptableSingleton<DivineDragonSettingsScriptableObject>
    {
        // Path to our mods
        public string engageModsPath = "engage/mods";

        // Legacy single path, kept so existing setups still resolve. New writes go to the
        // per-target fields below so switching tabs doesn't clobber another target's path.
        [SerializeField] string sdPath;
        [SerializeField] private string ryujinxPath;
        [SerializeField] private string yuzuPath;
        [SerializeField] private string sdUsbPath;

        [SerializeField] string modPath;

        [SerializeField] private bool openAfterBuildCheckbox;

        [SerializeField] private bool useLegacyRustPatcher;

        public string gameCodeAssemblyName = "DivineDragon.GameCode";

        [SerializeField] private int deliveryTarget;
        // Legacy single-connection fields, kept only to migrate into the first profile.
        [SerializeField] private string ftpHost;
        [SerializeField] private int ftpPort = 5000;
        [SerializeField] private string ftpModName;
        [SerializeField] private List<FtpProfile> ftpProfiles = new List<FtpProfile>();
        [SerializeField] private int ftpProfileIndex;

        public void setUseLegacyRustPatcher(bool value)
        {
            useLegacyRustPatcher = value;
            Save(true);
        }

        public bool getUseLegacyRustPatcher()
        {
            return useLegacyRustPatcher;
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

        // The SD root is stored per folder target, so autodetecting Ryujinx doesn't leak into Yuzu.
        public void setSDCardPath(string path)
        {
            switch ((DeliveryTarget)deliveryTarget)
            {
                case DeliveryTarget.Yuzu: yuzuPath = path; break;
                case DeliveryTarget.SdOrUsb: sdUsbPath = path; break;
                default: ryujinxPath = path; break;
            }
            Save(true);
        }

        public string getSDCardPath()
        {
            switch ((DeliveryTarget)deliveryTarget)
            {
                case DeliveryTarget.Yuzu: return yuzuPath;
                case DeliveryTarget.SdOrUsb: return sdUsbPath;
                case DeliveryTarget.Ftp: return null;
                // Ryujinx falls back to the legacy field for setups saved before per-target paths.
                default: return string.IsNullOrEmpty(ryujinxPath) ? sdPath : ryujinxPath;
            }
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

        public DeliveryTarget getDeliveryTarget()
        {
            return (DeliveryTarget)deliveryTarget;
        }

        public void setDeliveryTarget(DeliveryTarget target)
        {
            deliveryTarget = (int)target;
            Save(true);
        }

        private void EnsureProfiles()
        {
            if (ftpProfiles == null)
                ftpProfiles = new List<FtpProfile>();
            if (ftpProfiles.Count == 0)
            {
                // Seed the default sys-ftpd profile, folding in any legacy connection that was set.
                ftpProfiles.Add(new FtpProfile
                {
                    name = "sys-ftpd",
                    host = ftpHost ?? "",
                    port = ftpPort <= 0 ? 5000 : ftpPort,
                    anonymous = true
                });
            }
            if (ftpProfileIndex < 0 || ftpProfileIndex >= ftpProfiles.Count)
                ftpProfileIndex = 0;
        }

        public List<FtpProfile> getFtpProfiles()
        {
            EnsureProfiles();
            return ftpProfiles;
        }

        public int getFtpProfileIndex()
        {
            EnsureProfiles();
            return ftpProfileIndex;
        }

        public void setFtpProfileIndex(int index)
        {
            EnsureProfiles();
            ftpProfileIndex = Mathf.Clamp(index, 0, ftpProfiles.Count - 1);
            Save(true);
        }

        public FtpProfile getActiveFtpProfile()
        {
            EnsureProfiles();
            return ftpProfiles[ftpProfileIndex];
        }

        public void addFtpProfile(FtpProfile profile)
        {
            EnsureProfiles();
            ftpProfiles.Add(profile);
            ftpProfileIndex = ftpProfiles.Count - 1;
            Save(true);
        }

        public void removeFtpProfile(int index)
        {
            EnsureProfiles();
            if (ftpProfiles.Count <= 1 || index < 0 || index >= ftpProfiles.Count)
                return;
            ftpProfiles.RemoveAt(index);
            ftpProfileIndex = Mathf.Clamp(ftpProfileIndex, 0, ftpProfiles.Count - 1);
            Save(true);
        }

        // Call after editing an active profile's fields in place, to persist the change.
        public void saveFtpProfiles()
        {
            Save(true);
        }

        public string getFtpHost() { return getActiveFtpProfile().host; }
        public void setFtpHost(string value) { getActiveFtpProfile().host = value; Save(true); }

        public int getFtpPort() { int p = getActiveFtpProfile().port; return p <= 0 ? 5000 : p; }
        public void setFtpPort(int value) { getActiveFtpProfile().port = value; Save(true); }

        public bool getFtpAnonymous() { return getActiveFtpProfile().anonymous; }
        public void setFtpAnonymous(bool value) { getActiveFtpProfile().anonymous = value; Save(true); }

        public string getFtpUser() { return getActiveFtpProfile().user; }
        public void setFtpUser(string value) { getActiveFtpProfile().user = value; Save(true); }

        public string getFtpPassword() { return getActiveFtpProfile().password; }
        public void setFtpPassword(string value) { getActiveFtpProfile().password = value; Save(true); }

        // For FTP this holds the remote mod folder name under engage/mods, not a local path.
        public string getFtpModName() { return ftpModName; }
        public void setFtpModName(string value) { ftpModName = value; Save(true); }
    }

    /// <summary>
    /// Copied from https://www.kodeco.com/6452218-uielements-tutorial-for-unity-getting-started?page=2
    /// </summary>
    public class SettingsWindow : EditorWindow
    {
        private TextField sdPathField;
        private TextField modPathField;
        private Label buildStatusLabel;
        private VisualElement buildResults;
        private ScrollView modList;
        private Button tabRyujinx;
        private Button tabYuzu;
        private Button tabSdUsb;
        private Button tabFtp;
        private Button detectButton;
        private VisualElement folderTargetPane;
        private VisualElement ftpTargetPane;
        private TextField ftpUserField;
        private TextField ftpPassField;
        private TextField ftpHostField;
        private TextField ftpPortField;
        private Toggle ftpAnonToggle;
        private Button ftpProfileButton;
        private VisualElement ftpCredsPane;
        private Label modSectionLabel;
        private Button browseModButton;
        private VisualElement modBody;
        private VisualElement modFoldHeader;
        private Label modFoldArrow;
        private Label modFoldSummary;
        private const string ModCollapsedPref = "DivineBuilder.ModSectionCollapsed";
        private VisualElement autoFitRoot;
        private float lastContentHeight = -1f;
        private Color cobaltBlue = new Color(0.0f, 0.28f, 0.67f, 1.0f);
        private Color okGreen = new Color(0.30f, 0.78f, 0.33f, 1.0f);
        private Color warnAmber = new Color(0.92f, 0.66f, 0.18f, 1.0f);
        private Color errRed = new Color(0.86f, 0.33f, 0.33f, 1.0f);

        [MenuItem("Divine Dragon/Divine Dragon Window #%d", false, 1501)]
        public static void ShowSettings()
        {
            // utility:true makes it a floating window with no dockable tab, matching the dumper.
            var wnd = GetWindow<SettingsWindow>(true, "Divine Builder");
            // Low floor so the window can auto-fit down to collapsed content, width kept comfortable.
            wnd.minSize = new Vector2(340, 200);
            var p = wnd.position;
            wnd.position = new Rect(p.x, p.y, Mathf.Max(p.width, 390f), Mathf.Max(p.height, 600f));
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
            // The cloned tree wrapper needs to fill the window so the ScrollView inside can too.
            divineWindow.style.flexGrow = 1;

            var styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>
                ("Packages/com.divinedragon.builder/Editor/DivineWindow.uss");
            if (styleSheet != null)
                root.styleSheets.Add(styleSheet);

            // Scroll vertically when the window is short so the cards aren't crushed/clipped.
            // 2020.3's ScrollView has no vertical-only mode, so just hide the horizontal scroller.
            var rootScroll = divineWindow.Q<ScrollView>("RootScroll");
            if (rootScroll != null)
                rootScroll.horizontalScroller.style.display = DisplayStyle.None;

            InitializeOpenAfterBuildCheckbox(divineWindow);
            InitializeLegacyPatcherCheckbox(divineWindow);
            InitializeTargetSelector(divineWindow);
            InitializeBrowseSDButton(divineWindow);
            InitializeSDCardField(divineWindow);
            InitializeFtpFields(divineWindow);
            InitializeBrowseModButton(divineWindow);
            InitializeModField(divineWindow);
            InitializeModList(divineWindow);
            InitializeModCollapse(divineWindow);
            InitializeDetectButton(divineWindow);
            InitializeBuildButton(divineWindow);
            InitializeBuildResults(divineWindow);
            InitializeBuildStatusLabel(divineWindow);

            // Everything exists now, so paint the target-specific layout once.
            UpdateTargetTabs();
            ApplyTargetLayout();
            PrefillCurrentTarget();
            SyncModField();
            RefreshModList();
            UpdateBuildStatus();

            // Grow/shrink the window to match content when things collapse, expand or swap.
            autoFitRoot = divineWindow.Q<VisualElement>("Root");
            if (autoFitRoot != null)
                autoFitRoot.RegisterCallback<GeometryChangedEvent>(evt => FitWindowToContent());
        }

        private void FitWindowToContent()
        {
            if (autoFitRoot == null)
                return;

            float content = autoFitRoot.resolvedStyle.height;
            if (float.IsNaN(content) || content <= 1f)
                return;

            // Only react when the content's own height changed, not when the user drags the window.
            if (Mathf.Abs(content - lastContentHeight) < 1f)
                return;
            lastContentHeight = content;

            // The window frame is a bit taller than the content area (title bar), so measure that gap.
            float client = rootVisualElement.resolvedStyle.height;
            float chrome = position.height - client;
            if (float.IsNaN(chrome) || chrome < 0f)
                chrome = 0f;

            float target = Mathf.Min(content + chrome, 1000f);
            var p = position;
            if (Mathf.Abs(p.height - target) > 1f)
            {
                p.height = target;
                position = p;
            }
        }

        private void InitializeBuildResults(VisualElement divineWindow)
        {
            buildResults = divineWindow.Q<VisualElement>("buildResults");
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

        private void InitializeLegacyPatcherCheckbox(VisualElement divineWindow)
        {
            Toggle legacyToggle = divineWindow.Q<Toggle>("useLegacyRustPatcherCheckbox");
            if (legacyToggle == null)
                return;
            legacyToggle.value = DivineDragonSettingsScriptableObject.instance.getUseLegacyRustPatcher();
            legacyToggle.RegisterValueChangedCallback(evt =>
            {
                DivineDragonSettingsScriptableObject.instance.setUseLegacyRustPatcher(evt.newValue);
            });
        }

        private void InitializeSDCardField(VisualElement divineWindow)
        {
            TextField mySDCardField = divineWindow.Q<TextField>("SDCardField");

            sdPathField = mySDCardField;
            sdPathField.value = DivineDragonSettingsScriptableObject.instance.getSDCardPath() ?? "";

            // reflect edits of the field back to the scriptable object
            sdPathField.RegisterValueChangedCallback(evt =>
            {
                DivineDragonSettingsScriptableObject.instance.setSDCardPath(evt.newValue);
            });
        }

        private void InitializeModField(VisualElement divineWindow)
        {
            modSectionLabel = divineWindow.Q<Label>("modlabel");
            modPathField = divineWindow.Q<TextField>("modPathField");
            modPathField.value = DivineDragonSettingsScriptableObject.instance.getModPath();

            // For FTP the field holds a remote mod name, otherwise a local folder path.
            modPathField.RegisterValueChangedCallback(evt =>
            {
                if (IsFtp())
                    DivineDragonSettingsScriptableObject.instance.setFtpModName(evt.newValue);
                else
                    DivineDragonSettingsScriptableObject.instance.setModPath(evt.newValue);
            });
        }

        // Show the right stored value (local path vs FTP mod name) without firing the write-back.
        private void SyncModField()
        {
            if (modPathField == null)
                return;
            var s = DivineDragonSettingsScriptableObject.instance;
            modPathField.SetValueWithoutNotify(IsFtp() ? (s.getFtpModName() ?? "") : (s.getModPath() ?? ""));
            UpdateFoldSummary();
        }

        private void InitializeBrowseSDButton(VisualElement divineWindow)
        {
            Button browseButton = divineWindow.Q<Button>("BrowseSDPath");

            browseButton.clickable.clicked += () =>
            {
                var outputPath = EditorUtility.OpenFolderPanel("Choose your destination's SD root",
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
            browseModButton = divineWindow.Q<Button>("BrowseModPath");

            browseModButton.clickable.clicked += () =>
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

        private void InitializeModCollapse(VisualElement divineWindow)
        {
            modFoldHeader = divineWindow.Q<VisualElement>("modHeader");
            modBody = divineWindow.Q<VisualElement>("modBody");
            modFoldArrow = divineWindow.Q<Label>("modFoldArrow");
            modFoldSummary = divineWindow.Q<Label>("modFoldSummary");
            if (modFoldHeader == null)
                return;

            ApplyModCollapsed(EditorPrefs.GetBool(ModCollapsedPref, false));
            // Keep the collapsed summary current whenever the selected mod/path changes.
            if (modPathField != null)
                modPathField.RegisterValueChangedCallback(evt => UpdateFoldSummary());

            modFoldHeader.RegisterCallback<MouseDownEvent>(evt =>
            {
                bool collapsed = !EditorPrefs.GetBool(ModCollapsedPref, false);
                EditorPrefs.SetBool(ModCollapsedPref, collapsed);
                ApplyModCollapsed(collapsed);
            });
        }

        private void ApplyModCollapsed(bool collapsed)
        {
            if (modBody != null)
                modBody.style.display = collapsed ? DisplayStyle.None : DisplayStyle.Flex;
            if (modFoldArrow != null)
                modFoldArrow.text = collapsed ? "▸" : "▾";
            // Drop the header's bottom gap when there's no body under it, so the title stays centered.
            if (modFoldHeader != null)
                modFoldHeader.style.marginBottom = collapsed ? 0 : 6;
            UpdateFoldSummary();
        }

        // When collapsed, show the selected mod/path next to the title so you can read it folded.
        private void UpdateFoldSummary()
        {
            if (modFoldSummary == null)
                return;

            bool collapsed = EditorPrefs.GetBool(ModCollapsedPref, false);
            string value = modPathField != null ? modPathField.value : "";
            string shown = "";
            if (!string.IsNullOrEmpty(value))
                shown = IsFtp() ? value : Path.GetFileName(value.TrimEnd(Path.DirectorySeparatorChar));

            modFoldSummary.text = shown;
            modFoldSummary.style.display = (collapsed && !string.IsNullOrEmpty(shown))
                ? DisplayStyle.Flex
                : DisplayStyle.None;
        }

        private void InitializeModList(VisualElement divineWindow)
        {
            modList = divineWindow.Q<ScrollView>("modList");
            if (modList != null)
                modList.horizontalScroller.style.display = DisplayStyle.None;

            Button addBtn = divineWindow.Q<Button>("AddMod");
            Button renameBtn = divineWindow.Q<Button>("RenameMod");
            Button delBtn = divineWindow.Q<Button>("DeleteMod");
            if (addBtn != null)
                addBtn.clickable.clicked += AddMod;
            if (renameBtn != null)
                renameBtn.clickable.clicked += RenameSelectedMod;
            if (delBtn != null)
                delBtn.clickable.clicked += DeleteSelectedMod;

            // Keep the highlighted row in sync when the path changes from Browse or manual edits.
            modPathField.RegisterValueChangedCallback(evt => UpdateModSelection());
            // A new sdcard path means a different engage/mods folder, so rebuild the list.
            if (sdPathField != null)
                sdPathField.RegisterValueChangedCallback(evt => RefreshModList());
        }

        private void RefreshModList()
        {
            if (modList == null)
                return;

            modList.Clear();

            if (IsFtp())
            {
                RefreshFtpModList();
                return;
            }

            string modsDir = GetModsDirectory();
            if (string.IsNullOrEmpty(modsDir) || !Directory.Exists(modsDir))
            {
                modList.Add(MakeModPlaceholder("Set a valid destination to list your mods."));
                return;
            }

            var dirs = Directory.GetDirectories(modsDir)
                .OrderBy(d => Path.GetFileName(d), StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (dirs.Count == 0)
            {
                modList.Add(MakeModPlaceholder("No mods yet, use + to create one."));
                return;
            }

            foreach (var dir in dirs)
                modList.Add(MakeModRow(dir));

            UpdateModSelection();
        }

        // No automatic connecting here: a LIST on an offline device blocks until the timeout and
        // freezes the editor. Just offer a button to list on demand.
        private void RefreshFtpModList()
        {
            var s = DivineDragonSettingsScriptableObject.instance;
            if (string.IsNullOrEmpty(s.getFtpHost()))
            {
                modList.Add(MakeModPlaceholder("Set the FTP host to list your mods."));
                return;
            }

            var box = new VisualElement();
            box.style.paddingTop = 8;
            box.style.paddingBottom = 8;
            box.style.paddingLeft = 8;
            box.style.paddingRight = 8;

            var label = new Label("Connect to list the mods on the device.");
            label.style.whiteSpace = WhiteSpace.Normal;
            label.style.opacity = 0.7f;
            box.Add(label);

            var listButton = new Button(ConnectAndListFtpMods) { text = "List mods" };
            listButton.AddToClassList("dd-btn");
            listButton.AddToClassList("dd-btn-ghost");
            listButton.style.alignSelf = Align.FlexStart;
            listButton.style.marginLeft = 0;
            listButton.style.marginTop = 6;
            box.Add(listButton);

            modList.Add(box);
        }

        // Actually connects and lists, only on an explicit user action (List button, or after an
        // add/delete/rename/test that already talked to the device).
        private void ConnectAndListFtpMods()
        {
            if (modList == null)
                return;

            modList.Clear();

            var s = DivineDragonSettingsScriptableObject.instance;
            string host = s.getFtpHost();
            int port = s.getFtpPort();
            if (string.IsNullOrEmpty(host))
            {
                modList.Add(MakeModPlaceholder("Set the FTP host to list your mods."));
                return;
            }

            List<string> names;
            try
            {
                // Mods are folders, so keep only directory entries from the listing.
                names = MakeFtpClient().ListDetailed(s.engageModsPath)
                    .Where(entry => entry.Value)
                    .Select(entry => entry.Key)
                    .ToList();
            }
            catch (System.Net.WebException we) when (IsMissingDirectory(we))
            {
                // Server is reachable, it just has no engage/mods folder yet.
                FtpStatus.Set(host, port, true);
                ShowFtpMissingModsFolder();
                UpdateBuildStatus();
                return;
            }
            catch (Exception e)
            {
                FtpStatus.Set(host, port, false);
                modList.Add(MakeModPlaceholder("Couldn't reach the FTP server: " + e.Message));
                UpdateBuildStatus();
                return;
            }

            FtpStatus.Set(host, port, true);

            names.Sort(StringComparer.OrdinalIgnoreCase);
            if (names.Count == 0)
            {
                modList.Add(MakeModPlaceholder("No mods yet, use New mod to create one."));
                UpdateBuildStatus();
                return;
            }

            foreach (var name in names)
                modList.Add(MakeFtpModRow(name));

            UpdateModSelection();
            UpdateBuildStatus();
        }

        // A LIST on a path that isn't there comes back as an FTP 550. That means the server is up
        // but engage/mods hasn't been made yet, which is different from not reaching it at all.
        private static bool IsMissingDirectory(System.Net.WebException we)
        {
            if (we.Status != System.Net.WebExceptionStatus.ProtocolError)
                return false;
            var resp = we.Response as System.Net.FtpWebResponse;
            return resp != null &&
                resp.StatusCode == System.Net.FtpStatusCode.ActionNotTakenFileUnavailable;
        }

        private void ShowFtpMissingModsFolder()
        {
            var s = DivineDragonSettingsScriptableObject.instance;

            var box = new VisualElement();
            box.style.paddingTop = 8;
            box.style.paddingBottom = 8;
            box.style.paddingLeft = 8;
            box.style.paddingRight = 8;

            var label = new Label("The " + s.engageModsPath + " folder doesn't exist on the server yet.");
            label.style.whiteSpace = WhiteSpace.Normal;
            label.style.opacity = 0.7f;
            box.Add(label);

            var create = new Button(() =>
            {
                try
                {
                    MakeFtpClient().EnsureDirectory(s.engageModsPath);
                }
                catch (Exception e)
                {
                    EditorUtility.DisplayDialog("Couldn't create folder", e.Message, "OK");
                    return;
                }
                ConnectAndListFtpMods();
                UpdateBuildStatus();
            })
            {
                text = "Create " + s.engageModsPath
            };
            create.AddToClassList("dd-btn");
            create.AddToClassList("dd-btn-ghost");
            create.style.alignSelf = Align.FlexStart;
            create.style.marginLeft = 0;
            create.style.marginTop = 6;
            box.Add(create);

            modList.Add(box);
        }

        private VisualElement MakeFtpModRow(string modName)
        {
            var row = new VisualElement();
            row.AddToClassList("dd-mod-row");
            row.userData = modName;

            var label = new Label(modName);
            label.style.flexGrow = 1;
            row.Add(label);

            row.RegisterCallback<MouseDownEvent>(evt =>
            {
                DivineDragonSettingsScriptableObject.instance.setFtpModName(modName);
                SyncModField();
                UpdateModSelection();
                UpdateBuildStatus();
            });

            return row;
        }

        private VisualElement MakeModRow(string fullPath)
        {
            var row = new VisualElement();
            row.AddToClassList("dd-mod-row");
            row.userData = fullPath;

            var label = new Label(Path.GetFileName(fullPath));
            label.style.flexGrow = 1;
            row.Add(label);

            // Clicking a row picks that mod. The value-changed callback repaints the selection.
            row.RegisterCallback<MouseDownEvent>(evt =>
            {
                DivineDragonSettingsScriptableObject.instance.setModPath(fullPath);
                modPathField.value = fullPath;
            });

            return row;
        }

        private VisualElement MakeModPlaceholder(string text)
        {
            var label = new Label(text);
            label.AddToClassList("dd-mod-empty");
            return label;
        }

        private void UpdateModSelection()
        {
            if (modList == null)
                return;

            bool ftp = IsFtp();
            string currentName = ftp ? DivineDragonSettingsScriptableObject.instance.getFtpModName() : null;
            string currentPath = (!ftp && !string.IsNullOrEmpty(modPathField.value))
                ? Path.GetFullPath(modPathField.value).TrimEnd(Path.DirectorySeparatorChar)
                : null;

            foreach (var child in modList.Children())
            {
                if (!(child.userData is string entry))
                    continue;

                bool selected;
                if (ftp)
                {
                    selected = !string.IsNullOrEmpty(currentName) &&
                        string.Equals(entry, currentName, StringComparison.OrdinalIgnoreCase);
                }
                else
                {
                    selected = currentPath != null &&
                        string.Equals(Path.GetFullPath(entry).TrimEnd(Path.DirectorySeparatorChar),
                            currentPath, StringComparison.OrdinalIgnoreCase);
                }

                if (selected)
                    child.AddToClassList("dd-mod-row-selected");
                else
                    child.RemoveFromClassList("dd-mod-row-selected");
            }
        }

        private void AddMod()
        {
            if (IsFtp())
            {
                AddFtpMod();
                return;
            }

            string modsDir = GetModsDirectory();
            if (string.IsNullOrEmpty(modsDir))
            {
                EditorUtility.DisplayDialog("No destination",
                    "Set a valid destination first so I know where engage/mods is.", "OK");
                return;
            }

            if (!Directory.Exists(modsDir))
                Directory.CreateDirectory(modsDir);

            TextPromptWindow.Show("New mod", "Name your new mod folder:", "NewMod", "Create", rawName =>
            {
                string name = (rawName ?? "").Trim();
                if (string.IsNullOrEmpty(name))
                    return;

                if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                {
                    EditorUtility.DisplayDialog("Invalid name",
                        "That name has characters that aren't allowed in a folder name.", "OK");
                    return;
                }

                string full = Path.Combine(modsDir, name);
                if (Directory.Exists(full))
                {
                    EditorUtility.DisplayDialog("Already exists",
                        $"A mod folder named \"{name}\" already exists.", "OK");
                    return;
                }

                Directory.CreateDirectory(full);
                DivineDragonSettingsScriptableObject.instance.setModPath(full);
                modPathField.value = full;
                RefreshModList();
            });
        }

        private void AddFtpMod()
        {
            var s = DivineDragonSettingsScriptableObject.instance;
            if (string.IsNullOrEmpty(s.getFtpHost()))
            {
                EditorUtility.DisplayDialog("No FTP host",
                    "Set the FTP host first so I know where to create the mod.", "OK");
                return;
            }

            TextPromptWindow.Show("New mod", "Name your new mod folder:", "NewMod", "Create", rawName =>
            {
                string name = (rawName ?? "").Trim();
                if (string.IsNullOrEmpty(name))
                    return;

                if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                {
                    EditorUtility.DisplayDialog("Invalid name",
                        "That name has characters that aren't allowed in a folder name.", "OK");
                    return;
                }

                try
                {
                    MakeFtpClient().EnsureDirectory(s.engageModsPath.TrimEnd('/') + "/" + name);
                }
                catch (Exception e)
                {
                    EditorUtility.DisplayDialog("Couldn't create mod", e.Message, "OK");
                    return;
                }

                s.setFtpModName(name);
                SyncModField();
                ConnectAndListFtpMods();
                UpdateBuildStatus();
            });
        }

        private void DeleteSelectedMod()
        {
            if (IsFtp())
            {
                DeleteFtpMod();
                return;
            }

            string current = modPathField.value;
            if (string.IsNullOrEmpty(current) || !Directory.Exists(current))
            {
                EditorUtility.DisplayDialog("No mod selected", "Pick a mod from the list first.", "OK");
                return;
            }

            string modsDir = GetModsDirectory();
            string fullCurrent = Path.GetFullPath(current).TrimEnd(Path.DirectorySeparatorChar);
            string fullMods = string.IsNullOrEmpty(modsDir)
                ? null
                : Path.GetFullPath(modsDir).TrimEnd(Path.DirectorySeparatorChar);

            // Safety net: only delete things that actually live inside engage/mods.
            bool underMods = fullMods != null &&
                fullCurrent.StartsWith(fullMods + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
            if (!underMods)
            {
                EditorUtility.DisplayDialog("Can't delete this",
                    "For safety, only mod folders inside engage/mods can be deleted here. " +
                    "Remove anything else yourself in a file browser.", "OK");
                return;
            }

            string name = Path.GetFileName(fullCurrent);
            ConfirmByTypingWindow.Show("Delete mod?",
                $"This permanently deletes the mod \"{name}\" and everything in it.\n{fullCurrent}\n\n" +
                "Type the mod name to confirm.",
                name, "Delete", () =>
            {
                try
                {
                    Directory.Delete(fullCurrent, true);
                }
                catch (Exception e)
                {
                    EditorUtility.DisplayDialog("Delete failed", e.Message, "OK");
                    return;
                }

                DivineDragonSettingsScriptableObject.instance.setModPath("");
                modPathField.value = "";
                RefreshModList();
            });
        }

        private void DeleteFtpMod()
        {
            var s = DivineDragonSettingsScriptableObject.instance;
            string name = s.getFtpModName();
            if (string.IsNullOrEmpty(name))
            {
                EditorUtility.DisplayDialog("No mod selected", "Pick a mod from the list first.", "OK");
                return;
            }

            ConfirmByTypingWindow.Show("Delete mod?",
                $"This permanently deletes the remote mod \"{name}\" and everything in it.\n\n" +
                "Type the mod name to confirm.",
                name, "Delete", () =>
            {
                try
                {
                    MakeFtpClient().DeleteDirectoryRecursive(s.engageModsPath.TrimEnd('/') + "/" + name);
                }
                catch (Exception e)
                {
                    EditorUtility.DisplayDialog("Delete failed", e.Message, "OK");
                    return;
                }

                s.setFtpModName("");
                SyncModField();
                ConnectAndListFtpMods();
                UpdateBuildStatus();
            });
        }

        private void RenameSelectedMod()
        {
            if (IsFtp())
            {
                RenameFtpMod();
                return;
            }

            string current = modPathField.value;
            if (string.IsNullOrEmpty(current) || !Directory.Exists(current))
            {
                EditorUtility.DisplayDialog("No mod selected", "Pick a mod from the list first.", "OK");
                return;
            }

            string modsDir = GetModsDirectory();
            string fullCurrent = Path.GetFullPath(current).TrimEnd(Path.DirectorySeparatorChar);
            string fullMods = string.IsNullOrEmpty(modsDir)
                ? null
                : Path.GetFullPath(modsDir).TrimEnd(Path.DirectorySeparatorChar);

            // Same safety net as delete: only touch folders inside engage/mods.
            bool underMods = fullMods != null &&
                fullCurrent.StartsWith(fullMods + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
            if (!underMods)
            {
                EditorUtility.DisplayDialog("Can't rename this",
                    "For safety, only mod folders inside engage/mods can be renamed here.", "OK");
                return;
            }

            string oldName = Path.GetFileName(fullCurrent);
            TextPromptWindow.Show("Rename mod", "New name:", oldName, "Rename", raw =>
            {
                string name = (raw ?? "").Trim();
                if (string.IsNullOrEmpty(name) || name == oldName)
                    return;

                if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                {
                    EditorUtility.DisplayDialog("Invalid name",
                        "That name has characters that aren't allowed in a folder name.", "OK");
                    return;
                }

                string target = Path.Combine(Path.GetDirectoryName(fullCurrent), name);
                if (Directory.Exists(target))
                {
                    EditorUtility.DisplayDialog("Already exists",
                        $"A mod folder named \"{name}\" already exists.", "OK");
                    return;
                }

                try
                {
                    Directory.Move(fullCurrent, target);
                }
                catch (Exception e)
                {
                    EditorUtility.DisplayDialog("Rename failed", e.Message, "OK");
                    return;
                }

                DivineDragonSettingsScriptableObject.instance.setModPath(target);
                modPathField.value = target;
                RefreshModList();
            });
        }

        private void RenameFtpMod()
        {
            var s = DivineDragonSettingsScriptableObject.instance;
            string oldName = s.getFtpModName();
            if (string.IsNullOrEmpty(oldName))
            {
                EditorUtility.DisplayDialog("No mod selected", "Pick a mod from the list first.", "OK");
                return;
            }

            TextPromptWindow.Show("Rename mod", "New name:", oldName, "Rename", raw =>
            {
                string name = (raw ?? "").Trim();
                if (string.IsNullOrEmpty(name) || name == oldName)
                    return;

                if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                {
                    EditorUtility.DisplayDialog("Invalid name",
                        "That name has characters that aren't allowed in a folder name.", "OK");
                    return;
                }

                string basePath = s.engageModsPath.TrimEnd('/');
                try
                {
                    MakeFtpClient().Rename(basePath + "/" + oldName, name);
                }
                catch (Exception e)
                {
                    EditorUtility.DisplayDialog("Rename failed", e.Message, "OK");
                    return;
                }

                s.setFtpModName(name);
                SyncModField();
                ConnectAndListFtpMods();
                UpdateBuildStatus();
            });
        }

        // The Cobalt mods live under <sdcard>/engage/mods.
        private static string GetModsDirectory()
        {
            string sd = DivineDragonSettingsScriptableObject.instance.getSDCardPath();
            if (string.IsNullOrEmpty(sd))
                return null;
            return Path.Combine(sd, DivineDragonSettingsScriptableObject.instance.engageModsPath);
        }

        private Button buildButton;
        private void InitializeBuildButton(VisualElement divineWindow)
        {
            buildButton = divineWindow.Q<Button>("BuildAddressablesDivine");

            buildButton.SetEnabled(!String.IsNullOrEmpty(DivineDragonSettingsScriptableObject.instance.getModPath()));

            buildButton.clickable.clicked += () =>
            {
                var outcome = Build.BuildAddressableContent();
                RenderBuildOutcome(outcome);
            };

            modPathField.RegisterValueChangedCallback(evt =>
            {
                buildButton.SetEnabled(!String.IsNullOrEmpty(evt.newValue));
            });
        }

        private void InitializeTargetSelector(VisualElement divineWindow)
        {
            folderTargetPane = divineWindow.Q<VisualElement>("folderTarget");
            ftpTargetPane = divineWindow.Q<VisualElement>("ftpTarget");

            tabRyujinx = divineWindow.Q<Button>("TabRyujinx");
            tabYuzu = divineWindow.Q<Button>("TabYuzu");
            tabSdUsb = divineWindow.Q<Button>("TabSdUsb");
            tabFtp = divineWindow.Q<Button>("TabFtp");

            WireTab(tabRyujinx, DeliveryTarget.Ryujinx);
            WireTab(tabYuzu, DeliveryTarget.Yuzu);
            WireTab(tabSdUsb, DeliveryTarget.SdOrUsb);
            WireTab(tabFtp, DeliveryTarget.Ftp);
        }

        private void WireTab(Button tab, DeliveryTarget t)
        {
            if (tab == null)
                return;
            tab.clickable.clicked += () => SelectTarget(t);
        }

        private void UpdateTargetTabs()
        {
            var current = DivineDragonSettingsScriptableObject.instance.getDeliveryTarget();
            SetTabActive(tabRyujinx, current == DeliveryTarget.Ryujinx);
            SetTabActive(tabYuzu, current == DeliveryTarget.Yuzu);
            SetTabActive(tabSdUsb, current == DeliveryTarget.SdOrUsb);
            SetTabActive(tabFtp, current == DeliveryTarget.Ftp);
        }

        private static void SetTabActive(Button tab, bool active)
        {
            if (tab == null)
                return;
            if (active)
                tab.AddToClassList("dd-tab-active");
            else
                tab.RemoveFromClassList("dd-tab-active");
        }

        private void SelectTarget(DeliveryTarget t)
        {
            DivineDragonSettingsScriptableObject.instance.setDeliveryTarget(t);
            UpdateTargetTabs();
            ApplyTargetLayout();
            PrefillCurrentTarget();
            SyncModField();
            RefreshModList();
            UpdateBuildStatus();
        }

        // Show the stored SD root for the active target, autodetecting emulators when it's empty
        // so the field comes up prefilled instead of blank (and never with another target's path).
        private void PrefillCurrentTarget()
        {
            var s = DivineDragonSettingsScriptableObject.instance;
            var t = s.getDeliveryTarget();
            if (t == DeliveryTarget.Ftp)
                return;

            if (string.IsNullOrEmpty(s.getSDCardPath()))
            {
                string found = AutoDetectSilent(t);
                if (!string.IsNullOrEmpty(found))
                    s.setSDCardPath(found);
            }
            SyncSdField();
        }

        private void SyncSdField()
        {
            if (sdPathField == null)
                return;
            sdPathField.SetValueWithoutNotify(DivineDragonSettingsScriptableObject.instance.getSDCardPath() ?? "");
        }

        // Silent lookup (no dialogs), used when prefilling on tab switch. SD/USB has no
        // deterministic path, so it stays empty for the user to pick a volume.
        private static string AutoDetectSilent(DeliveryTarget t)
        {
            switch (t)
            {
                case DeliveryTarget.Ryujinx: return FindRyujinxSdCard();
                case DeliveryTarget.Yuzu: return FindYuzuSdCard();
                default: return null;
            }
        }

        private static FtpClient MakeFtpClient()
        {
            var s = DivineDragonSettingsScriptableObject.instance;
            return new FtpClient(s.getFtpHost(), s.getFtpPort(), s.getFtpAnonymous(),
                s.getFtpUser(), s.getFtpPassword());
        }

        // Swap the folder controls for the FTP fields, and relabel the detect button for the target.
        private void ApplyTargetLayout()
        {
            var t = DivineDragonSettingsScriptableObject.instance.getDeliveryTarget();
            bool ftp = t == DeliveryTarget.Ftp;

            if (folderTargetPane != null)
                folderTargetPane.style.display = ftp ? DisplayStyle.None : DisplayStyle.Flex;
            if (ftpTargetPane != null)
                ftpTargetPane.style.display = ftp ? DisplayStyle.Flex : DisplayStyle.None;

            if (detectButton != null)
            {
                switch (t)
                {
                    case DeliveryTarget.Ryujinx: detectButton.text = "Autodetect Ryujinx"; break;
                    case DeliveryTarget.Yuzu: detectButton.text = "Autodetect Yuzu"; break;
                    case DeliveryTarget.SdOrUsb: detectButton.text = "Pick volume ▾"; break;
                }
            }

            // Over FTP the mod field is a remote name and browsing a local folder makes no sense.
            if (modSectionLabel != null)
                modSectionLabel.text = ftp ? "Mod name" : "Mod path";
            if (browseModButton != null)
                browseModButton.style.display = ftp ? DisplayStyle.None : DisplayStyle.Flex;
        }

        private void InitializeDetectButton(VisualElement divineWindow)
        {
            detectButton = divineWindow.Q<Button>("DetectButton");
            if (detectButton == null)
                return;

            detectButton.clickable.clicked += () =>
            {
                switch (DivineDragonSettingsScriptableObject.instance.getDeliveryTarget())
                {
                    case DeliveryTarget.Ryujinx:
                        ApplyDetectedPath("Ryujinx", FindRyujinxSdCard());
                        break;
                    case DeliveryTarget.Yuzu:
                        ApplyDetectedPath("Yuzu", FindYuzuSdCard());
                        break;
                    case DeliveryTarget.SdOrUsb:
                        ShowVolumeMenu();
                        break;
                }
            };
        }

        private void ApplyDetectedPath(string label, string found)
        {
            if (string.IsNullOrEmpty(found))
            {
                EditorUtility.DisplayDialog(label + " not found",
                    "Could not find a " + label + " SD folder in the usual place for this OS. " +
                    "Set it manually with Browse.", "OK");
                return;
            }

            DivineDragonSettingsScriptableObject.instance.setSDCardPath(found);
            sdPathField.value = found;
            Debug.Log("Set the destination to " + found);
            EditorUtility.DisplayDialog("Success", "Set the destination to:\n" + found, "OK");
        }

        private void ShowVolumeMenu()
        {
            var volumes = FindMountedVolumes();
            var menu = new GenericMenu();
            if (volumes.Count == 0)
            {
                menu.AddDisabledItem(new GUIContent("No removable volumes found, use Browse instead"));
            }
            else
            {
                foreach (var vol in volumes)
                {
                    string full = vol;
                    string name = Path.GetFileName(full.TrimEnd(Path.DirectorySeparatorChar));
                    if (string.IsNullOrEmpty(name))
                        name = full; // e.g. a Windows drive root like E:\
                    menu.AddItem(new GUIContent(name), false, () =>
                    {
                        DivineDragonSettingsScriptableObject.instance.setSDCardPath(full);
                        sdPathField.value = full;
                    });
                }
            }
            menu.DropDown(detectButton.worldBound);
        }

        // Removable drives / mounted volumes, where a console SD or USB shows up per OS.
        private static System.Collections.Generic.List<string> FindMountedVolumes()
        {
            var list = new System.Collections.Generic.List<string>();
            switch (Application.platform)
            {
                case RuntimePlatform.OSXEditor:
                    if (Directory.Exists("/Volumes"))
                        list.AddRange(Directory.GetDirectories("/Volumes"));
                    break;
                case RuntimePlatform.WindowsEditor:
                    foreach (var drive in DriveInfo.GetDrives())
                        if (drive.IsReady && drive.DriveType == DriveType.Removable)
                            list.Add(drive.RootDirectory.FullName);
                    break;
                case RuntimePlatform.LinuxEditor:
                    string user = Environment.UserName;
                    foreach (var baseDir in new[] { "/media/" + user, "/run/media/" + user })
                        if (Directory.Exists(baseDir))
                            list.AddRange(Directory.GetDirectories(baseDir));
                    break;
            }
            return list;
        }

        private void InitializeFtpFields(VisualElement divineWindow)
        {
            var s = DivineDragonSettingsScriptableObject.instance;
            ftpProfileButton = divineWindow.Q<Button>("FtpProfileSelect");
            ftpHostField = divineWindow.Q<TextField>("FtpHost");
            ftpPortField = divineWindow.Q<TextField>("FtpPort");
            ftpAnonToggle = divineWindow.Q<Toggle>("FtpAnon");
            ftpCredsPane = divineWindow.Q<VisualElement>("ftpCreds");
            ftpUserField = divineWindow.Q<TextField>("FtpUser");
            ftpPassField = divineWindow.Q<TextField>("FtpPass");

            // Every field edits the active profile in place.
            if (ftpHostField != null)
            {
                ftpHostField.RegisterValueChangedCallback(evt =>
                {
                    s.setFtpHost(evt.newValue);
                    UpdateBuildStatus();
                });
                // Re-list mods once editing the host is done, rather than connecting on each keystroke.
                ftpHostField.RegisterCallback<FocusOutEvent>(evt => RefreshModList());
            }
            if (ftpPortField != null)
                ftpPortField.RegisterValueChangedCallback(evt =>
                {
                    if (int.TryParse(evt.newValue, out int p))
                        s.setFtpPort(p);
                });
            if (ftpAnonToggle != null)
                ftpAnonToggle.RegisterValueChangedCallback(evt =>
                {
                    s.setFtpAnonymous(evt.newValue);
                    UpdateFtpCredsVisibility();
                });
            if (ftpUserField != null)
                ftpUserField.RegisterValueChangedCallback(evt => s.setFtpUser(evt.newValue));
            if (ftpPassField != null)
                ftpPassField.RegisterValueChangedCallback(evt => s.setFtpPassword(evt.newValue));

            if (ftpProfileButton != null)
                ftpProfileButton.clickable.clicked += ShowFtpProfileMenu;

            var test = divineWindow.Q<Button>("FtpTest");
            if (test != null)
                test.clickable.clicked += TestFtpConnection;

            RefreshFtpFields();
        }

        // Load the active profile's values into the fields without firing the write-back callbacks.
        private void RefreshFtpFields()
        {
            var p = DivineDragonSettingsScriptableObject.instance.getActiveFtpProfile();
            ftpHostField?.SetValueWithoutNotify(p.host ?? "");
            ftpPortField?.SetValueWithoutNotify(p.port.ToString());
            ftpAnonToggle?.SetValueWithoutNotify(p.anonymous);
            ftpUserField?.SetValueWithoutNotify(p.user ?? "");
            ftpPassField?.SetValueWithoutNotify(p.password ?? "");
            UpdateFtpProfileButton();
            UpdateFtpCredsVisibility();
        }

        private void UpdateFtpProfileButton()
        {
            if (ftpProfileButton == null)
                return;
            ftpProfileButton.text =
                DivineDragonSettingsScriptableObject.instance.getActiveFtpProfile().name + "  ▾";
        }

        // The profile button is also the management menu: select a profile, or new/dup/rename/delete.
        private void ShowFtpProfileMenu()
        {
            var s = DivineDragonSettingsScriptableObject.instance;
            var profiles = s.getFtpProfiles();
            int active = s.getFtpProfileIndex();

            var menu = new GenericMenu();
            for (int i = 0; i < profiles.Count; i++)
            {
                int idx = i;
                menu.AddItem(new GUIContent(profiles[i].name), i == active, () =>
                {
                    s.setFtpProfileIndex(idx);
                    RefreshFtpFields();
                    RefreshModList();
                    UpdateBuildStatus();
                });
            }
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("New"), false, AddFtpProfile);
            menu.AddItem(new GUIContent("Duplicate"), false, DuplicateFtpProfile);
            menu.AddItem(new GUIContent("Rename…"), false, RenameFtpProfile);
            if (profiles.Count > 1)
                menu.AddItem(new GUIContent("Delete"), false, DeleteFtpProfile);
            else
                menu.AddDisabledItem(new GUIContent("Delete"));

            menu.DropDown(ftpProfileButton.worldBound);
        }

        private void AddFtpProfile()
        {
            var s = DivineDragonSettingsScriptableObject.instance;
            s.addFtpProfile(new FtpProfile { name = "New profile", host = "", port = 5000, anonymous = true });
            RefreshFtpFields();
            RefreshModList();
            UpdateBuildStatus();
        }

        private void DuplicateFtpProfile()
        {
            var s = DivineDragonSettingsScriptableObject.instance;
            var cur = s.getActiveFtpProfile();
            s.addFtpProfile(new FtpProfile
            {
                name = cur.name + " copy",
                host = cur.host,
                port = cur.port,
                anonymous = cur.anonymous,
                user = cur.user,
                password = cur.password
            });
            RefreshFtpFields();
            RefreshModList();
            UpdateBuildStatus();
        }

        private void RenameFtpProfile()
        {
            var s = DivineDragonSettingsScriptableObject.instance;
            string current = s.getActiveFtpProfile().name;
            TextPromptWindow.Show("Rename profile", "Profile name:", current, "Rename", raw =>
            {
                string name = (raw ?? "").Trim();
                if (string.IsNullOrEmpty(name))
                    return;
                s.getActiveFtpProfile().name = name;
                s.saveFtpProfiles();
                UpdateFtpProfileButton();
            });
        }

        private void DeleteFtpProfile()
        {
            var s = DivineDragonSettingsScriptableObject.instance;
            if (s.getFtpProfiles().Count <= 1)
                return;

            string name = s.getActiveFtpProfile().name;
            bool ok = EditorUtility.DisplayDialog("Delete profile?",
                $"Delete the FTP profile \"{name}\"?", "Delete", "Cancel");
            if (!ok)
                return;

            s.removeFtpProfile(s.getFtpProfileIndex());
            RefreshFtpFields();
            RefreshModList();
            UpdateBuildStatus();
        }

        private void TestFtpConnection()
        {
            var s = DivineDragonSettingsScriptableObject.instance;
            if (string.IsNullOrEmpty(s.getFtpHost()))
            {
                EditorUtility.DisplayDialog("No FTP host", "Set the FTP host first.", "OK");
                return;
            }

            try
            {
                MakeFtpClient().TestConnection();
            }
            catch (Exception e)
            {
                FtpStatus.Set(s.getFtpHost(), s.getFtpPort(), false);
                UpdateBuildStatus();
                EditorUtility.DisplayDialog("Connection failed", e.Message, "OK");
                return;
            }

            FtpStatus.Set(s.getFtpHost(), s.getFtpPort(), true);
            ConnectAndListFtpMods();
            UpdateBuildStatus();
            EditorUtility.DisplayDialog("Connected", "Reached the FTP server at " + s.getFtpHost() + ".", "OK");
        }

        // User and password are only relevant for a non-anonymous login, so hide them otherwise.
        private void UpdateFtpCredsVisibility()
        {
            bool anon = DivineDragonSettingsScriptableObject.instance.getFtpAnonymous();
            if (ftpCredsPane != null)
                ftpCredsPane.style.display = anon ? DisplayStyle.None : DisplayStyle.Flex;
        }

        private static bool IsFtp()
        {
            return DivineDragonSettingsScriptableObject.instance.getDeliveryTarget() == DeliveryTarget.Ftp;
        }

        // Yuzu (and its forks) keep the virtual SD under an sdmc folder, one per OS.
        private static string FindYuzuSdCard()
        {
            var candidates = new System.Collections.Generic.List<string>();
            switch (Application.platform)
            {
                case RuntimePlatform.WindowsEditor:
                    candidates.Add(Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                        "yuzu", "sdmc"));
                    break;
                case RuntimePlatform.OSXEditor:
                    candidates.Add(Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                        "Library/Application Support/yuzu/sdmc"));
                    break;
                case RuntimePlatform.LinuxEditor:
                    string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                    candidates.Add(Path.Combine(home, ".local/share/yuzu/sdmc"));
                    candidates.Add(Path.Combine(home, ".var/app/org.yuzu_emu.yuzu/data/yuzu/sdmc"));
                    break;
            }

            foreach (var c in candidates)
                if (Directory.Exists(c))
                    return c;
            return null;
        }

        // Looks for the Ryujinx sdcard in the default spot for each OS. Linux covers both the
        // native install and the Flatpak sandbox.
        private static string FindRyujinxSdCard()
        {
            var candidates = new System.Collections.Generic.List<string>();
            switch (Application.platform)
            {
                case RuntimePlatform.WindowsEditor:
                    candidates.Add(Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                        "Ryujinx", "sdcard"));
                    break;
                case RuntimePlatform.OSXEditor:
                    candidates.Add(Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                        "Library/Application Support/Ryujinx/sdcard"));
                    break;
                case RuntimePlatform.LinuxEditor:
                    string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                    candidates.Add(Path.Combine(home, ".config/Ryujinx/sdcard"));
                    candidates.Add(Path.Combine(home, ".var/app/org.ryujinx.Ryujinx/config/Ryujinx/sdcard"));
                    break;
            }

            foreach (var c in candidates)
                if (Directory.Exists(c))
                    return c;
            return null;
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

            // Editing the paths means whatever the last build reported is stale now.
            buildResults?.Clear();

            if (IsFtp())
            {
                var s = DivineDragonSettingsScriptableObject.instance;
                string host = s.getFtpHost();
                string name = s.getFtpModName();

                if (string.IsNullOrEmpty(host))
                {
                    buildStatusLabel.text = "Set the FTP host to upload builds.";
                    buildStatusLabel.style.color = warnAmber;
                }
                else if (string.IsNullOrEmpty(name))
                {
                    buildStatusLabel.text = "Pick or create a mod to upload to.";
                    buildStatusLabel.style.color = warnAmber;
                }
                else
                {
                    buildStatusLabel.text = $"Ready to build and upload to {host}.";
                    buildStatusLabel.style.color = okGreen;
                    SetFieldBorderColorAndWidth(buildButton, cobaltBlue, 1);
                }

                if (buildButton != null)
                    buildButton.SetEnabled(!string.IsNullOrEmpty(host) && !string.IsNullOrEmpty(name));
                return;
            }

            string sd = sdPathField.value;
            string mod = modPathField.value;

            if (buildButton != null)
                buildButton.SetEnabled(!string.IsNullOrEmpty(mod));

            if (string.IsNullOrEmpty(sd))
            {
                buildStatusLabel.text = "Set the destination to locate your engage/mods folder.";
                buildStatusLabel.style.color = warnAmber;
                SetFieldBorderColorAndWidth(sdPathField, cobaltBlue, 1);
            }
            else if (!Directory.Exists(sd))
            {
                buildStatusLabel.text = "Destination not found: " + sd;
                buildStatusLabel.style.color = errRed;
                SetFieldBorderColorAndWidth(sdPathField, errRed, 1);
            }
            else if (string.IsNullOrEmpty(mod))
            {
                buildStatusLabel.text = "Set the mod path to build into.";
                buildStatusLabel.style.color = warnAmber;
                SetFieldBorderColorAndWidth(modPathField, cobaltBlue, 1);
            }
            else if (!Directory.Exists(mod))
            {
                // Not an error, the build creates it, but worth flagging in case it's a typo.
                buildStatusLabel.text = "Mod path will be created on build.";
                buildStatusLabel.style.color = warnAmber;
                SetFieldBorderColorAndWidth(modPathField, cobaltBlue, 1);
            }
            else
            {
                buildStatusLabel.text = "Ready to build.";
                buildStatusLabel.style.color = okGreen;
                SetFieldBorderColorAndWidth(buildButton, cobaltBlue, 1);
            }
        }

        private void RenderBuildOutcome(BuildOutcome outcome)
        {
            buildResults?.Clear();

            if (outcome.Cancelled)
            {
                buildStatusLabel.text = $"Build cancelled at {DateTime.Now:HH:mm:ss}.";
                buildStatusLabel.style.color = warnAmber;
                RenderDetails(outcome);
                return;
            }

            if (outcome.Success)
            {
                string summary =
                    $"Build complete · patched {outcome.Patched}, skipped {outcome.Skipped} · {outcome.ElapsedSeconds:0.0}s";
                if (!string.IsNullOrEmpty(outcome.DeliveryNote))
                    summary += " · " + outcome.DeliveryNote;
                buildStatusLabel.text = summary;
                buildStatusLabel.style.color = okGreen;

                if (!string.IsNullOrEmpty(outcome.OutputDirectory) && buildResults != null)
                {
                    var reveal = new Button(() => EditorUtility.RevealInFinder(outcome.OutputDirectory))
                    {
                        text = "Reveal output folder"
                    };
                    reveal.AddToClassList("dd-btn");
                    reveal.AddToClassList("dd-btn-ghost");
                    reveal.style.alignSelf = Align.FlexStart;
                    reveal.style.marginLeft = 0;
                    reveal.style.marginTop = 4;
                    buildResults.Add(reveal);
                }

                // Warnings can show up on an otherwise-good build, still worth seeing.
                RenderDetails(outcome);
                return;
            }

            string stage = string.IsNullOrEmpty(outcome.FailureStage) ? "build" : outcome.FailureStage;
            buildStatusLabel.text =
                $"Build failed during {stage} · {outcome.Errors.Count} error(s), {outcome.Warnings.Count} warning(s)";
            buildStatusLabel.style.color = errRed;
            RenderDetails(outcome);
        }

        private void RenderDetails(BuildOutcome outcome)
        {
            if (buildResults == null || (outcome.Errors.Count == 0 && outcome.Warnings.Count == 0))
                return;

            var header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.justifyContent = Justify.SpaceBetween;
            header.style.alignItems = Align.Center;
            header.style.marginTop = 6;

            var title = new Label(outcome.Errors.Count > 0 ? "Problems" : "Warnings");
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            header.Add(title);

            var copy = new Button(() => EditorGUIUtility.systemCopyBuffer = BuildDetailsText(outcome))
            {
                text = "Copy details"
            };
            copy.AddToClassList("dd-btn");
            copy.AddToClassList("dd-btn-ghost");
            copy.style.marginTop = 0;
            copy.style.marginBottom = 0;
            copy.style.marginRight = 0;
            header.Add(copy);
            buildResults.Add(header);

            var scroll = new ScrollView();
            scroll.style.maxHeight = 180;
            scroll.style.marginTop = 4;
            SetFieldBorderColorAndWidth(scroll, new Color(0f, 0f, 0f, 0.25f), 1);

            foreach (var err in outcome.Errors)
                scroll.Add(MakeErrorRow(err));
            foreach (var w in outcome.Warnings)
                scroll.Add(MakeMessageRow(w, warnAmber));

            buildResults.Add(scroll);
        }

        private VisualElement MakeErrorRow(BuildError err)
        {
            var row = MakeRowShell();

            string where = string.IsNullOrEmpty(err.AssetType)
                ? err.BundlePath
                : $"{err.BundlePath} · {err.AssetType}";
            var head = new Label($"{err.Kind}: {where}");
            head.style.unityFontStyleAndWeight = FontStyle.Bold;
            head.style.color = errRed;
            head.style.whiteSpace = WhiteSpace.Normal;
            row.Add(head);

            string body = err.Detail;
            if (!string.IsNullOrEmpty(err.Hint))
                body = string.IsNullOrEmpty(body) ? err.Hint : $"{body}  {err.Hint}";
            if (!string.IsNullOrEmpty(body))
            {
                var detail = new Label(body);
                detail.style.whiteSpace = WhiteSpace.Normal;
                row.Add(detail);
            }
            return row;
        }

        private VisualElement MakeMessageRow(string message, Color color)
        {
            var row = MakeRowShell();
            var label = new Label(message);
            label.style.whiteSpace = WhiteSpace.Normal;
            label.style.color = color;
            row.Add(label);
            return row;
        }

        private static VisualElement MakeRowShell()
        {
            var row = new VisualElement();
            row.style.paddingTop = 3;
            row.style.paddingBottom = 3;
            row.style.paddingLeft = 4;
            row.style.paddingRight = 4;
            row.style.borderBottomWidth = 1;
            row.style.borderBottomColor = new Color(0f, 0f, 0f, 0.15f);
            return row;
        }

        private static string BuildDetailsText(BuildOutcome outcome)
        {
            var sb = new StringBuilder();
            if (outcome.Errors.Count > 0)
            {
                sb.AppendLine($"Errors ({outcome.Errors.Count}):");
                foreach (var err in outcome.Errors)
                    sb.AppendLine("  " + err);
            }
            if (outcome.Warnings.Count > 0)
            {
                if (sb.Length > 0)
                    sb.AppendLine();
                sb.AppendLine($"Warnings ({outcome.Warnings.Count}):");
                foreach (var w in outcome.Warnings)
                    sb.AppendLine("  " + w);
            }
            return sb.ToString();
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

    // Shared styling for the little dialog windows, so their buttons match the main window.
    internal static class DialogStyle
    {
        public static void Apply(VisualElement root)
        {
            var sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(
                "Packages/com.divinedragon.builder/Editor/DivineWindow.uss");
            if (sheet != null)
                root.styleSheets.Add(sheet);
            root.style.paddingLeft = 12;
            root.style.paddingRight = 12;
            root.style.paddingTop = 10;
            root.style.paddingBottom = 10;
        }

        public static VisualElement ButtonRow()
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.justifyContent = Justify.FlexEnd;
            row.style.marginTop = 12;
            return row;
        }

        public static Button GhostButton(string text, Action onClick)
        {
            var b = new Button(onClick) { text = text };
            b.AddToClassList("dd-btn");
            b.AddToClassList("dd-btn-ghost");
            return b;
        }
    }

    // Tiny modal for naming or renaming a mod, since Unity has no built-in text-input dialog.
    public class TextPromptWindow : EditorWindow
    {
        private string _message = "";
        private string _initial = "";
        private string _confirmLabel = "OK";
        private Action<string> _onConfirm;
        private TextField _field;

        public static void Show(string title, string message, string initial, Action<string> onConfirm)
        {
            Show(title, message, initial, "OK", onConfirm);
        }

        public static void Show(string title, string message, string initial, string confirmLabel,
            Action<string> onConfirm)
        {
            var w = CreateInstance<TextPromptWindow>();
            w.titleContent = new GUIContent(title);
            w._message = message;
            w._initial = initial ?? "";
            w._confirmLabel = string.IsNullOrEmpty(confirmLabel) ? "OK" : confirmLabel;
            w._onConfirm = onConfirm;
            w.minSize = new Vector2(360, 130);
            w.maxSize = new Vector2(360, 130);
            w.BuildUI();
            w.ShowUtility();
        }

        private void BuildUI()
        {
            var root = rootVisualElement;
            root.Clear();
            DialogStyle.Apply(root);

            var message = new Label(_message);
            message.style.whiteSpace = WhiteSpace.Normal;
            root.Add(message);

            _field = new TextField { value = _initial };
            _field.style.marginTop = 8;
            root.Add(_field);

            var row = DialogStyle.ButtonRow();
            var confirm = new Button(Submit) { text = _confirmLabel };
            confirm.AddToClassList("dd-btn");
            confirm.AddToClassList("dd-btn-accent");
            row.Add(DialogStyle.GhostButton("Cancel", Close));
            row.Add(confirm);
            root.Add(row);

            root.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
                {
                    Submit();
                    evt.StopPropagation();
                }
                else if (evt.keyCode == KeyCode.Escape)
                {
                    Close();
                    evt.StopPropagation();
                }
            });

            _field.schedule.Execute(() => _field.Focus()).ExecuteLater(1);
        }

        private void Submit()
        {
            var callback = _onConfirm;
            string value = _field != null ? _field.value : _initial;
            Close();
            callback?.Invoke(value);
        }
    }

    // GitHub-style confirmation: the confirm button stays disabled until the user types the exact
    // required text (a mod name), so a destructive action can't happen on a stray click.
    public class ConfirmByTypingWindow : EditorWindow
    {
        private string _message = "";
        private string _required = "";
        private string _confirmLabel = "Delete";
        private Action _onConfirm;
        private TextField _field;
        private Button _confirmButton;

        public static void Show(string title, string message, string requiredText, string confirmLabel,
            Action onConfirm)
        {
            var w = CreateInstance<ConfirmByTypingWindow>();
            w.titleContent = new GUIContent(title);
            w._message = message;
            w._required = requiredText ?? "";
            w._confirmLabel = string.IsNullOrEmpty(confirmLabel) ? "Delete" : confirmLabel;
            w._onConfirm = onConfirm;
            w.minSize = new Vector2(420, 190);
            w.maxSize = new Vector2(420, 190);
            w.BuildUI();
            w.ShowUtility();
        }

        private void BuildUI()
        {
            var root = rootVisualElement;
            root.Clear();
            DialogStyle.Apply(root);

            var message = new Label(_message);
            message.style.whiteSpace = WhiteSpace.Normal;
            root.Add(message);

            _field = new TextField();
            _field.style.marginTop = 8;
            root.Add(_field);

            var row = DialogStyle.ButtonRow();
            _confirmButton = new Button(Submit) { text = _confirmLabel };
            _confirmButton.AddToClassList("dd-btn");
            _confirmButton.AddToClassList("dd-btn-danger");
            _confirmButton.SetEnabled(false);
            row.Add(DialogStyle.GhostButton("Cancel", Close));
            row.Add(_confirmButton);
            root.Add(row);

            _field.RegisterValueChangedCallback(evt =>
                _confirmButton.SetEnabled(evt.newValue == _required));

            root.RegisterCallback<KeyDownEvent>(evt =>
            {
                if ((evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
                    && _field.value == _required)
                {
                    Submit();
                    evt.StopPropagation();
                }
                else if (evt.keyCode == KeyCode.Escape)
                {
                    Close();
                    evt.StopPropagation();
                }
            });

            _field.schedule.Execute(() => _field.Focus()).ExecuteLater(1);
        }

        private void Submit()
        {
            if (_field.value != _required)
                return;
            var callback = _onConfirm;
            Close();
            callback?.Invoke();
        }
    }
}