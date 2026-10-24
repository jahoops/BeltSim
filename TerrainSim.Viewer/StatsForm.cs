using System;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using TerrainSim.Core;

public sealed class StatsForm : Form
{
    private readonly Func<TerrainGrid> _getGrid;
    private readonly Func<bool> _getRunning;
    private readonly Action<bool> _setRunning;
    private readonly Func<int> _getTps;
    private readonly Action<int> _setTps;
    private readonly Func<MainForm.ViewMode> _getView;
    private readonly Action<MainForm.ViewMode> _setView;
    private readonly Func<MainForm.WeatherOverlayMode> _getWeatherOverlay;
    private readonly Action<MainForm.WeatherOverlayMode> _setWeatherOverlay;
    private readonly Func<bool> _getSoilWaterOverlay;
    private readonly Action<bool> _setSoilWaterOverlay;
    private readonly Func<TerrainShape> _getShape;
    private readonly Action<TerrainShape> _setShape;
    private readonly Func<int> _getSeed;
    private readonly Action _reseed;
    private readonly Action _newSeed;
    private readonly Action _stepOnce;
    private readonly Action _applyInitialFlood;

    private readonly System.Windows.Forms.Timer _timer = new();

    private readonly CheckBox _runningCheck = new();
    private readonly CheckBox _soilOverlayCheck = new();
    private readonly CheckBox _pauseUpdatesCheck = new();
    private readonly ComboBox _viewBox = new();
    private readonly ComboBox _weatherBox = new();
    private readonly ComboBox _shapeBox = new();
    private readonly NumericUpDown _tpsUpDown = new();
    private readonly Button _reseedButton = new();
    private readonly Button _newSeedButton = new();
    private readonly Button _stepButton = new();
    private readonly Button _floodButton = new();

    private readonly Label _simStats = new();
    private readonly Label _terrainStats = new();
    private readonly Label _weatherStats = new();
    private readonly Label _waterBudgetStats = new();
    private readonly Label _diagnostics = new();

    private readonly StringBuilder _terrainSb = new();
    private readonly StringBuilder _weatherSb = new();
    private readonly StringBuilder _rainRowsSb = new();
    private readonly StringBuilder _waterSb = new();
    private readonly StringBuilder _simSb = new();
    private readonly StringBuilder _diagSb = new();

    private bool _syncing;
    private float _lastTotalWater;
    private float _lastSoilWaterTotal;
    private float _lastMoistureTotal;
    private int _lastRainTick = -1;
    private DateTime _lastSampleTime;
    private DateTime _rainWindowStart;
    private int _rainEventCount;
    private float _rainEventSum;
    private float[]? _lastPlants;
    private float[]? _lastPioneers;

    public StatsForm(
        Func<TerrainGrid> getGrid,
        Func<bool> getRunning,
        Action<bool> setRunning,
        Func<int> getTps,
        Action<int> setTps,
        Func<MainForm.ViewMode> getView,
        Action<MainForm.ViewMode> setView,
        Func<MainForm.WeatherOverlayMode> getWeatherOverlay,
        Action<MainForm.WeatherOverlayMode> setWeatherOverlay,
        Func<bool> getSoilWaterOverlay,
        Action<bool> setSoilWaterOverlay,
        Func<TerrainShape> getShape,
        Action<TerrainShape> setShape,
        Func<int> getSeed,
        Action reseed,
        Action newSeed,
        Action stepOnce,
        Action applyInitialFlood)
    {
        _getGrid = getGrid;
        _getRunning = getRunning;
        _setRunning = setRunning;
        _getTps = getTps;
        _setTps = setTps;
        _getView = getView;
        _setView = setView;
        _getWeatherOverlay = getWeatherOverlay;
        _setWeatherOverlay = setWeatherOverlay;
        _getSoilWaterOverlay = getSoilWaterOverlay;
        _setSoilWaterOverlay = setSoilWaterOverlay;
        _getShape = getShape;
        _setShape = setShape;
        _getSeed = getSeed;
        _reseed = reseed;
        _newSeed = newSeed;
        _stepOnce = stepOnce;
        _applyInitialFlood = applyInitialFlood;

        Text = "TerrainSim - Stats / Controls";
        StartPosition = FormStartPosition.Manual;
        Width = 760;
        Height = 900;
        Font = new Font(Font.FontFamily, 9f, FontStyle.Regular);

        BuildUi();

        _timer.Interval = 1000;
        _timer.Tick += (_, __) => SafeUpdateStats();
        _timer.Start();

        _lastSampleTime = DateTime.UtcNow;
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 6,
            AutoScroll = true
        };

