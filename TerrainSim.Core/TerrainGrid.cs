using System;

namespace TerrainSim.Core;

public enum SoilType : byte
{
    Soil = 0,
    Rock = 1
}

public enum TerrainShape : byte
{
    Plain,
    SlopeX,
    SlopeY,
    Basin,
    Ridge
}

public sealed class TerrainGrid
{
    public int Width { get; }
    public int Height { get; }

    // Flat arrays: index = x + y*Width
    public byte[] Elevation { get; }
    public SoilType[] Soil { get; }
    public float[] SoilDepth { get; }
    public float[] Moisture { get; }
    public float[] Plants { get; }
    public float[] Pioneer { get; }
    public float[] SoilWater { get; }
    public float[] RainPulse { get; }
    public float[] DryStress { get; }
    public bool[] WaterSource { get; }
    public float[] SurfaceWaterDepth { get; }

    public int WeatherWidth { get; } = 16;
    public int WeatherHeight { get; } = 16;
    public float[] WeatherTemp01 { get; }
    public float[] WeatherHum01 { get; }
    public float[] WeatherWindX { get; }
    public float[] WeatherWindY { get; }
    public float[] CloudWater { get; }
    public float[] CloudNoise { get; }
    public float[] Vapor { get; }

    private readonly float[] _nextMoisture;
    private readonly float[] _nextCloud;
    private readonly float[] _nextVapor;
    private readonly float[] _wetMemory;
    private readonly bool[] _stormActive;
    private readonly int[] _stormCooldown;
    private int _weatherSeed = 1;

    public int TickCount { get; private set; }
    public int StormCooldownActiveCount { get; private set; }

    public float Season01 { get; private set; }
    public float SeasonPhase { get; private set; }

    public float LastEvapTotal { get; private set; }
    public float LastRainTotal { get; private set; }
    public float LastUptakeTotal { get; private set; }
    public float LastInfiltrationTotal { get; private set; }
    public float[] LastRainByWeatherRow { get; }

    public TerrainGrid(int width, int height)
    {
        Width = width;
        Height = height;

        int n = width * height;
        Elevation = new byte[n];
        Soil = new SoilType[n];
        SoilDepth = new float[n];
        Moisture = new float[n];
        Plants = new float[n];
        Pioneer = new float[n];
        SoilWater = new float[n];
        RainPulse = new float[n];
        DryStress = new float[n];
        WaterSource = new bool[n];
        SurfaceWaterDepth = new float[n];

        _nextMoisture = new float[n];

        int wn = WeatherWidth * WeatherHeight;
        WeatherTemp01 = new float[wn];
        WeatherHum01 = new float[wn];
        WeatherWindX = new float[wn];
        WeatherWindY = new float[wn];
        CloudWater = new float[wn];
        CloudNoise = new float[wn];
        Vapor = new float[wn];
        _nextCloud = new float[wn];
        _nextVapor = new float[wn];
        _wetMemory = new float[n];
        _stormActive = new bool[wn];
        _stormCooldown = new int[wn];
        LastRainByWeatherRow = new float[WeatherHeight];

        InitializeWeather();
    }

    public int Idx(int x, int y) => x + y * Width;

    public bool InBounds(int x, int y) => (uint)x < (uint)Width && (uint)y < (uint)Height;

    public void Randomize(int seed = 1)
    {
        Randomize(seed, TerrainShape.Plain);
    }

