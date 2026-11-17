namespace Cthangover.Mods.Fog2d.Tests
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using Cthangover.Core.Mods.Testing;
    using Cthangover.Mods.Rpg;
    using Cthangover.Mods.Rpg.Model;
    using Godot;

    /// <summary>
    /// Last-seen drawings preserve observed states, absence, detached saves and the renderer's malformed-part quarantine.
    /// </summary>
    public sealed class Fog2dHistoryTests
    {
        [ModTest("fog_history/unseen_changes_observed_absence_and_round_trip")]
        private static void Changes(ModTestContext context)
        {
            var previous = Fog2dHistory.Snapshot();
            try
            {
                Fog2dHistory.Reset();
                using var fixture = new Fog2dFixture();
                fixture.Profile.Radius = 1;
                fixture.Session.Relocate(new Vector2I(2, 2), RpgDirection.Right);
                using var observer = new Fog2dObserver(fixture.Session, fixture.Profile);
                observer.Update();
                ModAssert.True(Fog2dHistory.Observe(fixture.Session, observer.Memory), "first observation records scenery");
                ModAssert.False(Fog2dHistory.Observe(fixture.Session, observer.Memory), "stable observations reuse their detached drawing");
                ModAssert.True(Known().Look.Solid == true, "closed door was observed");
                fixture.Session.Relocate(Vector2I.Zero, RpgDirection.Down);
                fixture.OpenDoor = true;
                fixture.Session.RefreshStates();
                observer.Update();
                Fog2dHistory.Observe(fixture.Session, observer.Memory);
                ModAssert.True(Known().Look.Solid == true, "unseen open state never replaces the remembered closed door");
                fixture.Session.Relocate(new Vector2I(4, 2), RpgDirection.Left);
                observer.Update();
                Fog2dHistory.Observe(fixture.Session, observer.Memory);
                ModAssert.True(Known().Look.Solid == false, "observing the opened door updates its recorded drawing");
                fixture.Session.Find("fog_probe/door").Actor.Place(new Vector2I(5, 4), RpgDirection.Down);
                observer.Update();
                Fog2dHistory.Observe(fixture.Session, observer.Memory);
                ModAssert.True(Known().Look.Hidden, "seeing the old location empty removes its remembered ghost");
                ModAssert.Equal(3, Known().X, "unseen new location is not disclosed");
                ModAssert.Equal(2, Known().Y, "absence is recorded at the old observed row");
                var wire = JsonSerializer.Serialize(Fog2dHistory.Snapshot());
                var restored = JsonSerializer.Deserialize<Fog2dHistoryState>(wire);
                restored.Validate();
                Fog2dHistory.Restore(restored);
                ModAssert.Equal(wire, JsonSerializer.Serialize(Fog2dHistory.Snapshot()),
                    "every history field survives full JSON save/load");
                restored.Maps["fog_probe"].Props["fog_probe/door"].Look.Hidden = false;
                ModAssert.True(Known().Look.Hidden, "restored input is detached from live history");
            }
            finally
            {
                Fog2dHistory.Restore(previous);
            }
        }

        [ModTest("fog_history/initial_reveal_uses_authored_base_and_excludes_npcs")]
        private static void Initial(ModTestContext context)
        {
            var previous = Fog2dHistory.Snapshot();
            try
            {
                Fog2dHistory.Reset();
                using var fixture = new Fog2dFixture(128, 128, 20);
                fixture.Profile.Radius = 1;
                fixture.Profile.InitialExplored.Add(2 * 128 + 3);
                fixture.OpenDoor = true;
                fixture.Session.RefreshStates();
                fixture.Session.Relocate(Vector2I.Zero, RpgDirection.Down);
                using var observer = new Fog2dObserver(fixture.Session, fixture.Profile);
                observer.Update();
                ModAssert.False(observer.Memory.IsVisible(3, 2), "pre-revealed door remains outside current sight");
                Fog2dHistory.Observe(fixture.Session, observer.Memory);
                ModAssert.True(Known().Look.Solid == true, "initial known area receives authored base instead of unseen active state");
                ModAssert.Equal(1, Fog2dHistory.Snapshot().Maps["fog_probe"].Props.Count, "twenty NPCs are not persisted as scenery");
                var snapshot = Fog2dHistory.Snapshot();
                snapshot.Maps["fog_probe"].Props["fog_probe/door"].X = 9;
                ModAssert.Equal(3, Known().X, "editing a returned snapshot cannot change live memory");
            }
            finally
            {
                Fog2dHistory.Restore(previous);
            }
        }

        [ModTest("fog_history/invalid_restore_is_atomic")]
        private static void Invalid(ModTestContext context)
        {
            var previous = Fog2dHistory.Snapshot();
            try
            {
                Fog2dHistory.Reset();
                using var fixture = new Fog2dFixture();
                using var observer = new Fog2dObserver(fixture.Session, fixture.Profile);
                observer.Update();
                Fog2dHistory.Observe(fixture.Session, observer.Memory);
                var wire = JsonSerializer.Serialize(Fog2dHistory.Snapshot());
                for (var scenario = 0; scenario < 4; scenario++)
                {
                    var broken = JsonSerializer.Deserialize<Fog2dHistoryState>(wire);
                    var map = broken.Maps["fog_probe"];
                    switch (scenario)
                    {
                        case 0:
                            map.Width = 0;
                            break;
                        case 1:
                            map.Props["fog_probe/door"].X = 7;
                            break;
                        case 2:
                            map.Props["fog_probe/door"].Look.Form = RpgObjectForm.Actor;
                            break;
                        case 3:
                            map.Props["fog_probe/door"].Id = "another_map/door";
                            break;
                    }

                    var rejected = false;
                    try
                    {
                        Fog2dHistory.Restore(broken);
                    }
                    catch (ArgumentException)
                    {
                        rejected = true;
                    }

                    ModAssert.True(rejected, "invalid remembered drawing rejected: " + scenario);
                    ModAssert.Equal(wire, JsonSerializer.Serialize(Fog2dHistory.Snapshot()),
                        "invalid restore leaves all live data untouched");
                }
            }
            finally
            {
                Fog2dHistory.Restore(previous);
            }
        }

        [ModTest("fog_history/nonfinite_external_parts_remain_quarantined")]
        private static void Quarantine(ModTestContext context)
        {
            var previous = Fog2dHistory.Snapshot();
            try
            {
                Fog2dHistory.Reset();
                using var fixture = new Fog2dFixture();
                var source = fixture.Session.Find("fog_probe/door").Current;
                source.Parts = new List<RpgObjectPart>
                {
                    new RpgObjectPart
                    {
                        Image = "probe/valid",
                        Size = new float[] { 1, 1 },
                    },
                    new RpgObjectPart
                    {
                        Image = "probe/invalid",
                        Size = new float[]
                        {
                            float.NaN,
                            1,
                        },
                    },
                };
                using var observer = new Fog2dObserver(fixture.Session, fixture.Profile);
                observer.Update();
                Fog2dHistory.Observe(fixture.Session, observer.Memory);
                ModAssert.Equal(1, Known().Look.Parts.Count, "invalid part cannot break history JSON serialization");
                ModAssert.Equal("probe/valid", Known().Look.Parts[0].Image, "valid neighbour remains in the remembered drawing");
                ModAssert.Equal(2, source.Parts.Count, "quarantine does not edit the current world object");
                source.Anchor = new float[]
                {
                    float.NaN,
                    1,
                };
                Fog2dHistory.Observe(fixture.Session, observer.Memory);
                ModAssert.True(Known().Look.Hidden, "nonfinite shared origin hides the same complete drawing as the renderer");
                ModAssert.True(float.IsNaN(source.Anchor[0]), "recording never normalizes the source's bad origin");
                var wire = JsonSerializer.Serialize(Fog2dHistory.Snapshot());
                Fog2dHistory.Restore(JsonSerializer.Deserialize<Fog2dHistoryState>(wire));
                ModAssert.Equal(wire, JsonSerializer.Serialize(Fog2dHistory.Snapshot()), "quarantined drawing is safe to save and load");
            }
            finally
            {
                Fog2dHistory.Restore(previous);
            }
        }

        private static Fog2dKnownProp Known()
        {
            return Fog2dHistory.Snapshot().Maps["fog_probe"].Props["fog_probe/door"];
        }
    }
}