        var controlsGroup = new GroupBox { Text = "Controls", Dock = DockStyle.Top, Height = 180 };
        var controlsLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 4
        };

        _runningCheck.Text = "Running";
        _runningCheck.CheckedChanged += (_, __) => { if (!_syncing) _setRunning(_runningCheck.Checked); };

        _soilOverlayCheck.Text = "SoilWater Overlay";
        _soilOverlayCheck.CheckedChanged += (_, __) => { if (!_syncing) _setSoilWaterOverlay(_soilOverlayCheck.Checked); };

        _pauseUpdatesCheck.Text = "Pause Updates";

        _viewBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _viewBox.Items.AddRange(new object[]
        {
            MainForm.ViewMode.Moisture,
            MainForm.ViewMode.Plants,
            MainForm.ViewMode.Combined,
            MainForm.ViewMode.WeatherOnlyHumidity,
            MainForm.ViewMode.WeatherOnlyTemperature,
            MainForm.ViewMode.WeatherOnlyCloud,
            MainForm.ViewMode.Elevation,
            MainForm.ViewMode.SoilDepth
        });
        _viewBox.SelectedIndexChanged += (_, __) =>
        {
            if (_syncing) return;
            if (_viewBox.SelectedItem is MainForm.ViewMode view) _setView(view);
        };

        _weatherBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _weatherBox.Items.AddRange(new object[]
        {
            MainForm.WeatherOverlayMode.Off,
            MainForm.WeatherOverlayMode.Humidity,
            MainForm.WeatherOverlayMode.Temperature,
            MainForm.WeatherOverlayMode.Cloud
        });
        _weatherBox.SelectedIndexChanged += (_, __) =>
        {
            if (_syncing) return;
            if (_weatherBox.SelectedItem is MainForm.WeatherOverlayMode mode) _setWeatherOverlay(mode);
        };

        _shapeBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _shapeBox.Items.AddRange(new object[]
        {
            TerrainShape.Plain,
            TerrainShape.SlopeX,
            TerrainShape.SlopeY,
            TerrainShape.Basin,
            TerrainShape.Ridge
        });
        _shapeBox.SelectedIndexChanged += (_, __) =>
        {
            if (_syncing) return;
            if (_shapeBox.SelectedItem is TerrainShape shape) _setShape(shape);
        };

        _tpsUpDown.Minimum = 1;
        _tpsUpDown.Maximum = 240;
        _tpsUpDown.ValueChanged += (_, __) => { if (!_syncing) _setTps((int)_tpsUpDown.Value); };

        _reseedButton.Text = "Reseed";
        _reseedButton.Click += (_, __) => _reseed();

        _newSeedButton.Text = "New Seed";
        _newSeedButton.Click += (_, __) => _newSeed();

        _stepButton.Text = "Step Once";
        _stepButton.Click += (_, __) => _stepOnce();

        _floodButton.Text = "Apply Initial Flood";
        _floodButton.Click += (_, __) => _applyInitialFlood();

        controlsLayout.Controls.Add(_runningCheck, 0, 0);
        controlsLayout.Controls.Add(_soilOverlayCheck, 1, 0);
        controlsLayout.Controls.Add(_pauseUpdatesCheck, 2, 0);
        controlsLayout.Controls.Add(new Label { Text = "View", AutoSize = true }, 0, 1);
        controlsLayout.Controls.Add(_viewBox, 1, 1);
        controlsLayout.Controls.Add(new Label { Text = "Weather Overlay", AutoSize = true }, 2, 1);
        controlsLayout.Controls.Add(_weatherBox, 3, 1);
        controlsLayout.Controls.Add(new Label { Text = "Shape", AutoSize = true }, 0, 2);
        controlsLayout.Controls.Add(_shapeBox, 1, 2);
        controlsLayout.Controls.Add(new Label { Text = "TPS", AutoSize = true }, 2, 2);
        controlsLayout.Controls.Add(_tpsUpDown, 3, 2);
        controlsLayout.Controls.Add(_reseedButton, 0, 3);
        controlsLayout.Controls.Add(_newSeedButton, 1, 3);
        controlsLayout.Controls.Add(_stepButton, 2, 3);
        controlsLayout.Controls.Add(_floodButton, 3, 3);

        controlsGroup.Controls.Add(controlsLayout);

        var simGroup = new GroupBox { Text = "Sim", Dock = DockStyle.Top, Height = 80 };
        _simStats.Dock = DockStyle.Fill;
        _simStats.AutoSize = false;
        _simStats.Padding = new Padding(8);
        simGroup.Controls.Add(_simStats);

        var terrainGroup = new GroupBox { Text = "Terrain Stats", Dock = DockStyle.Top, Height = 230 };
        _terrainStats.Dock = DockStyle.Fill;
        _terrainStats.AutoSize = false;
        _terrainStats.Padding = new Padding(8);
        terrainGroup.Controls.Add(_terrainStats);

        var weatherGroup = new GroupBox { Text = "Weather Stats", Dock = DockStyle.Top, Height = 170 };
        _weatherStats.Dock = DockStyle.Fill;
        _weatherStats.AutoSize = false;
        _weatherStats.Padding = new Padding(8);
        weatherGroup.Controls.Add(_weatherStats);

        var budgetGroup = new GroupBox { Text = "Water Budget", Dock = DockStyle.Top, Height = 130 };
        _waterBudgetStats.Dock = DockStyle.Fill;
        _waterBudgetStats.AutoSize = false;
        _waterBudgetStats.Padding = new Padding(8);
        budgetGroup.Controls.Add(_waterBudgetStats);

        var diagGroup = new GroupBox { Text = "Diagnostics", Dock = DockStyle.Top, Height = 160 };
        _diagnostics.Dock = DockStyle.Fill;
        _diagnostics.AutoSize = false;
        _diagnostics.Padding = new Padding(8);
        _diagnostics.BorderStyle = BorderStyle.FixedSingle;
        diagGroup.Controls.Add(_diagnostics);

        root.Controls.Add(controlsGroup);
        root.Controls.Add(simGroup);
        root.Controls.Add(terrainGroup);
        root.Controls.Add(weatherGroup);
        root.Controls.Add(budgetGroup);
        root.Controls.Add(diagGroup);

        Controls.Add(root);
    }

    private void SafeUpdateStats()
    {
        if (InvokeRequired)
        {
            BeginInvoke(new Action(SafeUpdateStats));
            return;
        }

        if (_pauseUpdatesCheck.Checked) return;

        UpdateStats();
    }

    private void UpdateStats()
    {
        var g = _getGrid();
        if (g == null) return;

        _syncing = true;
        _runningCheck.Checked = _getRunning();
        _soilOverlayCheck.Checked = _getSoilWaterOverlay();
        _tpsUpDown.Value = Math.Clamp(_getTps(), (int)_tpsUpDown.Minimum, (int)_tpsUpDown.Maximum);
        _viewBox.SelectedItem = _getView();
        _weatherBox.SelectedItem = _getWeatherOverlay();
        _shapeBox.SelectedItem = _getShape();
        _syncing = false;

        int n = g.Width * g.Height;
        if (n == 0) return;

        int[] elevCounts = new int[16];
        int soilBelow = 0;
        int soilLow = 0;
        int soilMid = 0;
        int soilHigh = 0;

        float moistSum = 0f;
        float moistMin = 1f;
        float moistMax = 0f;
        int moistGt01 = 0;
        int moistGt03 = 0;
        int moistGt06 = 0;

        float soilWaterSum = 0f;
        float soilWaterMin = 1f;
        float soilWaterMax = 0f;
        int soilWaterGt01 = 0;
        int soilWaterGt03 = 0;
        int soilWaterGt06 = 0;

        float rainPulseSum = 0f;
        float rainPulseMax = 0f;
        int rainPulseGt01 = 0;

        float plantsSum = 0f;
        float plantsMax = 0f;
        int plantsGt01 = 0;
        int plantsGt05 = 0;

        float pioneerSum = 0f;
        float pioneerMax = 0f;
        int pioneerGt01 = 0;

        int waterLike = 0;

        float surfaceWaterSum = 0f;
        float surfaceWaterMax = 0f;
        int surfaceWaterCount = 0;

        float plantsDeltaSum = 0f;
        float pioneerDeltaSum = 0f;

        if (_lastPlants == null || _lastPlants.Length != n)
        {
            _lastPlants = new float[n];
            _lastPioneers = new float[n];
        }

        for (int i = 0; i < n; i++)
        {
            byte elev = g.Elevation[i];
            elevCounts[elev]++;

            float d = g.SoilDepth[i];
            if (d <= TerrainGrid.SimParams.SoilDepthEps) soilBelow++;
            else if (d < 0.2f) soilLow++;
            else if (d < 0.6f) soilMid++;
            else soilHigh++;

            float m = g.Moisture[i];
            moistSum += m;
            if (m < moistMin) moistMin = m;
            if (m > moistMax) moistMax = m;
            if (m > 0.1f) moistGt01++;
            if (m > 0.3f) moistGt03++;
            if (m > 0.6f) moistGt06++;

            float sw = g.SoilWater01(i);
            soilWaterSum += sw;
            if (sw < soilWaterMin) soilWaterMin = sw;
            if (sw > soilWaterMax) soilWaterMax = sw;
            if (sw > 0.1f) soilWaterGt01++;
            if (sw > 0.3f) soilWaterGt03++;
            if (sw > 0.6f) soilWaterGt06++;

            float rp = g.RainPulse[i];
            rainPulseSum += rp;
            if (rp > rainPulseMax) rainPulseMax = rp;
            if (rp > 0.1f) rainPulseGt01++;

            float p = g.Plants[i];
            plantsSum += p;
            if (p > plantsMax) plantsMax = p;
            if (p > 0.1f) plantsGt01++;
            if (p > 0.5f) plantsGt05++;

            float pn = g.Pioneer[i];
            pioneerSum += pn;
            if (pn > pioneerMax) pioneerMax = pn;
            if (pn > 0.1f) pioneerGt01++;

            plantsDeltaSum += MathF.Abs(p - _lastPlants[i]);
            pioneerDeltaSum += MathF.Abs(pn - _lastPioneers[i]);
            _lastPlants[i] = p;
            _lastPioneers[i] = pn;

            if (m > TerrainGrid.SimParams.WetBandVeryHigh || m > 0.85f)
                waterLike++;

            float swd = g.SurfaceWaterDepth[i];
            surfaceWaterSum += swd;
            if (swd > surfaceWaterMax) surfaceWaterMax = swd;
            if (swd > 0f) surfaceWaterCount++;
        }

        float moistMean = moistSum / n;
        float soilWaterMean = soilWaterSum / n;
        float rainPulseMean = rainPulseSum / n;
        float plantsMean = plantsSum / n;
        float pioneerMean = pioneerSum / n;
        float plantsDeltaMean = plantsDeltaSum / n;
        float pioneerDeltaMean = pioneerDeltaSum / n;

        _terrainSb.Clear();
        _terrainSb.AppendLine($"Grid {g.Width}x{g.Height}  Tick {g.TickCount}  Seed {_getSeed()}");
        _terrainSb.Append("Elevation counts: ");
        for (int i = 0; i < elevCounts.Length; i++)
            _terrainSb.Append($"{i}:{elevCounts[i]} ");
        _terrainSb.AppendLine();
        _terrainSb.AppendLine($"SoilDepth %: <=eps {(soilBelow * 100f / n):F1}%  0.02..0.2 {(soilLow * 100f / n):F1}%  0.2..0.6 {(soilMid * 100f / n):F1}%  0.6..1 {(soilHigh * 100f / n):F1}%");
        _terrainSb.AppendLine($"Moisture mean/min/max {moistMean:F3}/{moistMin:F3}/{moistMax:F3}  >0.1 {moistGt01}  >0.3 {moistGt03}  >0.6 {moistGt06}");
        _terrainSb.AppendLine($"SoilWater01 mean/min/max {soilWaterMean:F3}/{soilWaterMin:F3}/{soilWaterMax:F3}  >0.1 {soilWaterGt01}  >0.3 {soilWaterGt03}  >0.6 {soilWaterGt06}");
        _terrainSb.AppendLine($"RainPulse mean/max {rainPulseMean:F3}/{rainPulseMax:F3}  >0.1 {rainPulseGt01}");
        _terrainSb.AppendLine($"Plants mean/max {plantsMean:F3}/{plantsMax:F3}  >0.1 {plantsGt01}  >0.5 {plantsGt05}  Δmean {plantsDeltaMean:F4}");
        _terrainSb.AppendLine($"Pioneer mean/max {pioneerMean:F3}/{pioneerMax:F3}  >0.1 {pioneerGt01}  Δmean {pioneerDeltaMean:F4}");
        _terrainSb.AppendLine($"Water-like cells {waterLike}");
        _terrainSb.AppendLine($"SurfaceWater mean/max {surfaceWaterSum / n:F3}/{surfaceWaterMax:F3}  cells {surfaceWaterCount}");
        _terrainStats.Text = _terrainSb.ToString();

        float humMin = 1f, humMax = 0f, humSum = 0f;
        float tempMin = 1f, tempMax = 0f, tempSum = 0f;
        float windMin = float.MaxValue, windMax = 0f, windSum = 0f;
        float cloudMin = 1f, cloudMax = 0f, cloudSum = 0f;
        float vaporMin = 1f, vaporMax = 0f, vaporSum = 0f;
        int cloudGtThreshold = 0;
        float cloudWeightedY = 0f;
        float cloudWeight = 0f;
        int wn = g.WeatherWidth * g.WeatherHeight;
        for (int i = 0; i < wn; i++)
        {
            float hum = g.Vapor[i];
            float temp = g.WeatherTemp01[i];
            float wx = g.WeatherWindX[i];
            float wy = g.WeatherWindY[i];
            float wind = MathF.Sqrt(wx * wx + wy * wy);
            float cloud = g.CloudWater[i];

            vaporSum += hum;
            if (hum < vaporMin) vaporMin = hum;
            if (hum > vaporMax) vaporMax = hum;

            tempSum += temp;
            if (temp < tempMin) tempMin = temp;
            if (temp > tempMax) tempMax = temp;

            windSum += wind;
            if (wind < windMin) windMin = wind;
            if (wind > windMax) windMax = wind;

            cloudSum += cloud;
            if (cloud < cloudMin) cloudMin = cloud;
            if (cloud > cloudMax) cloudMax = cloud;

            if (cloud > TerrainGrid.SimParams.StormOnThreshold) cloudGtThreshold++;

            int wyIdx = i / g.WeatherWidth;
            cloudWeightedY += wyIdx * cloud;
            cloudWeight += cloud;
        }

        float humMean = wn > 0 ? vaporSum / wn : 0f;
        float tempMean = wn > 0 ? tempSum / wn : 0f;
        float windMean = wn > 0 ? windSum / wn : 0f;
        float cloudMean = wn > 0 ? cloudSum / wn : 0f;
        float cloudCentroidY = cloudWeight > 0f ? cloudWeightedY / cloudWeight : 0f;

        _weatherSb.Clear();
        _weatherSb.AppendLine($"Vapor min/mean/max {vaporMin:F3}/{humMean:F3}/{vaporMax:F3}");
        _weatherSb.AppendLine($"Temp min/mean/max {tempMin:F3}/{tempMean:F3}/{tempMax:F3}");
        _weatherSb.AppendLine($"Wind min/mean/max {windMin:F3}/{windMean:F3}/{windMax:F3}");
        _weatherSb.AppendLine($"Cloud min/mean/max {cloudMin:F3}/{cloudMean:F3}/{cloudMax:F3}  >Threshold {cloudGtThreshold}");
        _weatherSb.AppendLine($"Cloud centroid Y {cloudCentroidY:F2}");
        _weatherSb.Append(_rainRowsSb.ToString());
        _weatherStats.Text = _weatherSb.ToString();

        float totalSurface = moistSum;
        float totalSoil = 0f;
        for (int i = 0; i < n; i++) totalSoil += g.SoilWater[i];
        float totalCloud = 0f;
        for (int i = 0; i < wn; i++) totalCloud += g.CloudWater[i];
        float totalWater = totalSurface + totalSoil + totalCloud;

        DateTime now = DateTime.UtcNow;
        double dt = Math.Max(0.001, (now - _lastSampleTime).TotalSeconds);
        float deltaPerSec = (float)((totalWater - _lastTotalWater) / dt);
        if (float.IsNaN(deltaPerSec) || float.IsInfinity(deltaPerSec)) deltaPerSec = 0f;
        _lastTotalWater = totalWater;
        _lastSampleTime = now;

        float deltaSoilWater = totalSoil - _lastSoilWaterTotal;
        float deltaMoisture = totalSurface - _lastMoistureTotal;
        _lastSoilWaterTotal = totalSoil;
        _lastMoistureTotal = totalSurface;

        float approxRainPerSec = g.LastRainTotal * _getTps();
        float approxEvapPerSec = g.LastEvapTotal * _getTps();
        float approxInfilPerSec = g.LastInfiltrationTotal * _getTps();
        float approxUptakePerSec = g.LastUptakeTotal * _getTps();

        if (_rainWindowStart == default)
            _rainWindowStart = now;

        if (g.LastRainTotal > 0f)
        {
            _rainEventCount++;
            _rainEventSum += g.LastRainTotal;
            _lastRainTick = g.TickCount;
        }

        double rainWindowSec = (now - _rainWindowStart).TotalSeconds;
        float eventsPerMin = rainWindowSec > 0 ? (float)(_rainEventCount * 60.0 / rainWindowSec) : 0f;
        float meanRainPerEvent = _rainEventCount > 0 ? _rainEventSum / _rainEventCount : 0f;
        if (rainWindowSec >= 60.0)
        {
            _rainWindowStart = now;
            _rainEventCount = 0;
            _rainEventSum = 0f;
        }

        float secSinceRain = _lastRainTick >= 0 ? (g.TickCount - _lastRainTick) / (float)Math.Max(1, _getTps()) : float.NaN;

        _waterSb.Clear();
        _waterSb.AppendLine($"Surface {totalSurface:F2}  Soil {totalSoil:F2}  Cloud {totalCloud:F2}  Total {totalWater:F2}");
        _waterSb.AppendLine($"DeltaTotal/sec {deltaPerSec:F3}  DeltaMoist {deltaMoisture:F3}  DeltaSoil {deltaSoilWater:F3}");
        _waterSb.AppendLine($"LastTick Evap {g.LastEvapTotal:F4}  Rain {g.LastRainTotal:F4}  Infil {g.LastInfiltrationTotal:F4}  Uptake {g.LastUptakeTotal:F4}");
        _waterSb.AppendLine($"Approx/sec Evap {approxEvapPerSec:F3}  Rain {approxRainPerSec:F3}  Infil {approxInfilPerSec:F3}  Uptake {approxUptakePerSec:F3}");
        _waterSb.AppendLine($"Rain events/min {eventsPerMin:F2}  Mean/event {meanRainPerEvent:F3}  StormCooldown {g.StormCooldownActiveCount}");
        _waterSb.Append($"Weather step interval {TerrainGrid.SimParams.WeatherStepInterval}  LastRainTick {_lastRainTick}  SecSinceRain {(float.IsNaN(secSinceRain) ? "N/A" : secSinceRain.ToString("F1"))}");
        _waterBudgetStats.Text = _waterSb.ToString();

        _simSb.Clear();
        _simSb.Append($"Running {_getRunning()}  TPS {_getTps()}  View {_getView()}  Weather {_getWeatherOverlay()}  SoilWater {_getSoilWaterOverlay()}  Shape {_getShape()}");
        _simStats.Text = _simSb.ToString();

        _diagSb.Clear();
        if (deltaPerSec < -0.01f && cloudMean < 0.02f && g.LastRainTotal <= 0.0001f)
            _diagSb.AppendLine("Atmosphere lacks genesis water; consider initial flood or higher initial cloud water.");
        if (g.LastRainTotal > 0.0001f && deltaPerSec < -0.02f)
            _diagSb.AppendLine("Evap seems high relative to rain; check BaseEvap/SoilEvapBase/StormDumpFraction.");
        if ((soilBelow * 1f / n) > 0.6f)
            _diagSb.AppendLine("Soil capacity near zero; consider higher soil floor or stronger deposition/pioneer buildup.");
        if (cloudMean > 0.1f && g.LastRainTotal <= 0.0001f)
            _diagSb.AppendLine("Clouds high but rain low; check thresholds (CloudThreshold/CloudHumThreshold).");
        if (_diagSb.Length == 0) _diagSb.AppendLine("No obvious issues detected.");

        _diagnostics.Text = _diagSb.ToString();
    }
}
