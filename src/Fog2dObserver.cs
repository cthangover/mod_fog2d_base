namespace Cthangover.Mods.Fog2d
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using Cthangover.Mods.Exploration;
    using Cthangover.Mods.Rpg.Play;
    using Godot;

    /// <summary>
    /// Per-session 2D adapter for the shared explored-bit owner. It updates visibility on cell/occluder changes,
    /// reopens handles after load or new game, and drops only transient visibility when the observer leaves.
    /// </summary>
    public sealed class Fog2dObserver : IDisposable
    {
        private readonly RpgWorldSession session;

        private readonly Fog2dProfile profile;

        private readonly Fog2dOcclusion occlusion;

        private Vector2I position;

        private long occlusionRevision;

        private bool disposed;

        /// <summary>Borrowed shared handle; callers must re-read this property after Update can reopen a generation.</summary>
        public ExplorationMap Memory { get; private set; }

        /// <summary>Last owned visibility result; useful for masks and tests without copying the shared persistent bits.</summary>
        public IReadOnlyList<int> VisibleCells { get; private set; } = Array.Empty<int>();

        /// <summary>Number of actual sight computations; stationary frames and transparent NPC steps do not advance it.</summary>
        public long Recomputations { get; private set; }

        /// <summary>Validates and copies settings before opening shared memory; does not reveal current visibility until Update.</summary>
        public Fog2dObserver(RpgWorldSession session, Fog2dProfile profile)
        {
            this.session = session ?? throw new ArgumentNullException(nameof(session));
            ArgumentNullException.ThrowIfNull(profile);
            profile.Validate(session.Map.Width, session.Map.Height);
            if (!string.Equals(profile.ID, session.Map.ID, StringComparison.Ordinal))
            {
                throw new ArgumentException("Visibility profile belongs to another RPG map");
            }

            this.profile = JsonSerializer.Deserialize<Fog2dProfile>(JsonSerializer.Serialize(profile));
            occlusion = new Fog2dOcclusion(session, this.profile);
            Open();
        }

        /// <summary>Reconciles the player and ordinary door states, returning whether current visibility was recomputed.</summary>
        public bool Update()
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (!Memory.IsCurrent)
            {
                Open();
            }

            occlusion.Refresh();
            if (occlusionRevision == occlusion.Revision && position == session.Player.Cell)
            {
                return false;
            }

            position = session.Player.Cell;
            if (profile.Enabled)
            {
                VisibleCells = Fog2dField.Compute(Memory.Width, Memory.Height, occlusion.Cells,
                    position.X, position.Y, profile.Radius);
            }
            else
            {
                var all = new int[Memory.Width * Memory.Height];
                for (var i = 0; i < all.Length; i++)
                {
                    all[i] = i;
                }

                VisibleCells = Array.AsReadOnly(all);
            }

            Memory.SetVisible(VisibleCells);
            occlusionRevision = occlusion.Revision;
            Recomputations++;
            return true;
        }

        /// <summary>Clears visibility only for the still-current generation; persistent exploration remains common-owned.</summary>
        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            if (Memory.IsCurrent)
            {
                Memory.ClearVisibility();
            }
        }

        private void Open()
        {
            Memory = ExplorationMemory.Open("rpg/" + session.Map.ID, session.Map.Width, session.Map.Height, profile.InitialExplored);
            occlusionRevision = 0;
        }
    }
}
