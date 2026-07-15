using System.Collections.Generic;
using AssetsTools.NET;

namespace DivineDragon.Patcher
{
    public class AssetRetargeter
    {
        public const uint SwitchPlatform = 38;
        public const uint SerializedFileVersion = 22;

        private const string HintDeps =
            "Make sure all dependencies in your project are Addressables and match with the game.";
        private const string HintMoved =
            "Make sure not to delete or move files before running the build.";
        private const string HintAddr =
            "Make sure your Addressables are correct.";

        private readonly IReadOnlyList<ExternalSource> _deps;
        private readonly Dictionary<string, string> _localCache;
        private readonly GameCache _game;
        private readonly List<BuildError> _errors;
        private readonly string _bundlePath;

        public AssetRetargeter(
            IReadOnlyList<ExternalSource> deps,
            Dictionary<string, string> localCache,
            GameCache game,
            List<BuildError> errors,
            string bundlePath)
        {
            _deps = deps;
            _localCache = localCache;
            _game = game;
            _errors = errors;
            _bundlePath = bundlePath;
        }

        public void Material(AssetTypeValueField bf)
        {
            TryRetargetPPtr(bf["m_Shader"], "Material.m_Shader");

            var texEnvs = bf["m_SavedProperties"]["m_TexEnvs"]["Array"];
            if (texEnvs == null || texEnvs.IsDummy)
                return;
            foreach (var pair in texEnvs.Children)
            {
                TryRetargetPPtr(pair["second"]["m_Texture"], "Material.m_TexEnvs");
            }
        }

        public void AnimationClip(AssetTypeValueField bf)
        {
            var events = bf["m_Events"]["Array"];
            if (events == null || events.IsDummy)
                return;
            foreach (var ev in events.Children)
                TryRetargetPPtr(ev["objectReferenceParameter"], "AnimationClip.objectReferenceParameter");
        }

        public void AnimatorOverrideController(AssetTypeValueField bf, Dictionary<long, string> uacThisBuild)
        {
            var controller = bf["m_Controller"];
            if (controller.IsDummy || controller["m_FileID"].AsInt == 0)
                return;
            TryRetargetPPtr(controller, "AnimatorOverrideController.m_Controller");

            var clips = bf["m_Clips"]["Array"];
            if (clips == null || clips.IsDummy)
                return;
            foreach (var clip in clips.Children)
            {
                var original = clip["m_OriginalClip"];
                long originalPathId = original["m_PathID"].AsLong;
                if (originalPathId != 0)
                {
                    if (!uacThisBuild.TryGetValue(originalPathId, out string name))
                    {
                        AddError(BuildErrorKind.MissingUacClip, "AnimatorOverrideController.m_OriginalClip",
                            $"clip with PathID {originalPathId} was not found in the UAC template of this build",
                            HintAddr);
                    }
                    else if (!_game.UacByName.TryGetValue(name, out long gamePathId))
                    {
                        AddError(BuildErrorKind.MissingUacClip, "AnimatorOverrideController.m_OriginalClip",
                            $"clip '{name}' has no match in the game's UAC cache", HintAddr);
                    }
                    else
                    {
                        original["m_PathID"].AsLong = gamePathId;
                    }
                }

                var over = clip["m_OverrideClip"];
                if (over["m_FileID"].AsInt != 0)
                    TryRetargetPPtr(over, "AnimatorOverrideController.m_OverrideClip");
            }
        }

        public void TextMeshPro(AssetTypeValueField bf)
        {
            var font = bf["m_fontAsset"];
            if (!font.IsDummy)
                TryRetargetPPtr(font, "TextMeshProUGUI.m_fontAsset");
            var sprite = bf["m_spriteAsset"];
            if (!sprite.IsDummy)
                TryRetargetPPtr(sprite, "TextMeshProUGUI.m_spriteAsset");
        }

        public bool AssetBundleContainer(AssetTypeValueField bf)
        {
            var container = bf["m_Container"]["Array"];
            if (container == null || container.IsDummy)
                return false;
            bool changed = false;
            foreach (var pair in container.Children)
            {
                var key = pair["first"];
                string name = key.AsString;
                if (name != null && name.EndsWith(".anim"))
                {
                    key.AsString = name.Substring(0, name.Length - 5) + ".fbx";
                    changed = true;
                }
            }
            return changed;
        }

        public bool MonoScript(AssetTypeValueField bf, string gameCodeAsm)
        {
            var asmField = bf["m_AssemblyName"];
            if (asmField.IsDummy)
                return false;
            string asm = asmField.AsString;
            if (asm == gameCodeAsm || (asm != null && asm.StartsWith(gameCodeAsm + ".")))
            {
                asmField.AsString = "Assembly-CSharp";
                return true;
            }
            return false;
        }

        public static void SetSwitchPlatform(AssetsFile file)
        {
            file.Metadata.TargetPlatform = SwitchPlatform;
            file.Header.Version = SerializedFileVersion;
        }

        private bool TryRetargetPPtr(AssetTypeValueField pptr, string assetType)
        {
            if (pptr == null || pptr.IsDummy)
                return true;

            int fileId = pptr["m_FileID"].AsInt;
            if (fileId == 0)
                return true;

            int idx = fileId - 1;
            if (idx < 0 || idx >= _deps.Count)
            {
                AddError(BuildErrorKind.MissingDependency, assetType,
                    $"could not read dependency #{idx}", HintDeps);
                return false;
            }

            var dep = _deps[idx];
            if (dep.IsLibrary)
            {
                AddError(BuildErrorKind.MissingDependency, assetType,
                    $"dependency #{idx} is a resource library, not a bundle", HintDeps);
                return false;
            }

            if (!_localCache.TryGetValue(dep.Cab, out string internalId))
            {
                AddError(BuildErrorKind.MissingInternalId, assetType,
                    $"no InternalId for CAB '{dep.Cab}'", HintMoved);
                return false;
            }

            if (!_game.ByInternalId.TryGetValue(internalId, out CacheEntry ce))
            {
                AddError(BuildErrorKind.MissingCacheEntry, assetType,
                    $"no game asset with InternalId '{internalId}'", HintAddr);
                return false;
            }

            pptr["m_PathID"].AsLong = ce.path_id;
            return true;
        }

        private void AddError(BuildErrorKind kind, string assetType, string detail, string hint)
        {
            _errors.Add(new BuildError
            {
                BundlePath = _bundlePath,
                AssetType = assetType,
                Kind = kind,
                Detail = detail,
                Hint = hint,
            });
        }
    }
}
