using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace DivineDragon.PreFlightCheck
{
    internal class PreFlightRuleSettingsProvider : SettingsProvider
    {
        private Vector2 scrollPosition;
        private string searchText = string.Empty;
        private static readonly GUIContent[] ModeLabelsWithAuto = {
            new GUIContent("Enabled"),
            new GUIContent("Auto-fix"),
            new GUIContent("Disabled")
        };
        private static readonly PreFlightRuleSettings.RuleExecutionMode[] ModeValuesWithAuto = {
            PreFlightRuleSettings.RuleExecutionMode.Check,
            PreFlightRuleSettings.RuleExecutionMode.CheckAndAutoApply,
            PreFlightRuleSettings.RuleExecutionMode.Skip
        };
        private static readonly GUIContent[] ModeLabelsWithoutAuto = {
            new GUIContent("Enabled"),
            new GUIContent("Disabled")
        };
        private static readonly PreFlightRuleSettings.RuleExecutionMode[] ModeValuesWithoutAuto = {
            PreFlightRuleSettings.RuleExecutionMode.Check,
            PreFlightRuleSettings.RuleExecutionMode.Skip
        };

        private PreFlightRuleSettingsProvider(string path, SettingsScope scope) : base(path, scope)
        {
            label = "Preflight Rules";
        }

        public override void OnGUI(string searchContext)
        {
            DrawToolbar();
            DrawRulesList();
        }

        private void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                var searchStyle = GUI.skin.FindStyle("ToolbarSeachTextField") ?? GUI.skin.textField;
                var cancelStyle = GUI.skin.FindStyle("ToolbarSeachCancelButton") ?? GUI.skin.button;
                searchText = GUILayout.TextField(searchText, searchStyle);
                if (GUILayout.Button(string.Empty, cancelStyle))
                {
                    searchText = string.Empty;
                    GUI.FocusControl(null);
                }
            }
        }

        private void DrawRulesList()
        {
            var registeredRules = PreFlightRuleRegistry.GetRegisteredRuleInfos();

            if (!string.IsNullOrEmpty(searchText))
            {
                string filter = searchText.ToLowerInvariant();
                registeredRules = registeredRules
                    .Where(info =>
                        (!string.IsNullOrEmpty(info.RuleName) && info.RuleName.ToLowerInvariant().Contains(filter)) ||
                        (!string.IsNullOrEmpty(info.PackageDisplayName) && info.PackageDisplayName.ToLowerInvariant().Contains(filter)) ||
                        (!string.IsNullOrEmpty(info.PackageName) && info.PackageName.ToLowerInvariant().Contains(filter)))
                    .ToList();
            }

            if (registeredRules.Count == 0)
            {
                EditorGUILayout.HelpBox("No rules are currently registered. Ensure the builder and rule packages are referenced in the project manifest.", MessageType.Info);
                return;
            }

            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

            var grouped = registeredRules
                .GroupBy(info =>
                    info.PackageDisplayName ?? info.PackageName ??
                    (info.AssemblyName != null ? $"Assembly: {info.AssemblyName}" : "Unknown Source"))
                .OrderBy(g => g.Key);

            foreach (var group in grouped)
            {
                EditorGUILayout.BeginVertical(GUI.skin.box);

                string header = group.Key;
                var sample = group.First();

                // Show cleaner header - just display name with version as subtitle if available
                if (!string.IsNullOrEmpty(sample.PackageDisplayName))
                {
                    EditorGUILayout.LabelField(sample.PackageDisplayName, EditorStyles.boldLabel);
                    if (!string.IsNullOrEmpty(sample.PackageVersion))
                    {
                        EditorGUILayout.LabelField($"Version {sample.PackageVersion}", EditorStyles.miniLabel);
                    }
                }
                else
                {
                    EditorGUILayout.LabelField(header, EditorStyles.boldLabel);
                }

                foreach (var info in group.OrderBy(i => i.RuleName))
                {
                    DrawRuleRow(info);
                }

                EditorGUILayout.EndVertical();
                EditorGUILayout.Space(5);
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawRuleRow(PreFlightRuleRegistry.RegisteredRuleInfo info)
        {
            if (info.RuleType == null)
            {
                EditorGUILayout.HelpBox("Rule type could not be resolved.", MessageType.Warning);
                return;
            }

            var titleStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 11 };

            EditorGUILayout.BeginVertical();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(info.RuleName ?? "Unknown Rule", titleStyle);
            GUILayout.FlexibleSpace();
            DrawModeDropdown(info);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginVertical();
            if (!string.IsNullOrEmpty(info.RuleDescription))
            {
                EditorGUILayout.LabelField(info.RuleDescription, EditorStyles.wordWrappedMiniLabel);
            }

            EditorGUILayout.LabelField($"{info.DefaultSeverity}", EditorStyles.miniLabel);
            EditorGUILayout.EndVertical();

            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(6);
        }

        private void DrawModeDropdown(PreFlightRuleRegistry.RegisteredRuleInfo info)
        {
            var currentMode = info.Mode;
            if (!info.CanAutoFix && currentMode == PreFlightRuleSettings.RuleExecutionMode.CheckAndAutoApply)
            {
                currentMode = PreFlightRuleSettings.RuleExecutionMode.Check;
                PreFlightRuleRegistry.SetRuleMode(info.RuleType, currentMode);
            }

            PreFlightRuleSettings.RuleExecutionMode[] values;
            GUIContent[] labels;

            if (info.CanAutoFix)
            {
                values = ModeValuesWithAuto;
                labels = ModeLabelsWithAuto;
            }
            else
            {
                values = ModeValuesWithoutAuto;
                labels = ModeLabelsWithoutAuto;
            }

            int currentIndex = System.Array.IndexOf(values, currentMode);
            if (currentIndex < 0) currentIndex = 0;

            EditorGUI.BeginChangeCheck();
            int newIndex = EditorGUILayout.Popup(currentIndex, labels, GUILayout.Width(200));
            if (EditorGUI.EndChangeCheck())
            {
                var newMode = values[newIndex];
                PreFlightRuleRegistry.SetRuleMode(info.RuleType, newMode);
            }
        }

        [SettingsProvider]
        public static SettingsProvider CreateSettingsProvider()
        {
            return new PreFlightRuleSettingsProvider("Project/Divine Dragon/Preflight Rules", SettingsScope.Project);
        }
    }
}
