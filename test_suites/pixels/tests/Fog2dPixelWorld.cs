namespace Cthangover.Mods.Fog2d.Tests.Pixels
{
    using System;
    using System.Threading.Tasks;
    using Cthangover.Core.Mods.Testing;
    using Cthangover.Core.Utils;
    using Cthangover.Mods.Interface.View;
    using Cthangover.Mods.TileRender;
    using Godot;

    /// <summary>
    /// Owns a normal-renderer viewport and synthetic borrowed artwork; disposal detaches material texture references first.
    /// </summary>
    internal sealed class Fog2dPixelWorld : IDisposable
    {
        internal SubViewport Viewport { get; private set; }

        internal ITileWorldPresentation View { get; private set; }

        internal ImageTexture Artwork { get; private set; }

        internal ImageTexture Mask { get; private set; }

        internal TileRenderScene Scene { get; private set; }

        internal Fog2dPixelWorld(ModTestContext context, string renderer, bool upright)
        {
            ModAssert.True(GameEnvironment.IsRuntime, "pixel contracts require the native runtime");
            ModAssert.False(DisplayServer.GetName() == "headless", "pixel contracts require a windowed launch");
            try
            {
                Build(context, renderer, upright);
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        private void Build(ModTestContext context, string renderer, bool upright)
        {
            Viewport = new SubViewport
            {
                Size = new Vector2I(256, 256),
                Disable3D = true,
                TransparentBg = true,
                UseHdr2D = ((SceneTree)Godot.Engine.GetMainLoop()).Root.UseHdr2D,
                RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            };
            ((SceneTree)Godot.Engine.GetMainLoop()).Root.AddChild(Viewport);
            using (var image = Image.CreateEmpty(16, 16, false, Image.Format.Rgba8))
            {
                image.Fill(Colors.White);
                Artwork = ImageTexture.CreateFromImage(image);
            }

            Scene = new TileRenderScene
            {
                Size = new Vector2I(4, 3),
                TileSize = 32,
                Zoom = 1,
            };
            Scene.Visuals.Add(new TileVisual
            {
                Id = "probe/art",
                Texture = Artwork,
                Source = new Rect2(0, 0, 16, 16),
                Bounds = upright ? new Rect2(0, 0, 1, 2) : new Rect2(0, 0, 4, 3),
                Upright = upright,
                Anchor = new Vector2(0.5f, 2),
                VisibilityCell = upright ? new Vector2(0.5f, 1.5f) : null,
            });
            View = TileRendererRegistry.Instance.Resolve(renderer).Create();
            Viewport.AddChild(View.Root);
            View.Mount(Scene);
            View.Resize(new Vector2(256, 256));
            View.Present(new TileFrame
            {
                Focus = new Vector2(2, 1.5f),
                Ambient = Colors.White,
            }, 0);
        }

        internal void SetMask(bool upright, bool enabled)
        {
            View.SharedMaterial.SetShaderParameter("visibility_mask", default(Variant));
            Mask?.Dispose();
            using var image = Image.CreateEmpty(4, 3, false, Image.Format.Rg8);
            image.Fill(new Color(0, 0, 0));
            if (upright)
            {
                image.SetPixel(0, 1, new Color(1, 1, 0));
            }
            else
            {
                for (var y = 0; y < 3; y++)
                {
                    image.SetPixel(0, y, new Color(1, 1, 0));
                    image.SetPixel(1, y, new Color(1, 0, 0));
                }
            }

            Mask = ImageTexture.CreateFromImage(image);
            View.SharedMaterial.SetShaderParameter("visibility_mask", Mask);
            View.SharedMaterial.SetShaderParameter("visibility_size", new Vector2(4, 3));
            View.SharedMaterial.SetShaderParameter("remembered_brightness", 0.25f);
            View.SharedMaterial.SetShaderParameter("use_visibility_mask", enabled);
        }

        /// <summary>Transfers an sRGB capture to the caller, which must dispose the returned image.</summary>
        internal async Task<Image> Capture()
        {
            await Viewport.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            await Viewport.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            return ViewportCapture.Take(Viewport);
        }

        internal Vector2I CellPixel(float x, float y)
        {
            return (Vector2I)View.ProjectToView(new Vector3(x, y, 0));
        }

        internal void MountWallQuad()
        {
            Scene.Visuals.Clear();
            Scene.Visuals.Add(new TileVisual
            {
                Id = "probe/wall_quad",
                Texture = Artwork,
                Source = new Rect2(8, 0, 8, 16),
                Bounds = new Rect2(0, -1, 3, 2),
                DrawOrder = TileDrawOrder.ByPosition,
                WorldQuad = new[]
                {
                    new Vector3(0, 1, 2),
                    new Vector3(3, 1, 2),
                    new Vector3(3, 1, 0),
                    new Vector3(0, 1, 0),
                },
            });
            View.Mount(Scene);
            View.Resize(new Vector2(256, 256));
            View.Present(new TileFrame
            {
                Focus = new Vector2(2, 1.5f),
                Ambient = Colors.White,
            }, 0);
        }

        /// <summary>Releases the renderer before its borrowed image and mask; the detached viewport is always freed.</summary>
        public void Dispose()
        {
            if (View?.SharedMaterial != null)
            {
                View.SharedMaterial.SetShaderParameter("use_visibility_mask", false);
                View.SharedMaterial.SetShaderParameter("visibility_mask", default(Variant));
            }

            View?.Dispose();
            Viewport?.Free();
            Mask?.Dispose();
            Artwork?.Dispose();
            Scene?.Release();
        }
    }
}
