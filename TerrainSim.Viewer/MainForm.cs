using System;
using System.Drawing;
using System.Windows.Forms;
using TerrainSim.Core;

public sealed class MainForm : Form
{
    private readonly TerrainGrid _g;
    private readonly System.Windows.Forms.Timer _timer = new();
    private readonly StatsForm _stats;

    private bool _running = true;
    private int _tps = 30;

    private const int Cell = 12;
    private const int Pad = 10;

    private enum PaintMode { Rock, WaterSource }
    private PaintMode _mode = PaintMode.Rock;

    public enum ViewMode
    {
        Moisture = 1,
        Plants = 2,
        Combined = 3,
        WeatherOnlyHumidity = 4,
        WeatherOnlyTemperature = 5,
        WeatherOnlyCloud = 6,
        Elevation = 7,
        SoilDepth = 8
    }
    private ViewMode _view = ViewMode.Combined;

    public enum WeatherOverlayMode { Off, Humidity, Temperature, Cloud }
    private WeatherOverlayMode _weatherOverlay = WeatherOverlayMode.Off;

    private bool _soilWaterOverlay;

    private TerrainShape _shape = TerrainShape.Basin;
    private int _seed = 1;

    public MainForm()
    {
        Text = "TerrainSim";
        DoubleBuffered = true;
        KeyPreview = true;

        _g = new TerrainGrid(64, 64);
        _g.Randomize(seed: _seed, shape: _shape);

        ClientSize = new Size(Pad * 2 + _g.Width * Cell, Pad * 2 + _g.Height * Cell);

        ApplyTimer();

        _timer.Tick += (_, __) =>
        {
            if (_running) _g.Step();
            Invalidate();
        };
        _timer.Start();

        _stats = new StatsForm(
            () => _g,
            () => _running,
            v => SetRunning(v),
            () => _tps,
            v => SetTps(v),
            () => _view,
            v => SetView(v),
            () => _weatherOverlay,
            v => SetWeatherOverlay(v),
            () => _soilWaterOverlay,
            v => SetSoilWaterOverlay(v),
            () => _shape,
            v => SetShape(v),
            () => _seed,
            () => Reseed(),
            () => NewSeed(),
            () => StepOnce(),
            () => ApplyInitialFloodOnce());
        _stats.Show(this);

        MouseDown += OnMouseDown;
        MouseMove += (_, e) => { if (e.Button != MouseButtons.None) ApplyPaint(e); };
        KeyDown += OnKeyDown;
    }

    public void SetRunning(bool running)
    {
        _running = running;
        ApplyTimer();
    }

    public void StepOnce()
    {
        if (_running) return;
        _g.Step();
        Invalidate();
        ApplyTimer();
    }

    public void SetTps(int tps)
    {
        _tps = Math.Clamp(tps, 1, 240);
        ApplyTimer();
    }

    public void SetView(ViewMode view)
    {
        _view = view;
        ApplyTimer();
    }

    public void SetWeatherOverlay(WeatherOverlayMode mode)
    {
        _weatherOverlay = mode;
        ApplyTimer();
    }

    public void SetSoilWaterOverlay(bool on)
    {
        _soilWaterOverlay = on;
        ApplyTimer();
    }

    public void SetShape(TerrainShape shape)
    {
        _shape = shape;
        _g.Randomize(seed: _seed, shape: _shape);
        ApplyTimer();
    }

    public void Reseed()
    {
        _g.Randomize(seed: _seed, shape: _shape);
        ApplyTimer();
    }

    public void NewSeed()
    {
        _seed = Environment.TickCount;
        _g.Randomize(seed: _seed, shape: _shape);
        ApplyTimer();
    }

    public void ApplyInitialFloodOnce()
    {
        _g.ApplyInitialFloodNow();
        Invalidate();
    }

