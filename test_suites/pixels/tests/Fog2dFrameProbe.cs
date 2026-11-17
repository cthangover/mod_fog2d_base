namespace Cthangover.Mods.Fog2d.Tests.Pixels
{
    using Cthangover.Mods.Rpg;
    using Cthangover.Mods.TileRender;

    /// <summary>Named synchronous subscriber returns a detached frame while keeping the original cached pose intact.</summary>
    internal sealed class Fog2dFrameProbe
    {
        internal RpgWorldView Target { get; set; }

        internal int Calls { get; private set; }

        internal TileActorPose Original { get; private set; }

        internal TileActorPose Selected { get; private set; }

        internal void Filter(RpgWorldView view, ITileWorldPresentation presentation, ref TileFrame frame)
        {
            if (!ReferenceEquals(view, Target))
            {
                return;
            }

            Calls++;
            Original = frame.Actors[0];
            Selected = new TileActorPose
            {
                Id = Original.Id,
                Position = Original.Position,
                Visible = false,
                Look = Original.Look,
                Charset = Original.Charset,
                Facing = Original.Facing,
                Column = Original.Column,
                Tint = Original.Tint,
            };
            var copy = new TileFrame
            {
                Focus = frame.Focus,
                Ambient = frame.Ambient,
                TimeWeights = frame.TimeWeights,
                CameraOnArtwork = frame.CameraOnArtwork,
                PortableLight = frame.PortableLight,
                InteractionCell = frame.InteractionCell,
            };
            copy.Lights.AddRange(frame.Lights);
            copy.Actors.Add(Selected);
            frame = copy;
        }
    }
}
