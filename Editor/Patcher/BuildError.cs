namespace DivineDragon.Patcher
{
    public enum BuildErrorKind
    {
        MissingDependency,
        MissingInternalId,
        MissingCacheEntry,
        MissingUacClip,
        BundleIo,
        PreFlight,
    }

    public class BuildError
    {
        public string BundlePath;
        public string AssetType;
        public BuildErrorKind Kind;
        public string Detail;
        public string Hint;

        public override string ToString()
        {
            string where = string.IsNullOrEmpty(AssetType) ? BundlePath : $"{BundlePath} ({AssetType})";
            string msg = $"[{Kind}] {where}: {Detail}";
            return string.IsNullOrEmpty(Hint) ? msg : $"{msg} {Hint}";
        }
    }
}
