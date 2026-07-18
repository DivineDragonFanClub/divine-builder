using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;
using UnityEngine;

namespace DivineDragon.PreFlightCheck
{
    public static class PreFlightRuleRegistry
    {
        internal sealed class RegisteredRuleInfo
        {
            public RegisteredRuleInfo(
                Type ruleType,
                string ruleName,
                string ruleDescription,
                IssueSeverity defaultSeverity,
                string assemblyName,
                string packageName,
                string packageDisplayName,
                string packageVersion,
                PreFlightRuleSettings.RuleExecutionMode mode,
                bool canAutoFix)
            {
                RuleType = ruleType;
                RuleName = ruleName;
                RuleDescription = ruleDescription;
                DefaultSeverity = defaultSeverity;
                AssemblyName = assemblyName;
                PackageName = packageName;
                PackageDisplayName = packageDisplayName;
                PackageVersion = packageVersion;
                Mode = mode;
                CanAutoFix = canAutoFix;
            }

            public Type RuleType { get; }
            public string RuleName { get; }
            public string RuleDescription { get; }
            public IssueSeverity DefaultSeverity { get; }
            public string AssemblyName { get; }
            public string PackageName { get; }
            public string PackageDisplayName { get; }
            public string PackageVersion { get; }
            public PreFlightRuleSettings.RuleExecutionMode Mode { get; }
            public bool CanAutoFix { get; }
        }

        internal sealed class ActiveRule
        {
            public ActiveRule(BuildRule rule, bool autoApply)
            {
                Rule = rule;
                AutoApply = autoApply;
            }

            public BuildRule Rule { get; }
            public bool AutoApply { get; }
        }

        private class RegisteredRuleEntry
        {
            public Func<BuildRule> Factory;
            public Type RuleType;
            public PreFlightRuleSettings.RuleExecutionMode Mode = PreFlightRuleSettings.RuleExecutionMode.Check;
            public bool CanAutoFix;
            public bool MetadataInitialized;
            public string DisplayName;
            public string Description;
            public IssueSeverity DefaultSeverity;
        }

        private static readonly List<RegisteredRuleEntry> RegisteredRules = new List<RegisteredRuleEntry>();
        private static readonly HashSet<Type> RegisteredTypes = new HashSet<Type>();
        private static readonly object SyncRoot = new object();

        public static void Register<T>() where T : BuildRule, new()
        {
            Register(() => new T(), typeof(T));
        }

        public static void Register(Func<BuildRule> factory, Type ruleType = null)
        {
            if (factory == null) throw new ArgumentNullException(nameof(factory));

            lock (SyncRoot)
            {
                if (ruleType != null && !RegisteredTypes.Add(ruleType))
                {
                    return;
                }

                var entry = new RegisteredRuleEntry
                {
                    Factory = factory,
                    RuleType = ruleType
                };

                RegisteredRules.Add(entry);

                if (ruleType != null)
                {
                    SyncEntryState(entry);
                }

                RemoveMissingSettingsLocked();
            }
        }

        internal static List<ActiveRule> CreateActiveRules()
        {
            lock (SyncRoot)
            {
                var activeRules = new List<ActiveRule>();

                foreach (var entry in RegisteredRules)
                {
                    SyncEntryState(entry);

                    if (entry.Mode == PreFlightRuleSettings.RuleExecutionMode.Skip)
                        continue;

                    try
                    {
                        var ruleInstance = entry.Factory();
                        if (ruleInstance == null)
                            continue;

                        entry.CanAutoFix = ruleInstance.CanAutoFix;
                        entry.DisplayName = ruleInstance.Name;
                        entry.Description = ruleInstance.Description;
                        entry.DefaultSeverity = ruleInstance.DefaultSeverity;
                        entry.MetadataInitialized = true;

                        bool autoApply = entry.Mode == PreFlightRuleSettings.RuleExecutionMode.CheckAndAutoApply && ruleInstance.CanAutoFix;
                        if (entry.Mode == PreFlightRuleSettings.RuleExecutionMode.CheckAndAutoApply && !ruleInstance.CanAutoFix)
                        {
                            entry.Mode = PreFlightRuleSettings.RuleExecutionMode.Check;
                            PreFlightRuleSettings.instance.SetMode(entry.RuleType, entry.Mode);
                            autoApply = false;
                        }
                        activeRules.Add(new ActiveRule(ruleInstance, autoApply));
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"Failed to instantiate pre-flight rule: {ex}");
                    }
                }

                return activeRules;
            }
        }

