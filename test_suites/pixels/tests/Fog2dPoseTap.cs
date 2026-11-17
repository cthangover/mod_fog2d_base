namespace Cthangover.Mods.Fog2d.Tests.Pixels
{
    using Cthangover.Mods.Rpg;
    using Cthangover.Mods.TileRender;
    using Godot;

    /// <summary>Observes submitted poses through the public synchronous frame contract without changing either frame owner.</summary>
    internal sealed class Fog2dPoseTap
    {
        internal RpgWorldView Target { get; set; }

        internal Texture2D Texture { get; set; }

        internal bool Found { get; private set; }

        internal Vector2 Position { get; private set; }

        internal void Filter(RpgWorldView view, ITileWorldPresentation presentation, ref TileFrame frame)
        {
            if (!ReferenceEquals(Target, view))
            {
                return;
            }

            Found = false;
            foreach (var pose in frame.Actors)
            {
                if (pose.Visible && ReferenceEquals(pose.Look?.Texture, Texture))
                {
                    Found = true;
                    Position = pose.Position;
                }
            }
        }
    }
}
