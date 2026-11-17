namespace Cthangover.Mods.Fog2d
{
    using System;
    using System.Collections.Generic;
    using Cthangover.Mods.Exploration;

    /// <summary>
    /// Deterministic cell-centre sight rays, independent of tile projection and native graphics. The first opaque
    /// target is visible; cells behind it are hidden. Two opaque neighbours close a diagonal corner.
    /// </summary>
    public static class Fog2dField
    {
        /// <summary>
        /// Returns owned row-major visible indices inside a circular radius, without changing the supplied opacity.
        /// Invalid dimensions, mask, observer or radius fail before any result is published; radius zero sees only self.
        /// </summary>
        public static IReadOnlyList<int> Compute(int width, int height, IReadOnlyList<bool> opaque, int x, int y, int radius)
        {
            ExplorationRecord.ValidateDimensions(width, height);
            ArgumentNullException.ThrowIfNull(opaque);
            if (opaque.Count != width * height || x < 0 || y < 0 || x >= width || y >= height || radius < 0 || radius > 64)
            {
                throw new ArgumentException("Visibility mask, observer or radius is outside its grid contract");
            }

            var visible = new List<int>();
            for (var ty = Math.Max(0, y - radius); ty <= Math.Min(height - 1, y + radius); ty++)
            {
                for (var tx = Math.Max(0, x - radius); tx <= Math.Min(width - 1, x + radius); tx++)
                {
                    var dx = tx - x;
                    var dy = ty - y;
                    if (dx * dx + dy * dy <= radius * radius && Sees(width, opaque, x, y, tx, ty))
                    {
                        visible.Add(ty * width + tx);
                    }
                }
            }

            return visible.AsReadOnly();
        }

        private static bool Sees(int width, IReadOnlyList<bool> opaque, int x, int y, int tx, int ty)
        {
            var nx = Math.Abs(tx - x);
            var ny = Math.Abs(ty - y);
            var sx = Math.Sign(tx - x);
            var sy = Math.Sign(ty - y);
            var ix = 0;
            var iy = 0;
            while (ix < nx || iy < ny)
            {
                var decision = (1 + 2 * ix) * ny - (1 + 2 * iy) * nx;
                if (decision == 0)
                {
                    if (opaque[y * width + x + sx] && opaque[(y + sy) * width + x])
                    {
                        return false;
                    }

                    x += sx;
                    y += sy;
                    ix++;
                    iy++;
                }
                else if (decision < 0)
                {
                    x += sx;
                    ix++;
                }
                else
                {
                    y += sy;
                    iy++;
                }

                if (x == tx && y == ty)
                {
                    return true;
                }

                if (opaque[y * width + x])
                {
                    return false;
                }
            }

            return true;
        }
    }
}
