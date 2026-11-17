namespace Cthangover.Mods.Fog2d.Tests.Pixels
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text.Json;
    using System.Text.Json.Nodes;
    using System.Threading;
    using Cthangover.Core.Mods;
    using Cthangover.Core.Mods.Resolvers;
    using Cthangover.Core.Mods.Testing;
    using Cthangover.Core.Scenes;
    using Cthangover.Core.Utils;
    using Cthangover.Mods.Rpg;
    using Cthangover.Mods.Rpg.Model;
    using Cthangover.Mods.Rpg.Play;
    using Cthangover.Mods.Exploration;
    using Cthangover.Mods.TileRender;
    using Godot;

    /// <summary>Actual RPG views apply replacement frames on first mount and renderer swaps without mutating cached world poses.</summary>
    public sealed class Fog2dFrameTests : IModFileProvider
    {
        private readonly IModFileProvider source;
        private readonly HashSet<string> hidden = new(StringComparer.Ordinal);
        private RpgWorldView target;
        private TileFrame originalFrame;
        private TileFrame expectedFrame;
        private TileActorPose[] originalPoses;
        private string expectedMap;
        private int frameChecks;
        private int missingCatalogueErrors;

        private Fog2dFrameTests()
        {
        }

        private Fog2dFrameTests(IModFileProvider source, ModManifest manifest)
        {
            this.source = source;
            foreach (var path in EntityResolver.JsonPathsOf(source, manifest))
            {
                var text = ModManager.Instance.ReadResolvedText(source.Mod, path, source);
                if (text == null)
                {
                    continue;
                }

                try
                {
                    using var document = JsonDocument.Parse(text);
                    if (document.RootElement.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    foreach (var property in document.RootElement.EnumerateObject())
                    {
                        if (property.Name.Equals("Kind", StringComparison.OrdinalIgnoreCase)
                            && property.Value.ValueKind == JsonValueKind.String && property.Value.GetString() == "fog2d_profile")
                        {
                            hidden.Add(path);
                        }
                    }
                }
                catch (JsonException)
                {
                    // Unrelated malformed content remains visible to its normal resolver and diagnostics.
                }
            }
        }

        /// <summary>Exercises a genuinely absent profile catalogue, not merely an unmatched map inside a populated catalogue.</summary>
        [ModTest("fog_pixels/empty_profile_catalogue_preserves_original_frame", TimeoutSeconds = 60)]
        private static void EmptyCatalogue(ModTestContext context)
        {
            ModAssert.True(GameEnvironment.IsRuntime, "empty catalogue contract uses actual views and materials");
            var history = Fog2dHistory.Snapshot();
            var exploration = ExplorationMemory.Snapshot();
            var profiles = JsonSerializer.SerializeToNode(Entities.Collect<Fog2dProfile>(required: false));
            var providers = new Dictionary<ModInfo, IModFileProvider>();
            var observer = new Fog2dFrameTests();
            var lifecycle = new Fog2dInitializer();
            RpgWorldView world = null;
            Node dialog = null;
            GameLogger.LogEmitted += observer.ObserveError;
            try
            {
                lifecycle.OnModUnloading();
                foreach (var mod in ModManager.Instance.Mods.Values)
                {
                    ModAssert.True(mod is ModInfo, "native registry exposes restorable provider handles");
                    var info = (ModInfo)mod;
                    var replacement = new Fog2dFrameTests(info.FileProvider, info.Manifest);
                    providers.Add(info, info.FileProvider);
                    info.FileProvider = replacement;
                }

                ClearProviderCaches();
                ModAssert.Equal(0, Entities.Collect<Fog2dProfile>(required: false).Count, "provider-backed catalogue is truly empty");
                RpgWorldView.FrameFiltering += observer.BeforeFrame;
                lifecycle.OnModResourcesReady();
                RpgWorldView.FrameFiltering += observer.AfterFrame;
                var root = ((SceneTree)Godot.Engine.GetMainLoop()).Root;
                if (root.FindChild("DialogBox", true, false) == null)
                {
                    dialog = TscnScenes.LoadAndBuild("scenes/ui/dialog_box.tscn");
                    root.AddChild(dialog);
                }

                var map = new RpgMap
                {
                    ID = "empty_fog_catalogue",
                    Width = 3,
                    Height = 3
                };
                var session = new RpgWorldSession(map, ObjectOf, Array.Empty<RpgPlacement>(), Evaluate);
                observer.expectedMap = JsonSerializer.Serialize(map);
                world = new RpgWorldView();
                observer.target = world;
                root.AddChild(world);
                world.Build(session);
                var renderers = new[] { "topdown", "isometric" };
                foreach (var renderer in renderers)
                {
                    var before = observer.frameChecks;
                    ModAssert.True(world.SetRenderer(renderer), "renderer accepts the unconfigured map: " + renderer);
                    world.Present(0);
                    ModAssert.Equal(before + 2, observer.frameChecks, "first and ordinary frames are both checked");
                    ModAssert.True(ReferenceEquals(session, world.Session), "fog absence preserves the actual simulation");
                }

                ModAssert.Equal(0, observer.missingCatalogueErrors, "optional absence emits no missing-catalogue ERROR");
            }
            finally
            {
                try
                {
                    world?.Free();
                    dialog?.Free();
                }
                finally
                {
                    try
                    {
                        lifecycle.OnModUnloading();
                    }
                    finally
                    {
                        RpgWorldView.FrameFiltering -= observer.BeforeFrame;
                        RpgWorldView.FrameFiltering -= observer.AfterFrame;
                        foreach (var pair in providers)
                        {
                            pair.Key.FileProvider = pair.Value;
                        }

                        try
                        {
                            ClearProviderCaches();
                            Fog2dHistory.Restore(history);
                            ExplorationMemory.Restore(exploration);
                            lifecycle.OnModResourcesReady();
                        }
                        finally
                        {
                            GameLogger.LogEmitted -= observer.ObserveError;
                        }
                    }
                }
            }

            foreach (var pair in providers)
            {
                ModAssert.True(ReferenceEquals(pair.Key.FileProvider, pair.Value), "the exact borrowed provider is restored");
            }

            ModAssert.True(JsonNode.DeepEquals(profiles,
                JsonSerializer.SerializeToNode(Entities.Collect<Fog2dProfile>(required: false))), "restored catalogue has its exact data");
            context.Log("empty catalogue preserves original frame, poses, map and disabled mask in both projections");
        }

        private static void ClearProviderCaches()
        {
            ModManager.Instance.GetResolver<EntityResolver>().ClearCache();
            ModManager.Instance.GetResolver<PatchResolver>()?.ClearCache();
            ModManager.Instance.GetResolver<FileListResolver>()?.ClearCache();
        }

        private void ObserveError(string category, string message, LogLevel level)
        {
            if (level == LogLevel.Error && message.Contains("Fog2dProfile", StringComparison.Ordinal)
                && message.Contains("no data found", StringComparison.Ordinal))
            {
                Interlocked.Increment(ref missingCatalogueErrors);
            }
        }

        private void BeforeFrame(RpgWorldView view, ITileWorldPresentation presentation, ref TileFrame frame)
        {
            if (!ReferenceEquals(view, target))
            {
                return;
            }

            originalFrame = frame;
            originalPoses = frame.Actors.ToArray();
            expectedFrame = new TileFrame
            {
                Focus = frame.Focus,
                Ambient = frame.Ambient,
                TimeWeights = frame.TimeWeights,
                CameraOnArtwork = frame.CameraOnArtwork,
                PortableLight = frame.PortableLight,
                InteractionCell = frame.InteractionCell
            };
            foreach (var pose in frame.Actors)
            {
                expectedFrame.Actors.Add(new TileActorPose
                {
                    Id = pose.Id,
                    Position = pose.Position,
                    Visible = pose.Visible,
                    Look = pose.Look,
                    Charset = pose.Charset,
                    Facing = pose.Facing,
                    Column = pose.Column,
                    Tint = pose.Tint
                });
            }

            expectedFrame.Lights.AddRange(frame.Lights);
        }

        private void AfterFrame(RpgWorldView view, ITileWorldPresentation presentation, ref TileFrame frame)
        {
            if (!ReferenceEquals(view, target))
            {
                return;
            }

            ModAssert.True(ReferenceEquals(originalFrame, frame), "optional fog keeps the original source frame identity");
            ModAssert.Equal(expectedMap, JsonSerializer.Serialize(view.Map), "map data remains unchanged");
            ModAssert.True(Fog2dWorld.MemoryOf(view) == null, "no exploration handle is allocated without a profile");
            ModAssert.False(presentation.SharedMaterial.GetShaderParameter("use_visibility_mask").AsBool(), "mask remains disabled");
            ModAssert.True(frame.Focus == expectedFrame.Focus && frame.Ambient == expectedFrame.Ambient
                && frame.TimeWeights == expectedFrame.TimeWeights && frame.CameraOnArtwork == expectedFrame.CameraOnArtwork
                && frame.InteractionCell == expectedFrame.InteractionCell
                && ReferenceEquals(frame.PortableLight, expectedFrame.PortableLight), "frame presentation values stay unchanged");
            ModAssert.Equal(expectedFrame.Actors.Count, frame.Actors.Count, "actor count stays unchanged");
            ModAssert.Equal(expectedFrame.Lights.Count, frame.Lights.Count, "light count stays unchanged");
            for (var i = 0; i < frame.Actors.Count; i++)
            {
                var pose = frame.Actors[i];
                var expected = expectedFrame.Actors[i];
                ModAssert.True(ReferenceEquals(originalPoses[i], pose) && ReferenceEquals(expected.Look, pose.Look)
                    && pose.Id == expected.Id && pose.Position == expected.Position && pose.Visible == expected.Visible
                    && pose.Charset == expected.Charset && pose.Facing == expected.Facing && pose.Column == expected.Column
                    && pose.Tint == expected.Tint, "original pose ownership and values remain unchanged");
            }

            for (var i = 0; i < frame.Lights.Count; i++)
            {
                ModAssert.True(ReferenceEquals(expectedFrame.Lights[i], frame.Lights[i]), "light ownership stays borrowed");
            }

            frameChecks++;
        }

        /// <summary>Retains the original mod identity while filtering only profile envelopes.</summary>
        public string Mod => source.Mod;

        /// <summary>Uses the borrowed provider root without creating a writable fixture tree.</summary>
        public string RootPath => source.RootPath;

        /// <summary>Hides profile envelopes consistently with enumeration and reads.</summary>
        public bool FileExists(string path)
        {
            return !hidden.Contains(path) && source.FileExists(path);
        }

        /// <summary>Excludes profile envelopes from resolver discovery while preserving all other paths.</summary>
        public IEnumerable<string> ListFiles(string directory = "")
        {
            foreach (var path in source.ListFiles(directory))
            {
                if (!hidden.Contains(path))
                {
                    yield return path;
                }
            }
        }

        /// <summary>Prevents direct text reads from bypassing the empty catalogue fixture.</summary>
        public string ReadFileText(string path)
        {
            return hidden.Contains(path) ? null : source.ReadFileText(path);
        }

        /// <summary>Preserves binary resources and excludes the same profile envelopes as text reads.</summary>
        public byte[] ReadFileBinary(string path)
        {
            return hidden.Contains(path) ? null : source.ReadFileBinary(path);
        }

        /// <summary>Returns the original owned stream only for resources visible through this fixture.</summary>
        public Stream OpenStream(string path)
        {
            return hidden.Contains(path) ? null : source.OpenStream(path);
        }

        /// <summary>Prevents physical-path access to envelopes hidden from the resolver.</summary>
        public string GetFileSystemPath(string path)
        {
            return hidden.Contains(path) ? null : source.GetFileSystemPath(path);
        }

        /// <summary>Retains the original include semantics for all visible resources.</summary>
        public string ResolveIncludePath(string path, string current)
        {
            return source.ResolveIncludePath(path, current);
        }

        /// <summary>The scope borrows original providers; wrapper disposal must never close another mod's archive.</summary>
        public void Dispose()
        {
        }

        [ModTest("fog_pixels/first_frame_renderer_swap_and_cached_pose_ownership")]
        private static void Frames(ModTestContext context)
        {
            ModAssert.True(GameEnvironment.IsRuntime, "presentation filters require the actual runtime");
            var world = new RpgWorldView();
            var probe = new Fog2dFrameProbe { Target = world };
            Node ownedDialog = null;
            RpgWorldView.FrameFiltering += probe.Filter;
            try
            {
                var root = ((SceneTree)Godot.Engine.GetMainLoop()).Root;
                if (root.FindChild("DialogBox", true, false) == null)
                {
                    ownedDialog = TscnScenes.LoadAndBuild("scenes/ui/dialog_box.tscn");
                    root.AddChild(ownedDialog);
                }

                root.AddChild(world);
                var session = new RpgWorldSession(new RpgMap
                {
                    ID = "frame_probe",
                    Width = 3,
                    Height = 3,
                }, ObjectOf, Array.Empty<RpgPlacement>(), Evaluate);
                world.Build(session);
                ModAssert.True(Fog2dWorld.MemoryOf(world) == null, "a map without a profile does not borrow any other view's memory");
                ModAssert.Equal(1, probe.Calls, "the first renderer frame passes through the filter");
                CheckPose(probe);
                ModAssert.True(world.SetRenderer("isometric"), "a replacement renderer mounts");
                ModAssert.Equal(2, probe.Calls, "the replacement's first frame also passes through the filter");
                CheckPose(probe);
                world.Present(0);
                ModAssert.Equal(3, probe.Calls, "ordinary presentation uses the same contract");
                CheckPose(probe);
                ModAssert.True(ReferenceEquals(session, world.Session), "frame replacement never replaces gameplay simulation");
            }
            finally
            {
                RpgWorldView.FrameFiltering -= probe.Filter;
                world.Free();
                ownedDialog?.Free();
            }
        }

        private static void CheckPose(Fog2dFrameProbe probe)
        {
            ModAssert.True(probe.Original.Visible, "hiding the submitted pose does not poison the world's cached player");
            ModAssert.False(probe.Selected.Visible, "adapter can submit its own evaluated visibility");
            ModAssert.False(ReferenceEquals(probe.Original, probe.Selected), "selected and cached poses have distinct ownership");
            ModAssert.True(ReferenceEquals(probe.Original.Look, probe.Selected.Look), "unchanged image resources remain borrowed");
        }

        private static RpgObject ObjectOf(string id)
        {
            return null;
        }

        private static bool Evaluate(string condition, string scope)
        {
            return false;
        }
    }
}
