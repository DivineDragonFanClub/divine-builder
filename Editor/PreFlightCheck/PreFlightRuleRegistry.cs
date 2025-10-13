using System;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using UnityEditor.PackageManager;

namespace DivineDragon.PreFlightCheck
{
    public static class PreFlightRuleRegistry
    {
        public sealed class RegisteredRuleInfo
        {
            public RegisteredRuleInfo(Type ruleType, string assemblyName, string packageName, string packageDisplayName, string packageVersion)
            {
                RuleType = ruleType;
                AssemblyName = assemblyName;
                PackageName = packageName;
                PackageDisplayName = packageDisplayName;
                PackageVersion = packageVersion;
            }

            public Type RuleType { get; }
            public string AssemblyName { get; }
            public string PackageName { get; }
            public string PackageDisplayName { get; }
            public string PackageVersion { get; }
        }

        private class RegisteredRuleEntry
        {
            public Func<BuildRule> Factory;
            public Type RuleType;
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

                RegisteredRules.Add(new RegisteredRuleEntry
                {
                    Factory = factory,
                    RuleType = ruleType
                });
            }
        }

        internal static List<BuildRule> CreateRegisteredRules()
        {
            lock (SyncRoot)
            {
                return RegisteredRules
                    .Select(entry =>
                    {
                        try
                        {
                            return entry.Factory();
                        }
                        catch (Exception ex)
                        {
                            Debug.LogError($"Failed to instantiate pre-flight rule: {ex}");
                            return null;
                        }
                    })
                    .Where(rule => rule != null)
                    .ToList();
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

                    if (type != null)
                    {
                        var package = PackageInfo.FindForAssembly(type.Assembly);
                        if (package != null)
                        {
                            packageName = package.name;
                            packageDisplayName = package.displayName;
                            packageVersion = package.version;
                        }
                    }

                    infos.Add(new RegisteredRuleInfo(type, assemblyName, packageName, packageDisplayName, packageVersion));
                }

                return infos;
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
    }
}
