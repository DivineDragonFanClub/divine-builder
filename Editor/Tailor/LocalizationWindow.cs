using System.Collections.Generic;
using Code.Combat;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

public class LocalizationWindow : EditorWindow
{
    private VisualElement m_RightPane;

    public void CreateGUI()
    {
        // Get a list of all the Costume objects in the project
        var allCostumeGuids = AssetDatabase.FindAssets("t:Costume");
        var allCostumes = new List<Costume>();
        foreach (var costume in allCostumeGuids)
            allCostumes.Add(AssetDatabase.LoadAssetAtPath<Costume>(AssetDatabase.GUIDToAssetPath(costume)));

        // Create a two-pane view with the left pane being fixed.
        var splitView = new TwoPaneSplitView(0, 250, TwoPaneSplitViewOrientation.Horizontal);

// Add the view to the visual tree by adding it as a child to the root element.
        rootVisualElement.Add(splitView);

// A TwoPaneSplitView needs exactly two child elements.
        var leftPane = new ListView();
        splitView.Add(leftPane);

        leftPane.makeItem = () => new Label();
        leftPane.bindItem = (item, index) => { (item as Label).text = allCostumes[index].Name; };
        leftPane.itemsSource = allCostumes;
        leftPane.onSelectionChange += OnCostumeSelection;

        m_RightPane = new ScrollView();
        splitView.Add(m_RightPane);
    }

    private void OnCostumeSelection(IEnumerable<object> selectedItems)
    {
        // Clear all previous content from the pane.
        m_RightPane.Clear();

        // Get the selected sprite and display it.
        var enumerator = selectedItems.GetEnumerator();
        if (enumerator.MoveNext())
        {
            var selectedCostume = enumerator.Current as Costume;
            if (selectedCostume != null) Debug.Log("Selected: " + selectedCostume.Name);
            var languages = Languages.SupportedLanguages;
            foreach (var languageGroup in languages)
            {
                var regionLabel = new Label(languageGroup.HumanName);
                regionLabel.style.fontSize =
                    new StyleLength(new Length(16, LengthUnit.Pixel));
                regionLabel.style.marginLeft = new StyleLength(new Length(10, LengthUnit.Pixel)); // Left margin
                regionLabel.style.marginTop = new StyleLength(new Length(10, LengthUnit.Pixel)); // Top margin

                m_RightPane.Add(regionLabel);
                var languageGroupBox = new Box();

                foreach (var language in languageGroup.Languages)
                {
                    var box = new Box();
                    var languageLabel = new Label(language.HumanName);
                    languageLabel.style.fontSize =
                        new StyleLength(new Length(12, LengthUnit.Pixel));
                    box.Add(languageLabel);
                    var textField = new TextField("Title");
                    if (selectedCostume.LocalizedStrings.ContainsKey(language.Code) &&
                        selectedCostume.LocalizedStrings[language.Code] != null)
                        textField.value = selectedCostume.LocalizedStrings[language.Code].Title;
                    textField.RegisterValueChangedCallback(
                        evt =>
                        {
                            if (!selectedCostume.LocalizedStrings.ContainsKey(language.Code) ||
                                selectedCostume.LocalizedStrings[language.Code] == null)
                                selectedCostume.LocalizedStrings[language.Code] = new CostumeShopStrings();
                            selectedCostume.LocalizedStrings[language.Code].Title = evt.newValue;
                            EditorUtility.SetDirty(selectedCostume);
                            AssetDatabase.SaveAssets();
                            AssetDatabase.Refresh();
                        });

                    var descriptionTextField = new TextField("Description")
                    {
                        multiline = true
                    };
                    if (selectedCostume.LocalizedStrings.ContainsKey(language.Code) &&
                        selectedCostume.LocalizedStrings[language.Code] != null)
                        descriptionTextField.value = selectedCostume.LocalizedStrings[language.Code].Description;
                    descriptionTextField.RegisterValueChangedCallback(evt =>
                    {
                        if (!selectedCostume.LocalizedStrings.ContainsKey(language.Code) ||
                            selectedCostume.LocalizedStrings[language.Code] == null)
                            selectedCostume.LocalizedStrings[language.Code] = new CostumeShopStrings();
                        selectedCostume.LocalizedStrings[language.Code].Description = evt.newValue;
                        // lol
                        EditorUtility.SetDirty(selectedCostume);
                        AssetDatabase.SaveAssets();
                        AssetDatabase.Refresh();
                    });
                    box.Add(textField);
                    box.Add(descriptionTextField);

                    box.style.marginTop = new StyleLength(new Length(10, LengthUnit.Pixel)); // Top margin
                    box.style.marginRight = new StyleLength(new Length(10, LengthUnit.Pixel)); // Right margin
                    box.style.marginBottom = new StyleLength(new Length(10, LengthUnit.Pixel)); // Bottom margin
                    box.style.marginLeft = new StyleLength(new Length(10, LengthUnit.Pixel)); // Left margin

                    box.style.paddingTop = new StyleLength(new Length(10, LengthUnit.Pixel)); // Top padding
                    box.style.paddingRight = new StyleLength(new Length(10, LengthUnit.Pixel)); // Right padding
                    box.style.paddingBottom = new StyleLength(new Length(10, LengthUnit.Pixel)); // Bottom padding
                    box.style.paddingLeft = new StyleLength(new Length(10, LengthUnit.Pixel)); // Left padding
                    languageGroupBox.Add(box);
                }

                // Create a new VisualElement that will act as a divider
                var divider = new VisualElement();

                divider.style.height = new StyleLength(new Length(1, LengthUnit.Pixel));

                divider.style.backgroundColor = new StyleColor(Color.gray);
                var foldout = new Foldout();
                foldout.contentContainer.Add(languageGroupBox);
                foldout.value = languageGroup.HumanName == "United States";
                m_RightPane.Add(foldout);
                m_RightPane.Add(divider);
            }
        }
    }

    [MenuItem("Window/UI Toolkit/LocalizationWindow")]
    public static void ShowExample()
    {
        var wnd = GetWindow<LocalizationWindow>();
        wnd.titleContent = new GUIContent("LocalizationWindow");
    }
}