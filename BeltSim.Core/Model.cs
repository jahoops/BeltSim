using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

public enum Dir { N, E, S, W }

public sealed class Map
{
    public int Width { get; init; }
    public int Height { get; init; }
    public Tile[] Tiles { get; init; } = Array.Empty<Tile>();

    public int Index(int x, int y) => y * Width + x;
    public bool InBounds(int x, int y) => (uint)x < (uint)Width && (uint)y < (uint)Height;
    public Tile TileAt(int x, int y) => Tiles[Index(x, y)];
}

public enum TileType { Empty, Belt, Source, Sink, Splitter }

public sealed partial class Tile
{
    public TileType Type { get; init; }
    public Dir Dir { get; init; } = Dir.E;

    // Source
    public int SpawnEveryTicks { get; init; } = 1;

    // Splitter
    public Dir DirA { get; init; } = Dir.E;
    public Dir DirB { get; init; } = Dir.S;
    public bool Toggle { get; set; } = false;

}
