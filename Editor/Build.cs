using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
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
            var outcome = BuildAddressableContent();
            // The menu has no window to show the result, so surface a failure in a dialog.
            if (!outcome.Success && !outcome.Cancelled)
            {
                string detail = outcome.Errors.Count > 0 ? outcome.Errors[0].Detail : "See the console for details.";
                EditorUtility.DisplayDialog("Build failed", detail, "OK");
            }
        }

        [MenuItem("Divine Dragon/Build", true, 1500)]
        static bool ValidateBuildAddressables()
        {
            var s = DivineDragonSettingsScriptableObject.instance;

            // Don't do a live reachability check here (validators run constantly and it would hang).
            // The build itself pre-flights the FTP device and reports if it can't be reached.
            if (s.getDeliveryTarget() == DeliveryTarget.Ftp)
            {
                return !string.IsNullOrEmpty(s.getFtpHost()) && !string.IsNullOrEmpty(s.getFtpModName());
            }

            return !string.IsNullOrEmpty(s.getModPath());
        }

        private static string dataPath = "Data/StreamingAssets/aa/Switch";

        public const string PackageRoot = "Packages/com.divinedragon.builder";

        public static BuildOutcome BuildAddressableContent()
        {
            var outcome = new BuildOutcome();
            var sw = Stopwatch.StartNew();
            var settings = DivineDragonSettingsScriptableObject.instance;
            bool ftp = settings.getDeliveryTarget() == DeliveryTarget.Ftp;

            // Check the FTP destination before the expensive build so we fail fast.
            string ftpModName = null;
            if (ftp)
            {
                if (string.IsNullOrEmpty(settings.getFtpHost()))
                    return Fail(outcome, sw, "FTP setup", "(ftp)", "Set the FTP host before building.");
                ftpModName = settings.getFtpModName();
                if (string.IsNullOrEmpty(ftpModName))
                    return Fail(outcome, sw, "FTP setup", "(ftp)", "Pick or create a mod to upload to before building.");

                // Make sure the device is actually reachable before running a long build we'd only
                // fail to deliver. Short timeout so an offline device reports quickly.
                try
                {
                    new FtpClient(settings.getFtpHost(), settings.getFtpPort(), settings.getFtpAnonymous(),
                        settings.getFtpUser(), settings.getFtpPassword(), 4000).TestConnection();
                    FtpStatus.Set(settings.getFtpHost(), settings.getFtpPort(), true);
                }
                catch (Exception e)
                {
                    FtpStatus.Set(settings.getFtpHost(), settings.getFtpPort(), false);
                    Debug.LogError($"Divine Builder: could not reach the FTP device at " +
                                   $"{settings.getFtpHost()}:{settings.getFtpPort()}. {e.Message}");
                    return Fail(outcome, sw, "FTP", "(ftp)",
                        $"Could not reach the FTP device at {settings.getFtpHost()}:{settings.getFtpPort()}.");
                }
            }

            // Validate addressables before the expensive build. With Autofix on, fixable
            // issues are repaired first. Of whatever remains, only errors with no autofix
            // block the build. Everything else that isn't a minor issue rides along on the
            // outcome as a note - including fixable issues left unfixed when Autofix is off.
            if (settings.getPreBuildAutofix())
            {
                var initialIssues = PreFlightCheck.PreFlightCheckManager.RunAllChecks();
                if (initialIssues.Any(i => i.Rule.CanAutoFix))
                {
                    int fixedCount = PreFlightCheck.PreFlightCheckManager.AutoFixAll(initialIssues);
                    Debug.Log($"Divine Builder: autofixed {fixedCount} validation issue(s).");
                }
            }

            var validationIssues = PreFlightCheck.PreFlightCheckManager.RunAllChecks();
            var blockingErrors = validationIssues
                .Where(i => i.Severity == PreFlightCheck.IssueSeverity.Error && !i.Rule.CanAutoFix).ToList();
            foreach (var issue in validationIssues)
            {
                // Minor issues (info) are window-only; blocking errors are reported below.
                if (issue.Severity == PreFlightCheck.IssueSeverity.Info) continue;
                if (issue.Severity == PreFlightCheck.IssueSeverity.Error && !issue.Rule.CanAutoFix) continue;

                string warning = $"Validation: {issue.AssetPath}: {issue.Message}";
                outcome.Warnings.Add(warning);
                Debug.LogWarning($"Divine Builder: {warning}");
            }

            if (blockingErrors.Count > 0)
            {
                PreFlightCheck.PreflightCheckWindow.ShowWithIssues(validationIssues);
                Debug.LogError($"Divine Builder: build cancelled, {blockingErrors.Count} validation error(s) found. " +
                               "See the Validation window.");
                outcome.FailureStage = "validation";
                foreach (var issue in blockingErrors)
                {
                    outcome.Errors.Add(new BuildError
                    {
                        BundlePath = issue.AssetPath,
                        Kind = BuildErrorKind.PreFlight,
                        Detail = issue.Message,
                        Hint = "Fix it in the Validation window."
                    });
                }
                outcome.ElapsedSeconds = sw.Elapsed.TotalSeconds;
                return outcome;
            }

            AddressableAssetSettings
                .BuildPlayerContent(out AddressablesPlayerBuildResult result);

            if (!string.IsNullOrEmpty(result.Error))
            {
                Debug.LogError("Addressables build error encountered: " + result.Error);
                return Fail(outcome, sw, "Addressables build", "(Addressables build)", result.Error);
            }

            // Local targets build straight into the mod folder. FTP builds into a temp staging
            // folder that we upload afterwards.
            string stagingRoot = null;
            string outputDirectory;
            if (ftp)
            {
                stagingRoot = Path.Combine(Path.GetTempPath(), "DivineBuilderFtp", MakeSafeName(ftpModName));
                TryDeleteDirectory(stagingRoot);
                outputDirectory = Path.Combine(stagingRoot, dataPath);
            }
            else
            {
                outputDirectory = BuildModOutputPath();
                outcome.OutputDirectory = outputDirectory;
            }

            if (!Directory.Exists(outputDirectory))
            {
                Directory.CreateDirectory(outputDirectory);
            }

            string projectCurrentDir = Directory.GetCurrentDirectory();
            string settingsJsonPath = Path.GetFullPath(Path.Combine(projectCurrentDir, result.OutputPath));

            if (settings.getUseLegacyRustPatcher())
            {
                // The Rust tool doesn't hand back structured results, so we can only assume it worked.
                RunLegacyPatcher(outputDirectory, settingsJsonPath);
                outcome.Success = true;
            }
            else
            {
                var patch = RunPatcher(outputDirectory, settingsJsonPath);
                outcome.Patched = patch.Patched;
                outcome.Skipped = patch.Skipped;
                outcome.Warnings.AddRange(patch.Warnings);
                outcome.Errors.AddRange(patch.Errors);
                outcome.Cancelled = patch.Cancelled;
                outcome.Success = patch.Success;
                if (patch.Cancelled)
                    outcome.FailureStage = "cancelled";
                else if (!patch.Success)
                    outcome.FailureStage = "patching";
            }

            if (!outcome.Success)
            {
                TryDeleteDirectory(stagingRoot);
                outcome.ElapsedSeconds = sw.Elapsed.TotalSeconds;
                return outcome;
            }

            AddressableUtility.RemoveAddressablesWithLabel(outputDirectory, "removePostBuild");

            if (ftp)
            {
                bool uploaded = RunFtpUpload(settings, stagingRoot, ftpModName, outcome);
                TryDeleteDirectory(stagingRoot);
                if (!uploaded)
                {
                    outcome.Success = false;
                    if (string.IsNullOrEmpty(outcome.FailureStage))
                        outcome.FailureStage = outcome.Cancelled ? "cancelled" : "FTP upload";
                    outcome.ElapsedSeconds = sw.Elapsed.TotalSeconds;
                    return outcome;
                }

                outcome.DeliveryNote = $"uploaded to ftp://{settings.getFtpHost()}:{settings.getFtpPort()}/" +
                                       $"{settings.engageModsPath}/{ftpModName}";
            }
            else if (settings.getOpenAfterBuild())
            {
                EditorUtility.RevealInFinder(outputDirectory);
            }

            outcome.ElapsedSeconds = sw.Elapsed.TotalSeconds;
            return outcome;
        }

        static BuildOutcome Fail(BuildOutcome outcome, Stopwatch sw, string stage, string where, string detail)
        {
            outcome.FailureStage = stage;
            outcome.Errors.Add(new BuildError
            {
                BundlePath = where,
                Kind = BuildErrorKind.BundleIo,
                Detail = detail
            });
            outcome.ElapsedSeconds = sw.Elapsed.TotalSeconds;
            return outcome;
        }

        // Builds live in a temp folder named after the mod, so strip anything not valid in a path.
        static string MakeSafeName(string name)
        {
            foreach (var c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name;
        }

        static void TryDeleteDirectory(string dir)
        {
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
                return;
            try
            {
                Directory.Delete(dir, true);
            }
            catch (Exception e)
            {
                Debug.LogWarning("Divine Builder: could not clean up staging folder: " + e.Message);
            }
        }

        // Uploads the staged build tree to engage/mods/<mod> over FTP, with a cancelable progress bar.
        static bool RunFtpUpload(DivineDragonSettingsScriptableObject settings, string stagingRoot,
            string modName, BuildOutcome outcome)
        {
            string remoteModDir = settings.engageModsPath.Replace("\\", "/").TrimEnd('/') + "/" + modName;
            var client = new FtpClient(settings.getFtpHost(), settings.getFtpPort(),
                settings.getFtpAnonymous(), settings.getFtpUser(), settings.getFtpPassword());

            var files = Directory.GetFiles(stagingRoot, "*", SearchOption.AllDirectories);
            int total = files.Length;
            int done = 0;
            var ensured = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                client.EnsureDirectory(remoteModDir);
                ensured.Add(remoteModDir);

                foreach (var file in files)
                {
                    string rel = file.Substring(stagingRoot.Length).Replace("\\", "/").TrimStart('/');
                    string remoteFile = remoteModDir + "/" + rel;

                    int slash = remoteFile.LastIndexOf('/');
                    if (slash > 0)
                    {
                        string parent = remoteFile.Substring(0, slash);
                        if (ensured.Add(parent))
                            client.EnsureDirectory(parent);
                    }

                    if (EditorUtility.DisplayCancelableProgressBar("Divine Builder (FTP upload)",
                        $"{done}/{total}  {rel}", total > 0 ? (float)done / total : 0f))
                    {
                        outcome.Cancelled = true;
                        Debug.LogWarning("Divine Builder: FTP upload cancelled.");
                        return false;
                    }

                    client.UploadFile(file, remoteFile);
                    done++;
                }
            }
            catch (Exception e)
            {
                Debug.LogError("Divine Builder: FTP upload failed: " + e);
                outcome.Errors.Add(new BuildError
                {
                    BundlePath = "(ftp)",
                    Kind = BuildErrorKind.BundleIo,
                    Detail = "FTP upload failed: " + e.Message,
                    Hint = "Check the host, port and that the server is reachable."
                });
                return false;
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            Debug.Log($"Divine Builder: uploaded {done} file(s) to {remoteModDir}");
            return true;
        }

        static PatchResult RunPatcher(string outputDirectory, string settingsJsonPath)
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
                var failed = new PatchResult();
                failed.Errors.Add(new BuildError
                {
                    BundlePath = "(patcher)",
                    Kind = BuildErrorKind.BundleIo,
                    Detail = "Patching failed to start: " + e.Message
                });
                return failed;
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
                Debug.LogWarning("Divine Builder: build cancelled.");
            else if (!result.Success)
                Debug.LogError($"Divine Builder: build failed, {result.Errors.Count} bundle(s) had errors. " +
                               "See the messages above.");
            else
                Debug.Log($"Divine Builder: patched {result.Patched} bundle(s), skipped {result.Skipped}.");

            return result;
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

    // Everything the Build window needs to show after a build, so it doesn't have to send people
    // digging through the console to find out what happened.
    public class BuildOutcome
    {
        public bool Success;
        public bool Cancelled;
        public string FailureStage;
        public int Patched;
        public int Skipped;
        public string OutputDirectory;
        public string DeliveryNote;
        public double ElapsedSeconds;
        public readonly List<BuildError> Errors = new List<BuildError>();
        public readonly List<string> Warnings = new List<string>();
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
