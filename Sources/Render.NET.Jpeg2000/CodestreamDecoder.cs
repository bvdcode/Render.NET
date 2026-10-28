using System;
using System.IO;
using System.Threading;

namespace Render.NET.Jpeg2000
{
    internal class CodestreamDecoder
    {
        private readonly ByteReader _input;
        private readonly ContainerInfo _info;
        private readonly DecodeBudget _budget;
        private readonly HeaderSettings _main = new();
        private Jpeg2000Component[] _components = Array.Empty<Jpeg2000Component>();
        private Tile?[] _tiles = Array.Empty<Tile?>();
        private int _x0, _y0, _x1, _y1, _tileX, _tileY, _tileWidth, _tileHeight, _across;
        internal CodestreamDecoder(ReadOnlyMemory<byte> input, ContainerInfo info, DecodeBudget budget)
        {
            _input = new ByteReader(input);
            _info = info;
            _budget = budget;
        }

        internal Jpeg2000Image Decode()
        {
            if (_input.Marker() != 0xff4f || _input.Marker() != 0xff51)
            {
                throw new InvalidDataException("JPEG 2000 must start with SOC and SIZ.");
            }

            Size(_input.Segment());
            bool end = false;
            while (_input.Remaining > 0)
            {
                _budget.Cancellation.ThrowIfCancellationRequested();
                int start = _input.Position;
                int marker = _input.Marker();
                if (marker == 0xffd9)
                {
                    end = true;
                    break;
                }

                if (marker == 0xff90)
                {
                    ReadTile(start);
                }
                else
                {
                    if (Array.Exists(_tiles, t => t is not null))
                    {
                        throw new InvalidDataException("Main header marker after tile data.");
                    }

                    HeaderParser.Read(marker, _input.Segment(), _main, _components.Length);
                }
            }

            if (!end)
            {
                throw new InvalidDataException("JPEG 2000 is missing EOC.");
            }

            _input.End();
            ReconstructTiles();
            return new Jpeg2000Image(_x0, _y0, _x1 - _x0, _y1 - _y0, _components, _info);
        }

        private void ReconstructTiles()
        {
            foreach (Tile? tile in _tiles)
            {
                if (tile is null || tile.PacketIndex != tile.Packets.Length)
                {
                    throw new InvalidDataException("JPEG 2000 is missing tile packets.");
                }

                foreach (TileComponent component in tile.Components)
                {
                    InverseWavelet.Reconstruct(component, _budget);
                }

                TileAssembler.Copy(tile, _components, _budget);
            }
        }

        private static int Dimension(ByteReader input)
        {
            uint value = input.U32();
            if (value > int.MaxValue)
            {
                throw new InvalidDataException("JPEG 2000 coordinate exceeds supported range.");
            }

            return (int)value;
        }

        private void Size(ByteReader data)
        {
            if (data.U16() > 2)
            {
                throw new NotSupportedException("JPEG 2000 extended profile.");
            }

            _x1 = Dimension(data);
            _y1 = Dimension(data);
            _x0 = Dimension(data);
            _y0 = Dimension(data);
            _tileWidth = Dimension(data);
            _tileHeight = Dimension(data);
            _tileX = Dimension(data);
            _tileY = Dimension(data);
            _budget.Pixels((long)_x1 - _x0, (long)_y1 - _y0, true);
            if (_tileWidth == 0 || _tileHeight == 0 || _tileX > _x0 || _tileY > _y0 || (long)_tileX + _tileWidth <= _x0 || (long)_tileY + _tileHeight <= _y0)
            {
                throw new InvalidDataException("Invalid JPEG 2000 tile grid.");
            }

            _across = ByteReader.Ceil(_x1 - _tileX, _tileWidth);
            long tiles = (long)_across * ByteReader.Ceil(_y1 - _tileY, _tileHeight);
            if (tiles > 65535)
            {
                throw new NotSupportedException("JPEG 2000 tile count exceeds 65535.");
            }

            _budget.Reserve(tiles * 8);
            _tiles = new Tile?[(int)tiles];
            int count = data.U16();
            if (count < 1 || count > 16)
            {
                throw new NotSupportedException("JPEG 2000 supports 1 to 16 components.");
            }

            _components = new Jpeg2000Component[count];
            for (int c = 0; c < count; c++)
            {
                _components[c] = Component(data);
            }

            data.End();
            ValidateContainer();
        }