    public void Randomize(int seed, TerrainShape shape)
    {
        _weatherSeed = seed;
        var rng = new Random(seed);
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
            {
                int i = Idx(x, y);

                float nx = Width <= 1 ? 0.5f : (float)x / (Width - 1);
                float ny = Height <= 1 ? 0.5f : (float)y / (Height - 1);
                float dx = nx - 0.5f;
                float dy = ny - 0.5f;
                float dist01 = Math.Clamp(MathF.Sqrt(dx * dx + dy * dy) * 2f, 0f, 1f); 
                float baseElev01 = shape switch
                {
                    TerrainShape.Plain => 0.35f,
                    TerrainShape.SlopeX => nx,
                    TerrainShape.SlopeY => ny,
                    TerrainShape.Basin => Math.Clamp(dist01 * 1.1f - 0.05f, 0f, 1f),
                    TerrainShape.Ridge => Math.Clamp(1f - dist01 * 1.1f + 0.05f, 0f, 1f),
                    _ => 0.35f
                };

                float noiseAmp = shape switch
                {
                    TerrainShape.Plain => SimParams.TerrainNoiseAmpPlain,
                    TerrainShape.SlopeX => SimParams.TerrainNoiseAmpHills,
                    TerrainShape.SlopeY => SimParams.TerrainNoiseAmpHills,
                    TerrainShape.Basin => SimParams.TerrainNoiseAmpMountains,
                    TerrainShape.Ridge => SimParams.TerrainNoiseAmpMountains,
                    _ => SimParams.TerrainNoiseAmpPlain
                };

                float noise = (float)((rng.NextDouble() * 2.0 - 1.0) * noiseAmp);
                float elev01 = Math.Clamp(baseElev01 + noise, 0f, 1f);
                Elevation[i] = (byte)Math.Clamp((int)MathF.Round(elev01 * 15f), 0, 15);

                float rockChance = 0.10f + 0.05f * (Elevation[i] / 15f);
                bool isRock = Elevation[i] >= SimParams.BedrockElevationMin && rng.NextDouble() < (rockChance * 0.4f);
                SoilDepth[i] = isRock ? 0f : Math.Max(SimParams.SoilDepthFloor, (float)(0.35 + rng.NextDouble() * 0.65));
                Soil[i] = SoilDepth[i] <= SimParams.SoilDepthEps ? SoilType.Rock : SoilType.Soil;

                Moisture[i] = 0f;
                Plants[i] = 0f;
                Pioneer[i] = 0f;
                SoilWater[i] = 0f;
                RainPulse[i] = 0f;
                DryStress[i] = 0f;
                WaterSource[i] = false;
                SurfaceWaterDepth[i] = 0f;
            }

        byte elevMin = 255;
        byte elevMax = 0;
        for (int i = 0; i < Elevation.Length; i++)
        {
            byte e = Elevation[i];
            if (e < elevMin) elevMin = e;
            if (e > elevMax) elevMax = e;
        }

        int span = elevMax - elevMin;
        if (span > 0 && span < 4)
        {
            for (int i = 0; i < Elevation.Length; i++)
            {
                float t = (Elevation[i] - elevMin) / (float)span;
                Elevation[i] = (byte)Math.Clamp((int)MathF.Round(t * 15f), 0, 15);
            }
        }

        // Add a couple water sources as starting "springs"
        // (disabled by default to avoid permanent point sources)
        // for (int k = 0; k < 6; k++)
        // {
        //     int sx = rng.Next(0, Width);
        //     int sy = rng.Next(0, Height);
        //     int si = Idx(sx, sy);
        //     if (Soil[si] == SoilType.Soil)
        //         WaterSource[si] = true;
        // }

        InitializeWeather();
        for (int i = 0; i < CloudWater.Length; i++)
        {
            Vapor[i] = WeatherHum01[i];
            CloudWater[i] = 0f;
        }

        ApplyInitialFlood();

        if (SimParams.InitialSoilWater01 > 0f)
        {
            for (int i = 0; i < SoilWater.Length; i++)
            {
                if (SoilDepth[i] <= SimParams.SoilDepthEps) continue;
                float elev01 = Elevation[i] / 15f;
                float bias = MathF.Pow(1f - elev01, SimParams.InitialFloodBiasPower);
                float capacity = SoilCapacity(i);
                float target = capacity * SimParams.InitialSoilWater01 * bias;
                if (target > SoilWater[i])
                    SoilWater[i] = Math.Min(capacity, target);
            }
        }

        for (int i = 0; i < _wetMemory.Length; i++)
            _wetMemory[i] = Moisture[i];
        Array.Clear(_stormActive, 0, _stormActive.Length);
        Array.Clear(_stormCooldown, 0, _stormCooldown.Length);

        if (SimParams.InitialFloodSettleTicks > 0)
        {
            for (int i = 0; i < SimParams.InitialFloodSettleTicks; i++)
            {
                RunoffStep();
                InfiltrationStep();
                EvaporationStep();
                CloudAdvectionStep();
                CondenseEvaporateCloudStep();
                RainOutStep();
            }
        }

        TickCount = 0;
    }

