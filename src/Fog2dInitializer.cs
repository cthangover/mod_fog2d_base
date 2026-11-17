namespace Cthangover.Mods.Fog2d
{
    using Cthangover.Core.Bootstrap;
    using Cthangover.Core.Mods;

    /// <summary>Connects remembered drawings to ordinary new-game and unload lifetimes after application services exist.</summary>
    public sealed class Fog2dInitializer : IModInitializer, IModUnloadAware
    {
        /// <summary>Defers service access until all resources and the shared exploration owner are available.</summary>
        public void OnModLoaded(string id)
        {
        }

        /// <summary>Installs one stable reset owner; duplicate resource-ready notifications are deduplicated by the registry.</summary>
        public void OnModResourcesReady()
        {
            AppServices.Current.RegisterResettable(new Fog2dReset());
            Fog2dWorld.Start();
        }

        /// <summary>Releases frame/material owners and source object references before producing assemblies can leave.</summary>
        public void OnModUnloading()
        {
            Fog2dWorld.Stop();
            Fog2dHistory.Reset();
        }

        /// <summary>No native owners or workers remain after the unload preparation phase.</summary>
        public void OnModUnloaded(string id)
        {
        }
    }
}
