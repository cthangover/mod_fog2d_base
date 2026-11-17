namespace Cthangover.Mods.Fog2d
{
    using System.Collections.Generic;
    using Cthangover.Mods.Rpg;
    using Cthangover.Mods.TileRender;
    using Godot;

    /// <summary>Reusable poses borrow the world's existing texture cache; a changed history record replaces only this drawing.</summary>
    internal sealed class Fog2dRememberedDrawing
    {
        internal Fog2dKnownProp Source { get; }

        internal List<TileActorPose> Poses { get; } = new();

        internal Fog2dRememberedDrawing(RpgWorldView view, Fog2dKnownProp source)
        {
            Source = source;
            var index = 0;
            foreach (var look in view.SpriteLooks(source.Look))
            {
                Poses.Add(new TileActorPose
                {
                    Id = "memory/" + source.Id + "#" + index,
                    Look = look,
                    Position = new Vector2(source.X, source.Y),
                    Visible = !source.Look.Hidden,
                });
                index++;
            }
        }
    }
}
