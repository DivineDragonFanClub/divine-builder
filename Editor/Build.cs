using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using DivineDragon.Patcher;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Build;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace DivineDragon
{
    public class Build
    {
        [MenuItem("Divine Dragon/Build", false, 1500)]
        public static void BuildAddressables()
        {
            BuildAddressableContent();
        }

        [MenuItem("Divine Dragon/Build", true, 1500)]
        static bool ValidateBuildAddressables()
        {
            // Return false if no mod output path is set
            return !string.IsNullOrEmpty(DivineDragonSettingsScriptableObject.instance.getModPath());
        }

        private static string dataPath = "Data/StreamingAssets/aa/Switch";

        public const string PackageRoot = "Packages/com.divinedragon.builder";

        public static bool BuildAddressableContent()
        {
            AddressableAssetSettings
                .BuildPlayerContent(out AddressablesPlayerBuildResult result);
            bool success = string.IsNullOrEmpty(result.Error);

            if (!success)
            {
                Debug.LogError("Addressables build error encountered: " + result.Error);
                return false;
            }

            var outputDirectory = BuildModOutputPath();

            if (!Directory.Exists(outputDirectory))
            {
                Directory.CreateDirectory(outputDirectory);
            }

            string projectCurrentDir = Directory.GetCurrentDirectory();
            string settingsJsonPath = Path.GetFullPath(Path.Combine(projectCurrentDir, result.OutputPath));

            bool patched = DivineDragonSettingsScriptableObject.instance.getUseLegacyRustPatcher()
                ? RunLegacyPatcher(outputDirectory, settingsJsonPath)
                : RunPatcher(outputDirectory, settingsJsonPath);

            if (!patched)
                return false;

            AddressableUtility.RemoveAddressablesWithLabel(outputDirectory, "removePostBuild");

            if (DivineDragonSettingsScriptableObject.instance.getOpenAfterBuild())
            {
                EditorUtility.RevealInFinder(outputDirectory);
            }

            return true;
        }

        static bool RunPatcher(string outputDirectory, string settingsJsonPath)
        {
            string cacheJson = Path.GetFullPath(Path.Combine(PackageRoot, "PatcherData~/cache.json"));
            string uacJson = Path.GetFullPath(Path.Combine(PackageRoot, "PatcherData~/uac-cache.json"));
            string gameCodeAsm = DivineDragonSettingsScriptableObject.instance.gameCodeAssemblyName;

            var cts = new CancellationTokenSource();
            var progress = new EditorPatchProgress(cts);
            PatchResult result;
            try
            {
                var patcher = new BundlePatcher(gameCodeAsm);
                result = patcher.Run(cacheJson, uacJson, settingsJsonPath, outputDirectory, progress, cts.Token);
            }
            catch (Exception e)
            {
                Debug.LogError($"Divine Builder: patching failed to start: {e}");
                return false;
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                cts.Dispose();
            }

            foreach (var w in result.Warnings)
                Debug.LogWarning($"Divine Builder: {w}");
            foreach (var err in result.Errors)
                Debug.LogError($"Divine Builder: {err}");

            if (result.Cancelled)
            {
                Debug.LogWarning("Divine Builder: build cancelled.");
                return false;
            }

            if (!result.Success)
            {
                Debug.LogError($"Divine Builder: build failed, {result.Errors.Count} bundle(s) had errors. " +
                               "See the messages above.");
                return false;
            }

            Debug.Log($"Divine Builder: patched {result.Patched} bundle(s), skipped {result.Skipped}.");
            return true;
        }

        static bool RunLegacyPatcher(string outputDirectory, string settingsJsonPath)
        {
            var args = String.Format("fix \"{0}\" \"{1}\"", outputDirectory, settingsJsonPath);

            var bundleTools = "bundle_tools";
            if (Application.platform == RuntimePlatform.WindowsEditor)
                bundleTools += ".exe";

            RunProcess(bundleTools, false, args);
            return true;
        }

        static string BuildModOutputPath()
        {
            return Path.Combine(DivineDragonSettingsScriptableObject.instance.getModPath(), dataPath);
        }

        static void RunProcess(string command, bool runShell, string args = null)
        {
            string projectCurrentDir = Directory.GetCurrentDirectory();
            command = Path.GetFullPath(Path.Combine(projectCurrentDir, PackageRoot, command));

            Debug.Log(string.Format("{0} Run command: {1}", DateTime.Now, command));

            ProcessStartInfo ps = new ProcessStartInfo(command);
            using (Process p = new Process())
            {
                ps.UseShellExecute = runShell;
                if (!runShell)
                {
                    ps.RedirectStandardOutput = true;
                    ps.RedirectStandardError = true;
                    ps.StandardOutputEncoding = System.Text.ASCIIEncoding.ASCII;
                    ps.CreateNoWindow = true;
                }
                if (args != null && args != "")
                {
                    ps.Arguments = args;
                }

                p.OutputDataReceived += (sender, e) => {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        Debug.Log($"Divine Builder: {e.Data}");
                    }
                };

                p.ErrorDataReceived += (sender, e) => {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        Debug.LogError($"Divine Builder: {e.Data}");
                    }
                };

                p.EnableRaisingEvents = true;
                p.StartInfo = ps;
                p.Start();
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
                p.WaitForExit();
            }
        }

    }

    public class EditorPatchProgress : IProgress<PatchProgress>
    {
        private readonly CancellationTokenSource _cts;

        public EditorPatchProgress(CancellationTokenSource cts)
        {
            _cts = cts;
        }

        public void Report(PatchProgress p)
        {
            float ratio = p.Total > 0 ? (float)p.Current / p.Total : 0f;
            string title = $"Divine Builder ({p.Phase})";
            string info = $"{p.Current}/{p.Total}  {p.BundleName}";
            if (EditorUtility.DisplayCancelableProgressBar(title, info, ratio))
                _cts.Cancel();
        }
    }

    public static class AddressableUtility
    {
        public static void RemoveAddressablesWithLabel(string outputDirectory, string label)
        {
            var settings = AddressableAssetSettingsDefaultObject.Settings;

            if (settings == null)
            {
                Debug.LogError("AddressableAssetSettings not found.");
                return;
            }

            int deleted = 0;
            int missed = 0;

            foreach (var group in settings.groups)
            {
                if (group == null || group.HasSchema<PlayerDataGroupSchema>())
                    continue;

                var groupPrefix = group.name + "_" + "assets";

                foreach (var entry in group.entries)
                {
                    if (!entry.labels.Contains(label))
                        continue;

                    var sanitized = entry.address.ToLower().Replace(" ", "");
                    var entryPath = groupPrefix + "_" + sanitized + ".bundle";
                    var filePath = Path.Combine(outputDirectory, entryPath);

                    if (File.Exists(filePath))
                    {
                        File.Delete(filePath);
                        deleted++;
                    }
                    else
                    {
                        missed++;
                        Debug.LogWarning($"[removePostBuild] expected bundle not found: {filePath}");
                    }
                }
            }

            Debug.Log($"[removePostBuild] deleted {deleted} bundle(s), {missed} not found.");
        }
    }
}
