namespace Cthangover.Mods.Fog2d
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using Cthangover.Mods.Rpg.Model;
    using Cthangover.Mods.Rpg.Play;
    using Godot;

    /// <summary>
    /// Detached cell opacity for gameplay or authoring previews. Terrain and static footprints are folded once;
    /// only sight-relevant instance changes rebuild the grid, so ordinary moving NPCs do not invalidate sight.
    /// </summary>
    public sealed class Fog2dOcclusion
    {
        private readonly RpgWorldSession session;

        private readonly bool[] terrain;

        private readonly bool[] cells;

        private readonly Dictionary<string, Fog2dOpacity> rules = new(StringComparer.OrdinalIgnoreCase);

        private List<(RpgObject Look, Vector2I Cell, bool Visible)> previous = new();

        private List<(RpgObject Look, Vector2I Cell, bool Visible)> next = new();

        /// <summary>Borrowed read-only row-major mask; a changed Revision announces changed contents.</summary>
        public IReadOnlyList<bool> Cells { get; }

        /// <summary>Advances when relevant object state or footprint changes, independent of walk-grid revisions.</summary>
        public long Revision { get; private set; }

        /// <summary>
        /// Copies authored settings before building a grid. It never opens exploration memory or writes gameplay state,
        /// making the same operation safe for a detached editor session.
        /// </summary>
        public Fog2dOcclusion(RpgWorldSession session, Fog2dProfile profile)
        {
            this.session = session ?? throw new ArgumentNullException(nameof(session));
            ArgumentNullException.ThrowIfNull(profile);
            profile.Validate(session.Map.Width, session.Map.Height);
            var settings = JsonSerializer.Deserialize<Fog2dProfile>(JsonSerializer.Serialize(profile));
            foreach (var rule in settings.Occluders)
            {
                rules.Add(rule.Object, rule.Mode);
            }

            terrain = new bool[session.Map.Width * session.Map.Height];
            cells = new bool[terrain.Length];
            Cells = Array.AsReadOnly(cells);
            for (var y = 0; y < session.Map.Height; y++)
            {
                for (var x = 0; x < session.Map.Width; x++)
                {
                    terrain[y * session.Map.Width + x] = Blocks(session.Cells.Structure(x, y), 0, 0)
                        || Blocks(session.Cells.Ground(x, y), 0, 0);
                }
            }

            foreach (var entry in session.Statics)
            {
                Paint(terrain, entry.Object, entry.X, entry.Y);
            }

            foreach (var index in settings.OpaqueCells)
            {
                terrain[index] = true;
            }

            Refresh();
        }

        /// <summary>Checks cached instance identities, cells and visibility; returns true only when the mask is rebuilt.</summary>
        public bool Refresh()
        {
            next.Clear();
            foreach (var instance in session.Instances)
            {
                if (Relevant(instance.Effective) || Relevant(instance.Current))
                {
                    next.Add((instance.Current, instance.Actor.Cell, instance.Visible));
                }
            }

            var changed = Revision == 0 || next.Count != previous.Count;
            for (var i = 0; !changed && i < next.Count; i++)
            {
                changed = next[i] != previous[i];
            }

            if (!changed)
            {
                return false;
            }

            Array.Copy(terrain, cells, terrain.Length);
            foreach (var stamp in next)
            {
                if (stamp.Visible)
                {
                    Paint(cells, stamp.Look, stamp.Cell.X, stamp.Cell.Y);
                }
            }

            (previous, next) = (next, previous);
            Revision++;
            return true;
        }

        private bool Relevant(RpgObject value)
        {
            if (value == null)
            {
                return false;
            }

            if (value.ID != null && rules.TryGetValue(value.ID, out var mode))
            {
                return mode != Fog2dOpacity.Transparent;
            }

            return value.Form == RpgObjectForm.Segment;
        }

        private bool Blocks(RpgObject value, int x, int y)
        {
            if (value == null || value.Hidden)
            {
                return false;
            }

            var mode = value.Form == RpgObjectForm.Segment ? Fog2dOpacity.WhenSolid : Fog2dOpacity.Transparent;
            if (value.ID != null && rules.TryGetValue(value.ID, out var authored))
            {
                mode = authored;
            }

            return mode == Fog2dOpacity.Opaque || mode == Fog2dOpacity.WhenSolid && value.BlocksAt(x, y);
        }

        private void Paint(bool[] target, RpgObject value, int x, int y)
        {
            if (value == null || value.Hidden)
            {
                return;
            }

            var right = Math.Min(session.Map.Width, (long)x + value.Width);
            var bottom = Math.Min(session.Map.Height, (long)y + value.Height);
            for (var cy = Math.Max(0, y); cy < bottom; cy++)
            {
                for (var cx = Math.Max(0, x); cx < right; cx++)
                {
                    target[cy * session.Map.Width + cx] |= Blocks(value, cx - x, cy - y);
                }
            }
        }
    }
}
