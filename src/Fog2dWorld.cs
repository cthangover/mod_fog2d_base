namespace Cthangover.Mods.Fog2d
{
    using System.Collections.Generic;
    using System.Text.Json;
    using System.Threading.Tasks;
    using Cthangover.Core.Bootstrap;
    using Cthangover.Core.Mods.Resolvers;
    using Cthangover.Mods.Exploration;
    using Cthangover.Mods.Rpg;
    using Cthangover.Mods.TileRender;
    using Godot;

    /// <summary>
    /// Opt-in world adapter and lifecycle registry; an unconfigured map submits the original frame without allocating a mask.
    /// </summary>
    public static class Fog2dWorld
    {
        private static readonly Dictionary<RpgWorldView, Fog2dViewBinding> bindings = new();

        /// <summary>
        /// Borrows exploration from the requested view. Null means no live configured observer or a generation awaiting
        /// its next world update; a terrain-map UI cannot accidentally borrow a different preview's exploration.
        /// </summary>
        public static ExplorationMap MemoryOf(RpgWorldView view)
        {
            var memory = view != null && bindings.TryGetValue(view, out var binding) ? binding.Observer?.Memory : null;
            return memory?.IsCurrent == true ? memory : null;
        }

        /// <summary>
        /// Reopens copied profiles after an authoring save; redraw occurs through the ordinary world frame on its next tick.
        /// </summary>
        public static void RefreshProfiles()
        {
            foreach (var binding in bindings.Values)
            {
                binding.Dispose();
            }

            bindings.Clear();
        }

        internal static void Start()
        {
            RpgWorldView.FrameFiltering -= Filter;
            RpgWorldView.FrameFiltering += Filter;
            ApplicationExit.Preparing -= PrepareExit;
            ApplicationExit.Preparing += PrepareExit;
        }

        internal static void Stop()
        {
            RpgWorldView.FrameFiltering -= Filter;
            ApplicationExit.Preparing -= PrepareExit;
            RefreshProfiles();
        }

        internal static void Release(RpgWorldView view)
        {
            if (bindings.Remove(view, out var binding))
            {
                binding.Dispose();
            }
        }

        private static void Filter(RpgWorldView view, ITileWorldPresentation presentation, ref TileFrame frame)
        {
            if (ApplicationExit.IsQuitting)
            {
                return;
            }

            if (!bindings.TryGetValue(view, out var binding))
            {
                Entities.Collect<Fog2dProfile>(required: false).TryGetValue(view.Map.ID, out var profile);
                var copy = profile == null ? null : JsonSerializer.Deserialize<Fog2dProfile>(JsonSerializer.Serialize(profile));
                binding = new Fog2dViewBinding(view, copy);
                bindings.Add(view, binding);
            }

            frame = binding.Filter(presentation, frame);
        }

        private static Task PrepareExit(SceneTree tree)
        {
            Stop();
            return Task.CompletedTask;
        }
    }
}
