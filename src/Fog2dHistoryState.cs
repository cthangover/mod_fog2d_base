namespace Cthangover.Mods.Fog2d
{
    using System;
    using System.Collections.Generic;
    using Cthangover.Mods.Exploration;
    using Cthangover.Mods.Rpg.Model;

    /// <summary>Bounded, projection-free saved drawing history; validation never touches the shared exploration owner.</summary>
    public sealed class Fog2dHistoryState
    {
        /// <summary>RPG map identities use their ordinary case-sensitive names, independently of presentation choice.</summary>
        public Dictionary<string, Fog2dMapHistory> Maps { get; set; } = new(StringComparer.Ordinal);

        /// <summary>Checks all dimensions, placement identities and sprite records before any live data can be replaced.</summary>
        public void Validate()
        {
            if (Maps == null || Maps.Count > 256)
            {
                throw new ArgumentException("Remembered scenery supports at most 256 maps");
            }

            foreach (var map in Maps)
            {
                ExplorationState.ValidateIdentity("rpg/" + map.Key);
                if (string.IsNullOrWhiteSpace(map.Key) || map.Value == null || map.Value.Props == null || map.Value.Props.Count > 4096)
                {
                    throw new ArgumentException("Invalid remembered scenery map or placement budget");
                }

                ExplorationRecord.ValidateDimensions(map.Value.Width, map.Value.Height);
                var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var prop in map.Value.Props)
                {
                    var value = prop.Value;
                    if (string.IsNullOrWhiteSpace(prop.Key) || prop.Key.Length > 256 || !ids.Add(prop.Key) || value == null
                        || value.Id != prop.Key || !prop.Key.StartsWith(map.Key + "/", StringComparison.OrdinalIgnoreCase)
                        || value.X < 0 || value.Y < 0 || value.X >= map.Value.Width || value.Y >= map.Value.Height
                        || value.Look == null || value.Look.Form != RpgObjectForm.Sprite)
                    {
                        throw new ArgumentException("Invalid remembered sprite identity, origin or drawing description");
                    }
                }
            }
        }
    }
}
