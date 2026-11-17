namespace Cthangover.Mods.Fog2d.Tests.Pixels
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using System.Threading.Tasks;
    using Cthangover.Core.Mods.Testing;
    using Cthangover.Core.Scenes;
    using Cthangover.Core.Utils;
    using Cthangover.Mods.Exploration;
    using Cthangover.Mods.Interface.View;
    using Cthangover.Mods.Rpg;
    using Cthangover.Mods.Rpg.Model;
    using Cthangover.Mods.Rpg.Play;
    using Cthangover.Mods.TileRender;
    using Godot;

    /// <summary>Owns a detached-world viewport and restores both independent save owners after actual renderer disposal.</summary>
    internal sealed class Fog2dRuntimeWorld : IDisposable
    {
        private readonly ExplorationState previousExploration = ExplorationMemory.Snapshot();

        private readonly Fog2dHistoryState previousHistory = Fog2dHistory.Snapshot();

        private readonly Dictionary<string, RpgObject> objects = new();

        private Node ownedDialog;

        internal SubViewport Viewport { get; private set; }

        internal RpgWorldView View { get; private set; }

        internal RpgWorldSession Session { get; private set; }

        internal bool OpenDoor { get; set; }

        internal bool HideDoor { get; set; }

        internal RpgObject Floor { get; private set; }

        internal Fog2dRuntimeWorld(int width = 9, int height = 5, int npcCount = 1)
        {
            ModAssert.True(GameEnvironment.IsRuntime, "live world contracts require the native runtime");
            ModAssert.False(DisplayServer.GetName() == "headless", "live world pixels require a windowed launch");
            try
            {
                ExplorationMemory.Reset();
                Fog2dHistory.Reset();
                Build(width, height, npcCount);
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        internal void Present(string renderer = null)
        {
            if (renderer != null)
            {
                ModAssert.True(View.SetRenderer(renderer), "the actual world accepts renderer replacement");
            }

            View.Present(0);
        }

        internal Vector2I CellPixel(float x, float y)
        {
            return (Vector2I)View.Presentation.ProjectToView(new Vector3(x, y, 0));
        }

        internal Vector2I NpcPixel()
        {
            var projection = TileRendererRegistry.Instance.Resolve(View.RendererId).Projection(32);
            var foot = projection.ActorAnchor(new Vector2(0.5f, 1));
            var point = View.Presentation.ProjectToView(new Vector3(6 + foot.X, 2 + foot.Y, 0));
            return (Vector2I)(point - new Vector2(0, 16));
        }

        internal Vector2I DoorPixel()
        {
            var instance = Session.Find("fog_runtime/door");
            var look = View.SpriteLooks(objects["fog2d_probe/door"])[0];
            var anchor = instance.Actor.Position + look.Anchor;
            var point = View.Presentation.ProjectToView(new Vector3(anchor.X, anchor.Y, 0));
            var offset = (look.Bounds.Position - look.Anchor + look.Bounds.Size / 2) * View.TileSize;
            return (Vector2I)(point + offset);
        }

        /// <summary>Transfers an owned display-space image to the caller; native HDR conversion uses the shared helper.</summary>
        internal async Task<Image> Capture()
        {
            await Viewport.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            await Viewport.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            return ViewportCapture.Take(Viewport);
        }

        /// <summary>Releases view/material owners before restoring previous persistent generations.</summary>
        public void Dispose()
        {
            View?.Free();
            View = null;
            Viewport?.Free();
            Viewport = null;
            ownedDialog?.Free();
            ownedDialog = null;
            ExplorationMemory.Restore(previousExploration);
            Fog2dHistory.Restore(previousHistory);
        }

        private void Build(int width, int height, int npcCount)
        {
            var root = ((SceneTree)Godot.Engine.GetMainLoop()).Root;
            if (root.FindChild("DialogBox", true, false) == null)
            {
                ownedDialog = TscnScenes.LoadAndBuild("scenes/ui/dialog_box.tscn");
                root.AddChild(ownedDialog);
            }

            Viewport = new SubViewport
            {
                Size = new Vector2I(512, 320),
                Disable3D = true,
                TransparentBg = true,
                UseHdr2D = root.UseHdr2D,
                RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            };
            root.AddChild(Viewport);
            var wall = new RpgObject
            {
                ID = "fog2d_probe/wall",
                Form = RpgObjectForm.Segment,
                Segment = new RpgSegment { Image = "fog2d_probe/wall" },
            };
            Floor = JsonSerializer.Deserialize<RpgObject>("{\"Id\":\"fog2d_probe/floor\",\"Form\":\"sprite\",\"Solid\":false,"
                + "\"Footprint\":{\"Size\":[9,5]},\"Parts\":[{\"Id\":\"base\",\"Image\":\"fog2d_probe/floor\","
                + "\"Geometry\":\"ground\",\"Size\":[9,5]}]}");
            var door = JsonSerializer.Deserialize<RpgObject>("{\"Id\":\"fog2d_probe/door\",\"Form\":\"sprite\",\"Solid\":true,"
                + "\"Parts\":[{\"Id\":\"door\",\"Image\":\"fog2d_probe/closed\",\"Size\":[1,1]}],"
                + "\"States\":[{\"When\":\"door_open\",\"Solid\":false,\"Parts\":[{\"Id\":\"door\","
                + "\"Image\":\"fog2d_probe/open\",\"Size\":[1,1]}]},"
                + "{\"When\":\"door_hidden\",\"Hidden\":true,\"Parts\":[]}]}");
            var npc = new RpgObject
            {
                ID = "fog2d_probe/npc",
                Form = RpgObjectForm.Actor,
                Charset = "fog2d_probe/npc",
                Route = new RpgMoveRoute(),
            };
            objects.Add(wall.ID, wall);
            objects.Add(Floor.ID, Floor);
            objects.Add(door.ID, door);
            objects.Add(npc.ID, npc);
            var map = new RpgMap
            {
                ID = "fog_runtime",
                Width = width,
                Height = height,
                TileSize = 32,
                Zoom = 1,
                LightMode = "day",
                Legend = new Dictionary<string, string> { ["w"] = wall.ID },
            };
            Floor.Parts[0].Size = new float[]
            {
                width,
                height,
            };
            Floor.Footprint.Size = new[]
            {
                width,
                height,
            };
            for (var y = 0; y < height; y++)
            {
                var row = new string('.', width).ToCharArray();
                if (y != 2)
                {
                    row[4] = 'w';
                }

                map.Structure.Add(new string(row));
            }

            var placements = new List<RpgPlacement>
            {
                new RpgPlacement
                {
                    ID = "fog_runtime/floor",
                    Object = Floor.ID,
                },
                new RpgPlacement
                {
                    ID = "fog_runtime/door",
                    Object = door.ID,
                    X = 4,
                    Y = 2,
                },
            };
            for (var i = 0; i < npcCount; i++)
            {
                placements.Add(new RpgPlacement
                {
                    ID = i == 0 ? "fog_runtime/npc" : "fog_runtime/npc_" + i,
                    Object = npc.ID,
                    X = 6 + i,
                    Y = 2,
                });
            }

            Session = new RpgWorldSession(map, ObjectOf, placements, Evaluate);
            Session.Relocate(new Vector2I(2, 2), RpgDirection.Right);
            View = new RpgWorldView();
            Viewport.AddChild(View);
            View.Build(Session);
            ModAssert.NotNull(View.Presentation, "profile wiring permits the world's first renderer frame");
        }

        private RpgObject ObjectOf(string id)
        {
            return objects.TryGetValue(id, out var value) ? value : null;
        }

        private bool Evaluate(string condition, string scope)
        {
            return condition == "door_open" && OpenDoor || condition == "door_hidden" && HideDoor;
        }
    }
}