    private void ApplyTimer()
    {
        int interval = (int)Math.Round(1000.0 / _tps);
        if (interval < 1) interval = 1;
        if (interval > 1000) interval = 1000;
        _timer.Interval = interval;

        Text = $"TerrainSim - {(_running ? "Running" : "Paused")} - TPS {_tps} - Paint {_mode} - View {_view} - WeatherOverlay {_weatherOverlay} - SoilWater {(_soilWaterOverlay ? "On" : "Off")} - Shape {_shape} - Seed {_seed} - Tick {_g.TickCount}";
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.F1)
        {
            _stats.Visible = !_stats.Visible;
            if (_stats.Visible) _stats.BringToFront();
            return;
        }

        if (e.KeyCode == Keys.Space)
        {
            _running = !_running;
            ApplyTimer();
            return;
        }

        if (e.KeyCode == Keys.R)
        {
            _g.Randomize(seed: _seed, shape: _shape);
            Invalidate();
            return;
        }

        if (e.KeyCode == Keys.W)
        {
            _mode = (_mode == PaintMode.Rock) ? PaintMode.WaterSource : PaintMode.Rock;
            ApplyTimer();
            return;
        }

        if (e.KeyCode == Keys.V)
        {
            _weatherOverlay = _weatherOverlay == WeatherOverlayMode.Off ? WeatherOverlayMode.Humidity : WeatherOverlayMode.Off;
            ApplyTimer();
            return;
        }

        if (e.KeyCode == Keys.B)
        {
            _weatherOverlay = _weatherOverlay == WeatherOverlayMode.Temperature ? WeatherOverlayMode.Humidity : WeatherOverlayMode.Temperature;
            ApplyTimer();
            return;
        }

        if (e.KeyCode == Keys.C)
        {
            _weatherOverlay = _weatherOverlay == WeatherOverlayMode.Cloud ? WeatherOverlayMode.Humidity : WeatherOverlayMode.Cloud;
            ApplyTimer();
            return;
        }

        if (e.KeyCode == Keys.S)
        {
            _soilWaterOverlay = !_soilWaterOverlay;
            ApplyTimer();
            return;
        }

        if (e.KeyCode == Keys.T)
        {
            _shape = _shape switch
            {
                TerrainShape.Plain => TerrainShape.SlopeX,
                TerrainShape.SlopeX => TerrainShape.SlopeY,
                TerrainShape.SlopeY => TerrainShape.Basin,
                TerrainShape.Basin => TerrainShape.Ridge,
                _ => TerrainShape.Plain
            };
            _g.Randomize(seed: _seed, shape: _shape);
            ApplyTimer();
            return;
        }

        if (e.KeyCode == Keys.N)
        {
            _seed = Environment.TickCount;
            _g.Randomize(seed: _seed, shape: _shape);
            ApplyTimer();
            return;
        }

        if (e.KeyCode == Keys.D1) { _view = ViewMode.Moisture; ApplyTimer(); return; }
        if (e.KeyCode == Keys.D2) { _view = ViewMode.Plants; ApplyTimer(); return; }
        if (e.KeyCode == Keys.D3) { _view = ViewMode.Combined; ApplyTimer(); return; }
        if (e.KeyCode == Keys.D4 || e.KeyCode == Keys.NumPad4) { _view = ViewMode.WeatherOnlyHumidity; ApplyTimer(); return; }
        if (e.KeyCode == Keys.D5 || e.KeyCode == Keys.NumPad5) { _view = ViewMode.WeatherOnlyTemperature; ApplyTimer(); return; }
        if (e.KeyCode == Keys.D6 || e.KeyCode == Keys.NumPad6) { _view = ViewMode.WeatherOnlyCloud; ApplyTimer(); return; }
        if (e.KeyCode == Keys.D7 || e.KeyCode == Keys.NumPad7) { _view = ViewMode.Elevation; ApplyTimer(); return; }
        if (e.KeyCode == Keys.D8 || e.KeyCode == Keys.NumPad8) { _view = ViewMode.SoilDepth; ApplyTimer(); return; }