    public void Step()
    {
        bool doWeather = TickCount % SimParams.WeatherStepInterval == 0;
        if (doWeather)
            StepWeather();

        LastEvapTotal = 0f;
        LastRainTotal = 0f;
        LastUptakeTotal = 0f;
        LastInfiltrationTotal = 0f;
        Array.Clear(LastRainByWeatherRow, 0, LastRainByWeatherRow.Length);

        // Seasonality
        SeasonPhase = (TickCount % SimParams.SeasonTicks) * (MathF.Tau / SimParams.SeasonTicks);
        Season01 = 0.5f + 0.5f * MathF.Sin(SeasonPhase);
        float decayMultiplier = SimParams.DryDecayMultiplier + (SimParams.WetDecayMultiplier - SimParams.DryDecayMultiplier) * Season01;

        Array.Clear(_nextMoisture, 0, _nextMoisture.Length);

        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                int i = Idx(x, y);

                float m = Moisture[i];

                float outflow = 0f;

                PushToNeighbor(x - 1, y, i, m, ref outflow, SimParams.UphillPenalty, SimParams.DiffusionRate);
                PushToNeighbor(x + 1, y, i, m, ref outflow, SimParams.UphillPenalty, SimParams.DiffusionRate);
                PushToNeighbor(x, y - 1, i, m, ref outflow, SimParams.UphillPenalty, SimParams.DiffusionRate);
                PushToNeighbor(x, y + 1, i, m, ref outflow, SimParams.UphillPenalty, SimParams.DiffusionRate);

                int wx = x * WeatherWidth / Width;
                int wy = y * WeatherHeight / Height;
                int wi = WeatherIdx(wx, wy);
                float temp01 = WeatherTemp01[wi];
                float hum01 = Vapor[wi];
                float windX = WeatherWindX[wi];
                float windY = WeatherWindY[wi];
                float windSpeed = MathF.Sqrt(windX * windX + windY * windY);
                float plantShield = Math.Clamp(Plants[i], 0f, 1f);

                float elev01 = Elevation[i] / 15f;
                float evapScale = 1f + elev01 * SimParams.ElevEvapScale;
                float localDecay = SimParams.SurfaceDecay * (SimParams.LowlandDecayScale + (SimParams.HighlandDecayScale - SimParams.LowlandDecayScale) * elev01);

                float evap = SimParams.BaseEvap * evapScale * (0.5f + temp01) * (0.5f + windSpeed) * (1f - plantShield);
                float weatherDelta = -evap;

                float next = m - outflow - localDecay * decayMultiplier + weatherDelta;
                if (next < 0f) next = 0f;

                _nextMoisture[i] += next;
                if (_nextMoisture[i] > 1f) _nextMoisture[i] = 1f;
            }
        }

        Array.Copy(_nextMoisture, Moisture, _nextMoisture.Length);

        RunoffStep();
        PioneerStep();
        InfiltrationStep();
        PlantStep();
        EvaporationStep();

        if (doWeather)
        {
            CloudAdvectionStep();
            CondenseEvaporateCloudStep();
            RainOutStep();
        }

        SoilBuildingStep();
        DryStressStep();
        ErosionStep();

        DecayRainPulse();

        TickCount++;
    }

    private void DecayRainPulse()
    {
        for (int i = 0; i < RainPulse.Length; i++)
            RainPulse[i] *= SimParams.RainPulseDecay;
    }

    // Slow soil evolution creates biome migration and landscape memory.
    private void SoilBuildingStep()
    {
        for (int i = 0; i < SoilDepth.Length; i++)
        {
            float wet = Math.Max(Moisture[i], RainPulse[i]);
            float pioneer = Pioneer[i];
            if (pioneer > 0f)
                SoilDepth[i] = Math.Clamp(SoilDepth[i] + SimParams.PioneerSoilBuildRate * pioneer * wet, 0f, 1f);

            if (SoilDepth[i] <= SimParams.SoilDepthEps)
            {
                float deposit = SimParams.DepositionRate * RainPulse[i];
                if (deposit > 0f)
                    SoilDepth[i] = Math.Clamp(SoilDepth[i] + deposit, 0f, 1f);
                continue;
            }

            float p = Plants[i];
            if (p <= 0f) continue;

            float m = SoilWater01(i);
            float build = SimParams.SoilBuildRate * m * p;
            if (build <= 0f) continue;

            SoilDepth[i] = Math.Clamp(SoilDepth[i] + build, 0f, 1f);
        }
    }

    private void PioneerStep()
    {
        for (int i = 0; i < Pioneer.Length; i++)
        {
            float wetInstant = Math.Max(Moisture[i], RainPulse[i]);
            float wet = _wetMemory[i] + (wetInstant - _wetMemory[i]) * SimParams.WetMemoryBlend;
            _wetMemory[i] = wet;

            float p = Pioneer[i];
            float prev = p;

            if (wet >= SimParams.PioneerGrowThresh)
                p += SimParams.PioneerGrowRate;
            else if (wet <= SimParams.PioneerDieThresh)
                p -= SimParams.PioneerDieRate;
            else
                p -= SimParams.PioneerMidbandRate;

            if (SimParams.PioneerShadeFactor > 0f && Plants[i] > 0f)
                p *= 1f - Plants[i] * SimParams.PioneerShadeFactor;

            p = Math.Clamp(p, 0f, 1f);
            Pioneer[i] = p;

            float delta = prev - p;
            if (delta > 0f)
                SoilDepth[i] = Math.Clamp(SoilDepth[i] + SimParams.PioneerLitterRate * delta, 0f, 1f);
        }
    }

    private void DryStressStep()
    {
        for (int i = 0; i < DryStress.Length; i++)
        {
            float wet = Math.Max(SoilWater01(i), Math.Max(Moisture[i], RainPulse[i]));
            if (wet < SimParams.DryWetThreshold)
            {
                DryStress[i] = Math.Min(1f, DryStress[i] + SimParams.DryStressBuildRate);
            }
            else
            {
                DryStress[i] *= SimParams.DryStressRecovery;
            }
        }
    }

    private void ErosionStep()
    {
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                int i = Idx(x, y);
                if (SoilDepth[i] <= 0f) continue;

                float stress = DryStress[i];
                if (stress <= SimParams.DroughtThreshold) continue;

                float droughtFactor = (stress - SimParams.DroughtThreshold) / (1f - SimParams.DroughtThreshold);

                int wx = x * WeatherWidth / Width;
                int wy = y * WeatherHeight / Height;
                int wi = WeatherIdx(wx, wy);
                float windX = WeatherWindX[wi];
                float windY = WeatherWindY[wi];
                float wind01 = Math.Clamp(MathF.Sqrt(windX * windX + windY * windY), 0f, 1f);

                float erode = SimParams.ErosionRate * wind01 * droughtFactor;
                if (erode <= 0f) continue;

                float floor = Elevation[i] >= SimParams.BedrockElevationMin ? 0f : SimParams.SoilDepthFloor;
                SoilDepth[i] = Math.Clamp(Math.Max(floor, SoilDepth[i] - erode), 0f, 1f);
            }
        }
    }

    private void InfiltrationStep()
    {
        for (int i = 0; i < Moisture.Length; i++)
        {
            float capacity = SoilCapacity(i);
            if (capacity <= SimParams.SoilDepthEps)
            {
                SoilWater[i] = 0f;
                continue;
            }

            float space = Math.Max(0f, capacity - SoilWater[i]);
            float infilRate = SimParams.InfiltrationRate * (1f + SimParams.RainInfiltrationBoost * RainPulse[i]);
            float transfer = Math.Min(Moisture[i], space) * infilRate;
            if (transfer > 0f)
            {
                Moisture[i] -= transfer;
                SoilWater[i] += transfer;
                if (SoilWater[i] > capacity) SoilWater[i] = capacity;

                LastInfiltrationTotal += transfer;
            }

            if (Moisture[i] >= 1f - SimParams.SoilDepthEps && SurfaceWaterDepth[i] > 0f && space > 0f)
            {
                float seep = Math.Min(SurfaceWaterDepth[i], space) * SimParams.SurfaceWaterSeepRate;
                if (seep > 0f)
                {
                    SurfaceWaterDepth[i] -= seep;
                    SoilWater[i] += seep;
                    if (SoilWater[i] > capacity) SoilWater[i] = capacity;
                    LastInfiltrationTotal += seep;
                }
            }
        }
    }

    private void PlantStep()
    {
        for (int i = 0; i < Moisture.Length; i++)
        {
            if (SoilDepth[i] <= SimParams.SoilDepthEps)
            {
                Plants[i] = 0f;
                SoilWater[i] = 0f;
                continue;
            }

            if (SurfaceWaterDepth[i] > SimParams.WaterPlantKillDepth)
            {
                Plants[i] = 0f;
                Pioneer[i] = 0f;
                continue;
            }

            float p = Plants[i];
            float m = SoilWater01(i);

            if (m >= SimParams.GrowThresh)
            {
                p += SimParams.GrowRate;
            }
            else if (m <= SimParams.DieThresh)
            {
                p -= SimParams.DieRate;
            }
            else
            {
                p -= SimParams.MidbandRate;
            }

            if (p < 0f) p = 0f;
            if (p > 1f) p = 1f;

            Plants[i] = p;

            if (p > 0f && SoilWater[i] > 0f)
            {
                int x = i % Width;
                int y = i / Width;
                int wx = x * WeatherWidth / Width;
                int wy = y * WeatherHeight / Height;
                int wi = WeatherIdx(wx, wy);
                float temp01 = WeatherTemp01[wi];

                float uptake = SimParams.PlantUptakeBase * (1f + SimParams.PlantUptakeTempScale * temp01) * p;
                SoilWater[i] = Math.Max(0f, SoilWater[i] - uptake);
                Vapor[wi] = Math.Max(0f, Vapor[wi] + uptake * SimParams.PlantTranspireRate);
                WeatherHum01[wi] = Math.Clamp(Vapor[wi], 0f, 1f);
                LastUptakeTotal += uptake;
            }
        }
    }

    private void EvaporationStep()
    {
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                int i = Idx(x, y);
                if (SoilDepth[i] <= SimParams.SoilDepthEps)
                {
                    SoilWater[i] = 0f;
                }

                int wx = x * WeatherWidth / Width;
                int wy = y * WeatherHeight / Height;
                int wi = WeatherIdx(wx, wy);
                float temp01 = WeatherTemp01[wi];
                float hum01 = Vapor[wi];
                float windX = WeatherWindX[wi];
                float windY = WeatherWindY[wi];
                float windSpeed = MathF.Sqrt(windX * windX + windY * windY);
                float plantShield = Math.Clamp(Plants[i], 0f, 1f);

                float elev01 = Elevation[i] / 15f;
                float evapScale = 1f + elev01 * SimParams.SoilEvapElevScale;
                float humScale = 1.1f - 0.6f * hum01;

                float evap = SimParams.SoilEvapBase * evapScale * humScale * (0.5f + temp01) * (0.5f + windSpeed) * (1f - plantShield);
                if (evap > 0f)
                {
                    if (SoilDepth[i] > SimParams.SoilDepthEps)
                        SoilWater[i] = Math.Max(0f, SoilWater[i] - evap);
                    Moisture[i] = Math.Max(0f, Moisture[i] - evap * SimParams.SurfaceEvapFraction);

                    Vapor[wi] = Math.Max(0f, Vapor[wi] + evap * SimParams.EvapToVaporRate);
                    WeatherHum01[wi] = Math.Clamp(Vapor[wi], 0f, 1f);
                    LastEvapTotal += evap;
                }

                if (SurfaceWaterDepth[i] > 0f)
                {
                    float waterEvap = SimParams.WaterEvapBase * humScale * (0.5f + temp01) * (0.5f + windSpeed);
                    if (waterEvap > 0f)
                    {
                        float removed = Math.Min(SurfaceWaterDepth[i], waterEvap);
                        SurfaceWaterDepth[i] -= removed;
                        Vapor[wi] = Math.Max(0f, Vapor[wi] + removed * SimParams.EvapToVaporRate);
                        WeatherHum01[wi] = Math.Clamp(Vapor[wi], 0f, 1f);
                        LastEvapTotal += removed;
                    }
                }
            }
        }
    }

    private void ApplyInitialFlood()
    {
        if (SimParams.InitialFloodAmount <= 0f) return;

        for (int i = 0; i < Moisture.Length; i++)
        {
            float elev01 = Elevation[i] / 15f;
            float bias = MathF.Pow(1f - elev01, SimParams.InitialFloodBiasPower);
            float add = SimParams.InitialFloodAmount * bias;
            float moistAdd = Math.Min(1f - Moisture[i], add * SimParams.RainToMoistureScale);
            Moisture[i] = Math.Clamp(Moisture[i] + moistAdd, 0f, 1f);
            float leftover = add * SimParams.RainToSurfaceWaterScale - moistAdd;
            if (leftover > 0f)
                SurfaceWaterDepth[i] += leftover;
        }
    }

    public void ApplyInitialFloodNow() => ApplyInitialFlood();

    private int WeatherIdx(int x, int y) => x + y * WeatherWidth;

    private void InitializeWeather()
    {
        var rng = new Random(_weatherSeed * 9973 + 17);
        for (int i = 0; i < CloudNoise.Length; i++)
            CloudNoise[i] = (float)(rng.NextDouble() * 2.0 - 1.0);

        var tempNoise = new float[CloudNoise.Length];
        for (int pass = 0; pass < 2; pass++)
        {
            for (int y = 0; y < WeatherHeight; y++)
            {
                for (int x = 0; x < WeatherWidth; x++)
                {
                    int i = WeatherIdx(x, y);
                    float sum = CloudNoise[i];
                    int count = 1;
                    if (x > 0) { sum += CloudNoise[WeatherIdx(x - 1, y)]; count++; }
                    if (x + 1 < WeatherWidth) { sum += CloudNoise[WeatherIdx(x + 1, y)]; count++; }
                    if (y > 0) { sum += CloudNoise[WeatherIdx(x, y - 1)]; count++; }
                    if (y + 1 < WeatherHeight) { sum += CloudNoise[WeatherIdx(x, y + 1)]; count++; }
                    tempNoise[i] = sum / count;
                }
            }

            Array.Copy(tempNoise, CloudNoise, CloudNoise.Length);
        }

        for (int y = 0; y < WeatherHeight; y++)
        {
            float lat = WeatherHeight <= 1 ? 0f : (float)y / (WeatherHeight - 1);
            for (int x = 0; x < WeatherWidth; x++)
            {
                int i = WeatherIdx(x, y);
                float wave = 0.5f + 0.5f * MathF.Sin(x * 0.6f);
                WeatherTemp01[i] = Math.Clamp(0.15f + 0.65f * lat + 0.05f * wave, 0f, 1f);
                float humBase = Math.Clamp(0.15f + 0.50f * lat + 0.05f * wave, 0f, 1f);
                WeatherHum01[i] = Math.Clamp(humBase + CloudNoise[i] * SimParams.CloudHumNoiseAmp, 0f, 1f);
                Vapor[i] = WeatherHum01[i];
                WeatherWindX[i] = 0.20f * MathF.Sin(y * 0.12f);
                WeatherWindY[i] = 0.25f * MathF.Sin(x * 0.10f);
            }
        }
    }

    private void StepWeather()
    {
        int drift = (TickCount / 90) % WeatherWidth;
        for (int y = WeatherHeight - 1; y >= 1; y--)
        {
            for (int x = 0; x < WeatherWidth; x++)
            {
                int srcX = (x - drift + WeatherWidth) % WeatherWidth;
                int src = WeatherIdx(srcX, y - 1);
                int dst = WeatherIdx(x, y);
                WeatherTemp01[dst] = WeatherTemp01[src];
                WeatherWindX[dst] = WeatherWindX[src];
                WeatherWindY[dst] = WeatherWindY[src];
            }
        }

        for (int x = 0; x < WeatherWidth; x++)
        {
            int i = WeatherIdx(x, 0);
            float wave = 0.5f + 0.5f * MathF.Sin((TickCount * 0.005f) + x * 0.6f);
            WeatherTemp01[i] = Math.Clamp(0.15f + 0.10f * wave, 0f, 1f);
            WeatherWindX[i] = 0.20f * MathF.Sin((TickCount * 0.0025f) + x * 0.10f);
            WeatherWindY[i] = 0.25f * MathF.Sin((TickCount * 0.003f) + x * 0.08f);
        }

        for (int wy = 0; wy < WeatherHeight; wy++)
        {
            int y0 = wy * Height / WeatherHeight;
            int y1 = (wy + 1) * Height / WeatherHeight;
            for (int wx = 0; wx < WeatherWidth; wx++)
            {
                int x0 = wx * Width / WeatherWidth;
                int x1 = (wx + 1) * Width / WeatherWidth;

                float elevSum = 0f;
                float plantSum = 0f;
                int count = 0;

                for (int y = y0; y < y1; y++)
                    for (int x = x0; x < x1; x++)
                    {
                        int ti = Idx(x, y);
                        elevSum += Elevation[ti];
                        if (SoilDepth[ti] > SimParams.SoilDepthEps)
                        {
                            plantSum += Plants[ti];
                        }
                        count++;
                    }

                if (count == 0) continue;

                float elev01 = (elevSum / count) / 15f;
                float plant01 = plantSum / count;

                int wi = WeatherIdx(wx, wy);
                float targetTemp = Math.Clamp(WeatherTemp01[wi] - 0.04f * elev01, 0f, 1f);
                WeatherTemp01[wi] = WeatherTemp01[wi] + (targetTemp - WeatherTemp01[wi]) * (1f - SimParams.WeatherRelax);

                float windScale = 1f - 0.35f * plant01;
                float targetWindX = WeatherWindX[wi] * windScale;
                float targetWindY = WeatherWindY[wi] * windScale;
                WeatherWindX[wi] = WeatherWindX[wi] + (targetWindX - WeatherWindX[wi]) * (1f - SimParams.WeatherRelax);
                WeatherWindY[wi] = WeatherWindY[wi] + (targetWindY - WeatherWindY[wi]) * (1f - SimParams.WeatherRelax);
            }
        }

        for (int i = 0; i < Vapor.Length; i++)
            WeatherHum01[i] = Math.Clamp(Vapor[i], 0f, 1f);
    }

    private void CloudAdvectionStep()
    {
        Array.Clear(_nextCloud, 0, _nextCloud.Length);
        Array.Clear(_nextVapor, 0, _nextVapor.Length);

        for (int y = 0; y < WeatherHeight; y++)
        {
            for (int x = 0; x < WeatherWidth; x++)
            {
                int i = WeatherIdx(x, y);
                float c = CloudWater[i];
                float v = Vapor[i];

                float wx = WeatherWindX[i];
                float wy = WeatherWindY[i];
                float wind = MathF.Sqrt(wx * wx + wy * wy);
                float wind01 = Math.Min(0.4f, Math.Clamp(wind, 0f, 1f));

                int dx = wx > 0.15f ? 1 : wx < -0.15f ? -1 : 0;
                int dy = wy > 0.15f ? 1 : wy < -0.15f ? -1 : 0;
                int nx = (x + dx + WeatherWidth) % WeatherWidth;
                int ny = (y + dy + WeatherHeight) % WeatherHeight;
                int ni = WeatherIdx(nx, ny);

                float movedCloud = c * SimParams.VaporCloudAdvectRate * wind01;
                if (movedCloud > c) movedCloud = c;
                float movedVapor = v * SimParams.VaporCloudAdvectRate * wind01;
                if (movedVapor > v) movedVapor = v;

                _nextCloud[i] += c - movedCloud;
                _nextCloud[ni] += movedCloud;
                _nextVapor[i] += v - movedVapor;
                _nextVapor[ni] += movedVapor;
            }
        }

        Array.Copy(_nextCloud, CloudWater, _nextCloud.Length);
        Array.Copy(_nextVapor, Vapor, _nextVapor.Length);

        if (SimParams.VaporCloudDiffuseRate > 0f)
        {
            for (int y = 0; y < WeatherHeight; y++)
            {
                for (int x = 0; x < WeatherWidth; x++)
                {
                    int i = WeatherIdx(x, y);
                    int x0 = (x - 1 + WeatherWidth) % WeatherWidth;
                    int x1 = (x + 1) % WeatherWidth;
                    int y0 = (y - 1 + WeatherHeight) % WeatherHeight;
                    int y1 = (y + 1) % WeatherHeight;

                    float sumCloud = CloudWater[i];
                    float sumVapor = Vapor[i];
                    int count = 1;

                    sumCloud += CloudWater[WeatherIdx(x0, y)]; sumVapor += Vapor[WeatherIdx(x0, y)]; count++;
                    sumCloud += CloudWater[WeatherIdx(x1, y)]; sumVapor += Vapor[WeatherIdx(x1, y)]; count++;
                    sumCloud += CloudWater[WeatherIdx(x, y0)]; sumVapor += Vapor[WeatherIdx(x, y0)]; count++;
                    sumCloud += CloudWater[WeatherIdx(x, y1)]; sumVapor += Vapor[WeatherIdx(x, y1)]; count++;

                    float avgCloud = sumCloud / count;
                    float avgVapor = sumVapor / count;

                    _nextCloud[i] = CloudWater[i] + (avgCloud - CloudWater[i]) * SimParams.VaporCloudDiffuseRate;
                    _nextVapor[i] = Vapor[i] + (avgVapor - Vapor[i]) * SimParams.VaporCloudDiffuseRate;
                }
            }

            Array.Copy(_nextCloud, CloudWater, _nextCloud.Length);
            Array.Copy(_nextVapor, Vapor, _nextVapor.Length);
        }

        for (int i = 0; i < Vapor.Length; i++)
            WeatherHum01[i] = Math.Clamp(Vapor[i], 0f, 1f);
    }

    private void CondenseEvaporateCloudStep()
    {
        for (int i = 0; i < Vapor.Length; i++)
        {
            float temp = WeatherTemp01[i];
            float sat = Math.Clamp(SimParams.SatBase + SimParams.SatTempSlope * temp, 0f, 1f);
            float v = Vapor[i];

            if (v > sat)
            {
                float cond = SimParams.CondenseRate * (v - sat);
                Vapor[i] = Math.Max(0f, v - cond);
                CloudWater[i] += cond;
            }
            else
            {
                float evapC = SimParams.CloudEvapRate * (sat - v);
                if (evapC > CloudWater[i]) evapC = CloudWater[i];
                CloudWater[i] -= evapC;
                Vapor[i] = Math.Max(0f, v + evapC);
            }

            WeatherHum01[i] = Math.Clamp(Vapor[i], 0f, 1f);
        }
    }

    private void RainOutStep()
    {
        int cooldownCount = 0;
        for (int wy = 0; wy < WeatherHeight; wy++)
        {
            int y0 = wy * Height / WeatherHeight;
            int y1 = (wy + 1) * Height / WeatherHeight;
            for (int wx = 0; wx < WeatherWidth; wx++)
            {
                int x0 = wx * Width / WeatherWidth;
                int x1 = (wx + 1) * Width / WeatherWidth;
                int wi = WeatherIdx(wx, wy);

                if (_stormCooldown[wi] > 0)
                    _stormCooldown[wi]--;

                float cloud = CloudWater[wi];
                if (_stormActive[wi])
                {
                    if (cloud < SimParams.StormOffThreshold)
                    {
                        _stormActive[wi] = false;
                        _stormCooldown[wi] = SimParams.StormCooldownTicks;
                    }
                }
                else if (_stormCooldown[wi] == 0 && cloud >= SimParams.StormOnThreshold)
                {
                    _stormActive[wi] = true;
                }

                if (_stormCooldown[wi] > 0) cooldownCount++;
                if (!_stormActive[wi]) continue;

                float rain = Math.Min(SimParams.MaxRainPerStormStep, cloud * SimParams.StormDrainRate);
                if (rain > cloud) rain = cloud;
                if (rain <= 0f) continue;

                CloudWater[wi] -= rain;

                float rainMask = 0.5f + 0.5f * CloudNoise[wi];
                float rainToSurface = rain * SimParams.CloudRainToSurface * rainMask;
                float rainPulseGain = rain * SimParams.RainPulseGain;
                for (int y = y0; y < y1; y++)
                    for (int x = x0; x < x1; x++)
                    {
                        int ti = Idx(x, y);
                        float moistAdd = Math.Min(1f - Moisture[ti], rainToSurface * SimParams.RainToMoistureScale);
                        Moisture[ti] = Math.Clamp(Moisture[ti] + moistAdd, 0f, 1f);
                        float leftover = rainToSurface * SimParams.RainToSurfaceWaterScale - moistAdd;
                        if (leftover > 0f)
                            SurfaceWaterDepth[ti] += leftover;
                        RainPulse[ti] = Math.Min(1f, RainPulse[ti] + rainPulseGain);
                        LastRainTotal += rainToSurface;
                        LastRainByWeatherRow[wy] += rainToSurface;
                    }
            }
        }

        StormCooldownActiveCount = cooldownCount;
    }

    private void PushToNeighbor(int nx, int ny, int srcIdx, float srcMoisture, ref float outflow, float uphillPenalty, float diffusionRate)
    {
        if (!InBounds(nx, ny)) return;

        int ni = Idx(nx, ny);

        float dstMoisture = Moisture[ni];
        float diff = srcMoisture - dstMoisture;
        if (diff <= 0f) return;

        float w = 1f;
        if (Elevation[ni] > Elevation[srcIdx])
            w *= (1f - uphillPenalty);

        float amt = diffusionRate * diff * w;
        if (amt > srcMoisture - outflow) amt = srcMoisture - outflow;
        if (amt <= 0f) return;

        _nextMoisture[ni] += amt;
        if (_nextMoisture[ni] > 1f) _nextMoisture[ni] = 1f;

        outflow += amt;
    }

    private void RunoffStep()
    {
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                int i = Idx(x, y);
                float m = Moisture[i];
                float sw = SurfaceWaterDepth[i];
                if (m <= SimParams.RunoffThreshold && sw <= 0f) continue;

                int bestX = x;
                int bestY = y;
                byte bestElev = Elevation[i];

                void Consider(int nx, int ny)
                {
                    if (!InBounds(nx, ny)) return;
                    int ni = Idx(nx, ny);
                    byte e = Elevation[ni];
                    if (e < bestElev)
                    {
                        bestElev = e;
                        bestX = nx;
                        bestY = ny;
                    }
                }

                Consider(x - 1, y);
                Consider(x + 1, y);
                Consider(x, y - 1);
                Consider(x, y + 1);

                if (bestX == x && bestY == y) continue;

                int bi = Idx(bestX, bestY);

                if (m > SimParams.RunoffThreshold)
                {
                    float moved = (m - SimParams.RunoffThreshold) * SimParams.RunoffRate;
                    if (moved > 0f)
                    {
                        Moisture[i] -= moved;
                        Moisture[bi] = Math.Min(1f, Moisture[bi] + moved);
                    }
                }

                if (sw > 0f)
                {
                    float moved = sw * SimParams.SurfaceWaterRunoffRate;
                    if (moved > 0f)
                    {
                        SurfaceWaterDepth[i] -= moved;
                        SurfaceWaterDepth[bi] += moved;
                    }
                }
            }
        }
    }

    public static class SimParams
    {
        public const float UphillPenalty = 0.35f;
        public const float DiffusionRate = 0.18f;
        public const float SurfaceDecay = 0f;
        public const float BaseEvap = 0.0004f;
        public const float BaseCond = 0f;
        public const float RainRate = 0f;
        public const float ElevEvapScale = 0.35f;
        public const float SpringBoost = 0.02f;
        public const float TerrainNoiseAmpPlain = 0.08f;
        public const float TerrainNoiseAmpHills = 0.18f;
        public const float TerrainNoiseAmpMountains = 0.32f;

        public const int WeatherStepInterval = 16;
        public const float WeatherRelax = 0.995f;

        public const float InitialFloodAmount = 0.25f;
        public const float InitialFloodBiasPower = 2.0f;
        public const int InitialFloodSettleTicks = 120;
        public const float InitialSoilWater01 = 0.25f;

        public const float RainToMoistureScale = 0.6f;
        public const float RainToSurfaceWaterScale = 1f;
        public const float SurfaceWaterSeepRate = 0.01f;
        public const float SurfaceWaterRunoffRate = 0.10f;
        public const float WaterEvapBase = 0.00005f;
        public const float WaterPlantKillDepth = 0.05f;
        public const float WaterVisualDepthScale = 0.50f;

        public const int SeasonTicks = 36000;
        public const float WetDecayMultiplier = 0.7f;
        public const float DryDecayMultiplier = 1.3f;
        public const float MaxHumidityGain = 0f;

        public const float BaseSoilCapacity = 1.0f;
        public const float SoilDepthEps = 0.02f;
        public const float SoilDepthFloor = 0.10f;
        public const int BedrockElevationMin = 12;
        public const float InfiltrationRate = 0.18f;
        public const float RainInfiltrationBoost = 1.2f;
        public const float SoilEvapBase = 0.0004f;
        public const float SurfaceEvapFraction = 0.06f;
        public const float SoilEvapElevScale = 0.4f;

        public const float SoilBuildRate = 0.00002f;
        public const float ErosionRate = 0.00003f;
        public const float SoilDryThreshold = 0.15f;
        public const float DepositionRate = 0.000002f;

        public const float DryWetThreshold = 0.12f;
        public const float DryStressBuildRate = 0.0003f;
        public const float DryStressRecovery = 0.985f;
        public const float DroughtThreshold = 0.7f;

        public const float WetVisualSurfaceWeight = 0.6f;
        public const float WetVisualSoilWeight = 0.8f;
        public const float WetVisualSoilWeight = 0.8f;
        public const float WetBandDry = 0.12f;
        public const float WetBandLow = 0.25f;
        public const float WetBandMid = 0.45f;
        public const float WetBandHigh = 0.65f;
        public const float BedrockThreshold = 0.05f;

        public const float LowlandDecayScale = 0.6f;
        public const float HighlandDecayScale = 1.2f;

        public const float EvapToVaporRate = 0.25f;
        public const float VaporCloudAdvectRate = 0.04f;
        public const float VaporCloudDiffuseRate = 0.02f;
        public const float SatBase = 0.30f;
        public const float SatTempSlope = 0.40f;
        public const float CondenseRate = 0.03f;
        public const float CloudEvapRate = 0.06f;
        public const float StormOnThreshold = 0.60f;
        public const float StormOffThreshold = 0.40f;
        public const int StormCooldownTicks = 1125;
        public const float StormDrainRate = 0.25f;
        public const float MaxRainPerStormStep = 0.02f;
        public const float RainOutRate = 0.18f;
        public const float CloudRainToSurface = 0.03f;
        public const float InitialCloudWater = 0.20f;
        public const float CloudHumNoiseAmp = 0.06f;
        public const float CloudSeedNoiseAmp = 0.35f;
        public const float CloudMax = float.MaxValue;
        public const float PlantTranspireRate = 1.0f;
        public const float WetMemoryBlend = 0.02f;

        public const float InstabilityThreshold = 0.35f;
        public const float StormDumpFraction = 0.88f;
        public const float SeasonalInstabilityScale = 0.12f;

        public const float RunoffThreshold = 0.35f;
        public const float RunoffRate = 0.35f;

        public const float RainPulseGain = 0.75f;
        public const float RainPulseDecay = 0.985f;

        public const float GrowThresh = 0.40f;
        public const float DieThresh = 0.20f;
        public const float GrowRate = 0.0010f;
        public const float DieRate = 0.0005f;
        public const float MidbandRate = 0.0001f;

        public const float PioneerGrowThresh = 0.35f;
        public const float PioneerDieThresh = 0.10f;
        public const float PioneerGrowRate = 0.0004f;
        public const float PioneerDieRate = 0.0002f;
        public const float PioneerMidbandRate = 0.0001f;
        public const float PioneerSoilBuildRate = 0.00002f;
        public const float PioneerLitterRate = 0.002f;
        public const float PioneerShadeFactor = 0.6f;

        public const float PlantUptakeBase = 0.00025f;
        public const float PlantUptakeTempScale = 0.75f;

        public const float RainFalloff = 0.05f;
    }

    public float SoilWater01(int i)
    {
        float capacity = SoilCapacity(i);
        if (capacity <= SimParams.SoilDepthEps) return 0f;
        return Math.Clamp(SoilWater[i] / capacity, 0f, 1f);
    }

    public float SoilCapacity(int i) => SimParams.BaseSoilCapacity * Math.Max(0f, SoilDepth[i]);

    // Edits
    public void ToggleRock(int x, int y)
    {
        if (!InBounds(x, y)) return;
        int i = Idx(x, y);
        if (SoilDepth[i] <= SimParams.SoilDepthEps)
        {
            SoilDepth[i] = 0.7f;
            Soil[i] = SoilType.Soil;
        }
        else
        {
            SoilDepth[i] = 0f;
            Soil[i] = SoilType.Rock;
        }

        if (SoilDepth[i] <= SimParams.SoilDepthEps)
        {
            Moisture[i] = 0f;
            Plants[i] = 0f;
            Pioneer[i] = 0f;
            SoilWater[i] = 0f;
            RainPulse[i] = 0f;
            DryStress[i] = 0f;
            WaterSource[i] = false;
            SurfaceWaterDepth[i] = 0f;
        }
    }

    public void SetWaterSource(int x, int y, bool on)
    {
        if (!InBounds(x, y)) return;
        int i = Idx(x, y);
        if (SoilDepth[i] <= SimParams.SoilDepthEps) return;
        WaterSource[i] = on;
    }

    public void Raise(int x, int y)
    {
        if (!InBounds(x, y)) return;
        int i = Idx(x, y);
        if (Elevation[i] < 15) Elevation[i]++;
    }

    public void Lower(int x, int y)
    {
        if (!InBounds(x, y)) return;
        int i = Idx(x, y);
        if (Elevation[i] > 0) Elevation[i]--;
    }
}}