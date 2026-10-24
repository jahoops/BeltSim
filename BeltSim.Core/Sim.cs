public sealed class SimStats
{
    public long Delivered;
    public long Spawned;
    public long Moved;
    public long BlockedMoves;
}
public sealed class Simulator
{
    private readonly Map _map;
    private long _tick;
    public SimStats Stats { get; } = new();

    public Simulator(Map map) => _map = map;

    public void Tick()
    {
        _tick++;

        // 1) Spawn
        for (int y = 0; y < _map.Height; y++)
            for (int x = 0; x < _map.Width; x++)
            {
                var tile = _map.TileAt(x, y);
                if (tile.Type != TileType.Source) continue;
                if ((_tick - 1) % tile.SpawnEveryTicks != 0) continue;

                var (nx, ny) = Next(x, y, tile.Dir);
                if (!_map.InBounds(nx, ny)) continue;
                var outTile = _map.TileAt(nx, ny);
                if (outTile.Item is null)
                {
                    outTile.Item = 1; // item id placeholder
                    Stats.Spawned++;
                }
            }

        // 2) Move intents (simultaneous)
        var intents = new List<(int fromIdx, int toIdx)>();

        for (int y = 0; y < _map.Height; y++)
            for (int x = 0; x < _map.Width; x++)
            {
                var tile = _map.TileAt(x, y);
                if (tile.Type != TileType.Belt) continue;
                if (tile.Item is null) continue;

                var (nx, ny) = Next(x, y, tile.Dir);
                if (!_map.InBounds(nx, ny)) continue;
                var to = _map.TileAt(nx, ny);
                if (to.Item is not null) { Stats.BlockedMoves++; continue; }

                intents.Add((_map.Index(x, y), _map.Index(nx, ny)));
            }

        // Deterministic conflict resolution:
        // Sort by destination then source, so results are repeatable.
        intents.Sort((a, b) =>
        {
            int c = a.toIdx.CompareTo(b.toIdx);
            return c != 0 ? c : a.fromIdx.CompareTo(b.fromIdx);
        });

        var claimedDest = new HashSet<int>();
        foreach (var (fromIdx, toIdx) in intents)
        {
            if (!claimedDest.Add(toIdx)) { Stats.BlockedMoves++; continue; }
            var from = _map.Tiles[fromIdx];
            var to = _map.Tiles[toIdx];
            if (from.Item is null || to.Item is not null) continue; // recheck
            to.Item = from.Item;
            from.Item = null;
            Stats.Moved++;

            if (_map.Tiles[fromIdx].Type == TileType.Splitter)
                _map.Tiles[fromIdx].Toggle = !_map.Tiles[fromIdx].Toggle;
        }

        // 3) Sink consume
        for (int y = 0; y < _map.Height; y++)
            for (int x = 0; x < _map.Width; x++)
            {
                var tile = _map.TileAt(x, y);
                if (tile.Type != TileType.Sink) continue;

                // Sink consumes from its input tile (the tile behind its Dir)
                var (ix, iy) = Prev(x, y, tile.Dir);
                if (!_map.InBounds(ix, iy)) continue;
                var input = _map.TileAt(ix, iy);
                if (input.Item is not null)
                {
                    input.Item = null;
                    Stats.Delivered++;
                }
            }
    }

    private static (int x, int y) Next(int x, int y, Dir d) => d switch
    {
        Dir.N => (x, y - 1),
        Dir.E => (x + 1, y),
        Dir.S => (x, y + 1),
        Dir.W => (x - 1, y),
        _ => (x + 1, y)
    };

    private static (int x, int y) Prev(int x, int y, Dir d) => d switch
    {
        Dir.N => (x, y + 1),
        Dir.E => (x - 1, y),
        Dir.S => (x, y - 1),
        Dir.W => (x + 1, y),
        _ => (x - 1, y)
    };

    public void Render()
    {
        for (int y = 0; y < _map.Height; y++)
        {
            for (int x = 0; x < _map.Width; x++)
            {
                var tile = _map.TileAt(x, y);

                char baseChar = tile.Type switch
                {
                    TileType.Empty => '.',
                    TileType.Source => 'S',
                    TileType.Sink => 'K',
                    TileType.Splitter => 'T',
                    TileType.Belt => tile.Dir switch
                    {
                        Dir.N => '^',
                        Dir.E => '>',
                        Dir.S => 'v',
                        Dir.W => '<',
                        _ => '>'
                    },
                    _ => '?'
                };

                char itemChar = tile.Item is null ? '.' : 'o';

                Console.Write(baseChar);
                Console.Write(itemChar);
            }
            Console.WriteLine();
        }
    }
}
