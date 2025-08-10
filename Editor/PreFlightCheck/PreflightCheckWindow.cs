using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace DivineDragon.PreFlightCheck
{
    public class PreflightCheckWindow : EditorWindow
    {
        private List<BuildIssue> issues = new List<BuildIssue>();
        private Vector2 scrollPosition;
        private DateTime lastCheckTime = DateTime.MinValue;
        private bool isChecking = false;
        
        private enum ViewMode
        {
            GroupByIssue,
            GroupByPrefab
        }
        
        private ViewMode currentViewMode = ViewMode.GroupByIssue;
        
        [MenuItem("Divine Dragon/Preflight Check", false, 1510)]
        public static void ShowWindow()
        {
            var window = GetWindow<PreflightCheckWindow>("Preflight Check");
            window.minSize = new Vector2(600, 400);
        }
        
        public static void ShowWithIssues(List<BuildIssue> issues)
        {
            var window = GetWindow<PreflightCheckWindow>("Preflight Check");
            window.minSize = new Vector2(600, 400);
            window.issues = issues;
            window.lastCheckTime = DateTime.Now;
            window.Show();
        }
        
        private void OnEnable()
        {
            if (lastCheckTime == DateTime.MinValue)
            {
                RefreshIssues();
            }
        }

        private void OnGUI()
        {
            EditorGUILayout.BeginVertical();
            
            // Header
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            
            if (isChecking)
            {
                GUILayout.Label("Running checks...", EditorStyles.toolbarButton);
            }
            else if (lastCheckTime != DateTime.MinValue)
            {
                GUILayout.Label($"Found {issues.Count} issues", EditorStyles.toolbarButton);
                GUILayout.Label("|", EditorStyles.toolbarButton, GUILayout.Width(10));
                GUILayout.Label($"Last checked: {GetRelativeTime(lastCheckTime)}", EditorStyles.toolbarButton);
            }
            else
            {
                GUILayout.Label("No checks run yet", EditorStyles.toolbarButton);
            }
            
            GUILayout.FlexibleSpace();
            
            // View mode selector
            GUILayout.Label("View:", EditorStyles.toolbarButton);
            var viewModeOptions = new string[] { "By Issue Type", "By Prefab" };
            var selectedIndex = currentViewMode == ViewMode.GroupByIssue ? 0 : 1;
            var newIndex = EditorGUILayout.Popup(selectedIndex, viewModeOptions, EditorStyles.toolbarPopup, GUILayout.Width(100));
            if (newIndex != selectedIndex)
            {
                currentViewMode = newIndex == 0 ? ViewMode.GroupByIssue : ViewMode.GroupByPrefab;
            }
            
            if (issues.Any(i => i.Rule.CanAutoFix))
            {
                if (GUILayout.Button("Autofix All", EditorStyles.toolbarButton, GUILayout.Width(100)))
                {
                    FixAllIssues();
                }
            }
            
            if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(80)))
            {
                RefreshIssues();
            }
            
            EditorGUILayout.EndHorizontal();
            
            // Issue list
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
            
            if (isChecking)
            {
                EditorGUILayout.HelpBox("Running checks...", MessageType.Info);
            }
            else if (lastCheckTime == DateTime.MinValue)
            {
                EditorGUILayout.HelpBox("Click Refresh to run checks.", MessageType.Info);
            }
            else if (issues.Count == 0)
            {
                EditorGUILayout.HelpBox("No issues found! Your build is ready.", MessageType.Info);
            }
            else
            {
                switch (currentViewMode)
                {
                    case ViewMode.GroupByIssue:
                        DrawIssuesGroupedByRule();
                        break;
                    case ViewMode.GroupByPrefab:
                        DrawIssuesGroupedByPrefab();
                        break;
                }
            }
            
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void DrawIssuesGroupedByRule()
        {
            var groupedIssues = issues.GroupBy(i => i.Rule);
            
            foreach (var group in groupedIssues)
            {
                var rule = group.Key;
                var ruleIssues = group.ToList();
                
                EditorGUILayout.BeginVertical(GUI.skin.box);
                
                // Rule header
                EditorGUILayout.BeginHorizontal();
                var headerStyle = new GUIStyle(EditorStyles.boldLabel);
                headerStyle.fontSize = 12;
                EditorGUILayout.LabelField($"{rule.Name} ({ruleIssues.Count} issues)", headerStyle);
                
                if (rule.CanAutoFix && GUILayout.Button("Autofix All", GUILayout.Width(80)))
                {
                    FixIssuesForRule(ruleIssues);
                }
                EditorGUILayout.EndHorizontal();
                
                EditorGUILayout.LabelField(rule.Description, EditorStyles.wordWrappedLabel);
                EditorGUILayout.Space(5);
                
                // Issues for this rule
                foreach (var issue in ruleIssues)
                {
                    DrawIssue(issue);
                }
                
                EditorGUILayout.EndVertical();
                EditorGUILayout.Space(5);
            }
        }

        private void DrawIssuesFlat()
        {
            foreach (var issue in issues)
            {
                DrawIssue(issue);
            }
        }
        
        private void DrawIssuesGroupedByPrefab()
        {
            var groupedIssues = issues.GroupBy(i => i.AssetPath);
            
            foreach (var group in groupedIssues)
            {
                var assetPath = group.Key;
                var prefabIssues = group.ToList();
                
                EditorGUILayout.BeginVertical(GUI.skin.box);
                
                // Prefab header
                EditorGUILayout.BeginHorizontal();
                var headerStyle = new GUIStyle(EditorStyles.boldLabel);
                headerStyle.fontSize = 12;
                
                string filename = System.IO.Path.GetFileName(assetPath);
                EditorGUILayout.LabelField($"{filename} ({prefabIssues.Count} issues)", headerStyle);
                
                // Open prefab button
                if (GUILayout.Button("Open", GUILayout.Width(50)))
                {
                    var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath);
                    if (asset != null)
                    {
                        Selection.activeObject = asset;
                        EditorGUIUtility.PingObject(asset);
                    }
                }
                
                // Autofix all button for this prefab
                var canAutoFix = prefabIssues.Any(i => i.Rule.CanAutoFix);
                if (canAutoFix && GUILayout.Button("Fix All", GUILayout.Width(60)))
                {
                    FixIssuesForPrefab(prefabIssues);
                }
                
                EditorGUILayout.EndHorizontal();
                
                // Show directory path
                string directory = System.IO.Path.GetDirectoryName(assetPath);
                var pathStyle = new GUIStyle(EditorStyles.label);
                pathStyle.normal.textColor = new Color(0.6f, 0.6f, 0.6f);
                pathStyle.fontSize = 11;
                EditorGUILayout.LabelField(directory, pathStyle);
                
                EditorGUILayout.Space(5);
                
                // Issues for this prefab
                foreach (var issue in prefabIssues)
                {
                    DrawPrefabIssue(issue);
                }
                
                EditorGUILayout.EndVertical();
                EditorGUILayout.Space(5);
            }
        }
        
        private void DrawPrefabIssue(BuildIssue issue)
        {
            EditorGUILayout.BeginHorizontal();
            
            // Severity icon
            var severityIcon = GetSeverityIcon(issue.Severity);
            GUILayout.Label(severityIcon, GUILayout.Width(20));
            
            // Issue details
            EditorGUILayout.BeginVertical();
            
            // Rule name in bold
            var ruleStyle = new GUIStyle(EditorStyles.boldLabel);
            ruleStyle.fontSize = 11;
            EditorGUILayout.LabelField(issue.Rule.Name, ruleStyle);
            
            // Issue message
            EditorGUILayout.LabelField(issue.Message, EditorStyles.wordWrappedLabel);
            
            EditorGUILayout.EndVertical();
            
            // Open button
            if (GUILayout.Button("Open", GUILayout.Width(50)))
            {
                OpenIssueAsset(issue);
            }
            
            // Fix button
            if (issue.Rule.CanAutoFix)
            {
                if (GUILayout.Button("Fix", GUILayout.Width(40)))
                {
                    if (issue.Rule.AutoFix(issue))
                    {
                        RefreshIssues();
                    }
                }
            }
            
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(2);
        }
        
        private void FixIssuesForPrefab(List<BuildIssue> prefabIssues)
        {
            int fixedCount = PreFlightCheckManager.AutoFixAll(prefabIssues);
            
            if (fixedCount > 0)
            {
                RefreshIssues();
            }
        }

        private void DrawIssue(BuildIssue issue)
        {
            EditorGUILayout.BeginHorizontal();
            
            // Severity icon
            var severityIcon = GetSeverityIcon(issue.Severity);
            GUILayout.Label(severityIcon, GUILayout.Width(20));
            
            // Issue details
            EditorGUILayout.BeginVertical();
            
            // Extract just the filename
            string filename = System.IO.Path.GetFileName(issue.AssetPath);
            string directory = System.IO.Path.GetDirectoryName(issue.AssetPath);
            
            // Filename with open button
            EditorGUILayout.BeginHorizontal();
            
            // Filename as clickable link in blue
            var linkStyle = new GUIStyle(EditorStyles.linkLabel);
            linkStyle.normal.textColor = new Color(0.3f, 0.5f, 1f);
            linkStyle.hover.textColor = new Color(0.5f, 0.7f, 1f);
            
            var filenameContent = new GUIContent(filename);
            var rect = GUILayoutUtility.GetRect(filenameContent, linkStyle, GUILayout.Height(16));
            
            // Add cursor change on hover
            if (rect.Contains(Event.current.mousePosition))
            {
                EditorGUIUtility.AddCursorRect(rect, MouseCursor.Link);
            }
            
            if (GUI.Button(rect, filename, linkStyle))
            {
                OpenIssueAsset(issue);
            }
            
            
            GUILayout.FlexibleSpace();
            
            EditorGUILayout.EndHorizontal();
            
            // Full path in smaller, lighter grey text on a new line
            var pathStyle = new GUIStyle(EditorStyles.label);
            pathStyle.normal.textColor = new Color(0.6f, 0.6f, 0.6f);
            pathStyle.fontSize = 11;
            EditorGUILayout.LabelField(directory, pathStyle);
            
            EditorGUILayout.LabelField(issue.Message, EditorStyles.wordWrappedLabel);
            EditorGUILayout.EndVertical();
            
            // Action buttons
            if (GUILayout.Button("Open", GUILayout.Width(50)))
            {
                OpenIssueAsset(issue);
            }
            
            // Fix button
            if (issue.Rule.CanAutoFix)
            {
                if (GUILayout.Button("Autofix", GUILayout.Width(60)))
                {
                    if (issue.Rule.AutoFix(issue))
                    {
                        RefreshIssues();
                    }
                }
            }
            
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(2);
        }

        private string GetSeverityIcon(IssueSeverity severity)
        {
            switch (severity)
            {
                case IssueSeverity.Error:
                    return "⛔";
                case IssueSeverity.Warning:
                    return "⚠️";
                case IssueSeverity.Info:
                    return "ℹ️";
                default:
                    return "•";
            }
        }

        private void FixAllIssues()
        {
            int fixedCount = PreFlightCheckManager.AutoFixAll(issues);
            
            if (fixedCount > 0)
            {
                EditorUtility.DisplayDialog("Issues Fixed", 
                    $"Successfully fixed {fixedCount} issues.", "OK");
                RefreshIssues();
            }
        }

        private void FixIssuesForRule(List<BuildIssue> ruleIssues)
        {
            int fixedCount = PreFlightCheckManager.AutoFixAll(ruleIssues);
            
            if (fixedCount > 0)
            {
                RefreshIssues();
            }
        }

        private void RefreshIssues()
        {
            isChecking = true;
            Repaint();
            
            EditorApplication.delayCall += () =>
            {
                issues = PreFlightCheckManager.RunAllChecks();
                lastCheckTime = DateTime.Now;
                isChecking = false;
                Repaint();
            };
        }
        
        private string GetRelativeTime(DateTime time)
        {
            var timeSpan = DateTime.Now - time;
            
            if (timeSpan.TotalSeconds < 60)
                return "just now";
            if (timeSpan.TotalMinutes < 60)
                return $"{(int)timeSpan.TotalMinutes} minute{((int)timeSpan.TotalMinutes == 1 ? "" : "s")} ago";
            if (timeSpan.TotalHours < 24)
                return $"{(int)timeSpan.TotalHours} hour{((int)timeSpan.TotalHours == 1 ? "" : "s")} ago";
            
            return time.ToString("h:mm tt");
        }
        
        private void OpenIssueAsset(BuildIssue issue)
        {
            // If we have a specific component, open the prefab and select it
            if (issue.SpecificComponent != null && issue.Asset is GameObject)
            {
                // Open the prefab in prefab mode
                AssetDatabase.OpenAsset(issue.Asset);
                
                // Wait a frame for the prefab to open, then select the component
                EditorApplication.delayCall += () =>
                {
                    Selection.activeObject = issue.SpecificComponent;
                    EditorGUIUtility.PingObject(issue.SpecificComponent);
                };
            }
            else
            {
                // Just select the asset
                Selection.activeObject = issue.Asset;
                EditorGUIUtility.PingObject(issue.Asset);
            }
        }
    }
}