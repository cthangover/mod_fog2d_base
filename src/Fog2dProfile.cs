namespace Cthangover.Mods.Fog2d
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using Cthangover.Core.Factories;
    using Cthangover.Core.Mods.Patches;
    using Cthangover.Mods.Exploration;

    /// <summary>
    /// Opt-in per-map visibility settings. The identity equals the RPG map identity; missing profiles leave legacy
    /// rendering unchanged. Painted indices use that map's row-major grid and belong to authoring, not save data.
    /// </summary>
    [EntityKind(Fog2dProfile.ResourceKind)]
    public sealed class Fog2dProfile : IIdentifiable
    {
        /// <summary>Stable resource kind shared by the loader and authoring documents.</summary>
        public const string ResourceKind = "fog2d_profile";

        /// <summary>RPG map identity; the exploration namespace is added by the adapter, never by authors.</summary>
        [JsonPropertyName("Id")]
        public string ID { get; set; }

        /// <summary>False explicitly reveals a configured map; absence of a profile instead preserves legacy behavior.</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>Maximum centre-to-centre sight distance in cells, limited to 1–64 for predictable work.</summary>
        public int Radius { get; set; } = 8;

        /// <summary>Linear multiplier for remembered scenery, from zero through one; visible cells keep normal lighting.</summary>
        public float RememberedBrightness { get; set; } = 0.3f;

        /// <summary>Additional opaque cells for walls contained in a single room picture.</summary>
        public List<int> OpaqueCells { get; set; } = new();

        /// <summary>Persistent knowledge granted only when the common exploration record is first created.</summary>
        public List<int> InitialExplored { get; set; } = new();

        /// <summary>Overrides segment defaults without treating all solid sprites or NPCs as walls.</summary>
        [MergeKey("Object")]
        public List<Fog2dOccluder> Occluders { get; set; } = new();

        /// <summary>Rare extension fields survive form editing and ordinary resource round trips.</summary>
        [JsonExtensionData]
        public Dictionary<string, JsonElement> Extra { get; set; }

        /// <summary>Rejects an invalid profile before opening memory or allocating an opacity grid.</summary>
        public void Validate(int width, int height)
        {
            ExplorationRecord.ValidateDimensions(width, height);
            ExplorationState.ValidateIdentity("rpg/" + ID);
            if (string.IsNullOrWhiteSpace(ID) || Radius < 1 || Radius > 64 || !float.IsFinite(RememberedBrightness)
                || RememberedBrightness < 0 || RememberedBrightness > 1)
            {
                throw new ArgumentException("Invalid visibility map identity, radius or remembered brightness");
            }

            ValidateCells(OpaqueCells, width * height);
            ValidateCells(InitialExplored, width * height);
            if (Occluders == null || Occluders.Count > 4096)
            {
                throw new ArgumentException("Visibility object rules are missing or exceed their budget");
            }

            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var rule in Occluders)
            {
                if (rule == null || string.IsNullOrWhiteSpace(rule.Object) || !Enum.IsDefined(rule.Mode) || !ids.Add(rule.Object))
                {
                    throw new ArgumentException("Visibility object rules need unique identities and known policies");
                }
            }
        }

        private static void ValidateCells(IReadOnlyList<int> cells, int count)
        {
            if (cells == null || cells.Count > count)
            {
                throw new ArgumentException("Visibility cell list is missing or exceeds its grid");
            }

            foreach (var cell in cells)
            {
                if (cell < 0 || cell >= count)
                {
                    throw new ArgumentException("Visibility cell index is outside the map");
                }
            }
        }
    }
}
