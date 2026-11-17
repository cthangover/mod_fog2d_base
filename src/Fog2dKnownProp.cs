namespace Cthangover.Mods.Fog2d
{
    using Cthangover.Mods.Rpg.Model;

    /// <summary>Detached last-observed scenery; this is a drawing description, never a replacement gameplay instance.</summary>
    public sealed class Fog2dKnownProp
    {
        /// <summary>Placement identity preserves a remembered object independently of its current unseen state.</summary>
        public string Id { get; set; }

        /// <summary>Last observed footprint origin in map columns.</summary>
        public int X { get; set; }

        /// <summary>Last observed footprint origin in map rows.</summary>
        public int Y { get; set; }

        /// <summary>Copied resolved sprite description; Hidden also records an observed disappearance.</summary>
        public RpgObject Look { get; set; }
    }
}
