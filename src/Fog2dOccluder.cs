namespace Cthangover.Mods.Fog2d
{
    using System.Collections.Generic;
    using System.Text.Json;
    using System.Text.Json.Serialization;

    /// <summary>Named object rule, merged by resource identity so foreign profiles can receive narrow patches.</summary>
    public sealed class Fog2dOccluder
    {
        /// <summary>Effective RPG object identity, shared by all its placements and state variants.</summary>
        public string Object { get; set; }

        /// <summary>Controls sight only; it never changes the object's walkability or interaction state.</summary>
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public Fog2dOpacity Mode { get; set; } = Fog2dOpacity.WhenSolid;

        /// <summary>Mod-owned rule annotations survive copied runtime settings and typed foreign-patch normalization.</summary>
        [JsonExtensionData]
        public Dictionary<string, JsonElement> Extra { get; set; }
    }
}