        if (e.KeyCode == Keys.OemOpenBrackets) { _tps = Math.Max(1, _tps - 10); ApplyTimer(); return; }
        if (e.KeyCode == Keys.OemCloseBrackets) { _tps = Math.Min(240, _tps + 10); ApplyTimer(); return; }
    }

    private void OnMouseDown(object? sender, MouseEventArgs e) => ApplyPaint(e);

    private void ApplyPaint(MouseEventArgs e)
    {
        var (x, y) = ScreenToCell(e.Location);
        if (!_g.InBounds(x, y)) return;

        if (e.Button == MouseButtons.Left)
        {
            if (_mode == PaintMode.Rock)
                _g.ToggleRock(x, y);
            else
                _g.SetWaterSource(x, y, on: true);
        }
        else if (e.Button == MouseButtons.Right)
        {
            if (_mode == PaintMode.WaterSource)
                _g.SetWaterSource(x, y, on: false);
            else
                _g.ToggleRock(x, y);
        }
    }

    private (int x, int y) ScreenToCell(Point p)
    {
        int x = (p.X - Pad) / Cell;
        int y = (p.Y - Pad) / Cell;
        return (x, y);
    }

    private static Color LerpColor(Color a, Color b, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        int r = (int)MathF.Round(a.R + (b.R - a.R) * t);
        int g = (int)MathF.Round(a.G + (b.G - a.G) * t);
        int bl = (int)MathF.Round(a.B + (b.B - a.B) * t);
        return Color.FromArgb(r, g, bl);
    }

    private static Color Brighten(Color c, int amount)
    {
        int r = Math.Clamp(c.R + amount, 0, 255);
        int g = Math.Clamp(c.G + amount, 0, 255);
        int b = Math.Clamp(c.B + amount, 0, 255);
        return Color.FromArgb(r, g, b);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.Clear(Color.Black);

        bool weatherOnly = _view == ViewMode.WeatherOnlyHumidity || _view == ViewMode.WeatherOnlyTemperature || _view == ViewMode.WeatherOnlyCloud;
        int elevMin = 255;
        int elevMax = 0;
        if (!weatherOnly)
        {
            using var contourPen = _view == ViewMode.Combined ? new Pen(Color.FromArgb(70, 0, 0, 0)) : null;
            for (int y = 0; y < _g.Height; y++)
                for (int x = 0; x < _g.Width; x++)
                {
                    int i = _g.Idx(x, y);
                    int px = Pad + x * Cell;
                    int py = Pad + y * Cell;

                    byte elev = _g.Elevation[i];
                    if (elev < elevMin) elevMin = elev;
                    if (elev > elevMax) elevMax = elev;

                    Color col;
                    float m = _g.Moisture[i];
                    float pPlant = _g.Plants[i];

                    col = _view switch
                    {
                        ViewMode.Moisture => Color.FromArgb(10, 10, Math.Clamp(10 + (int)(m * 220), 0, 255)),
                        ViewMode.Plants => Color.FromArgb(10, Math.Clamp(10 + (int)(pPlant * 220), 0, 255), 10),
                        ViewMode.Elevation => GetElevationColor(i),
                        ViewMode.SoilDepth => GetSoilDepthColor(i),
                        _ => BuildCombinedColor(i, m, pPlant)
                    };

                    using (var br = new SolidBrush(col))
                        g.FillRectangle(br, px, py, Cell - 1, Cell - 1);

                    if (contourPen != null)
                    {
                        if (x + 1 < _g.Width && elev != _g.Elevation[_g.Idx(x + 1, y)])
                            g.DrawLine(contourPen, px + Cell - 1, py, px + Cell - 1, py + Cell - 1);
                        if (y + 1 < _g.Height && elev != _g.Elevation[_g.Idx(x, y + 1)])
                            g.DrawLine(contourPen, px, py + Cell - 1, px + Cell - 1, py + Cell - 1);
                    }

                    if (_g.WaterSource[i])
                    {
                        using var brW = new SolidBrush(Color.Cyan);
                        int r = Math.Max(4, Cell / 2);
                        g.FillEllipse(brW, px + (Cell - r) / 2, py + (Cell - r) / 2, r, r);
                    }
                }
        }

        bool showWeatherOverlay = _weatherOverlay != WeatherOverlayMode.Off && !weatherOnly;
        bool showWeatherOnly = weatherOnly;
        if (showWeatherOverlay || showWeatherOnly)
        {
            int cellW = Math.Max(1, _g.Width / _g.WeatherWidth);
            int cellH = Math.Max(1, _g.Height / _g.WeatherHeight);
            int terrainRight = Pad + _g.Width * Cell;
            int terrainBottom = Pad + _g.Height * Cell;

            float humMin = 1f;
            float humMax = 0f;
            for (int i = 0; i < _g.WeatherHum01.Length; i++)
            {
                float h = _g.WeatherHum01[i];
                if (h < humMin) humMin = h;
                if (h > humMax) humMax = h;
            }

            float tempMin = 1f;
            float tempMax = 0f;
            if (_g.WeatherTemp01.Length > 0)
            {
                for (int i = 0; i < _g.WeatherTemp01.Length; i++)
                {
                    float t = _g.WeatherTemp01[i];
                    if (t < tempMin) tempMin = t;
                    if (t > tempMax) tempMax = t;
                }
            }

            float cloudMin = float.MaxValue;
            float cloudMax = float.MinValue;
            if (_g.CloudWater.Length > 0)
            {
                for (int i = 0; i < _g.CloudWater.Length; i++)
                {
                    float c = _g.CloudWater[i];
                    if (c < cloudMin) cloudMin = c;
                    if (c > cloudMax) cloudMax = c;
                }
            }
            float cloudSpan = cloudMax - cloudMin;

            for (int wy = 0; wy < _g.WeatherHeight; wy++)
            {
                int y0 = Pad + wy * cellH * Cell;
                int y1 = (wy == _g.WeatherHeight - 1) ? terrainBottom : Pad + (wy + 1) * cellH * Cell;
                for (int wx = 0; wx < _g.WeatherWidth; wx++)
                {
                    int x0 = Pad + wx * cellW * Cell;
                    int x1 = (wx == _g.WeatherWidth - 1) ? terrainRight : Pad + (wx + 1) * cellW * Cell;
                    int wi = wx + wy * _g.WeatherWidth;

                    bool humidity = showWeatherOnly ? _view == ViewMode.WeatherOnlyHumidity : _weatherOverlay == WeatherOverlayMode.Humidity;
                    if (humidity)
                    {
                        float hum01 = _g.Vapor[wi];
                        int a = showWeatherOnly ? Math.Clamp(140 + (int)(hum01 * 100), 0, 255) : Math.Clamp(60 + (int)(hum01 * 140), 0, 255);
                        int b = showWeatherOnly ? Math.Clamp(80 + (int)(hum01 * 175), 0, 255) : Math.Clamp((int)(hum01 * 150), 0, 255);
                        int r = showWeatherOnly ? Math.Clamp(50 + (int)(hum01 * 125), 0, 255) : b;
                        using var br = new SolidBrush(Color.FromArgb(a, r, 0, b));
                        g.FillRectangle(br, x0, y0, x1 - x0, y1 - y0);
                    }
                    else if (showWeatherOnly ? _view == ViewMode.WeatherOnlyTemperature : _weatherOverlay == WeatherOverlayMode.Temperature)
                    {
                        float temp01 = _g.WeatherTemp01.Length > wi ? _g.WeatherTemp01[wi] : 0f;
                        int a = showWeatherOnly ? Math.Clamp(140 + (int)(temp01 * 100), 0, 255) : Math.Clamp(30 + (int)(temp01 * 140), 0, 255);
                        int r = showWeatherOnly ? Math.Clamp(80 + (int)(temp01 * 175), 0, 255) : 255;
                        using var br = new SolidBrush(Color.FromArgb(a, r, 0, 0));
                        g.FillRectangle(br, x0, y0, x1 - x0, y1 - y0);
                    }
                    else
                    {
                        float cloudRaw = _g.CloudWater[wi];
                        float cloud01 = cloudSpan > 0f ? (cloudRaw - cloudMin) / cloudSpan : 0f;
                        cloud01 = Math.Clamp(cloud01, 0f, 1f);
                        int a = showWeatherOnly ? Math.Clamp(140 + (int)(cloud01 * 100), 0, 255) : Math.Clamp(70 + (int)(cloud01 * 140), 0, 255);
                        int b = Math.Clamp(120 + (int)(cloud01 * 120), 0, 255);
                        using var br = new SolidBrush(Color.FromArgb(a, 90, 90, b));
                        g.FillRectangle(br, x0, y0, x1 - x0, y1 - y0);
                    }

                    if (showWeatherOnly)
                    {
                        using var pen = new Pen(Color.FromArgb(120, 0, 0, 0));
                        g.DrawRectangle(pen, x0, y0, x1 - x0, y1 - y0);
                    }
                }
            }

            using var hudMinMax = new SolidBrush(Color.White);
            string tempRange = _g.WeatherTemp01.Length > 0 ? $"  TempMin {tempMin:F2}  TempMax {tempMax:F2}" : "";
            g.DrawString($"HumMin {humMin:F2}  HumMax {humMax:F2}{tempRange}", Font, hudMinMax, 5, 25);
        }

        if (_soilWaterOverlay)
        {
            int alphaBase = weatherOnly ? 120 : 60;
            for (int y = 0; y < _g.Height; y++)
                for (int x = 0; x < _g.Width; x++)
                {
                    int i = _g.Idx(x, y);
                    if (_g.Soil[i] == SoilType.Rock) continue;

                    float sw = _g.SoilWater01(i);
                    int a = Math.Clamp(alphaBase + (int)(sw * 140), 0, 255);
                    int b = Math.Clamp(50 + (int)(sw * 205), 0, 255);
                    using var br = new SolidBrush(Color.FromArgb(a, 0, 60, b));
                    int px = Pad + x * Cell;
                    int py = Pad + y * Cell;
                    g.FillRectangle(br, px, py, Cell - 1, Cell - 1);
                }
        }

        using var hud = new SolidBrush(Color.White);
        g.DrawString("Space=Pause/Run  R=Reseed  W=Paint Rock/Water  V=Weather Overlay  B=Toggle Hum/Temp  C=Clouds  S=SoilWater  T=Cycle Shape  N=New Seed  F1=Stats  [ ] TPS  1/2/3 View  4=Weather Hum  5=Weather Temp  6=Weather Cloud  7=Elevation  8=SoilDepth", Font, hud, 5, 5);
        if (!weatherOnly && elevMin <= elevMax)
            g.DrawString($"ElevMin {elevMin}  ElevMax {elevMax}", Font, hud, 5, 45);

        // Update title occasionally (cheap HUD)
        if (_g.TickCount % 10 == 0) ApplyTimer();
    }

    private Color BuildCombinedColor(int i, float surfaceMoisture, float plants)
    {
        float soilDepth = _g.SoilDepth[i];
        if (soilDepth < TerrainGrid.SimParams.BedrockThreshold && _g.Elevation[i] >= TerrainGrid.SimParams.BedrockElevationMin)
            return Color.FromArgb(95, 95, 95);

        float soil01 = _g.SoilWater01(i);
        float wet = Math.Clamp(
            surfaceMoisture * TerrainGrid.SimParams.WetVisualSurfaceWeight +
            soil01 * TerrainGrid.SimParams.WetVisualSoilWeight,
            0f, 1f);

        Color dryGray = Color.FromArgb(140, 135, 120);
        Color sand = Color.FromArgb(210, 195, 150);
        Color brown = Color.FromArgb(110, 80, 50);
        Color paleGreen = Color.FromArgb(130, 170, 90);
        Color deepGreen = Color.FromArgb(50, 120, 50);
        Color waterBlue = Color.FromArgb(40, 90, 180);

        Color baseCol;
        if (soilDepth < TerrainGrid.SimParams.BedrockThreshold)
        {
            baseCol = sand;
        }
        else if (wet < TerrainGrid.SimParams.WetBandDry)
        {
            baseCol = LerpColor(dryGray, sand, wet / TerrainGrid.SimParams.WetBandDry);
        }
        else if (wet < TerrainGrid.SimParams.WetBandLow)
        {
            baseCol = LerpColor(sand, brown, (wet - TerrainGrid.SimParams.WetBandDry) / (TerrainGrid.SimParams.WetBandLow - TerrainGrid.SimParams.WetBandDry));
        }
        else if (wet < TerrainGrid.SimParams.WetBandMid)
        {
            baseCol = LerpColor(brown, paleGreen, (wet - TerrainGrid.SimParams.WetBandLow) / (TerrainGrid.SimParams.WetBandMid - TerrainGrid.SimParams.WetBandLow));
        }
        else if (wet < TerrainGrid.SimParams.WetBandHigh)
        {
            baseCol = LerpColor(paleGreen, deepGreen, (wet - TerrainGrid.SimParams.WetBandMid) / (TerrainGrid.SimParams.WetBandHigh - TerrainGrid.SimParams.WetBandMid));
        }
        else if (wet < TerrainGrid.SimParams.WetBandVeryHigh)
        {
            baseCol = LerpColor(deepGreen, waterBlue, (wet - TerrainGrid.SimParams.WetBandHigh) / (TerrainGrid.SimParams.WetBandVeryHigh - TerrainGrid.SimParams.WetBandHigh));
        }
        else
        {
            baseCol = waterBlue;
        }

        Color plantGreen = Color.FromArgb(40, 140, 50);
        float plantT = Math.Clamp(plants * 0.35f, 0f, 1f);
        Color col = LerpColor(baseCol, plantGreen, plantT);

        float surfaceWater = _g.SurfaceWaterDepth[i];
        if (surfaceWater > 0f)
        {
            float water01 = Math.Clamp(surfaceWater / TerrainGrid.SimParams.WaterVisualDepthScale, 0f, 1f);
            col = LerpColor(col, waterBlue, 0.6f + 0.4f * water01);
        }

        float pioneer = _g.Pioneer[i];
        if (pioneer > 0f && plants < 0.2f)
        {
            Color pioneerTint = Color.FromArgb(150, 190, 110);
            float t = Math.Clamp(pioneer * (1f - plants) * 0.35f, 0f, 1f);
            col = LerpColor(col, pioneerTint, t);
        }

        float pulse = Math.Clamp(_g.RainPulse[i], 0f, 1f);
        if (pulse > 0f)
        {
            Color sheen = Color.FromArgb(30, 40, 60);
            col = LerpColor(col, sheen, pulse * 0.35f);
        }

        float elev01 = _g.Elevation[i] / 15f;
        // darker in lowlands, lighter in highlands
        int shade = (int)MathF.Round((elev01 - 0.5f) * 30f); // tweak 30f -> 20–40
        col = Brighten(col, shade);

        return col;
    }

    private Color GetElevationColor(int i)
    {
        float elev01 = _g.Elevation[i] / 15f;
        int v = Math.Clamp(30 + (int)MathF.Round(elev01 * 200f), 0, 255);
        return Color.FromArgb(v, v, v);
    }

    private Color GetSoilDepthColor(int i)
    {
        float d = Math.Clamp(_g.SoilDepth[i], 0f, 1f);
        Color shallow = Color.FromArgb(230, 210, 160);
        Color deep = Color.FromArgb(110, 70, 40);
        return LerpColor(shallow, deep, d);
    }
}