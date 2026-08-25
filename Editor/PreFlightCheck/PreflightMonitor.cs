using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.SceneManagement;

namespace DivineDragon.PreFlightCheck
{
    /// <summary>
    /// Keeps pre-flight results fresh while someone is looking at them. Change events
    /// (asset imports, addressable edits, scene changes) only mark the results dirty;
    /// an actual sweep runs when a watcher window is open, debounced so import bursts
    /// cost one run. With no watcher open the flag just waits - the next window to
    /// open (or the build gate) picks it up.
    ///
    /// This relies on rules keeping Validate strictly read-only: a rule that raises
    /// change events during validation re-triggers the very sweep it runs in and the
    /// checks loop forever. See the contract on <see cref="BuildRule.Validate"/>.
    /// </summary>
    [InitializeOnLoad]
    public static class PreflightMonitor
    {
        private const double DebounceSeconds = 1.0;

        /// <summary>Results of the most recent completed run, whoever triggered it.</summary>
        public static List<BuildIssue> LastIssues { get; private set; } = new List<BuildIssue>();

        public static DateTime LastCheckTime { get; private set; } = DateTime.MinValue;

        public static bool HasRun => LastCheckTime != DateTime.MinValue;

        // Dirty until the first run so a freshly opened window always sweeps.
        private static bool dirty = true;
        private static double sweepDueAt = -1;

        static PreflightMonitor()
        {
            PreFlightCheckManager.ChecksCompleted += OnChecksCompleted;
            AddressableAssetSettings.OnModificationGlobal += (settings, evt, obj) => NotifyChanged();
            EditorSceneManager.sceneOpened += (scene, mode) => NotifyChanged();
            EditorSceneManager.sceneClosed += scene => NotifyChanged();
            EditorSceneManager.sceneSaved += scene => NotifyChanged();
            EditorSceneManager.sceneDirtied += scene => NotifyChanged();
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            EditorApplication.update += OnUpdate;
        }

        /// <summary>Something that could affect check results changed.</summary>
        internal static void NotifyChanged()
        {
            dirty = true;

            // (Re)arm the debounce timer only when the results are actually on screen.
            if (AnyWatcherOpen())
                sweepDueAt = EditorApplication.timeSinceStartup + DebounceSeconds;
        }

        /// <summary>
        /// Called by watcher windows when they open: sweep if anything changed since the
        /// last run, otherwise do nothing. Runs on the next update tick, never inline.
        /// </summary>
        public static void EnsureFresh()
        {
            if (dirty)
                sweepDueAt = EditorApplication.timeSinceStartup;
        }

        private static void OnUpdate()
        {
            if (sweepDueAt < 0 || EditorApplication.timeSinceStartup < sweepDueAt)
                return;

            sweepDueAt = -1;

            // Checks are disabled in play mode; stay dirty and catch up on exit.
            if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
                return;

            if (!AnyWatcherOpen())
                return;

            PreFlightCheckManager.RunAllChecks(); // results arrive via ChecksCompleted
        }

        private static void OnPlayModeChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.EnteredEditMode)
                EnsureFresh();
        }

        private static void OnChecksCompleted(List<BuildIssue> issues)
        {
            LastIssues = issues;
            LastCheckTime = DateTime.Now;
            dirty = false;
        }

        private static bool AnyWatcherOpen()
        {
            return EditorWindow.HasOpenInstances<PreflightCheckWindow>()
                   || EditorWindow.HasOpenInstances<SettingsWindow>();
        }
    }

    // Funnels every import/delete/move into the monitor's dirty flag. Cheap when nothing
    // is watching; the monitor debounces the rest.
    internal class PreflightAssetChangeListener : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(
            string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
        {
            if (importedAssets.Length == 0 && deletedAssets.Length == 0 && movedAssets.Length == 0)
                return;

            PreflightMonitor.NotifyChanged();
        }
    }
}
