using System;
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
        private Dictionary<string, bool> expandedRules = new Dictionary<string, bool>();
        private Dictionary<string, BuildRule> ruleInstances = new Dictionary<string, BuildRule>();

        private PreFlightRuleSettingsProvider(string path, SettingsScope scope) : base(path, scope)
        {
            label = "Validation Rules";
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

            // Check if rule has configuration and get/create instance
            BuildRule ruleInstance = null;
            bool hasConfig = false;

            string ruleKey = info.RuleType.FullName;
            if (!ruleInstances.TryGetValue(ruleKey, out ruleInstance))
            {
                try
                {
                    ruleInstance = Activator.CreateInstance(info.RuleType) as BuildRule;
                    if (ruleInstance != null)
                    {
                        ruleInstances[ruleKey] = ruleInstance;
                        hasConfig = ruleInstance.HasConfiguration;
                    }
                }
                catch { }
            }
            else
            {
                hasConfig = ruleInstance.HasConfiguration;
            }

            // Draw expand/collapse arrow if rule has configuration
            if (hasConfig)
            {
                if (!expandedRules.ContainsKey(ruleKey))
                    expandedRules[ruleKey] = false;

                var arrowContent = expandedRules[ruleKey] ? new GUIContent("▼") : new GUIContent("▶");
                if (GUILayout.Button(arrowContent, EditorStyles.label, GUILayout.Width(20)))
                {
                    expandedRules[ruleKey] = !expandedRules[ruleKey];
                }
            }
            else
            {
                GUILayout.Space(20); // Maintain alignment
            }

            EditorGUILayout.LabelField(info.RuleName ?? "Unknown Rule", titleStyle);
            GUILayout.FlexibleSpace();
            DrawEnableToggle(info);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginVertical();
            if (!string.IsNullOrEmpty(info.RuleDescription))
            {
                EditorGUILayout.LabelField(info.RuleDescription, EditorStyles.wordWrappedMiniLabel);
            }

            EditorGUILayout.LabelField($"{info.DefaultSeverity}", EditorStyles.miniLabel);

            // Draw configuration if expanded
            if (hasConfig && expandedRules[ruleKey] && ruleInstance != null)
            {
                EditorGUILayout.Space(5);
                ruleInstance.DrawConfiguration();
            }

            EditorGUILayout.EndVertical();

            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(6);
        }

        private void DrawEnableToggle(PreFlightRuleRegistry.RegisteredRuleInfo info)
        {
            EditorGUI.BeginChangeCheck();
            bool enabled = EditorGUILayout.ToggleLeft("Enabled", info.Enabled, GUILayout.Width(70));
            if (EditorGUI.EndChangeCheck())
            {
                PreFlightRuleRegistry.SetRuleEnabled(info.RuleType, enabled);
            }
        }

        [SettingsProvider]
        public static SettingsProvider CreateSettingsProvider()
        {
            return new PreFlightRuleSettingsProvider("Project/Divine Dragon/Validation Rules", SettingsScope.Project);
        }
    }
}
