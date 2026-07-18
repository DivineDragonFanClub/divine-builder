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
        public enum RuleExecutionMode
        {
            Skip = 0,
            Check = 1,
            CheckAndAutoApply = 2
        }

        [Serializable]
        internal class RuleState
        {
            public string typeName;
            public bool enabled = true; // legacy
            public bool autoApply;      // legacy
            public RuleExecutionMode mode = RuleExecutionMode.Check;
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
                mode = RuleExecutionMode.Check,
                version = CurrentVersion
            };
            rules.Add(state);
            Save(true);
            return state;
        }

        public RuleExecutionMode GetMode(Type ruleType)
        {
            return GetOrCreateState(ruleType).mode;
        }

        public void SetMode(Type ruleType, RuleExecutionMode mode)
        {
            var state = GetOrCreateState(ruleType);
            if (state.mode == mode)
                return;

            state.mode = mode;
            state.version = CurrentVersion;
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

        private const int CurrentVersion = 1;

        private void UpgradeState(RuleState state)
        {
            if (state.version >= CurrentVersion)
                return;

            // Legacy conversion from enabled/autoApply
            if (!state.enabled)
            {
                state.mode = RuleExecutionMode.Skip;
            }
            else
            {
                state.mode = state.autoApply ? RuleExecutionMode.CheckAndAutoApply : RuleExecutionMode.Check;
            }

            state.version = CurrentVersion;
            Save(true);
        }
    }
}
