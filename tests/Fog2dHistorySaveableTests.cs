namespace Cthangover.Mods.Fog2d.Tests
{
    using System.Text.Json;
    using Cthangover.Core.Mods.Testing;
    using Cthangover.Core.Settings;
    using Cthangover.Mods.Exploration;

    /// <summary>
    /// Real mod-slot persistence keeps drawing history independent of common bits and rejects unsupported versions atomically.
    /// </summary>
    public sealed class Fog2dHistorySaveableTests
    {
        [ModTest("fog_history/slot_round_trip_reset_and_version")]
        private static void Slot(ModTestContext context)
        {
            var previous = Fog2dHistory.Snapshot();
            try
            {
                Fog2dHistory.Reset();
                using var fixture = new Fog2dFixture();
                using var observer = new Fog2dObserver(fixture.Session, fixture.Profile);
                observer.Update();
                Fog2dHistory.Observe(fixture.Session, observer.Memory);
                var expected = JsonSerializer.Serialize(Fog2dHistory.Snapshot());
                var exploration = JsonSerializer.Serialize(ExplorationMemory.Snapshot());
                var owner = new Fog2dHistorySaveable();
                var slot = new ModSlot();
                owner.OnSave(slot);
                var wire = JsonSerializer.Serialize(slot);
                var restored = JsonSerializer.Deserialize<ModSlot>(wire);
                ModAssert.Equal(1, restored.Data.Count, "history owns one shelf without duplicating explored memory");
                new Fog2dReset().ResetForNewGame();
                ModAssert.Equal(0, Fog2dHistory.Snapshot().Maps.Count, "new game clears previous scenery drawings");
                ModAssert.True(owner.OnLoad(restored, out var error), "serialized history loads: " + error);
                ModAssert.Equal(expected, JsonSerializer.Serialize(Fog2dHistory.Snapshot()), "complete history returns from the mod slot");
                ModAssert.Equal(exploration, JsonSerializer.Serialize(ExplorationMemory.Snapshot()),
                    "drawing restore never changes explored bits");
                restored.Data["RememberedScenes"] = new DataBlob(99, new Fog2dHistoryState());
                ModAssert.False(owner.OnLoad(restored, out error), "unsupported save version is rejected");
                ModAssert.True(!string.IsNullOrWhiteSpace(error), "rejected version has a diagnostic");
                ModAssert.Equal(expected, JsonSerializer.Serialize(Fog2dHistory.Snapshot()), "failed load preserves live drawings");
                ModAssert.True(owner.OnLoad(new ModSlot(), out error), "an absent shelf starts fresh drawing memory");
                ModAssert.Equal(0, Fog2dHistory.Snapshot().Maps.Count, "absent shelf cannot retain another game's drawings");
                ModAssert.Equal(exploration, JsonSerializer.Serialize(ExplorationMemory.Snapshot()),
                    "absent history still leaves common memory intact");
            }
            finally
            {
                Fog2dHistory.Restore(previous);
            }
        }
    }
}
