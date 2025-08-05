using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace DivineDragon.PreFlightCheck
{
    public class OBodyAvatarRule : BuildRule
    {
        public override string Name => "oBody Avatar Check";
        public override string Description => "Ensures all oBody prefabs have an Avatar component for proper functionality";
        public override IssueSeverity DefaultSeverity => IssueSeverity.Error;
        public override bool CanAutoFix => false; // Can't auto-fix as Avatar needs to be configured on the FBX

        public override bool AppliesTo(string assetPath, Object asset)
        {
            // Check if it's an oBody prefab
            if (!assetPath.EndsWith(".prefab"))
                return false;
            
            // Check if the path or name indicates it's an oBody
            if (assetPath.Contains("/oBody/") || assetPath.Contains("oBody_"))
            {
                return asset is GameObject;
            }
            
            return false;
        }

        public override List<BuildIssue> Validate(string assetPath, Object asset)
        {
            var issues = new List<BuildIssue>();
            
            if (asset is GameObject prefab)
            {
                // Check if the prefab has an Animator component
                var animator = prefab.GetComponent<Animator>();
                
                if (animator == null)
                {
                    var issue = new BuildIssue(
                        assetPath,
                        $"oBody prefab '{prefab.name}' is missing an Animator component. " +
                        $"oBody prefabs require an Animator component with a configured Avatar to function correctly.",
                        prefab,
                        DefaultSeverity,
                        this
                    );
                    issues.Add(issue);
                }
                else if (animator.avatar == null)
                {
                    var issue = new BuildIssue(
                        assetPath,
                        $"oBody prefab '{prefab.name}' has an Animator component but no Avatar assigned. " +
                        $"Please assign an Avatar to the Animator component. You may need to configure the Avatar on the source FBX file.",
                        prefab,
                        DefaultSeverity,
                        this
                    );
                    issues.Add(issue);
                }
            }
            
            return issues;
        }

        public override bool AutoFix(BuildIssue issue)
        {
            // Can't auto-fix - Avatar configuration needs to be done on the FBX import settings
            // and requires user decisions about bone mapping
            return false;
        }
    }
}