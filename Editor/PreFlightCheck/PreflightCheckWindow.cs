using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DivineDragon.PreFlightCheck
{
    public class PreflightCheckWindow : EditorWindow
    {
        private enum ViewMode
        {
            ByStatus,
            ByRule,
            ByAsset
        }

        private List<BuildIssue> issues = new List<BuildIssue>();
        private DateTime lastCheckTime = DateTime.MinValue;
        private bool isChecking;
        private ViewMode currentViewMode = ViewMode.ByStatus;

        private Label statusLabel;
        private Button tabByStatus;
        private Button tabByRule;
        private Button tabByAsset;
        private Button saveCheckButton;
        private Button refreshButton;
        private VisualElement rulesHeader;
        private ScrollView rulesScroll;
        private VisualElement rulesBody;
        private Label rulesFoldArrow;
        private Label rulesFoldSummary;
        private VisualElement issueList;

        private const string RulesCollapsedPref = "DivineBuilder.PreflightRulesCollapsed";

        // Same palette as the Divine Builder window.
        private static readonly Color okGreen = new Color(0.30f, 0.78f, 0.33f, 1.0f);
        private static readonly Color warnAmber = new Color(0.92f, 0.66f, 0.18f, 1.0f);
        private static readonly Color errRed = new Color(0.86f, 0.33f, 0.33f, 1.0f);
        private static readonly Color infoBlue = new Color(0.39f, 0.58f, 0.93f, 1.0f);

        [MenuItem("Divine Dragon/Preflight Check", false, 1510)]
        public static void ShowWindow()
        {
            // A regular window (not utility) so it can dock and sit behind other windows
            // instead of floating on top of everything.
            var window = GetWindow<PreflightCheckWindow>("Preflight Check");
            window.minSize = new Vector2(480, 400);
        }

        public static void ShowWithIssues(List<BuildIssue> issues)
        {
            var window = GetWindow<PreflightCheckWindow>("Preflight Check");
            window.minSize = new Vector2(480, 400);
            window.issues = issues;
            window.lastCheckTime = DateTime.Now;
            window.isChecking = false;
            window.RefreshView();
        }

        public void OnEnable()
        {
            // Live results: any completed run (auto sweep, build gate, Refresh) lands here.
            PreFlightCheckManager.ChecksCompleted += OnChecksCompleted;
        }

        public void OnDisable()
        {
            PreFlightCheckManager.ChecksCompleted -= OnChecksCompleted;
        }

        private void OnChecksCompleted(List<BuildIssue> fresh)
        {
            issues = fresh;
            lastCheckTime = DateTime.Now;
            isChecking = false;
            RebuildRulesPanel();
            RefreshView();
        }

        public void CreateGUI()
        {
            var visualTree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
                Build.PackageRoot + "/Editor/PreFlightCheck/PreflightCheckWindow.uxml");
            // Layout restore at editor startup can run before the package is imported;
            // leave the window blank rather than throwing, a reopen rebuilds it.
            if (visualTree == null)
                return;
            VisualElement content = visualTree.CloneTree();
            rootVisualElement.Add(content);
            content.style.flexGrow = 1;

            // The shared builder sheet carries the dd-* design system; ours adds the pf-* bits.
            foreach (string sheetPath in new[]
                     {
                         "/Editor/DivineWindow.uss",
                         "/Editor/PreFlightCheck/PreflightCheckWindow.uss"
                     })
            {
                var styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(Build.PackageRoot + sheetPath);
                if (styleSheet != null)
                    rootVisualElement.styleSheets.Add(styleSheet);
            }

            statusLabel = content.Q<Label>("statusLabel");
            tabByStatus = content.Q<Button>("TabByStatus");
            tabByRule = content.Q<Button>("TabByRule");
            tabByAsset = content.Q<Button>("TabByAsset");
            saveCheckButton = content.Q<Button>("SaveCheckButton");
            refreshButton = content.Q<Button>("RefreshButton");
            rulesHeader = content.Q<VisualElement>("rulesHeader");
            rulesScroll = content.Q<ScrollView>("rulesScroll");
            if (rulesScroll != null)
                rulesScroll.horizontalScroller.style.display = DisplayStyle.None;
            rulesBody = content.Q<VisualElement>("rulesBody");
            rulesFoldArrow = content.Q<Label>("rulesFoldArrow");
            rulesFoldSummary = content.Q<Label>("rulesFoldSummary");
            issueList = content.Q<VisualElement>("issueList");

            var issueScroll = content.Q<ScrollView>("issueScroll");
            if (issueScroll != null)
                issueScroll.horizontalScroller.style.display = DisplayStyle.None;

            tabByStatus.clickable.clicked += () => SelectView(ViewMode.ByStatus);
            tabByRule.clickable.clicked += () => SelectView(ViewMode.ByRule);
            tabByAsset.clickable.clicked += () => SelectView(ViewMode.ByAsset);
            saveCheckButton.clickable.clicked += SaveAndCheck;
            refreshButton.clickable.clicked += RefreshIssues;

            ApplyRulesCollapsed(EditorPrefs.GetBool(RulesCollapsedPref, true));
            rulesHeader.RegisterCallback<MouseDownEvent>(evt =>
            {
                bool collapsed = !EditorPrefs.GetBool(RulesCollapsedPref, true);
                EditorPrefs.SetBool(RulesCollapsedPref, collapsed);
                ApplyRulesCollapsed(collapsed);
            });

            // Keep the relative time and the Save & check button (shown only when there's
            // something to save) current while the window sits open.
            rootVisualElement.schedule.Execute(() =>
            {
                UpdateStatus();
                RefreshSaveCheckButton();
            }).Every(1000);

            // Adopt what the monitor already knows, then let it re-sweep if anything is stale.
            if (lastCheckTime == DateTime.MinValue && PreflightMonitor.HasRun)
            {
                issues = PreflightMonitor.LastIssues;
                lastCheckTime = PreflightMonitor.LastCheckTime;
            }

            RebuildRulesPanel();
            RefreshView();
            RefreshSaveCheckButton();
            PreflightMonitor.EnsureFresh();
        }

        private void SelectView(ViewMode mode)
        {
            currentViewMode = mode;
            RefreshView();
        }

        private void SaveAndCheck()
        {
            PreFlightCheckManager.SavePendingEdits();
            RefreshSaveCheckButton();
            RefreshIssues();
        }

        // The button only earns its place when saving would change what the checks see.
        private void RefreshSaveCheckButton()
        {
            if (saveCheckButton != null)
                saveCheckButton.style.display =
                    PreFlightCheckManager.HasSavableEdits() ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void RefreshIssues()
        {
            isChecking = true;
            RefreshView();

            EditorApplication.delayCall += () =>
            {
                // The window may have been closed, or ShowWithIssues may have delivered
                // fresh results (clearing isChecking) while this call was pending.
                if (this == null || !isChecking)
                    return;

                // Results come back through ChecksCompleted. If the run was guarded
                // (play mode and friends) no event fires, so stop the spinner here.
                PreFlightCheckManager.RunAllChecks();
                if (this != null && isChecking)
                {
                    isChecking = false;
                    RefreshView();
                }
            };
        }

        private void AutofixIssues(List<BuildIssue> toFix)
        {
            int fixedCount = PreFlightCheckManager.AutoFixAll(toFix);
            if (fixedCount > 0)
                Debug.Log($"Preflight: autofixed {fixedCount} issue(s).");
            RefreshIssues();
        }

        // Repaints everything derived from the current issue list. Safe to call before
        // CreateGUI has run (ShowWithIssues can arrive first); the view is built from
        // state when the UI comes up.
        private void RefreshView()
        {
            if (issueList == null)
                return;

            UpdateTabs();
            UpdateStatus();

            refreshButton.SetEnabled(!isChecking);

            RebuildIssueList();
        }

        private void UpdateTabs()
        {
            SetTabActive(tabByStatus, currentViewMode == ViewMode.ByStatus);
            SetTabActive(tabByRule, currentViewMode == ViewMode.ByRule);
            SetTabActive(tabByAsset, currentViewMode == ViewMode.ByAsset);
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

        private void UpdateStatus()
        {
            if (statusLabel == null)
                return;

            if (isChecking)
            {
                statusLabel.text = "Running checks…";
                statusLabel.style.color = new StyleColor(StyleKeyword.Null);
            }
            else if (lastCheckTime == DateTime.MinValue)
            {
                statusLabel.text = "No checks run yet.";
                statusLabel.style.color = new StyleColor(StyleKeyword.Null);
            }
            else if (issues.Count == 0)
            {
                statusLabel.text = $"No issues · checked {TimeFormatter.GetRelativeTimeWithTimestamp(lastCheckTime)}" +
                                   RunCostSuffix();
                statusLabel.style.color = okGreen;
            }
            else
            {
                bool autofixOn = DivineDragonSettingsScriptableObject.instance.getPreBuildAutofix();
                var tiers = PreFlightCheckManager.CountTiers(issues);
                statusLabel.text = $"{PreFlightCheckManager.SummarizeTiers(tiers, autofixOn)} · checked " +
                                   TimeFormatter.GetRelativeTimeWithTimestamp(lastCheckTime) + RunCostSuffix();

                if (PreFlightCheckManager.WillBlockCount(tiers, autofixOn) > 0)
                    statusLabel.style.color = errRed;
                else if (tiers.Attention > 0 || tiers.Fixable > 0)
                    statusLabel.style.color = warnAmber;
                else
                    statusLabel.style.color = infoBlue;
            }
        }

        // How much the last full check cost, so we can judge whether continuous validation
        // would be affordable. Empty until a run has completed in this editor session.
        private static string RunCostSuffix()
        {
            if (PreFlightCheckManager.LastRunSeconds <= 0)
                return "";

            int count = PreFlightCheckManager.LastRunAssetCount;
            return $" · {count} asset{(count == 1 ? "" : "s")} in " +
                   PreFlightCheckManager.FormatDuration(PreFlightCheckManager.LastRunSeconds);
        }

        private void RebuildIssueList()
        {
            issueList.Clear();

            if (isChecking)
            {
                issueList.Add(MakeEmptyCard("Running checks…"));
                return;
            }

            if (lastCheckTime == DateTime.MinValue)
            {
                issueList.Add(MakeEmptyCard("Hit Refresh to run the checks."));
                return;
            }

            if (issues.Count == 0)
            {
                issueList.Add(MakeEmptyCard("No issues found - your build is ready."));
                return;
            }

            if (currentViewMode == ViewMode.ByStatus)
                BuildByStatusView();
            else if (currentViewMode == ViewMode.ByRule)
                BuildByRuleView();
            else
                BuildByAssetView();
        }

        private VisualElement MakeEmptyCard(string message)
        {
            var card = new VisualElement();
            card.AddToClassList("dd-card");

            var label = new Label(message);
            label.AddToClassList("pf-empty");
            card.Add(label);
            return card;
        }

        // Groups issues by what happens at build time, so the default tab answers
        // "can I build, and what should I look at first."
        private void BuildByStatusView()
        {
            var blockers = new List<BuildIssue>();
            var attention = new List<BuildIssue>();
            var fixable = new List<BuildIssue>();
            var infos = new List<BuildIssue>();

            foreach (var issue in issues)
            {
                if (issue.Rule.CanAutoFix)
                    fixable.Add(issue);
                else if (issue.Severity == IssueSeverity.Error)
                    blockers.Add(issue);
                else if (issue.Severity == IssueSeverity.Warning)
                    attention.Add(issue);
                else
                    infos.Add(issue);
            }

            if (blockers.Count > 0)
                issueList.Add(MakeTierCard($"Fatal ({blockers.Count})", errRed,
                    "The build can't run until these are fixed by hand - there's no autofix for them.",
                    null, blockers));

            if (attention.Count > 0)
                issueList.Add(MakeTierCard($"Needs your attention ({attention.Count})", warnAmber,
                    "The build proceeds, but these land in the build report as warnings.",
                    null, attention));

            if (fixable.Count > 0)
            {
                var fixAll = new Button(() => AutofixIssues(fixable))
                {
                    text = "Fix all",
                    tooltip = "Run every autofix now"
                };
                fixAll.AddToClassList("dd-btn");
                fixAll.AddToClassList("dd-btn-accent");
                issueList.Add(MakeTierCard($"Autofixable ({fixable.Count})", okGreen,
                    "Autofix clears these - during the build with the Autofix checkbox, or right now.",
                    fixAll, fixable));
            }

            if (infos.Count > 0)
                issueList.Add(MakeTierCard($"Info ({infos.Count})", infoBlue,
                    "Not blocking and not autofixable - things to handle when you're ready. The build ignores these.",
                    null, infos));
        }

        private VisualElement MakeTierCard(string title, Color accent, string blurb,
            VisualElement action, List<BuildIssue> tierIssues)
        {
            var card = new VisualElement();
            card.AddToClassList("dd-card");

            var head = new VisualElement();
            head.AddToClassList("dd-dest-header");

            var titleLabel = new Label(title);
            titleLabel.AddToClassList("dd-section");
            titleLabel.AddToClassList("dd-dest-title");
            titleLabel.style.color = accent;
            head.Add(titleLabel);
            if (action != null)
                head.Add(action);
            card.Add(head);

            if (!string.IsNullOrEmpty(blurb))
            {
                var desc = new Label(blurb);
                desc.AddToClassList("pf-group-desc");
                card.Add(desc);
            }

            foreach (var issue in tierIssues)
                card.Add(MakeIssueRow(issue, showFileLink: true, showRuleName: true));

            return card;
        }

        private void BuildByRuleView()
        {
            foreach (var group in issues.GroupBy(i => i.Rule))
            {
                var rule = group.Key;
                var ruleIssues = group.ToList();

                var card = new VisualElement();
                card.AddToClassList("dd-card");

                var head = new VisualElement();
                head.AddToClassList("dd-dest-header");

                var title = new Label($"{rule.Name} ({ruleIssues.Count})");
                title.AddToClassList("dd-section");
                title.AddToClassList("dd-dest-title");
                head.Add(title);

                if (rule.CanAutoFix)
                    head.Add(MakeGhostButton("Autofix", $"Fix every issue found by {rule.Name}",
                        () => AutofixIssues(ruleIssues)));

                card.Add(head);

                if (!string.IsNullOrEmpty(rule.Description))
                {
                    var desc = new Label(rule.Description);
                    desc.AddToClassList("pf-group-desc");
                    card.Add(desc);
                }

                foreach (var issue in ruleIssues)
                    card.Add(MakeIssueRow(issue, showFileLink: true, showRuleName: false));

                issueList.Add(card);
            }
        }

        private void BuildByAssetView()
        {
            foreach (var group in issues.GroupBy(i => i.AssetPath))
            {
                string assetPath = group.Key;
                var assetIssues = group.ToList();

                var card = new VisualElement();
                card.AddToClassList("dd-card");

                var head = new VisualElement();
                head.AddToClassList("dd-dest-header");

                var title = new Label($"{Path.GetFileName(assetPath)} ({assetIssues.Count})");
                title.AddToClassList("dd-section");
                title.AddToClassList("dd-dest-title");
                head.Add(title);

                var actions = new VisualElement();
                actions.AddToClassList("pf-issue-actions");
                actions.Add(MakeGhostButton("Open", "Select and ping this asset",
                    () => OpenIssueAsset(assetIssues[0])));
                if (assetIssues.Any(i => i.Rule.CanAutoFix))
                    actions.Add(MakeGhostButton("Autofix", "Fix every fixable issue on this asset",
                        () => AutofixIssues(assetIssues)));
                head.Add(actions);

                card.Add(head);

                var dir = new Label(Path.GetDirectoryName(assetPath));
                dir.AddToClassList("pf-issue-path");
                card.Add(dir);

                foreach (var issue in assetIssues)
                    card.Add(MakeIssueRow(issue, showFileLink: false, showRuleName: true));

                issueList.Add(card);
            }
        }

        private VisualElement MakeIssueRow(BuildIssue issue, bool showFileLink, bool showRuleName)
        {
            var row = new VisualElement();
            row.AddToClassList("pf-issue");

            var icon = new Label("●")
            {
                tooltip = issue.Severity.ToString()
            };
            icon.AddToClassList("pf-issue-icon");
            icon.style.color = SeverityColor(issue.Severity);
            row.Add(icon);

            var body = new VisualElement();
            body.AddToClassList("pf-issue-body");

            if (showRuleName)
            {
                var ruleName = new Label(issue.Rule.Name);
                ruleName.AddToClassList("pf-issue-rule");
                body.Add(ruleName);
            }

            if (showFileLink)
            {
                var link = new Button(() => OpenIssueAsset(issue))
                {
                    text = Path.GetFileName(issue.AssetPath),
                    tooltip = "Select and ping this asset"
                };
                link.AddToClassList("pf-link");
                body.Add(link);

                var dir = new Label(Path.GetDirectoryName(issue.AssetPath));
                dir.AddToClassList("pf-issue-path");
                body.Add(dir);
            }

            var message = new Label(issue.Message);
            message.AddToClassList("pf-issue-msg");
            body.Add(message);

            if (issue.Targets != null && issue.Targets.Count > 0)
            {
                var targets = new VisualElement();
                targets.AddToClassList("pf-issue-targets");
                foreach (var target in issue.Targets)
                {
                    if (target?.Target == null)
                        continue;

                    var captured = target;
                    var link = new Button(() => PingTarget(issue, captured))
                    {
                        text = captured.Label,
                        tooltip = string.IsNullOrEmpty(captured.Tooltip) ? $"Select {captured.Label}" : captured.Tooltip
                    };
                    link.AddToClassList("pf-link");
                    targets.Add(link);
                }
                body.Add(targets);
            }

            row.Add(body);

            var actions = new VisualElement();
            actions.AddToClassList("pf-issue-actions");

            if (issue.Actions != null)
            {
                foreach (var action in issue.Actions)
                {
                    if (action?.Execute == null)
                        continue;

                    var captured = action;
                    actions.Add(MakeGhostButton(captured.Label, captured.Tooltip, () =>
                    {
                        if (captured.Execute())
                            RefreshIssues();
                    }));
                }
            }

            if (issue.Rule.CanAutoFix)
                actions.Add(MakeGhostButton("Autofix", "Fix this issue", () =>
                {
                    if (issue.Rule.AutoFix(issue))
                        RefreshIssues();
                }));
            row.Add(actions);

            return row;
        }

        private static Button MakeGhostButton(string text, string tooltip, Action onClick)
        {
            var button = new Button(onClick)
            {
                text = text,
                tooltip = tooltip
            };
            button.AddToClassList("dd-btn");
            button.AddToClassList("dd-btn-ghost");
            return button;
        }

        private static Color SeverityColor(IssueSeverity severity)
        {
            switch (severity)
            {
                case IssueSeverity.Error:
                    return errRed;
                case IssueSeverity.Warning:
                    return warnAmber;
                default:
                    return infoBlue;
            }
        }

        private void ApplyRulesCollapsed(bool collapsed)
        {
            // The scroll wrapper is what shows/hides; rulesBody lives inside it.
            if (rulesScroll != null)
                rulesScroll.style.display = collapsed ? DisplayStyle.None : DisplayStyle.Flex;
            else if (rulesBody != null)
                rulesBody.style.display = collapsed ? DisplayStyle.None : DisplayStyle.Flex;
            if (rulesFoldArrow != null)
                rulesFoldArrow.text = collapsed ? "▸" : "▾";
            if (rulesHeader != null)
                rulesHeader.style.marginBottom = collapsed ? 0 : 6;
            UpdateRulesSummary();
        }

        private void RebuildRulesPanel()
        {
            if (rulesBody == null)
                return;

            rulesBody.Clear();

            var infos = PreFlightRuleRegistry.GetRegisteredRuleInfos();

            if (infos.Count == 0)
            {
                var none = new Label("No rules are registered. Additional rule packages may not be loaded.");
                none.AddToClassList("pf-empty");
                rulesBody.Add(none);
                UpdateRulesSummary();
                return;
            }

            var grouped = infos
                .GroupBy(info =>
                    info.PackageDisplayName ?? info.PackageName ??
                    (info.AssemblyName != null ? $"Assembly: {info.AssemblyName}" : "Unknown Source"))
                .OrderBy(g => g.Key);

            foreach (var group in grouped)
            {
                string header = group.Key;
                var sample = group.First();
                if (!string.IsNullOrEmpty(sample.PackageVersion) && sample.PackageDisplayName != null)
                    header = $"{sample.PackageDisplayName} ({sample.PackageName} {sample.PackageVersion})";

                var pkg = new Label(header);
                pkg.AddToClassList("pf-rule-pkg");
                rulesBody.Add(pkg);

                foreach (var info in group.OrderBy(i => i.RuleName))
                    rulesBody.Add(MakeRuleRow(info));
            }

            UpdateRulesSummary();
        }

        private VisualElement MakeRuleRow(PreFlightRuleRegistry.RegisteredRuleInfo info)
        {
            var row = new VisualElement();
            row.AddToClassList("pf-rule-row");

            var body = new VisualElement();
            body.AddToClassList("pf-rule-body");

            var name = new Label(info.RuleName ?? "Unknown Rule");
            name.AddToClassList("pf-rule-name");
            body.Add(name);

            if (!string.IsNullOrEmpty(info.RuleDescription))
            {
                var desc = new Label(info.RuleDescription);
                desc.AddToClassList("pf-rule-desc");
                body.Add(desc);
            }

            var meta = new Label(info.CanAutoFix ? $"{info.DefaultSeverity} · can autofix" : info.DefaultSeverity.ToString());
            meta.AddToClassList("pf-rule-meta");
            body.Add(meta);

            row.Add(body);

            if (info.RuleType != null)
            {
                var toggle = new Toggle
                {
                    text = "Enabled",
                    value = info.Enabled,
                    tooltip = "Run this rule before builds"
                };
                toggle.AddToClassList("pf-rule-toggle");
                toggle.RegisterValueChangedCallback(evt =>
                {
                    PreFlightRuleRegistry.SetRuleEnabled(info.RuleType, evt.newValue);
                    UpdateRulesSummary();
                });
                row.Add(toggle);
            }

            return row;
        }

        // When the card is folded, show a small count next to the title so the state reads at a glance.
        private void UpdateRulesSummary()
        {
            if (rulesFoldSummary == null)
                return;

            bool collapsed = EditorPrefs.GetBool(RulesCollapsedPref, true);
            if (!collapsed)
            {
                rulesFoldSummary.style.display = DisplayStyle.None;
                return;
            }

            var infos = PreFlightRuleRegistry.GetRegisteredRuleInfos();
            int off = infos.Count(i => !i.Enabled);
            string summary = $"{infos.Count} rule{(infos.Count == 1 ? "" : "s")}";
            if (off > 0)
                summary += $" · {off} off";

            rulesFoldSummary.text = summary;
            rulesFoldSummary.style.display = DisplayStyle.Flex;
        }

        private void PingTarget(BuildIssue issue, IssueTarget target)
        {
            var obj = target.Target;

            if (obj == null)
            {
                // References go stale when assets get unloaded (play mode round trips
                // and the like); a fresh check rebuilds them.
                Debug.LogWarning($"Preflight: '{target.Label}' is no longer loaded. Hit Refresh and try again.");
                return;
            }

            // Components live inside the prefab, so that needs prefab mode; assets
            // (materials, textures) show their Inspector straight from selection.
            if (obj is Component && issue.Asset is GameObject)
            {
                AssetDatabase.OpenAsset(issue.Asset);
                EditorApplication.delayCall += () =>
                {
                    Selection.activeObject = obj;
                    EditorGUIUtility.PingObject(obj);
                };
                return;
            }

            Selection.activeObject = obj;
            EditorGUIUtility.PingObject(obj);
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
