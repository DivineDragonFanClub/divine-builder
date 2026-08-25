using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using Object = UnityEngine.Object;

namespace DivineDragon.PreFlightCheck.Rules
{
    public class OBodyOriginCheckRule : BuildRule
    {
        // Roots should be exactly zero; the tolerance only absorbs serialization float dust.
        private const float Tolerance = 0.0001f;

        public override string Name => "oBody Origin Check";
        public override string Description => "Ensures oBody prefab roots sit at the origin so the model spawns where the game places the character";
        public override IssueSeverity DefaultSeverity => IssueSeverity.Error;
        public override bool CanAutoFix => true;

        public override bool AppliesTo(string assetPath, Object asset)
        {
            if (!assetPath.EndsWith(".prefab"))
                return false;

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
                Vector3 position = prefab.transform.localPosition;
                if (Mathf.Abs(position.x) > Tolerance ||
                    Mathf.Abs(position.y) > Tolerance ||
                    Mathf.Abs(position.z) > Tolerance)
                {
                    issues.Add(new BuildIssue(
                        assetPath,
                        $"oBody prefab '{prefab.name}' has its root at ({position.x:0.###}, {position.y:0.###}, {position.z:0.###}) instead of the origin (0, 0, 0). " +
                        $"An off-origin root makes the model look offset inside of the map grid.",
                        prefab,
                        DefaultSeverity,
                        this,
                        prefab.transform
                    ));
                }
            }

            return issues;
        }

        public override bool AutoFix(BuildIssue issue)
        {
            if (!(issue.Asset is GameObject prefabAsset))
                return false;

            string prefabPath = AssetDatabase.GetAssetPath(prefabAsset);
            if (string.IsNullOrEmpty(prefabPath))
                return false;

            var prefabRoot = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                prefabRoot.transform.localPosition = Vector3.zero;
                PrefabUtility.SaveAsPrefabAsset(prefabRoot, prefabPath);
                return true;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(prefabRoot);
            }
        }
    }
}
