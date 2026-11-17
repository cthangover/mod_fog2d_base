namespace Cthangover.Mods.Fog2d
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using Cthangover.Mods.Exploration;
    using Cthangover.Mods.Rpg.Model;
    using Cthangover.Mods.Rpg.Play;
    using Cthangover.Mods.Rpg.Validation;
    using Godot;

    /// <summary>
    /// Owns only last-observed sprite descriptions. Unseen active states never replace remembered looks; observing
    /// a vacated known footprint records absence. Shared exploration bits and actual world instances are never changed.
    /// </summary>
    public static class Fog2dHistory
    {
        private static Fog2dHistoryState state = new();

        private static readonly Dictionary<string, Dictionary<string, RpgObject>> sources = new(StringComparer.Ordinal);

        /// <summary>
        /// Captures currently visible sprite footprints. Newly pre-revealed cells receive their authored base look,
        /// without inspecting unseen active states. Returns whether a saved drawing record changed.
        /// </summary>
        public static bool Observe(RpgWorldSession session, ExplorationMap memory)
        {
            ArgumentNullException.ThrowIfNull(session);
            ArgumentNullException.ThrowIfNull(memory);
            if (!memory.IsCurrent || session.Map.Width != memory.Width || session.Map.Height != memory.Height)
            {
                throw new ArgumentException("Scenery observation needs a current matching exploration grid");
            }

            var changed = false;
            if (!state.Maps.TryGetValue(session.Map.ID, out var map))
            {
                if (state.Maps.Count >= 256)
                {
                    throw new ArgumentException("Remembered scenery map capacity reached");
                }

                map = new Fog2dMapHistory
                {
                    Width = memory.Width,
                    Height = memory.Height,
                };
                state.Maps.Add(session.Map.ID, map);
                changed = true;
            }

            if (map.Width != memory.Width || map.Height != memory.Height)
            {
                throw new ArgumentException("Remembered scenery grid changed; start a new game or use a new identity");
            }

            if (!sources.TryGetValue(session.Map.ID, out var seen))
            {
                seen = new Dictionary<string, RpgObject>(StringComparer.OrdinalIgnoreCase);
                sources.Add(session.Map.ID, seen);
            }

            changed |= ObserveAbsence(session, memory, map);
            foreach (var instance in session.Instances)
            {
                if (instance.Current?.Form != RpgObjectForm.Sprite)
                {
                    continue;
                }

                var x = instance.Actor.Cell.X;
                var y = instance.Actor.Cell.Y;
                var visible = Intersects(memory, instance.Current, x, y, true);
                map.Props.TryGetValue(instance.Id, out var known);
                if (!visible && (known != null || !Intersects(memory, instance.Effective, x, y, false)))
                {
                    continue;
                }

                var look = visible ? instance.Current : instance.Effective;
                if (known != null && known.X == x && known.Y == y && seen.TryGetValue(instance.Id, out var source)
                    && ReferenceEquals(source, look) && known.Look.Hidden == (look.Hidden || instance.RouteHidden || InvalidOrigin(look)))
                {
                    continue;
                }

                if (known == null && map.Props.Count >= 4096)
                {
                    throw new ArgumentException("Remembered scenery placement capacity reached");
                }

                var copy = CopyLook(look);
                copy.Hidden |= visible && instance.RouteHidden;
                map.Props[instance.Id] = new Fog2dKnownProp
                {
                    Id = instance.Id,
                    X = x,
                    Y = y,
                    Look = copy,
                };
                seen[instance.Id] = look;
                changed = true;
            }

            return changed;
        }

        /// <summary>Returns a fully detached save envelope; edits to it cannot change live remembered scenery.</summary>
        public static Fog2dHistoryState Snapshot()
        {
            return JsonSerializer.Deserialize<Fog2dHistoryState>(JsonSerializer.Serialize(state));
        }

        /// <summary>Validates and copies the full save before publication; transient source references are discarded.</summary>
        public static void Restore(Fog2dHistoryState restored)
        {
            ArgumentNullException.ThrowIfNull(restored);
            restored.Validate();
            var copy = JsonSerializer.Deserialize<Fog2dHistoryState>(JsonSerializer.Serialize(restored));
            foreach (var map in copy.Maps.Values)
            {
                map.Props = new Dictionary<string, Fog2dKnownProp>(map.Props, StringComparer.OrdinalIgnoreCase);
            }

            state = copy;
            sources.Clear();
        }

        /// <summary>Clears remembered drawings for a new game without resetting the separately owned explored bits.</summary>
        public static void Reset()
        {
            state = new();
            sources.Clear();
        }

        internal static Fog2dMapHistory Get(string map)
        {
            return state.Maps.TryGetValue(map, out var history) ? history : null;
        }

        private static RpgObject CopyLook(RpgObject source)
        {
            var validOrigin = !InvalidOrigin(source);
            float[] anchor = null;
            if (validOrigin && source.Anchor is { Length: >= 2 })
            {
                anchor = new[]
                {
                    source.Anchor[0],
                    source.Anchor[1],
                };
            }

            var parts = new List<RpgObjectPart>();
            foreach (var part in source.Parts ?? new List<RpgObjectPart>())
            {
                var input = new[] { part };
                if (part != null && RpgDataPolicy.FirstBlocking(RpgPartDataCheck.Check(input)) == null)
                {
                    parts.Add(part);
                }
            }

            var drawing = new RpgObject
            {
                ID = source.ID,
                Name = source.Name,
                Category = source.Category,
                Form = RpgObjectForm.Sprite,
                Hidden = source.Hidden || !validOrigin,
                Solid = source.Solid,
                Footprint = source.Footprint,
                Scale = source.Scale,
                Anchor = anchor,
                DrawOrder = source.DrawOrder,
                Parts = parts,
            };
            return JsonSerializer.Deserialize<RpgObject>(JsonSerializer.Serialize(drawing));
        }

        private static bool InvalidOrigin(RpgObject source)
        {
            return source.Anchor is { Length: >= 2 } && (!float.IsFinite(source.Anchor[0]) || !float.IsFinite(source.Anchor[1]));
        }

        private static bool ObserveAbsence(RpgWorldSession session, ExplorationMap memory, Fog2dMapHistory map)
        {
            var changed = false;
            foreach (var row in map.Props)
            {
                var known = row.Value;
                var instance = session.Find(row.Key);
                if (!known.Look.Hidden && (instance == null || instance.Actor.Cell != new Vector2I(known.X, known.Y))
                    && Intersects(memory, known.Look, known.X, known.Y, true))
                {
                    var copy = JsonSerializer.Deserialize<Fog2dKnownProp>(JsonSerializer.Serialize(known));
                    copy.Look.Hidden = true;
                    map.Props[row.Key] = copy;
                    changed = true;
                }
            }

            return changed;
        }

        private static bool Intersects(ExplorationMap memory, RpgObject look, int x, int y, bool visible)
        {
            var right = Math.Min(memory.Width, (long)x + look.Width);
            var bottom = Math.Min(memory.Height, (long)y + look.Height);
            for (var cy = Math.Max(0, y); cy < bottom; cy++)
            {
                for (var cx = Math.Max(0, x); cx < right; cx++)
                {
                    if (visible ? memory.IsVisible(cx, cy) : memory.IsExplored(cx, cy))
                    {
                        return true;
                    }
                }
            }

            return false;
        }
    }
}
