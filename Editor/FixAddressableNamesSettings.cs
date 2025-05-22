using System.Collections.Generic;
using UnityEngine;

namespace DivineDragon
{
    [CreateAssetMenu(fileName = "FixAddressableNamesSettings", menuName = "DivineDragon/Fix Addressable Names Settings")]
    public class FixAddressableNamesSettings : ScriptableObject
    {
        // Default extensions to remove
        public List<string> extensionsToRemove = new List<string> { ".anim", ".overrideController", ".prefab" };

        private static FixAddressableNamesSettings _instance;
        public static FixAddressableNamesSettings Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = Resources.Load<FixAddressableNamesSettings>("FixAddressableNamesSettings");
                }
                return _instance;
            }
        }
    }
}
