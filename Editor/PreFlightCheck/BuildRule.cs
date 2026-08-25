using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace DivineDragon.PreFlightCheck
{
    public enum IssueSeverity
    {
        Info,
        Warning,
        Error
    }

    /// <summary>
    /// An optional clickable target on an issue row: a specific object (material,
    /// renderer, …) to select and ping so the user lands right where the fix goes.
    /// </summary>
    public class IssueTarget
    {
        public string Label;
        public Object Target;
        public string Tooltip;

        public IssueTarget(string label, Object target, string tooltip = null)
        {
            Label = label;
            Target = target;
            Tooltip = tooltip;
        }
    }

    /// <summary>
    /// An optional one-click resolution on an issue row, run only when the user clicks
    /// it. Deliberately separate from AutoFix: actions never join the build-time Autofix
    /// pass or the Fix All button - they exist for judgment-call fixes where a human
    /// picks the resolution (apply vs revert, and so on).
    /// </summary>
    public class IssueAction
    {
        public string Label;
        public string Tooltip;

        /// <summary>Runs the resolution. Return true if something was changed so the
        /// window knows to re-check; false for a no-op (stale target and the like).</summary>
        public Func<bool> Execute;

        public IssueAction(string label, Func<bool> execute, string tooltip = null)
        {
            Label = label;
            Execute = execute;
            Tooltip = tooltip;
        }
    }

    public class BuildIssue
    {
        public string AssetPath { get; set; }
        public string Message { get; set; }
        public Object Asset { get; set; }
        public Object SpecificComponent { get; set; } // The specific component/object with the issue
        public IssueSeverity Severity { get; set; }
        public BuildRule Rule { get; set; }

        /// <summary>Optional per-issue jump links, rendered as small chips under the message.</summary>
        public List<IssueTarget> Targets { get; set; }

        /// <summary>Optional one-click resolutions, rendered as buttons on the row.
        /// Never run automatically - see <see cref="IssueAction"/>.</summary>
        public List<IssueAction> Actions { get; set; }
        
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
        /// Validates the given asset and returns any issues found.
        /// Checks run automatically and often (on asset imports, window opens, builds),
        /// so this must be strictly read-only: never open scenes, save assets, import,
        /// or mutate any editor state here. Anything that raises an editor change event
        /// (an import, a scene touch, an addressables edit) re-triggers the checks, so
        /// a Validate with side effects puts the editor in an endless check loop.
        /// Mutations belong in <see cref="AutoFix"/> or an <see cref="IssueAction"/>,
        /// which only run when the user asked for them.
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

        /// <summary>
        /// Indicates whether this rule has configurable settings
        /// </summary>
        public virtual bool HasConfiguration => false;

        /// <summary>
        /// Draws the configuration UI for this rule in the settings window
        /// </summary>
        public virtual void DrawConfiguration()
        {
            // Override in derived classes to provide configuration UI
        }

        /// <summary>
        /// Loads the configuration for this rule
        /// </summary>
        public virtual void LoadConfiguration()
        {
            // Override in derived classes to load saved configuration
        }

        /// <summary>
        /// Saves the configuration for this rule
        /// </summary>
        public virtual void SaveConfiguration()
        {
            // Override in derived classes to save configuration
        }
    }
}