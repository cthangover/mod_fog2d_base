namespace Cthangover.Mods.Fog2d
{
    using System;
    using System.Text.Json;
    using Cthangover.Core.Mods;
    using Cthangover.Core.Settings;

    /// <summary>Ordinary mod-slot persistence for remembered drawings only; shared exploration keeps its own independent shelf.</summary>
    public sealed class Fog2dHistorySaveable : IModSaveable
    {
        /// <summary>Stores a detached version-one history, excluding source-reference caches, visibility and native resources.</summary>
        public void OnSave(ModSlot slot)
        {
            slot.Data["RememberedScenes"] = new DataBlob(1, Fog2dHistory.Snapshot());
        }

        /// <summary>Rejects malformed or unsupported data before publication; an absent shelf clears stale drawings.</summary>
        public bool OnLoad(ModSlot slot, out string errorMessage)
        {
            errorMessage = null;
            try
            {
                var restored = new Fog2dHistoryState();
                if (slot.Data.TryGetValue("RememberedScenes", out var blob))
                {
                    if (blob.Version != 1)
                    {
                        throw new ArgumentException("Unsupported remembered scenery save version");
                    }

                    restored = JsonSerializer.Deserialize<Fog2dHistoryState>(blob.GetBytes())
                        ?? throw new ArgumentException("Missing remembered scenery save data");
                }

                Fog2dHistory.Restore(restored);
                return true;
            }
            catch (Exception error)
            {
                errorMessage = error.Message;
                return false;
            }
        }
    }
}
