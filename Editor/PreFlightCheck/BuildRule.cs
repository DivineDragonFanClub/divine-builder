using System.Collections.Generic;
using UnityEngine;

namespace DivineDragon.PreFlightCheck
{
    public enum IssueSeverity
    {
        Info,
        Warning,
        Error
    }

    public class BuildIssue
    {
        public string AssetPath { get; set; }
        public string Message { get; set; }
        public Object Asset { get; set; }
        public Object SpecificComponent { get; set; } // The specific component/object with the issue
        public IssueSeverity Severity { get; set; }
        public BuildRule Rule { get; set; }
        
        public BuildIssue(string assetPath, string message, Object asset, IssueSeverity severity, BuildRule rule, Object specificComponent = null)
        {
            AssetPath = assetPath;
            Message = message;
            Asset = asset;
            SpecificComponent = specificComponent;
            Severity = severity;
            Rule = rule;
        }
    }

    public abstract class BuildRule
    {
        public abstract string Name { get; }
        public abstract string Description { get; }
        public abstract IssueSeverity DefaultSeverity { get; }
        
        /// <summary>
        /// Validates the given asset and returns any issues found
        /// </summary>
        /// <param name="assetPath">Path to the asset being validated</param>
        /// <param name="asset">The asset object to validate</param>
        /// <returns>List of issues found, empty if no issues</returns>
        public abstract List<BuildIssue> Validate(string assetPath, Object asset);
        
        /// <summary>
        /// Checks if this rule applies to the given asset
        /// </summary>
        /// <param name="assetPath">Path to the asset</param>
        /// <param name="asset">The asset object</param>
        /// <returns>True if this rule should validate this asset</returns>
        public abstract bool AppliesTo(string assetPath, Object asset);
        
        /// <summary>
        /// Indicates whether this rule can automatically fix issues
        /// </summary>
        public abstract bool CanAutoFix { get; }
        
        /// <summary>
        /// Attempts to fix the issue automatically
        /// </summary>
        /// <param name="issue">The issue to fix</param>
        /// <returns>True if the fix was successful</returns>
        public abstract bool AutoFix(BuildIssue issue);
    }
}