        internal static List<RegisteredRuleInfo> GetRegisteredRuleInfos()
        {
            lock (SyncRoot)
            {
                var infos = new List<RegisteredRuleInfo>(RegisteredRules.Count);

                foreach (var entry in RegisteredRules)
                {
                    var type = EnsureRuleType(entry);
                    string assemblyName = type?.Assembly?.GetName().Name;
                    string packageName = null;
                    string packageDisplayName = null;
                    string packageVersion = null;
                    bool canAutoFix = false;
                    string ruleName = type != null ? type.Name : "Unknown Rule";
                    string ruleDescription = string.Empty;
                    IssueSeverity defaultSeverity = IssueSeverity.Warning;
                    var mode = PreFlightRuleSettings.RuleExecutionMode.Check;

                    if (type != null)
                    {
                        SyncEntryState(entry);
                        mode = entry.Mode;
                        EnsureMetadata(entry);
                        canAutoFix = entry.CanAutoFix;
                        if (!string.IsNullOrEmpty(entry.DisplayName))
                        {
                            ruleName = entry.DisplayName;
                        }
                        ruleDescription = entry.Description ?? string.Empty;
                        defaultSeverity = entry.DefaultSeverity;

                        var package = PackageInfo.FindForAssembly(type.Assembly);
                        if (package != null)
                        {
                            packageName = package.name;
                            packageDisplayName = package.displayName;
                            packageVersion = package.version;
                        }
                    }

                    infos.Add(new RegisteredRuleInfo(
                        type,
                        ruleName,
                        ruleDescription,
                        defaultSeverity,
                        assemblyName,
                        packageName,
                        packageDisplayName,
                        packageVersion,
                        mode,
                        canAutoFix));
                }

                return infos;
            }
        }

        internal static void SetRuleMode(Type ruleType, PreFlightRuleSettings.RuleExecutionMode mode)
        {
            if (ruleType == null) throw new ArgumentNullException(nameof(ruleType));

            lock (SyncRoot)
            {
                var settings = PreFlightRuleSettings.instance;
                settings.SetMode(ruleType, mode);

                foreach (var entry in RegisteredRules)
                {
                    if (entry.RuleType == ruleType)
                    {
                        entry.Mode = mode;
                        break;
                    }
                }
            }
        }

        private static Type EnsureRuleType(RegisteredRuleEntry entry)
        {
            if (entry.RuleType != null)
                return entry.RuleType;

            try
            {
                var instance = entry.Factory();
                if (instance != null)
                {
                    entry.RuleType = instance.GetType();
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to resolve pre-flight rule type: {ex}");
            }

            return entry.RuleType;
        }

        private static void SyncEntryState(RegisteredRuleEntry entry)
        {
            var type = EnsureRuleType(entry);
            if (type == null)
                return;

            var settings = PreFlightRuleSettings.instance;
            entry.Mode = settings.GetMode(type);
        }

        private static void EnsureMetadata(RegisteredRuleEntry entry)
        {
            if (entry.MetadataInitialized)
                return;

            try
            {
                var prototype = entry.Factory();
                if (prototype != null)
                {
                    entry.CanAutoFix = prototype.CanAutoFix;
                    if (!entry.CanAutoFix && entry.Mode == PreFlightRuleSettings.RuleExecutionMode.CheckAndAutoApply)
                    {
                        entry.Mode = PreFlightRuleSettings.RuleExecutionMode.Check;
                        PreFlightRuleSettings.instance.SetMode(entry.RuleType, entry.Mode);
                    }
                    entry.DisplayName = prototype.Name;
                    entry.Description = prototype.Description;
                    entry.DefaultSeverity = prototype.DefaultSeverity;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to evaluate metadata for pre-flight rule: {ex}");
            }
            finally
            {
                entry.MetadataInitialized = true;
            }
        }

        private static void RemoveMissingSettingsLocked()
        {
            var activeTypeNames = new HashSet<string>(
                RegisteredRules
                    .Select(r => r.RuleType?.AssemblyQualifiedName)
                    .Where(n => !string.IsNullOrEmpty(n)));

            PreFlightRuleSettings.instance.RemoveMissingTypes(activeTypeNames);
        }
    }
}
