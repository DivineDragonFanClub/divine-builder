using System.Collections.Generic;
using System.IO;
using DivineDragon.PreFlightCheck;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace DivineDragon.PreFlightCheck.Rules
{
    // Looping motions (idle, run, and every other *loop) need Loop Time on or they
    // visibly hitch/reset in game. Verified against the game's own clips
    // (fe_assets_unit/anim, 6600+ clips): every "loop"/"idle"/"standby" clip has Loop
    // Time on, and Loop Pose (m_LoopBlend) is off on all but one - so these should be Loop
    // Time on, Loop Pose off. Naming note: "runstart"/"standbybreak" are non-looping
    // transitions, so only "runloop" and plain "standby" count.
    public class AnimationLoopSettingsRule : BuildRule
    {
        public override string Name => "Animation Loop Settings";

        public override string Description =>
            "Looping animation clips (idle, run, and other loops) should have Loop Time on and Loop Pose off, matching the game.";

        public override IssueSeverity DefaultSeverity => IssueSeverity.Warning;

        public override bool CanAutoFix => true;

        public override bool AppliesTo(string assetPath, Object asset)
        {
            return asset is AnimationClip && ShouldLoop(assetPath);
        }

        public override List<BuildIssue> Validate(string assetPath, Object asset)
        {
            var issues = new List<BuildIssue>();
            if (!(asset is AnimationClip clip))
                return issues;

            var settings = AnimationUtility.GetAnimationClipSettings(clip);

            if (!settings.loopTime)
            {
                issues.Add(new BuildIssue(assetPath,
                    $"Animation '{clip.name}' looks like a looping clip but Loop Time is off - it won't loop in game.",
                    clip, DefaultSeverity, this));
            }
            else if (settings.loopBlend)
            {
                // The game keeps Loop Pose off on looping clips; on, it can shift the pose.
                issues.Add(new BuildIssue(assetPath,
                    $"Animation '{clip.name}' has Loop Pose on. The game keeps it off on looping clips.",
                    clip, DefaultSeverity, this));
            }

            return issues;
        }

        public override bool AutoFix(BuildIssue issue)
        {
            if (!(issue.Asset is AnimationClip clip))
                return false;

            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            bool changed = false;
            if (!settings.loopTime) { settings.loopTime = true; changed = true; }
            if (settings.loopBlend) { settings.loopBlend = false; changed = true; }
            if (!changed)
                return false;

            AnimationUtility.SetAnimationClipSettings(clip, settings);
            EditorUtility.SetDirty(clip);
            AssetDatabase.SaveAssets();
            return true;
        }

        // Looping motions by the game's naming, checked against every game clip with no
        // real counterexamples: anything with "loop" in it (runloop, winloop, relaxloop,
        // hoveringloop, ...) and every "idle". "standby" loops too, but "standbybreak" is
        // a one-shot, and "runstart" is the non-looping transition into the run - both
        // excluded because they don't contain "loop"/"idle" (break is guarded explicitly).
        private static bool ShouldLoop(string assetPath)
        {
            string name = Path.GetFileNameWithoutExtension(assetPath).ToLowerInvariant();
            if (name.Contains("loop"))
                return true;
            if (name.Contains("idle"))
                return true;
            if (name.Contains("standby") && !name.Contains("break"))
                return true;
            return false;
        }
    }
}
