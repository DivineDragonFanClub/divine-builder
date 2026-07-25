using System.Collections.Generic;
using System.Linq;
using DivineDragon.PreFlightCheck;
using UnityEngine;
using Object = UnityEngine.Object;

namespace DivineDragon.PreFlightCheck.Rules
{
    // Engage's Chara* toon shaders expect these slots to be hand-assigned; the shader
    // defaults render visibly wrong in game (flat shading, missing tinting). There is
    // nothing safe to auto-assign - the right texture usually doesn't exist yet, and a
    // WIP model with an unfinished material is a legitimate state - so this is Info: a
    // note in the Preflight window, never a build blocker.
    public class CharaMaterialTexturesRule : BuildRule
    {
        // DisplayNames match what the Inspector shows: both the stock material editor
        // (shader display names) and CharaStandardShaderGUI draw exactly these strings.
        private static readonly (string Property, string DisplayName)[] RequiredSlots =
        {
            ("_BaseMap", "Albedo"),
            ("_BumpMap", "Normal Map"),
            ("_MultiMap", "Multi Map"),
            ("_ToonRamp", "Toon Ramp"),
            ("_ToonRampMetal", "Toon Ramp Metal"),
        };

        public override string Name => "Character Material Textures";

        public override string Description =>
            "Ensures materials on character prefabs have their core texture slots assigned (Albedo, Normal, Multi Map, Toon Ramps)";

        public override IssueSeverity DefaultSeverity => IssueSeverity.Info;

        public override bool CanAutoFix => false;

        public override bool AppliesTo(string assetPath, Object asset)
        {
            return assetPath.EndsWith(".prefab") && asset is GameObject;
        }

        public override List<BuildIssue> Validate(string assetPath, Object asset)
        {
            var issues = new List<BuildIssue>();
            if (!(asset is GameObject prefab))
                return issues;

            var seenMaterials = new HashSet<Material>();

            foreach (var renderer in prefab.GetComponentsInChildren<Renderer>(true))
            {
                var materials = renderer.sharedMaterials;
                for (int slot = 0; slot < materials.Length; slot++)
                {
                    var material = materials[slot];
                    if (material == null)
                    {
                        issues.Add(new BuildIssue(
                            assetPath,
                            $"Renderer '{renderer.name}' has no material assigned in slot {slot}.",
                            prefab,
                            DefaultSeverity,
                            this,
                            renderer)
                        {
                            Targets = new List<IssueTarget>
                            {
                                new IssueTarget(renderer.name, renderer,
                                    tooltip: "Open the prefab and select this renderer"),
                            }
                        });
                        continue;
                    }

                    if (!seenMaterials.Add(material))
                        continue;

                    // Only Engage-style toon materials are checked; the ramp slot is the
                    // fingerprint of that shader family. Foreign shaders are the
                    // Addressable Shader Check's problem, not ours.
                    if (material.shader == null || !material.HasProperty("_ToonRamp"))
                        continue;

                    var missing = RequiredSlots
                        .Where(s => material.HasProperty(s.Property) && material.GetTexture(s.Property) == null)
                        .ToList();

                    if (missing.Count == 0)
                        continue;

                    string missingNames = string.Join(", ", missing.Select(s => s.DisplayName));

                    issues.Add(new BuildIssue(
                        assetPath,
                        $"Material '{material.name}' on '{renderer.name}' is missing: {missingNames}.",
                        prefab,
                        DefaultSeverity,
                        this,
                        material)
                    {
                        Targets = new List<IssueTarget>
                        {
                            new IssueTarget(material.name, material,
                                tooltip: $"Select the material - '{missing[0].DisplayName}' is empty"),
                            new IssueTarget(renderer.name, renderer,
                                tooltip: "Open the prefab and select this renderer"),
                        }
                    });
                }
            }

            return issues;
        }

        public override bool AutoFix(BuildIssue issue)
        {
            return false;
        }
    }
}
