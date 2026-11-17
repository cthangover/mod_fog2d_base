namespace Cthangover.Mods.Fog2d.Tests.Pixels
{
    using System.Threading.Tasks;
    using Cthangover.Core.Mods.Testing;
    using Cthangover.Mods.TileRender;
    using Godot;

    /// <summary>
    /// Rendered pixels verify opt-in visibility and upright sampling while preserving the normal HDR/sRGB capture pipeline.
    /// </summary>
    public sealed class Fog2dMaterialTests
    {
        [ModTest("fog_pixels/authored_rgb_alpha_and_tint_match_default_canvas", TimeoutSeconds = 60)]
        private static async Task ColorPipeline(ModTestContext context)
        {
            var renderers = new[] { "topdown", "isometric" };
            foreach (var renderer in renderers)
            {
                for (var scenario = 0; scenario < 4; scenario++)
                {
                    using var world = new Fog2dPixelWorld(context, renderer, false);
                    world.Viewport.TransparentBg = false;
                    using (var image = Image.CreateEmpty(16, 16, false, Image.Format.Rgba8))
                    {
                        image.Fill(new Color(80f / 255, 180f / 255, 80f / 255, scenario % 2 == 0 ? 1 : 128f / 255));
                        world.Artwork.Update(image);
                    }

                    var look = world.Scene.Visuals[0];
                    look.Tint = scenario % 2 == 0 ? Colors.White : new Color(0.6f, 0.8f, 0.5f, 0.75f);
                    if (scenario >= 2)
                    {
                        world.Scene.Visuals.Clear();
                    }

                    world.View.Mount(world.Scene);
                    var frame = new TileFrame
                    {
                        Focus = new Vector2(2, 1.5f),
                        Ambient = Colors.White
                    };
                    if (scenario >= 2)
                    {
                        frame.Actors.Add(new TileActorPose
                        {
                            Id = "color_probe",
                            Look = look,
                            Position = Vector2.Zero,
                            Visible = true
                        });
                    }

                    world.View.Present(frame, 0);
                    var point = world.CellPixel(0.5f, 1.5f);
                    var shader = world.View.SharedMaterial.Shader;
                    try
                    {
                        world.View.SharedMaterial.Shader = null;
                        using var baseline = await world.Capture();
                        world.View.SharedMaterial.Shader = shader;
                        using var lit = await world.Capture();
                        var expected = baseline.GetPixel(point.X, point.Y);
                        var actual = lit.GetPixel(point.X, point.Y);
                        using var raw = world.Viewport.GetTexture().GetImage();
                        context.Log(renderer + " scenario=" + scenario + " hdr=" + world.Viewport.UseHdr2D
                            + " renderer=" + RenderingServer.GetCurrentRenderingMethod() + " raw_format=" + raw.GetFormat()
                            + " raw=" + raw.GetPixel(point.X, point.Y) + " display=" + actual + " default_canvas=" + expected);
                        ModAssert.True(actual == expected,
                            renderer + " authored RGBA/tint is applied exactly once, for static and moving artwork: " + actual);
                        if (scenario == 0)
                        {
                            ModAssert.True(System.Math.Abs(expected.R - 80f / 255) < 0.01f
                                && System.Math.Abs(expected.G - 180f / 255) < 0.01f
                                && System.Math.Abs(expected.B - 80f / 255) < 0.01f,
                                "normal HDR capture preserves the opaque source PNG colour");
                        }
                    }
                    finally
                    {
                        world.View.SharedMaterial.Shader = shader;
                    }
                }
            }
        }

        [ModTest("fog_pixels/default_visible_remembered_and_unexplored", TimeoutSeconds = 60)]
        private static async Task Mask(ModTestContext context)
        {
            var renderers = new[] { "topdown", "isometric" };
            foreach (var renderer in renderers)
            {
                using var world = new Fog2dPixelWorld(context, renderer, false);
                using var before = await world.Capture();
                var visible = world.CellPixel(0.5f, 1.5f);
                var remembered = world.CellPixel(1.5f, 1.5f);
                var unknown = world.CellPixel(2.5f, 1.5f);
                ModAssert.True(before.GetPixel(unknown.X, unknown.Y).R > 0.95f, "without a profile ordinary artwork remains bright");
                world.SetMask(false, true);
                using var masked = await world.Capture();
                var current = masked.GetPixel(visible.X, visible.Y);
                var past = masked.GetPixel(remembered.X, remembered.Y);
                var hidden = masked.GetPixel(unknown.X, unknown.Y);
                ModAssert.True(current.R > 0.95f, renderer + " current visibility keeps ordinary daylight");
                ModAssert.True(past.R > 0.45f && past.R < 0.7f, renderer + " linear quarter brightness is correctly captured as sRGB");
                ModAssert.True(hidden.R < 0.02f && hidden.A > 0.98f, renderer + " unknown artwork is concealed by opaque black");
                world.View.SharedMaterial.SetShaderParameter("use_visibility_mask", false);
                using var restored = await world.Capture();
                ModAssert.True(restored.GetPixel(unknown.X, unknown.Y).IsEqualApprox(before.GetPixel(unknown.X, unknown.Y)),
                    "disabling the optional mask restores the exact original pixel in " + renderer);
                context.Log(renderer + " visible=" + current + "; remembered=" + past + "; unknown=" + hidden);
            }
        }

        [ModTest("fog_pixels/upright_head_samples_its_foot_cell", TimeoutSeconds = 60)]
        private static async Task Upright(ModTestContext context)
        {
            var renderers = new[] { "topdown", "isometric" };
            foreach (var renderer in renderers)
            {
                using var world = new Fog2dPixelWorld(context, renderer, true);
                world.SetMask(true, true);
                var placement = TileRendererRegistry.Instance.Resolve(renderer).Projection(32).Place(world.Scene.Visuals[0]);
                var point = (Vector2I)world.View.Root.ToGlobal(placement.Transform * new Vector2(0.5f, 0.25f));
                using var shown = await world.Capture();
                ModAssert.True(shown.GetPixel(point.X, point.Y).R > 0.95f,
                    "upper artwork remains visible even though the ground cells behind the head are unexplored: " + renderer);
                using var mask = world.Mask.GetImage();
                mask.SetPixel(0, 1, new Color(1, 0, 0));
                world.Mask.Update(mask);
                using var remembered = await world.Capture();
                var dim = remembered.GetPixel(point.X, point.Y);
                ModAssert.True(dim.R > 0.45f && dim.R < 0.7f, "the whole upright image follows remembered foot visibility: " + renderer);
                mask.SetPixel(0, 1, new Color(0, 0, 0));
                world.Mask.Update(mask);
                using var hidden = await world.Capture();
                ModAssert.True(hidden.GetPixel(point.X, point.Y).R < 0.02f,
                    "the whole upright image is concealed when its foot cell is unknown: " + renderer);
            }
        }

        [ModTest("fog_pixels/vertical_quad_uses_ground_xy_instead_of_projected_height", TimeoutSeconds = 60)]
        private static async Task Quad(ModTestContext context)
        {
            var renderers = new[] { "topdown", "isometric" };
            foreach (var renderer in renderers)
            {
                using var world = new Fog2dPixelWorld(context, renderer, false);
                world.MountWallQuad();
                world.SetMask(false, true);
                var placement = TileRendererRegistry.Instance.Resolve(renderer).Projection(32).Place(world.Scene.Visuals[0]);
                var currentPoint = (Vector2I)world.View.Root.ToGlobal(placement.Transform * new Vector2(1f / 6, 0.15f));
                var rememberedPoint = (Vector2I)world.View.Root.ToGlobal(placement.Transform * new Vector2(0.5f, 0.15f));
                var hiddenPoint = (Vector2I)world.View.Root.ToGlobal(placement.Transform * new Vector2(5f / 6, 0.15f));
                using var image = await world.Capture();
                var visible = image.GetPixel(currentPoint.X, currentPoint.Y);
                var remembered = image.GetPixel(rememberedPoint.X, rememberedPoint.Y);
                var hidden = image.GetPixel(hiddenPoint.X, hiddenPoint.Y);
                ModAssert.True(visible.R > 0.95f,
                    renderer + " quad head is visible from its actual ground cell even when projected above the grid: " + visible);
                ModAssert.True(remembered.R > 0.45f && remembered.R < 0.7f,
                    renderer + " a long quad wall keeps separate remembered cells along its width: " + remembered);
                ModAssert.True(hidden.R < 0.02f, renderer + " unknown part of a long wall remains concealed: " + hidden);
            }
        }
    }
}
