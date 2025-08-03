using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace DivineDragon.PreFlightCheck
{
    public class BuildIssuesWindow : EditorWindow
    {
        private List<BuildIssue> issues = new List<BuildIssue>();
        private Vector2 scrollPosition;
        private bool groupByRule = true;
        
        [MenuItem("Divine Dragon/Build Issues", false, 1510)]
        public static void ShowWindow()
        {
            var window = GetWindow<BuildIssuesWindow>("Build Issues");
            window.minSize = new Vector2(600, 400);
        }
        
        public static void ShowWithIssues(List<BuildIssue> issues)
        {
            var window = GetWindow<BuildIssuesWindow>("Build Issues");
            window.minSize = new Vector2(600, 400);
            window.issues = issues;
            window.Show();
        }

        private void OnGUI()
        {
            EditorGUILayout.BeginVertical();
            
            // Header
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Label($"Found {issues.Count} issues", EditorStyles.toolbarButton);
            GUILayout.FlexibleSpace();
            
            if (issues.Any(i => i.Rule.CanAutoFix))
            {
                if (GUILayout.Button("Fix All", EditorStyles.toolbarButton, GUILayout.Width(80)))
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
            
            if (issues.Count == 0)
            {
                EditorGUILayout.HelpBox("No issues found! Your build is ready.", MessageType.Info);
            }
            else
            {
                if (groupByRule)
                {
                    DrawIssuesGroupedByRule();
                }
                else
                {
                    DrawIssuesFlat();
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
                
                if (rule.CanAutoFix && GUILayout.Button("Fix All", GUILayout.Width(60)))
                {
                    FixIssuesForRule(ruleIssues);
                }
                EditorGUILayout.EndHorizontal();
                
                EditorGUILayout.LabelField(rule.Description, EditorStyles.wordWrappedMiniLabel);
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

        private void DrawIssue(BuildIssue issue)
        {
            EditorGUILayout.BeginHorizontal();
            
            // Severity icon
            var severityIcon = GetSeverityIcon(issue.Severity);
            GUILayout.Label(severityIcon, GUILayout.Width(20));
            
            // Issue details
            EditorGUILayout.BeginVertical();
            
            // Asset path as clickable link
            if (GUILayout.Button(issue.AssetPath, EditorStyles.linkLabel))
            {
                Selection.activeObject = issue.Asset;
                EditorGUIUtility.PingObject(issue.Asset);
            }
            
            EditorGUILayout.LabelField(issue.Message, EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.EndVertical();
            
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
                EditorUtility.DisplayDialog("Build Issues Fixed", 
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
            issues = PreFlightCheckManager.RunAllChecks();
            Repaint();
        }
    }
}