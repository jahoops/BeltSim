using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using System.Windows.Forms.VisualStyles;

// If your core types are in a namespace, uncomment and adjust:
// using BeltSim.Core;

public sealed class MainForm : Form
{
    private readonly Map _map;
    private readonly Simulator _sim;

    private readonly System.Windows.Forms.Timer _timer = new System.Windows.Forms.Timer();
    private bool _running = true;
    private int _ticksPerSecond = 20;

    private const int CellSize = 20;
    private const int PadPx = 10;

    public MainForm(string mapPath)
    {
        Text = "BeltSim Viewer";
        DoubleBuffered = true;
        KeyPreview = true;

        _map = MapLoader.Load(mapPath);
        _sim = new Simulator(_map);

        ClientSize = new Size(
            PadPx * 2 + _map.Width * CellSize,
            PadPx * 2 + _map.Height * CellSize
        );

        ApplyTimerInterval();

        _timer.Tick += (_, __) =>
        {
            if (_running)
                _sim.Tick();

            Invalidate(); // trigger repaint
        };
        _timer.Start();

        KeyDown += OnKeyDown;
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Space)
        {
            _running = !_running;
            e.Handled = true;
            return;
        }

        if (e.KeyCode == Keys.Up)
        {
            _ticksPerSecond += 10;
            if (_ticksPerSecond > 200) _ticksPerSecond = 200;
            ApplyTimerInterval();
            e.Handled = true;
            return;
        }

        if (e.KeyCode == Keys.Down)
        {
            _ticksPerSecond -= 10;
            if (_ticksPerSecond < 1) _ticksPerSecond = 1;
            ApplyTimerInterval();
            e.Handled = true;
            return;
        }
    }

    private void ApplyTimerInterval()
    {
        // Clamp to [1..1000] ms to keep WinForms Timer sane
        int intervalMs = (int)Math.Round(1000.0 / _ticksPerSecond);
        if (intervalMs < 1) intervalMs = 1;
        if (intervalMs > 1000) intervalMs = 1000;

        _timer.Interval = intervalMs;
        Text = $"BeltSim Viewer — {(_running ? "Running" : "Paused")} — TPS: {_ticksPerSecond}";
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;

        // background
        g.Clear(Color.Black);

        // draw tiles
        for (int y = 0; y < _map.Height; y++)
        {
            for (int x = 0; x < _map.Width; x++)
            {
                var tile = _map.TileAt(x, y);

                Color c = tile.Type switch
                {
                    TileType.Empty => Color.FromArgb(20, 20, 20),
                    TileType.Belt => Color.FromArgb(120, 120, 120),
                    TileType.Source => Color.FromArgb(0, 160, 0),
                    TileType.Sink => Color.FromArgb(180, 0, 0),
                    TileType.Splitter => Color.FromArgb(180, 180, 0),
                    _ => Color.Magenta
                };

                int px = PadPx + x * CellSize;
                int py = PadPx + y * CellSize;

                using (var br = new SolidBrush(c))
                    g.FillRectangle(br, px, py, CellSize - 1, CellSize - 1);

                if (tile.Item is not null)
                {
                    int r = Math.Max(4, CellSize / 4);
                    int cx = px + (CellSize - r) / 2;
                    int cy = py + (CellSize - r) / 2;
                    using (var brItem = new SolidBrush(Color.White))
                        g.FillEllipse(brItem, cx, cy, r, r);
                }
            }
        }

        // small HUD
        using var hud = new SolidBrush(Color.White);
        g.DrawString(
            $"TPS: {_ticksPerSecond}   (Space=Pause, Up/Down=TPS)",
            Font, hud, new PointF(5, 5)
        );
    }

    public static string ResolveMapPath(string? mapArg)
    {
        // If user provided a path, use it as-is.
        if (!string.IsNullOrWhiteSpace(mapArg))
            return mapArg;

        // Default: maps/test1.json relative to solution root.
        // Viewer runs from ...\BeltSim.Viewer\bin\Debug\netX\ so we walk up until we find "maps".
        string dir = AppContext.BaseDirectory;
        for (int i = 0; i < 6; i++)
        {
            var candidate = Path.Combine(dir, "maps", "test1.json");
            if (File.Exists(candidate))
                return candidate;

            var parent = Directory.GetParent(dir);
            if (parent is null) break;
            dir = parent.FullName;
        }

        // Fallback: relative path (may work if launched from solution root)
        return Path.Combine("maps", "test1.json");
    }
}