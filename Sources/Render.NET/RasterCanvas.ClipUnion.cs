using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;

namespace Render.NET
{
    public partial class RasterCanvas
    {
        /// <summary>Intersects the current clip with the union of independently filled transformed paths.
        /// Each path keeps its own fill rule; overlaps do not create even-odd holes between objects.</summary>
        public void ClipUnion(IEnumerable<(VectorPath Path, Matrix3x2 Transform, FillRule Rule)> paths, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(paths);
            cancellationToken.ThrowIfCancellationRequested();
            byte[]? previous = _clip;
            byte[]? next = new byte[_target.Width * _target.Height];
            foreach ((VectorPath Path, Matrix3x2 Transform, FillRule Rule) item in paths)
            {
                ArgumentNullException.ThrowIfNull(item.Path);
                cancellationToken.ThrowIfCancellationRequested();
                ScanlineRasterizer.Paint(item.Path, item.Transform, item.Rule, _target.Width, _target.Height, (y, left, right) =>
                {
                    for (int x = left; x < right; x++)
                    {
                        int index = (y * _target.Width) + x;
                        next[index] = previous is null ? (byte)255 : previous[index];
                    }
                }, cancellationToken);
            }

            _clip = next;
        }
    }
}
