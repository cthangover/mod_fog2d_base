namespace Cthangover.Mods.Fog2d
{
    using System;
    using System.Collections.Generic;
    using Cthangover.Core.Scenes;
    using Cthangover.Mods.Exploration;
    using Cthangover.Mods.Rpg;
    using Cthangover.Mods.Rpg.Model;
    using Cthangover.Mods.Rpg.Play;
    using Cthangover.Mods.TileRender;
    using Godot;

    /// <summary>
    /// Owns one view's optional mask and detached frame, while borrowing every artwork texture from that view.
    /// Even ignored profiles have a tree-exit owner so the registry never retains an unloaded map.
    /// </summary>
    internal sealed class Fog2dViewBinding : IDisposable
    {
        private readonly RpgWorldView view;

        private readonly Fog2dProfile profile;

        private readonly TileFrame selected = new();

        private readonly Dictionary<string, TileActorPose> actors = new(StringComparer.Ordinal);

        private readonly Dictionary<string, Fog2dRememberedDrawing> drawings = new(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, string> spriteIds = new(StringComparer.Ordinal);

        private readonly HashSet<string> observedSprites = new(StringComparer.OrdinalIgnoreCase);

        private ImageTexture mask;

        private byte[] maskBytes;

        private ShaderMaterial material;

        private readonly List<ShaderMaterial> boundMaterials = new();

        private ExplorationMap uploadedMemory;

        private long uploadedRevision = -1;

        private bool disposed;

        internal Fog2dObserver Observer { get; private set; }

        internal Fog2dViewBinding(RpgWorldView view, Fog2dProfile profile)
        {
            this.view = view;
            this.profile = profile;
            view.TreeExiting += OnViewExiting;
            try
            {
                if (profile != null)
                {
                    Observer = new Fog2dObserver(view.Session, profile);
                }
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        internal TileFrame Filter(ITileWorldPresentation presentation, TileFrame source)
        {
            if (Observer == null)
            {
                return source;
            }

            // Save restoration can synchronously redraw the outgoing world before a shell transition begins.
            // Its old session must not reveal the restored generation until the saved arrival agrees with it.
            var arrival = RpgState.Instance.Data;
            var outgoing = view.GetParent() is RpgWorldHost && (arrival.Map != view.Map.ID
                || arrival.X != view.Session.Player.Cell.X || arrival.Y != view.Session.Player.Cell.Y);
            if (!Observer.Memory.IsCurrent && (GodotSceneService.IsTransitioning || outgoing))
            {
                if (!profile.Enabled)
                {
                    return source;
                }

                BindMaterial(presentation.SharedMaterial);
                return selected;
            }

            Observer.Update();
            Fog2dHistory.Observe(view.Session, Observer.Memory);
            if (!profile.Enabled)
            {
                return source;
            }

            UpdateMask();
            BindMaterial(presentation.SharedMaterial);
            observedSprites.Clear();
            foreach (var instance in view.Session.Instances)
            {
                if (instance.Current.Form == RpgObjectForm.Sprite && Observed(instance))
                {
                    observedSprites.Add(instance.Id);
                }
            }

            selected.Actors.Clear();
            selected.Lights.Clear();
            selected.Focus = source.Focus;
            selected.Ambient = source.Ambient;
            selected.TimeWeights = source.TimeWeights;
            selected.CameraOnArtwork = source.CameraOnArtwork;
            selected.PortableLight = source.PortableLight;
            selected.InteractionCell = source.InteractionCell is Vector2 cue && Visible(cue) ? cue : null;
            foreach (var light in source.Lights)
            {
                if (Visible(light.Position))
                {
                    selected.Lights.Add(light);
                }
            }

            foreach (var pose in source.Actors)
            {
                var sprite = SpriteId(pose.Id);
                if (sprite != null && !observedSprites.Contains(sprite))
                {
                    continue;
                }

                if (!actors.TryGetValue(pose.Id, out var copy))
                {
                    copy = new TileActorPose();
                    actors.Add(pose.Id, copy);
                }

                copy.Id = pose.Id;
                copy.Look = pose.Look;
                copy.Position = pose.Position;
                copy.Visible = pose.Visible && (sprite != null || Visible(pose.Position + Vector2.One / 2));
                copy.Charset = pose.Charset;
                copy.Facing = pose.Facing;
                copy.Column = pose.Column;
                copy.Tint = pose.Tint;
                selected.Actors.Add(copy);
            }

            var history = Fog2dHistory.Get(view.Map.ID);
            if (history != null)
            {
                foreach (var row in history.Props)
                {
                    if (observedSprites.Contains(row.Key))
                    {
                        continue;
                    }

                    if (!drawings.TryGetValue(row.Key, out var drawing) || !ReferenceEquals(drawing.Source, row.Value))
                    {
                        drawing = new Fog2dRememberedDrawing(view, row.Value);
                        drawings[row.Key] = drawing;
                    }

                    selected.Actors.AddRange(drawing.Poses);
                }
            }

            return selected;
        }

        /// <summary>Detaches the live material before releasing its owned mask; borrowed artwork remains with the RPG view.</summary>
        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            view.TreeExiting -= OnViewExiting;
            foreach (var bound in boundMaterials)
            {
                if (GodotObject.IsInstanceValid(bound))
                {
                    bound.SetShaderParameter("use_visibility_mask", false);
                    bound.SetShaderParameter("visibility_mask", default(Variant));
                }
            }

            boundMaterials.Clear();
            selected.Actors.Clear();
            selected.Lights.Clear();
            actors.Clear();
            drawings.Clear();
            spriteIds.Clear();
            observedSprites.Clear();
            Observer?.Dispose();
            mask?.Dispose();
            mask = null;
            maskBytes = null;
            material = null;
        }

        private void UpdateMask()
        {
            var memory = Observer.Memory;
            if (ReferenceEquals(memory, uploadedMemory) && memory.Revision == uploadedRevision)
            {
                return;
            }

            maskBytes ??= new byte[memory.Width * memory.Height * 2];
            for (var y = 0; y < memory.Height; y++)
            {
                for (var x = 0; x < memory.Width; x++)
                {
                    var offset = (y * memory.Width + x) * 2;
                    maskBytes[offset] = memory.IsExplored(x, y) ? (byte)255 : (byte)0;
                    maskBytes[offset + 1] = memory.IsVisible(x, y) ? (byte)255 : (byte)0;
                }
            }

            using var image = Image.CreateFromData(memory.Width, memory.Height, false, Image.Format.Rg8, maskBytes);
            if (mask == null)
            {
                mask = ImageTexture.CreateFromImage(image);
            }
            else
            {
                mask.Update(image);
            }

            uploadedMemory = memory;
            uploadedRevision = memory.Revision;
        }

        private void BindMaterial(ShaderMaterial target)
        {
            if (ReferenceEquals(material, target))
            {
                return;
            }

            material = target;
            for (var i = boundMaterials.Count - 1; i >= 0; i--)
            {
                if (!GodotObject.IsInstanceValid(boundMaterials[i]))
                {
                    boundMaterials.RemoveAt(i);
                }
            }

            if (!boundMaterials.Contains(target))
            {
                boundMaterials.Add(target);
            }

            material.SetShaderParameter("visibility_mask", mask);
            material.SetShaderParameter("visibility_size", new Vector2(Observer.Memory.Width, Observer.Memory.Height));
            material.SetShaderParameter("remembered_brightness", profile.RememberedBrightness);
            material.SetShaderParameter("use_visibility_mask", true);
        }

        private bool Visible(Vector2 position)
        {
            var x = (int)Math.Floor(position.X);
            var y = (int)Math.Floor(position.Y);
            return x >= 0 && y >= 0 && x < Observer.Memory.Width && y < Observer.Memory.Height
                && Observer.Memory.IsVisible(x, y);
        }

        private string SpriteId(string id)
        {
            if (spriteIds.TryGetValue(id, out var cached))
            {
                return cached;
            }

            if (id?.StartsWith("instance/", StringComparison.Ordinal) != true)
            {
                spriteIds[id] = null;
                return null;
            }

            var separator = id.LastIndexOf('#');
            var key = separator > 9 ? id.Substring(9, separator - 9) : null;
            var instance = key != null ? view.Session.Find(key) : null;
            var result = instance?.Current.Form == RpgObjectForm.Sprite ? key : null;
            spriteIds[id] = result;
            return result;
        }

        private bool Observed(RpgInstance instance)
        {
            var cell = instance.Actor.Cell;
            var right = Math.Min(Observer.Memory.Width, (long)cell.X + instance.Current.Width);
            var bottom = Math.Min(Observer.Memory.Height, (long)cell.Y + instance.Current.Height);
            for (var y = Math.Max(0, cell.Y); y < bottom; y++)
            {
                for (var x = Math.Max(0, cell.X); x < right; x++)
                {
                    if (Observer.Memory.IsVisible(x, y))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private void OnViewExiting()
        {
            Fog2dWorld.Release(view);
        }
    }
}
