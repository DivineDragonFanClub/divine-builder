using System.Collections.Generic;

namespace DivineDragon.Patcher
{
    public struct PatchProgress
    {
        public string Phase;
        public int Current;
        public int Total;
        public string BundleName;
    }

    public class PatchResult
    {
        public readonly List<BuildError> Errors = new List<BuildError>();
        public readonly List<string> Warnings = new List<string>();
        public int Patched;
        public int Skipped;
        public bool Cancelled;

        public bool Success => !Cancelled && Errors.Count == 0;
    }
}
