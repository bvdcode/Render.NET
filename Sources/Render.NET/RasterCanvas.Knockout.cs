using System;
using System.Threading;

namespace Render.NET
{
    public sealed partial class RasterCanvas
    {
        /// <summary>Paints one elementary object in the current group. In a knockout layer the
        /// object's shape replaces earlier objects, while its opacity and blend use the initial backdrop.
        /// Call once per logical object; paths in the callback may combine to form that object.</summary>
        public void DrawObject(Action<RasterCanvas> draw, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(draw);
            if (!_knockout)
            {
                draw(this);
                return;
            }

            RasterCanvas element = PrepareElement(cancellationToken);
            draw(element);
            for (int y = element._paintTop; y < element._paintBottom; y++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                for (int x = element._paintLeft; x < element._paintRight; x++)
                {
                    int index = (y * _target.Width) + x;
                    byte shape = element._shape![index];
                    if (shape == 0)
                    {
                        continue;
                    }

                    Rgba32 initial = _knockoutBackdrop?[x, y] ?? default;
                    _target[x, y] = KnockoutPixel(_target[x, y], element._target[x, y], initial, shape);
                    _coverage![index] = (byte)Math.Min(255, element._coverage![index] + (((_coverage[index] * (255 - shape)) + 127) / 255));
                    _shape![index] = (byte)(shape + (((_shape[index] * (255 - shape)) + 127) / 255));
                }
            }
        }

        private RasterCanvas PrepareElement(CancellationToken token)
        {
            if (_element == null)
            {
                var buffer = new PixelBuffer(_target.Width, _target.Height);
                _knockoutBackdrop?.Pixels.CopyTo(buffer.Pixels);
                _element = new RasterCanvas(buffer)
                {
                    _coverage = new byte[_target.Width * _target.Height],
                    _shape = new byte[_target.Width * _target.Height],
                    _layerDepth = _layerDepth
                };
            }
            else
            {
                for (int y = _element._paintTop; y < _element._paintBottom; y++)
                {
                    token.ThrowIfCancellationRequested();
                    for (int x = _element._paintLeft; x < _element._paintRight; x++)
                    {
                        int index = (y * _target.Width) + x;
                        _element._target[x, y] = _knockoutBackdrop?[x, y] ?? default;
                        _element._coverage![index] = 0;
                        _element._shape![index] = 0;
                    }
                }
            }

            _element._paintLeft = int.MaxValue;
            _element._paintTop = int.MaxValue;
            _element._paintRight = 0;
            _element._paintBottom = 0;
            _element._clip = _clip;
            _element._clips.Clear();
            return _element;
        }

        private static Rgba32 KnockoutPixel(Rgba32 previous, Rgba32 element, Rgba32 initial, byte shape)
        {
            float retained = 1 - (shape / 255f);
            float alpha = (element.A + ((previous.A - initial.A) * retained)) / 255f;
            byte Channel(byte before, byte current, byte background)
            {
                float premultiplied = (current * (element.A / 255f))
                    + (((before * (previous.A / 255f)) - (background * (initial.A / 255f))) * retained);
                return alpha <= 0 ? (byte)0 : (byte)Math.Clamp((int)MathF.Round(premultiplied / alpha), 0, 255);
            }

            return new Rgba32(Channel(previous.R, element.R, initial.R), Channel(previous.G, element.G, initial.G),
                Channel(previous.B, element.B, initial.B), PixelCompositor.Byte(alpha));
        }
    }
}
