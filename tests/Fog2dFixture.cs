namespace Cthangover.Mods.Fog2d.Tests
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using Cthangover.Mods.Exploration;
    using Cthangover.Mods.Rpg;
    using Cthangover.Mods.Rpg.Model;
    using Cthangover.Mods.Rpg.Play;
    using Godot;

    /// <summary>Detached world and restored common memory; native renderers, files and actual game flags remain untouched.</summary>
    internal sealed class Fog2dFixture : IDisposable
    {
        private readonly ExplorationState previous = ExplorationMemory.Snapshot();

        private readonly Dictionary<string, RpgObject> objects = new();

        internal bool OpenDoor { get; set; }

        internal RpgWorldSession Session { get; }

        internal Fog2dProfile Profile { get; }

        internal Fog2dFixture(int width = 7, int height = 5, int npcCount = 0)
        {
            var wall = new RpgObject
            {
                ID = "probe/wall",
                Form = RpgObjectForm.Segment,
            };
            objects.Add(wall.ID, wall);
            var door = JsonSerializer.Deserialize<RpgObject>("{\"Id\":\"probe/door\",\"Form\":\"sprite\",\"Solid\":true,"
                + "\"Parts\":[],\"States\":[{\"When\":\"door_open\",\"Solid\":false}]}");
            objects.Add(door.ID, door);
            objects.Add("probe/npc", new RpgObject
            {
                ID = "probe/npc",
                Form = RpgObjectForm.Actor,
            });
            var map = new RpgMap
            {
                ID = "fog_probe",
                Width = width,
                Height = height,
                Legend = new Dictionary<string, string>
                {
                    ["w"] = wall.ID,
                },
            };
            for (var y = 0; y < height; y++)
            {
                var row = new string('.', width).ToCharArray();
                if (y != 2)
                {
                    row[3] = 'w';
                }

                map.Structure.Add(new string(row));
            }

            var placements = new List<RpgPlacement>
            {
                new RpgPlacement
                {
                    ID = "fog_probe/door",
                    Object = door.ID,
                    X = 3,
                    Y = 2,
                },
            };
            for (var i = 0; i < npcCount; i++)
            {
                placements.Add(new RpgPlacement
                {
                    ID = "fog_probe/npc_" + i,
                    Object = "probe/npc",
                    X = 10 + i,
                    Y = 10,
                });
            }

            Session = new RpgWorldSession(map, ObjectOf, placements, Evaluate);
            Session.Relocate(new Vector2I(1, 2), RpgDirection.Right);
            Profile = new Fog2dProfile
            {
                ID = map.ID,
                Radius = 6,
                InitialExplored = new List<int> { 0 },
                Occluders = new List<Fog2dOccluder>
                {
                    new Fog2dOccluder { Object = door.ID },
                },
            };
            ExplorationMemory.Reset();
        }

        /// <summary>Restores the previous game's persistent envelope even after a failed assertion.</summary>
        public void Dispose()
        {
            ExplorationMemory.Restore(previous);
        }

        private RpgObject ObjectOf(string id)
        {
            return objects.TryGetValue(id, out var value) ? value : null;
        }

        private bool Evaluate(string condition, string scope)
        {
            return condition == "door_open" && OpenDoor;
        }
    }
}
