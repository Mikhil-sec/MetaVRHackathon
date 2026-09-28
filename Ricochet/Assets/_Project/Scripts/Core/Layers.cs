using UnityEngine;

namespace Ricochet
{
    /// <summary>Physics layers. Indices are created by the editor configurator (QuestProjectConfigurator.EnsureLayers).</summary>
    public static class Layers
    {
        public const int Room = 6;
        public const int Spark = 7;
        public const int Crystal = 8;

        public static readonly LayerMask SparkHits = (1 << Room) | (1 << Crystal);
    }
}
