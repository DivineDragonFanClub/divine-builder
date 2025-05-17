using System;
using System.Diagnostics;
using System.IO;
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
        [MenuItem("Divine Dragon/Build")]
        public static void BuildAddressables()
        {
            BuildAddressableContent();
        }
        
        [MenuItem("Divine Dragon/Build", true)]
        static bool ValidateBuildAddressables()
        {
            // Return false if no mod output path is set
            return !string.IsNullOrEmpty(DivineDragonSettingsScriptableObject.instance.getModPath());
        }

        [MenuItem("Divine Dragon/Clean Addressable Paths")]
        public static void CleanAddressablePaths()
        {
            // list all addressable names in the groups
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
            {
                Debug.LogError("AddressableAssetSettings not found.");
                return;
            }
            foreach (var group in settings.groups)
            {
                if (group == null || group.HasSchema<PlayerDataGroupSchema>())
                    continue;

                foreach (var entry in group.entries)
                {
                    if (entry == null)
                        continue;

                    Debug.Log(entry.address);
                    // if entry has the Assets/Share/Addressables/ prefix, remove it
                    if (entry.address.StartsWith("Assets/Share/Addressables/"))
                    {
                        entry.address = entry.address.Substring("Assets/Share/Addressables/".Length);
                        Debug.Log("Removed prefix from address: " + entry.address);
                    }
                    // Remove common file extensions from addresses - could we always remove anything including/after the last dot?
                    string[] extensionsToRemove = { ".anim", ".overrideController" };
                    foreach (var extension in extensionsToRemove)
                    {
                        if (entry.address.EndsWith(extension))
                        {
                            string oldAddress = entry.address;
                            entry.address = entry.address.Substring(0, entry.address.Length - extension.Length);
                            Debug.Log($"Removed {extension} suffix from address: {entry.address}");
                            break;
                        }
                    }
                }
            }
        }
        
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
            
            Debug.Log(outputDirectory);
            
            if (!Directory.Exists(outputDirectory))
            {
                Directory.CreateDirectory(outputDirectory);
            }
            
            string projectCurrentDir = Directory.GetCurrentDirectory();

            var args = String.Format("fix \"{0}\" \"{1}\"", outputDirectory, Path.GetFullPath(Path.Combine(projectCurrentDir, result.OutputPath)));
            

            var bundleTools = "bundle_tools";
            // get the platform that this editor is running on
            var platform = Application.platform;
            
            // if the platform is windows, append .exe to the bundle_tools name
            if (platform == RuntimePlatform.WindowsEditor)
            {
                bundleTools += ".exe";
            }
            
            RunProcess(bundleTools, false, args);
            
            // Remove addressables with the label "removePostBuild"
            AddressableUtility.RemoveAddressablesWithLabel(outputDirectory, "removePostBuild");

            if (DivineDragonSettingsScriptableObject.instance.getOpenAfterBuild())
            {
                EditorUtility.RevealInFinder(outputDirectory);
            }
            
            

            return success;
        }

        private static string dataPath = "Data/StreamingAssets/aa/Switch";
        
        /**
         * Build the modPath/dataPath, path.
         */
        
        static string BuildModOutputPath()
        {
            return Path.Combine(DivineDragonSettingsScriptableObject.instance.getModPath(), dataPath);
        }
        
        static void RunProcess(string command, bool runShell, string args = null)
        {
            string projectCurrentDir = Directory.GetCurrentDirectory();
            command = Path.GetFullPath(Path.Combine(projectCurrentDir, "Packages/com.divinedragon.builder", command));
 
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
                }
                if (args != null && args != "")
                {
                    ps.Arguments = args;
                }
                p.StartInfo = ps;
                p.Start();
                p.WaitForExit();
                if (!runShell)
                {
                    string output = p.StandardOutput.ReadToEnd().Trim();
                    if (!string.IsNullOrEmpty(output))
                    {
                        // Split output into lines and debug log each line
                        string[] lines = output.Split(new[] { Environment.NewLine }, StringSplitOptions.None);
                        foreach (string line in lines)
                        {
                            Debug.Log(string.Format("{0} Output: {1}", DateTime.Now, line));
                        }
                    }
 
                    string errors = p.StandardError.ReadToEnd().Trim();
                    if (!string.IsNullOrEmpty(errors))
                    {
                        // Split output into lines and debug log each line
                        string[] lines = errors.Split(new[] { Environment.NewLine }, StringSplitOptions.None);
                        foreach (string line in lines)
                        {
                            Debug.Log(string.Format("{0} Output: {1}", DateTime.Now, line));
                        }
                    }
                }
            }
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

            foreach (var group in settings.groups)
            {
                if (group == null || group.HasSchema<PlayerDataGroupSchema>())
                    continue;

                var groupPrefix = group.name + "_" + "assets";

                foreach (var entry in group.entries)
                {
                    if (entry.labels.Contains(label))
                    {
                        var entryPath = groupPrefix + "_" + entry.address.ToLower() + ".bundle";
                        var filePath = Path.Combine(outputDirectory, entryPath);
                        if (File.Exists(filePath))
                        {
                            File.Delete(filePath);
                            Debug.Log($"Deleted bundle: {filePath}");
                        }
                    }
                }
            }
        }
    }
}
