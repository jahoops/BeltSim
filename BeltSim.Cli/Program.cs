using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

var argsDict = Args.Parse(args);
string mapPath = argsDict.GetValueOrDefault("--map", "maps/test1.json");
string mode = argsDict.GetValueOrDefault("--mode", "sim");
int ticks = int.TryParse(argsDict.GetValueOrDefault("--ticks", "500"), out var t) ? t : 500;
int printEvery = int.TryParse(argsDict.GetValueOrDefault("--printEvery", "0"), out var pe) ? pe : 0;

var map = MapLoader.Load(mapPath);
if (mode.Equals("analyze", StringComparison.OrdinalIgnoreCase))
{
    ThroughputAnalyzer.Run(map, ticks);
    return;
}
var sim = new Simulator(map);
for (int i = 0; i < ticks; i++)
{
    sim.Tick();

    if (printEvery > 0 && ((i + 1) % printEvery == 0))
    {
        Console.WriteLine($"\nTick {i + 1}");
        sim.Render();
        Console.WriteLine($"Delivered={sim.Stats.Delivered} Spawned={sim.Stats.Spawned} Moved={sim.Stats.Moved} Blocked={sim.Stats.BlockedMoves}");
    }
}

Console.WriteLine($"\nTicks: {ticks}");
Console.WriteLine($"Delivered: {sim.Stats.Delivered}");
Console.WriteLine($"Spawned: {sim.Stats.Spawned}");
Console.WriteLine($"Moved: {sim.Stats.Moved}");
Console.WriteLine($"BlockedMoves: {sim.Stats.BlockedMoves}");
double deliveredPerTick = (double)sim.Stats.Delivered / ticks;
Console.WriteLine($"Delivered/tick: {deliveredPerTick:F3}"); Console.WriteLine(deliveredPerTick);

static class Args
    {
        public static Dictionary<string, string> Parse(string[] args)
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < args.Length; i++)
            {
                var a = args[i];
                if (!a.StartsWith("--")) continue;
                var v = (i + 1 < args.Length && !args[i + 1].StartsWith("--")) ? args[++i] : "true";
                d[a] = v;
            }
            return d;
        }
    }
