namespace Cthangover.Mods.Fog2d.Tests
{
    using System;
    using System.Collections.Generic;
    using Cthangover.Core.Mods.Testing;

    /// <summary>Projection-free sight contracts cover wall endpoints, open doors, closed corners and bounded circular range.</summary>
    public sealed class Fog2dFieldTests
    {
        [ModTest("fog_field/circle_edges_and_opaque_observer")]
        private static void Circle(ModTestContext context)
        {
            var opaque = new bool[49];
            var result = new HashSet<int>(Fog2dField.Compute(7, 7, opaque, 3, 3, 2));
            ModAssert.Equal(13, result.Count, "radius two contains exactly thirteen cell centres");
            ModAssert.True(result.Contains(3 * 7 + 5), "exact circular boundary included");
            ModAssert.False(result.Contains(4 * 7 + 5), "outside circular range excluded");
            ModAssert.Equal(6, Fog2dField.Compute(7, 7, opaque, 0, 0, 2).Count, "map edge clips rather than wraps sight");
            opaque[24] = true;
            ModAssert.Equal(13, Fog2dField.Compute(7, 7, opaque, 3, 3, 2).Count, "observer can see out of its own opaque cell");
            var self = Fog2dField.Compute(7, 7, opaque, 3, 3, 0);
            ModAssert.Equal(1, self.Count, "zero radius sees only self");
            ModAssert.Equal(24, self[0], "indices remain row-major");
        }

        [ModTest("fog_field/wall_door_and_diagonal_corner")]
        private static void Walls(ModTestContext context)
        {
            var opaque = new bool[49];
            for (var y = 0; y < 7; y++)
            {
                opaque[y * 7 + 3] = true;
            }

            var closed = new HashSet<int>(Fog2dField.Compute(7, 7, opaque, 1, 3, 6));
            ModAssert.True(closed.Contains(3 * 7 + 3), "first opaque wall is seen");
            foreach (var index in closed)
            {
                ModAssert.True(index % 7 <= 3, "nothing behind a complete wall is visible");
            }

            opaque[3 * 7 + 3] = false;
            var open = new HashSet<int>(Fog2dField.Compute(7, 7, opaque, 1, 3, 6));
            ModAssert.True(open.Contains(3 * 7 + 5), "opening the door reveals the corridor behind it");
            Array.Clear(opaque);
            opaque[1 * 7 + 2] = true;
            opaque[2 * 7 + 1] = true;
            var corner = new HashSet<int>(Fog2dField.Compute(7, 7, opaque, 1, 1, 4));
            ModAssert.False(corner.Contains(2 * 7 + 2), "two closed edges stop a diagonal sight ray");
            ModAssert.True(corner.Contains(1 * 7 + 2), "the blocking corner face remains visible");
            opaque[2 * 7 + 1] = false;
            var gap = new HashSet<int>(Fog2dField.Compute(7, 7, opaque, 1, 1, 4));
            ModAssert.True(gap.Contains(2 * 7 + 2), "one open edge permits diagonal sight");
        }

        [ModTest("fog_field/reciprocal_empty_endpoints_and_input_ownership")]
        private static void Reciprocal(ModTestContext context)
        {
            var opaque = new bool[64];
            var random = new Random(671);
            for (var i = 0; i < opaque.Length; i++)
            {
                opaque[i] = random.Next(4) == 0;
            }

            var before = (bool[])opaque.Clone();
            var views = new List<HashSet<int>>();
            for (var i = 0; i < 64; i++)
            {
                views.Add(new HashSet<int>(Fog2dField.Compute(8, 8, opaque, i % 8, i / 8, 6)));
            }

            for (var a = 0; a < 64; a++)
            {
                ModAssert.True(before[a] == opaque[a], "sight computation never changes the opacity input");
                for (var b = 0; b < 64; b++)
                {
                    if (!opaque[a] && !opaque[b])
                    {
                        ModAssert.True(views[a].Contains(b) == views[b].Contains(a), "sight between empty endpoints is reciprocal");
                    }
                }
            }
        }

        [ModTest("fog_field/rejects_invalid_masks_observers_and_limits")]
        private static void Invalid(ModTestContext context)
        {
            var cases = new[]
            {
                (Width: 0, Height: 2, Mask: new bool[4], X: 0, Y: 0, Radius: 1),
                (Width: 2, Height: 2, Mask: new bool[3], X: 0, Y: 0, Radius: 1),
                (Width: 2, Height: 2, Mask: (bool[])null, X: 0, Y: 0, Radius: 1),
                (Width: 2, Height: 2, Mask: new bool[4], X: -1, Y: 0, Radius: 1),
                (Width: 2, Height: 2, Mask: new bool[4], X: 0, Y: 2, Radius: 1),
                (Width: 2, Height: 2, Mask: new bool[4], X: 0, Y: 0, Radius: -1),
                (Width: 2, Height: 2, Mask: new bool[4], X: 0, Y: 0, Radius: 65),
                (Width: 2048, Height: 2048, Mask: new bool[4], X: 0, Y: 0, Radius: 1),
            };
            foreach (var item in cases)
            {
                var rejected = false;
                try
                {
                    Fog2dField.Compute(item.Width, item.Height, item.Mask, item.X, item.Y, item.Radius);
                }
                catch (ArgumentException)
                {
                    rejected = true;
                }

                ModAssert.True(rejected, "invalid sight request is rejected before any result");
            }
        }
    }
}
