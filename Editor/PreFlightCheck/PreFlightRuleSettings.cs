using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace DivineDragon.PreFlightCheck
{
    [FilePath("ProjectSettings/DivineDragonPreFlightRules.asset", FilePathAttribute.Location.ProjectFolder)]
    internal class PreFlightRuleSettings : ScriptableSingleton<PreFlightRuleSettings>
    {
        [Serializable]
        internal class RuleState
        {
            public string typeName;
            public bool enabled = true;
            public bool autoApply;  // legacy (version 0)
            public int mode = 1;    // legacy three-way mode (version 1): 0 skip, 1 check, 2 check+autofix
            public int version;
        }

        [SerializeField]
        private List<RuleState> rules = new List<RuleState>();

        public RuleState GetOrCreateState(Type ruleType)
        {
            if (ruleType == null)
                throw new ArgumentNullException(nameof(ruleType));

            string key = ruleType.AssemblyQualifiedName;
            var state = rules.FirstOrDefault(r => r.typeName == key);
            if (state != null)
            {
                UpgradeState(state);
                return state;
            }

            state = new RuleState
            {
                typeName = key,
                version = CurrentVersion
            };
            rules.Add(state);
            Save(true);
            return state;
        }

        public bool IsEnabled(Type ruleType)
        {
            return GetOrCreateState(ruleType).enabled;
        }

        public void SetEnabled(Type ruleType, bool enabled)
        {
            var state = GetOrCreateState(ruleType);
            if (state.enabled == enabled)
                return;

            state.enabled = enabled;
            Save(true);
        }

        public IEnumerable<RuleState> AllStates => rules;

        public void RemoveMissingTypes(HashSet<string> activeTypeNames)
        {
            if (activeTypeNames == null)
                throw new ArgumentNullException(nameof(activeTypeNames));

            if (rules.RemoveAll(r => !activeTypeNames.Contains(r.typeName)) > 0)
            {
                Save(true);
            }
        }

        private const int CurrentVersion = 2;

        private void UpgradeState(RuleState state)
        {
            if (state.version >= CurrentVersion)
                return;

            // Version 0 stored enabled directly, so it already holds the answer. Version 1
            // stored a three-way mode where anything but Skip (0) means the rule ran.
            if (state.version == 1)
            {
                state.enabled = state.mode != 0;
            }

            state.version = CurrentVersion;
            Save(true);
        }
    }
}
