namespace Cthangover.Mods.Fog2d.Tests
{
    using System;
    using System.Text.Json;
    using Cthangover.Core.Mods.Testing;

    /// <summary>Authoring settings keep all fields and extension metadata, and invalid inputs cannot reach exploration memory.</summary>
    public sealed class Fog2dProfileTests
    {
        private const string Profile = "{\"Id\":\"fog_probe\",\"Enabled\":false,\"Radius\":12,\"RememberedBrightness\":0.45,"
            + "\"OpaqueCells\":[0,4],\"InitialExplored\":[2,24],\"Occluders\":[{\"Object\":\"probe/door\",\"Mode\":\"WhenSolid\","
            + "\"ExtraRule\":{\"keep\":13}}],"
            + "\"ExtraField\":{\"keep\":42}}";

        [ModTest("fog_profile/complete_json_round_trip")]
        private static void RoundTrip(ModTestContext context)
        {
            var original = JsonSerializer.Deserialize<Fog2dProfile>(Profile);
            original.Validate(5, 5);
            var wire = JsonSerializer.Serialize(original);
            var restored = JsonSerializer.Deserialize<Fog2dProfile>(wire);
            restored.Validate(5, 5);
            ModAssert.Equal("fog_probe", restored.ID, "profile uses the RPG map identity");
            ModAssert.Equal("fog2d_profile", Fog2dProfile.ResourceKind, "resource kind retains its stable wire identity");
            ModAssert.False(restored.Enabled, "explicit disable survives");
            ModAssert.Equal(12, restored.Radius, "sight radius survives");
            ModAssert.True(restored.RememberedBrightness == 0.45f, "remembered light multiplier survives exactly");
            ModAssert.Equal(4, restored.OpaqueCells[1], "painted opacity survives");
            ModAssert.Equal(24, restored.InitialExplored[1], "initial exploration survives");
            ModAssert.Equal("probe/door", restored.Occluders[0].Object, "object policy identity survives");
            ModAssert.True(restored.Occluders[0].Mode == Fog2dOpacity.WhenSolid, "door policy survives");
            ModAssert.Equal(13, restored.Occluders[0].Extra["ExtraRule"].GetProperty("keep").GetInt32(),
                "rare rule annotations survive typed copies and foreign patches");
            ModAssert.Equal(42, restored.Extra["ExtraField"].GetProperty("keep").GetInt32(), "rare extension data survives");
            ModAssert.Equal(wire, JsonSerializer.Serialize(restored), "all serialized fields survive the complete cycle");
        }

        [ModTest("fog_profile/rejects_limits_cells_and_ambiguous_rules")]
        private static void Invalid(ModTestContext context)
        {
            for (var scenario = 0; scenario < 11; scenario++)
            {
                var profile = JsonSerializer.Deserialize<Fog2dProfile>(Profile);
                switch (scenario)
                {
                    case 0:
                        profile.ID = "../probe";
                        break;
                    case 1:
                        profile.Radius = 0;
                        break;
                    case 2:
                        profile.Radius = 65;
                        break;
                    case 3:
                        profile.RememberedBrightness = float.NaN;
                        break;
                    case 4:
                        profile.RememberedBrightness = 1.01f;
                        break;
                    case 5:
                        profile.OpaqueCells[0] = -1;
                        break;
                    case 6:
                        profile.InitialExplored[0] = 25;
                        break;
                    case 7:
                        profile.Occluders.Add(new Fog2dOccluder { Object = "PROBE/DOOR" });
                        break;
                    case 8:
                        profile.Occluders[0].Mode = (Fog2dOpacity)99;
                        break;
                    case 9:
                        profile.Occluders = null;
                        break;
                    case 10:
                        profile.InitialExplored = null;
                        break;
                }

                var rejected = false;
                try
                {
                    profile.Validate(5, 5);
                }
                catch (ArgumentException)
                {
                    rejected = true;
                }

                ModAssert.True(rejected, "invalid profile rejected: " + scenario);
            }
        }
    }
}