        private Jpeg2000Component Component(ByteReader data)
        {
            int precision = data.U8();
            int bits = (precision & 127) + 1;
            int dx = data.U8();
            int dy = data.U8();
            if (bits > 16)
            {
                throw new NotSupportedException("JPEG 2000 sample precision above 16 bits.");
            }

            if (dx == 0 || dy == 0)
            {
                throw new InvalidDataException("Invalid JPEG 2000 component sampling.");
            }

            int x0 = ByteReader.Ceil(_x0, dx);
            int y0 = ByteReader.Ceil(_y0, dy);
            int width = ByteReader.Ceil(_x1, dx) - x0;
            int height = ByteReader.Ceil(_y1, dy) - y0;
            _budget.Pixels(width, height);
            _budget.Reserve((long)width * height * 4);
            return new Jpeg2000Component(x0, y0, width, height, bits, (precision & 128) != 0, dx, dy, new int[width * height]);
        }

        private void ValidateContainer()
        {
            if (_info.Components != 0 && (_info.Components != _components.Length || _info.Width != _x1 - _x0 || _info.Height != _y1 - _y0))
            {
                throw new InvalidDataException("JP2 header dimensions disagree with the codestream.");
            }

            if (_info.Alpha >= _components.Length)
            {
                throw new InvalidDataException("Invalid JP2 alpha component.");
            }
        }

        private void ReadTile(int start)
        {
            ByteReader? header = _input.Segment();
            int index = header.U16();
            uint size = header.U32();
            int part = header.U8();
            int parts = header.U8();
            header.End();
            if (index >= _tiles.Length || (parts != 0 && part >= parts))
            {
                throw new InvalidDataException("Invalid JPEG 2000 tile-part index.");
            }

            long end = size == 0 ? _input.Data.Length - 2 : start + (long)size;
            if (end > _input.Data.Length || end < _input.Position + 2)
            {
                throw new InvalidDataException("Invalid JPEG 2000 tile-part length.");
            }

            Tile? tile = _tiles[index];
            if (part != (tile?.NextPart ?? 0))
            {
                throw new InvalidDataException("Out-of-order JPEG 2000 tile-parts.");
            }

            HeaderSettings? settings = tile?.Settings ?? _main.Clone();
            ReadTileHeader(settings, tile is not null, end);
            if (tile is null)
            {
                _tiles[index] = tile = CreateTile(index, settings);
            }

            tile.NextPart++;
            ByteReader? packets = new ByteReader(_input.Take((int)(end - _input.Position)));
            PacketDecoder.Read(packets, tile, _budget);
        }

        private void ReadTileHeader(HeaderSettings settings, bool continuation, long end)
        {
            while (_input.Position < end)
            {
                int marker = _input.Marker();
                if (marker == 0xff93)
                {
                    return;
                }

                if (continuation && marker != 0xff58 && marker != 0xff64)
                {
                    throw new NotSupportedException("JPEG 2000 coding changes across tile-parts.");
                }

                HeaderParser.Read(marker, _input.Segment(), settings, _components.Length);
                if (_input.Position > end)
                {
                    throw new InvalidDataException("JPEG 2000 tile header exceeds its tile-part.");
                }
            }

            throw new InvalidDataException("JPEG 2000 tile-part is missing SOD.");
        }

        private Tile CreateTile(int index, HeaderSettings settings)
        {
            long left = _tileX + ((long)(index % _across) * _tileWidth);
            long top = _tileY + ((long)(index / _across) * _tileHeight);
            int x0 = (int)Math.Max(_x0, left);
            int y0 = (int)Math.Max(_y0, top);
            int x1 = (int)Math.Min(_x1, left + _tileWidth);
            int y1 = (int)Math.Min(_y1, top + _tileHeight);
            return TileBuilder.Build(_components, settings, x0, y0, x1, y1, _budget);
        }
    }
}
