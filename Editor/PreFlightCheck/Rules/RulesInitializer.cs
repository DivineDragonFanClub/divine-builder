using UnityEditor;

namespace DivineDragon.PreFlightCheck.Rules
{
    /// <summary>
    /// Registers every built-in validation rule when Unity loads. One place for all of
    /// them, grouped by the folders they live in.
    /// </summary>
    [InitializeOnLoad]
    public static class RulesInitializer
    {
        static RulesInitializer()
        {
            // Addressable
            PreFlightRuleRegistry.Register<AddressablePathsRule>();
            PreFlightRuleRegistry.Register<AddressableShaderRule>();

            // Model
            PreFlightRuleRegistry.Register<BodyPrefabComplianceRule>();
            PreFlightRuleRegistry.Register<AccPrefabComplianceRule>();
            PreFlightRuleRegistry.Register<OBodyAvatarRule>();
            PreFlightRuleRegistry.Register<OBodyOriginCheckRule>();
            PreFlightRuleRegistry.Register<SkinnedMeshRendererRule>();
            PreFlightRuleRegistry.Register<CharaMaterialTexturesRule>();

            // Scene
            PreFlightRuleRegistry.Register<PrefabOverridesInScenesRule>();

            // Animation
            PreFlightRuleRegistry.Register<AnimationLoopSettingsRule>();
        }
    }
}
