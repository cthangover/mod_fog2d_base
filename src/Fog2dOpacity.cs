namespace Cthangover.Mods.Fog2d
{
    /// <summary>Object-specific sight policy is independent of walking collision and visual geometry.</summary>
    public enum Fog2dOpacity
    {
        /// <summary>Low furniture and transparent barriers never stop sight, even while solid for walking.</summary>
        Transparent,

        /// <summary>The visible object's complete footprint stops sight, including non-solid scenery.</summary>
        Opaque,

        /// <summary>Only currently solid footprint cells stop sight; ordinary door states therefore change visibility.</summary>
        WhenSolid,
    }
}
