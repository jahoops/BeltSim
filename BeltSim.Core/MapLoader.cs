using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

// MapLoader.cs
public static class MapLoader
{
    private sealed class MapDto
    {
        public int width { get; set; }
        public int height { get; set; }
        public List<TileDto> tiles { get; set; } = new();
    }
    public static Map Load(string path)
    {
        var json = File.ReadAllText(path);
        var dto = JsonSerializer.Deserialize<MapDto>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? throw new Exception("Failed to parse map.");

        var tiles = new Tile[dto.width * dto.height];
        for (int i = 0; i < tiles.Length; i++)
        {
            tiles[i] = new Tile
            {
                Type = TileType.Empty,
                DirA = ParseDir("E"),
                DirB = ParseDir("S"),
                Dir = Dir.E,
                SpawnEveryTicks = 1
            };
        }

        foreach (var td in dto.tiles)
        {
            tiles[td.y * dto.width + td.x] = new Tile
            {
                Type = td.type.ToLowerInvariant() switch
                {
                    "belt" => TileType.Belt,
                    "source" => TileType.Source,
                    "sink" => TileType.Sink,
                    "splitter" => TileType.Splitter,
                    _ => TileType.Empty
                },
                Dir = ParseDir(td.dir),
                SpawnEveryTicks = td.spawnEveryTicks,
                DirA = ParseDir(td.dirA ?? "E"),
                DirB = ParseDir(td.dirB ?? "S")
            };
        }

        return new Map { Width = dto.width, Height = dto.height, Tiles = tiles };
    }

    private static Dir ParseDir(string s) => s.ToUpperInvariant() switch
    {
        "N" => Dir.N,
        "E" => Dir.E,
        "S" => Dir.S,
        "W" => Dir.W,
        _ => Dir.E
    };

    private sealed class TileDto
    {
        public int x { get; set; }
        public int y { get; set; }
        public string type { get; set; } = "empty";
        public string dir { get; set; } = "E";
        public int spawnEveryTicks { get; set; } = 1;

        // Splitter
        public string? dirA { get; set; }
        public string? dirB { get; set; }
    }

}
