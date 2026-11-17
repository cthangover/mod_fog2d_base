namespace Cthangover.Mods.Fog2d.Tests
{
    using System;
    using System.Diagnostics;
    using System.Text.Json;
    using Cthangover.Core.Mods.Testing;
    using Cthangover.Mods.Exploration;
    using Cthangover.Mods.Rpg;
    using Cthangover.Mods.Rpg.Model;
    using Godot;

    /// <summary>
    /// Ordinary door states affect sight while explored bits, load generations and twenty moving NPCs retain their own contracts.
    /// </summary>
    public sealed class Fog2dObserverTests
    {
        [ModTest("fog_observer/door_exploration_load_and_leave")]
        private static void Door(ModTestContext context)
        {
            using var fixture = new Fog2dFixture();
            using var observer = new Fog2dObserver(fixture.Session, fixture.Profile);
            ModAssert.True(observer.Memory.IsExplored(0, 0), "authored initial region is known before observer sight");
            ModAssert.False(observer.Memory.IsVisible(0, 0), "initial reveal is not current sight");
            ModAssert.True(observer.Update(), "first observation computes sight");
            ModAssert.False(observer.Memory.IsVisible(5, 2), "closed door stops sight");
            ModAssert.False(observer.Update(), "stationary world reuses sight");
            fixture.OpenDoor = true;
            fixture.Session.RefreshStates();
            ModAssert.True(observer.Update(), "ordinary effective solidity invalidates sight");
            ModAssert.True(observer.Memory.IsVisible(5, 2), "opened door reveals the other side");
            fixture.OpenDoor = false;
            fixture.Session.RefreshStates();
            ModAssert.True(observer.Update(), "closing door changes current visibility");
            ModAssert.False(observer.Memory.IsVisible(5, 2), "closed area is no longer currently visible");
            ModAssert.True(observer.Memory.IsExplored(5, 2), "seen area stays explored");
            var wire = JsonSerializer.Serialize(ExplorationMemory.Snapshot());
            var old = observer.Memory;
            ExplorationMemory.Restore(JsonSerializer.Deserialize<ExplorationState>(wire));
            ModAssert.False(old.IsCurrent, "load invalidates the old handle");
            ModAssert.True(observer.Update(), "observer reopens and recomputes after load");
            ModAssert.True(observer.Memory.IsExplored(5, 2), "exploration survives full JSON save/load");
            ModAssert.False(observer.Memory.IsVisible(5, 2), "load does not restore obsolete visibility");
            ModAssert.Equal(wire, JsonSerializer.Serialize(ExplorationMemory.Snapshot()), "persistent exploration is unchanged by reopen");
            observer.Dispose();
            ModAssert.False(observer.Memory.IsVisible(1, 2), "leaving drops only current visibility");
            ModAssert.True(observer.Memory.IsExplored(5, 2), "leaving retains knowledge");
        }

        [ModTest("fog_observer/copied_settings_explicit_disable_and_opacity")]
        private static void Policies(ModTestContext context)
        {
            using var fixture = new Fog2dFixture();
            var occlusion = new Fog2dOcclusion(fixture.Session, fixture.Profile);
            ModAssert.True(occlusion.Cells[2 * 7 + 3], "explicit when-solid sprite is opaque");
            ModAssert.True(occlusion.Cells[3], "solid segment is opaque by default");
            ModAssert.False(occlusion.Refresh(), "unchanged effective data reuse the cached mask");
            fixture.Profile.Occluders[0].Mode = Fog2dOpacity.Transparent;
            ModAssert.True(occlusion.Cells[2 * 7 + 3], "detached mask keeps its copied settings");
            var transparent = new Fog2dOcclusion(fixture.Session, fixture.Profile);
            ModAssert.False(transparent.Cells[2 * 7 + 3], "explicit transparency overrides solid walking state");
            fixture.OpenDoor = true;
            fixture.Session.RefreshStates();
            fixture.Profile.Occluders[0].Mode = Fog2dOpacity.Opaque;
            var opaque = new Fog2dOcclusion(fixture.Session, fixture.Profile);
            ModAssert.True(opaque.Cells[2 * 7 + 3], "explicit opacity also applies to an open non-solid sprite");
            fixture.Profile.Enabled = false;
            using var observer = new Fog2dObserver(fixture.Session, fixture.Profile);
            fixture.Profile.Enabled = true;
            observer.Update();
            ModAssert.Equal(35, observer.VisibleCells.Count, "explicit disable reveals all cells using the copied profile");
            ModAssert.True(observer.Memory.IsVisible(6, 4), "disabled fog ignores walls and range");
            ModAssert.True(observer.Memory.IsExplored(6, 4), "explicit disable also makes the terrain map known");
        }

        [ModTest("fog_observer/128_grid_twenty_npcs_reuse_stationary_sight", TimeoutSeconds = 60)]
        private static void Load(ModTestContext context)
        {
            using var fixture = new Fog2dFixture(128, 128, 20);
            fixture.Profile.Radius = 8;
            using var observer = new Fog2dObserver(fixture.Session, fixture.Profile);
            var cold = Stopwatch.StartNew();
            observer.Update();
            cold.Stop();
            var computations = observer.Recomputations;
            var timings = new double[600];
            for (var frame = 0; frame < timings.Length; frame++)
            {
                for (var i = 0; i < 20; i++)
                {
                    var instance = fixture.Session.Find("fog_probe/npc_" + i);
                    instance.Actor.Place(new Vector2I(10 + i, 10 + frame % 2), RpgDirection.Down);
                    fixture.Session.Walk.SetDynamic(instance.Id, instance.Current, instance.Actor.Cell.X, instance.Actor.Cell.Y);
                }

                var start = Stopwatch.GetTimestamp();
                ModAssert.False(observer.Update(), "transparent NPC movement does not trigger a sight computation");
                timings[frame] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            }

            Array.Sort(timings);
            ModAssert.Equal(computations, observer.Recomputations, "all six hundred stationary frames reuse the computed sight");
            fixture.Session.Relocate(new Vector2I(2, 2), RpgDirection.Right);
            ModAssert.True(observer.Update(), "an observer cell change still invalidates visibility");
            ModAssert.Equal(computations + 1, observer.Recomputations, "only actual observation changes perform work");
            context.Log("128x128/20 NPC: cold=" + cold.Elapsed.TotalMilliseconds + "ms; stationary p95=" + timings[569] + "ms");
        }

        [ModTest("fog_observer/explicit_transparent_rules_do_not_invalidate_sight")]
        private static void TransparentRules(ModTestContext context)
        {
            using var fixture = new Fog2dFixture(128, 128, 20);
            fixture.Profile.Occluders.Add(new Fog2dOccluder
            {
                Object = "probe/npc",
                Mode = Fog2dOpacity.Transparent,
            });
            fixture.Profile.Occluders.Add(new Fog2dOccluder
            {
                Object = "probe/wall",
                Mode = Fog2dOpacity.Transparent,
            });
            using var observer = new Fog2dObserver(fixture.Session, fixture.Profile);
            observer.Update();
            ModAssert.True(observer.Memory.IsVisible(5, 0), "explicit transparency overrides the default segment wall");
            var count = observer.Recomputations;
            for (var i = 0; i < 20; i++)
            {
                fixture.Session.Find("fog_probe/npc_" + i).Actor.Place(new Vector2I(10 + i, 11), RpgDirection.Down);
            }

            ModAssert.False(observer.Update(), "explicitly transparent moving NPCs are not occluder changes");
            ModAssert.Equal(count, observer.Recomputations,
                "transparent rules preserve cached sight exactly like default transparent actors");
        }
    }
}
