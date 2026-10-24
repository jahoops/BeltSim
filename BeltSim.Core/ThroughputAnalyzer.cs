using System;

public static class ThroughputAnalyzer
{
    public static void Run(Map map, int ticks)
    {
        var sim = new Simulator(map);
        for (int i = 0; i < ticks; i++)
        {
            sim.Tick();
        }

        double deliveredPerTick = ticks == 0 ? 0 : (double)sim.Stats.Delivered / ticks;
        double movedPerTick = ticks == 0 ? 0 : (double)sim.Stats.Moved / ticks;

        Console.WriteLine($"Delivered/tick: {deliveredPerTick:F3}");
        Console.WriteLine($"Moved/tick: {movedPerTick:F3}");
    }
}
