namespace Cthangover.Mods.Fog2d
{
    using Cthangover.Core.Bootstrap;

    /// <summary>Registers the remembered-drawing owner with new-game reset, separately from shared exploration ownership.</summary>
    public sealed class Fog2dReset : IResettable
    {
        /// <summary>Stable lifecycle identity lets retained assemblies replace their registration without duplicates.</summary>
        public string Id
        {
            get
            {
                return "fog2d_base";
            }
        }

        /// <summary>Clears only saved drawings; the common exploration reset owns explored bits and observer generations.</summary>
        public void ResetForNewGame()
        {
            Fog2dHistory.Reset();
        }
    }
}
