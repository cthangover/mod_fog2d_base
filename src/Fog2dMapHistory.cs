namespace Cthangover.Mods.Fog2d
{
    using System;
    using System.Collections.Generic;

    /// <summary>Remembered scenery for one fixed RPG grid; explored and visible bits remain absent from this save envelope.</summary>
    public sealed class Fog2dMapHistory
    {
        /// <summary>Declared columns prevent reinterpreting remembered origins after an authored dimension change.</summary>
        public int Width { get; set; }

        /// <summary>Declared rows are validated before a loaded history replaces the current generation.</summary>
        public int Height { get; set; }

        /// <summary>Last observed dynamic sprites keyed by placement; NPCs are never persisted as scenery.</summary>
        public Dictionary<string, Fog2dKnownProp> Props { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
