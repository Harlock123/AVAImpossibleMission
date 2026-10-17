using AVAImpossibleMission.Engine;

namespace AVAImpossibleMission.Tests;

/// <summary>
/// How the computer agent gets on over a whole mission (set IMPOSSIBLE_PROBE to run it, for tuning: "1" for the
/// pilot, "lazy" for an agent who never touches the controls).
/// </summary>
public sealed class PilotProbe(ITestOutputHelper output)
{
    [Fact]
    public void How_the_computer_plays()
    {
        string? mode = Environment.GetEnvironmentVariable("IMPOSSIBLE_PROBE");
        if (mode is null) return;
        foreach (int seed in new[] { 1, 2, 3, 4, 5 })
        {
            var g = new GameEngine(HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "impossible-probe-" + Guid.NewGuid().ToString("N"))), seed) { Silent = true };
            g.Go();
            g.Pilot.Reset();
            var deaths = new Dictionary<string, int>();
            var doing = new Dictionary<string, int>();
            var rooms = new Dictionary<string, int>();
            int t = 0, lastDeaths = 0;
            for (; t < 60 * 3000 && g.State == GameState.Playing && g.Phase is Phase.Play or Phase.Start; t++)
            {
                var c = mode == "lazy" ? default : g.Pilot.Decide(g);
                g.Tick(c);
                doing[g.Pilot.Doing] = doing.GetValueOrDefault(g.Pilot.Doing) + 1;
                if (g.Room is { } r) rooms[r.Name] = rooms.GetValueOrDefault(r.Name) + 1;
                if (g.Deaths != lastDeaths)
                {
                    lastDeaths = g.Deaths;
                    string key = $"{g.Room?.Name} {g.Cause}";
                    deaths[key] = deaths.GetValueOrDefault(key) + 1;
                }
            }
            var left = TimeSpan.FromSeconds(g.Clock);
            output.WriteLine($"seed {seed}: {g.Phase} after {t / 60}s real, clock {(int)left.TotalHours}:{left.Minutes:00} left, cards {g.CardsDone}, pieces held {g.Held.Count}, codes {g.Codes}, rooms done {g.Complex.Rooms.Count(r => r.Done)}/16, deaths {g.Deaths}, score {g.Score}");
            output.WriteLine("   deaths " + string.Join(", ", deaths.OrderByDescending(d => d.Value).Select(d => $"{d.Key} x{d.Value}")));
            output.WriteLine("   doing " + string.Join(", ", doing.OrderByDescending(d => d.Value).Take(12).Select(d => $"{d.Key} {d.Value / 60}s")));
            output.WriteLine("   rooms " + string.Join(", ", rooms.OrderByDescending(d => d.Value).Take(8).Select(d => $"{d.Key} {d.Value / 60}s")));
        }
    }
}
