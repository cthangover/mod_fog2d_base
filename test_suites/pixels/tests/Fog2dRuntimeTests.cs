namespace Cthangover.Mods.Fog2d.Tests.Pixels
{
    using System.Text.Json;
    using System;
    using System.Diagnostics;
    using System.Threading.Tasks;
    using Cthangover.Core.Mods.Testing;
    using Cthangover.Core.Utils;
    using Cthangover.Mods.Exploration;
    using Cthangover.Mods.Rpg;
    using Godot;

    /// <summary>
    /// Actual world frames reveal only observed scenery states, conceal unseen actors and retain ordinary save/cache ownership.
    /// </summary>
    public sealed class Fog2dRuntimeTests
    {
        [ModTest("fog_runtime/observed_door_remembered_state_and_hidden_npcs", TimeoutSeconds = 90)]
        private static async Task Visibility(ModTestContext context)
        {
            await Ready();
            var renderers = new[] { "topdown", "isometric" };
            foreach (var renderer in renderers)
            {
                using var world = new Fog2dRuntimeWorld();
                world.Present(renderer);
                var memory = Fog2dWorld.MemoryOf(world.View);
                ModAssert.NotNull(memory, "configured view exposes its borrowed exploration handle");
                ModAssert.True(ReferenceEquals(memory, ExplorationMemory.Open("rpg/fog_runtime", 9, 5)),
                    "world and terrain-map consumers share the exact common exploration owner");
                ModAssert.True(Fog2dWorld.MemoryOf(null) == null, "no view cannot borrow another world's exploration");
                ModAssert.True(memory.IsVisible(4, 2), "first opaque door is visible on the first world frame");
                ModAssert.False(memory.IsExplored(6, 2), "closed wall and range conceal the NPC side");
                using (var first = await world.Capture())
                {
                    var door = world.DoorPixel();
                    var color = first.GetPixel(door.X, door.Y);
                    ModAssert.True(color.R > 0.9f && color.G < 0.1f, renderer + " observed closed door is red: " + color);
                }

                world.OpenDoor = true;
                world.Session.RefreshStates();
                world.Session.Relocate(new Vector2I(4, 2), RpgDirection.Right);
                world.Present();
                ModAssert.True(memory.IsVisible(6, 2), "opening and approaching the doorway reveals the NPC cell");
                using (var shown = await world.Capture())
                {
                    var point = world.NpcPixel();
                    var color = shown.GetPixel(point.X, point.Y);
                    ModAssert.True(color.B > 0.9f && color.R < 0.1f, renderer + " currently visible NPC is blue: " + color);
                }

                world.Session.Relocate(Vector2I.Zero, RpgDirection.Down);
                world.OpenDoor = false;
                world.Session.RefreshStates();
                world.Session.Find("fog_runtime/npc").Actor.Place(new Vector2I(0, 4), RpgDirection.Up);
                world.Present();
                ModAssert.False(memory.IsVisible(4, 2), "changed door is outside current sight");
                ModAssert.True(memory.IsExplored(4, 2), "previously seen doorway remains known");
                var remembered = Fog2dHistory.Snapshot().Maps["fog_runtime"].Props["fog_runtime/door"];
                ModAssert.Equal("fog2d_probe/open", remembered.Look.Parts[0].Image,
                    "unseen closing does not replace the observed open image");
                using (var hidden = await world.Capture())
                {
                    var door = world.DoorPixel();
                    var color = hidden.GetPixel(door.X, door.Y);
                    ModAssert.True(color.G > 0.45f && color.G < 0.7f && color.R < 0.05f,
                        renderer + " unseen closed door still draws remembered green, dimmed: " + color);
                    var unknown = world.CellPixel(8.5f, 4.5f);
                    var concealed = hidden.GetPixel(unknown.X, unknown.Y);
                    ModAssert.True(concealed.R < 0.02f && concealed.G < 0.02f && concealed.B < 0.02f,
                        renderer + " unvisited terrain stays black");
                    var npc = world.NpcPixel();
                    var empty = hidden.GetPixel(npc.X, npc.Y);
                    ModAssert.True(System.Math.Abs(empty.B - empty.R) < 0.02f,
                        renderer + " an unseen moving NPC leaves no blue remembered actor: " + empty);
                }

                world.HideDoor = true;
                world.Session.RefreshStates();
                world.Present();
                ModAssert.True(world.Session.Find("fog_runtime/door").Current.Hidden,
                    "setup removes the actual current door drawing outside sight");
                ModAssert.Equal(0, world.Session.Find("fog_runtime/door").Current.Parts.Count,
                    "the actual world no longer has any door part to submit");
                using (var absent = await world.Capture())
                {
                    var point = world.DoorPixel();
                    var color = absent.GetPixel(point.X, point.Y);
                    ModAssert.True(color.G > 0.45f && color.G < 0.7f && color.R < 0.05f,
                        renderer + " removing unseen live parts still retains the last observed green image: " + color);
                }

                world.Session.Relocate(new Vector2I(2, 2), RpgDirection.Right);
                world.Present();
                using (var empty = await world.Capture())
                {
                    var point = world.DoorPixel();
                    var color = empty.GetPixel(point.X, point.Y);
                    ModAssert.True(color.R > 0.9f && color.G > 0.9f && color.B > 0.9f,
                        renderer + " observing disappearance removes the remembered image and exposes the floor: " + color);
                }

                world.HideDoor = false;
                world.Session.RefreshStates();
                world.Present();
                using var returned = await world.Capture();
                var location = world.DoorPixel();
                var current = returned.GetPixel(location.X, location.Y);
                ModAssert.True(current.R > 0.9f && current.G < 0.1f, renderer + " seeing the doorway again updates it to red");
                context.Log(renderer + " actual world preserved unseen state and concealed the moving NPC");
            }
        }

        [ModTest("fog_runtime/json_load_profile_refresh_and_borrowed_texture", TimeoutSeconds = 90)]
        private static async Task Saves(ModTestContext context)
        {
            await Ready();
            var renderers = new[] { "topdown", "isometric" };
            foreach (var renderer in renderers)
            {
                using var world = new Fog2dRuntimeWorld();
                world.Present(renderer);
                var look = world.View.SpriteLooks(world.Floor)[0];
                var borrowed = false;
                foreach (var visual in world.View.Scene.Visuals)
                {
                    if (visual.Id == "fog_runtime/floor#base")
                    {
                        borrowed = ReferenceEquals(look.Texture, visual.Texture);
                    }
                }

                ModAssert.True(borrowed, "remembered drawing uses the actual world's decoded texture instead of another cache");
                ModAssert.True(world.View.Presentation.SharedMaterial.GetShaderParameter("use_visibility_mask").AsBool(),
                    "a configured world has an active visibility mask");
                Fog2dWorld.RefreshProfiles();
                ModAssert.False(world.View.Presentation.SharedMaterial.GetShaderParameter("use_visibility_mask").AsBool(),
                    "profile refresh detaches the old mask before its owner is released");
                world.Present();
                ModAssert.True(world.View.Presentation.SharedMaterial.GetShaderParameter("use_visibility_mask").AsBool(),
                    "the ordinary next frame reopens the copied profile");
                world.Session.Relocate(Vector2I.Zero, RpgDirection.Down);
                world.Present();
                var exploredWire = JsonSerializer.Serialize(ExplorationMemory.Snapshot());
                var historyWire = JsonSerializer.Serialize(Fog2dHistory.Snapshot());
                world.OpenDoor = true;
                world.Session.RefreshStates();
                world.Present();
                ExplorationMemory.Reset();
                Fog2dHistory.Reset();
                ExplorationMemory.Restore(JsonSerializer.Deserialize<ExplorationState>(exploredWire));
                Fog2dHistory.Restore(JsonSerializer.Deserialize<Fog2dHistoryState>(historyWire));
                ModAssert.True(Fog2dWorld.MemoryOf(world.View) == null, "UI cannot borrow the obsolete generation before reconciliation");
                world.Present();
                ModAssert.Equal(exploredWire, JsonSerializer.Serialize(ExplorationMemory.Snapshot()), "load preserves every explored bit");
                ModAssert.Equal(historyWire, JsonSerializer.Serialize(Fog2dHistory.Snapshot()),
                    "unseen world changes do not alter restored drawings");
                using var restored = await world.Capture();
                var door = world.DoorPixel();
                var color = restored.GetPixel(door.X, door.Y);
                ModAssert.True(color.R > 0.45f && color.R < 0.7f && color.G < 0.05f,
                    renderer + " loading retains the last seen closed door although the live state is open: " + color);
            }
        }

        [ModTest("fog_runtime/128_grid_twenty_npcs_reuse_mask", TimeoutSeconds = 90)]
        private static async Task Load(ModTestContext context)
        {
            await Ready();
            var renderers = new[] { "topdown", "isometric" };
            foreach (var renderer in renderers)
            {
                var cold = Stopwatch.StartNew();
                using var world = new Fog2dRuntimeWorld(128, 128, 20);
                world.Present(renderer);
                cold.Stop();
                var memory = Fog2dWorld.MemoryOf(world.View);
                var revision = memory.Revision;
                using var initialMask = world.View.Presentation.SharedMaterial.GetShaderParameter("visibility_mask");
                var texture = initialMask.AsGodotObject();
                var samples = new double[120];
                for (var frame = 0; frame < samples.Length; frame++)
                {
                    for (var i = 0; i < 20; i++)
                    {
                        var id = i == 0 ? "fog_runtime/npc" : "fog_runtime/npc_" + i;
                        var instance = world.Session.Find(id);
                        instance.Actor.Place(new Vector2I(6 + i, 10 + frame % 2), RpgDirection.Down);
                        world.Session.Walk.SetDynamic(id, instance.Current, instance.Actor.Cell.X, instance.Actor.Cell.Y);
                    }

                    var start = Stopwatch.GetTimestamp();
                    world.Present();
                    samples[frame] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                    await world.Viewport.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                }

                Array.Sort(samples);
                ModAssert.Equal(revision, memory.Revision, "transparent moving NPCs do not dirty exploration or visibility");
                using var finalMask = world.View.Presentation.SharedMaterial.GetShaderParameter("visibility_mask");
                ModAssert.True(ReferenceEquals(texture, finalMask.AsGodotObject()), "stationary observation retains the same mask texture");
                ModAssert.Equal(1, Fog2dHistory.Snapshot().Maps["fog_runtime"].Props.Count,
                    "twenty rendered NPCs never grow persistent scenery history");
                context.Log(renderer + " 128x128/20 NPC: build+first=" + cold.Elapsed.TotalMilliseconds
                    + "ms; CPU presentation p95=" + samples[113] + "ms; p99=" + samples[118] + "ms");
            }
        }

        [ModTest("fog_runtime/visible_sprite_keeps_interpolated_pose", TimeoutSeconds = 60)]
        private static async Task Motion(ModTestContext context)
        {
            await Ready();
            var renderers = new[] { "topdown", "isometric" };
            foreach (var renderer in renderers)
            {
                using var world = new Fog2dRuntimeWorld();
                world.OpenDoor = true;
                world.Session.RefreshStates();
                world.Present(renderer);
                var instance = world.Session.Find("fog_runtime/door");
                var tap = new Fog2dPoseTap
                {
                    Target = world.View,
                    Texture = world.View.SpriteLooks(instance.Current)[0].Texture,
                };
                RpgWorldView.FrameFiltering += tap.Filter;
                try
                {
                    instance.Actor.StepTo(RpgDirection.Left);
                    instance.Actor.Advance(0.05);
                    world.Session.Walk.SetDynamic(instance.Id, instance.Current, instance.Actor.Cell.X, instance.Actor.Cell.Y);
                    ModAssert.True(instance.Actor.IsMoving, "fixture has an unfinished sprite movement");
                    ModAssert.False(instance.Actor.Position == new Vector2(instance.Actor.Cell.X, instance.Actor.Cell.Y),
                        "fixture has an actual interpolated position distinct from the reserved cell");
                    var before = instance.Actor.Position;
                    world.Present();
                    ModAssert.True(tap.Found, "the visible sprite reaches the submitted frame");
                    ModAssert.True(tap.Position == before, renderer + " current visible sprite keeps its exact interpolated position");
                    ModAssert.True(instance.Actor.Position == before, "presentation never advances the movement clock");
                }
                finally
                {
                    RpgWorldView.FrameFiltering -= tap.Filter;
                    tap.Texture = null;
                }
            }
        }

        private static async Task Ready()
        {
            ModAssert.True(GameEnvironment.IsRuntime, "world visibility needs the actual runtime");
            ModAssert.False(DisplayServer.GetName() == "headless", "world visibility pixels require a windowed launch");
            var tree = (SceneTree)Godot.Engine.GetMainLoop();
            for (var i = 0; i < 5; i++)
            {
                await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            }
        }
    }
}
