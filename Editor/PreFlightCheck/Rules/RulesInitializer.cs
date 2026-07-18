using UnityEditor;

namespace DivineDragon.PreFlightCheck.Rules
{
    /// <summary>
    /// Automatically registers all built-in preflight rules when Unity loads
    /// </summary>
    [InitializeOnLoad]
    public static class RulesInitializer
    {
        static RulesInitializer()
        {
            // Register the AddressablePathsRule
            PreFlightRuleRegistry.Register<AddressablePathsRule>();

            // Add more rule registrations here as needed
        }
    }
}