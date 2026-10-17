using AVAImpossibleMission.Engine;

namespace AVAImpossibleMission.Tests;

/// <summary>The whole mission, played by the computer agent: proof that it can be done in the time.</summary>
public class MissionTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void The_computer_agent_stops_the_professor_in_time(int seed)
    {
        var g = new GameEngine(HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "avaimpossiblemission-tests-" + Guid.NewGuid().ToString("N"))), seed) { Silent = true };
        g.Go();
        g.Pilot.Reset();
        int t = 0;
        for (; t < 60 * 2200 && g.Phase == Phase.Play; t++) g.Tick(g.Pilot.Decide(g));
        var left = TimeSpan.FromSeconds(g.Clock);
        output.WriteLine($"seed {seed}: {g.Phase} after {t / 60}s, {(int)left.TotalHours}:{left.Minutes:00} left, {g.Deaths} deaths, score {g.Score}");
        Assert.Equal(Phase.Won, g.Phase);
        Assert.Equal(Puzzle.Cards, g.CardsDone);
        Assert.True(g.Clock > 2 * 3600, "it should finish with hours to spare");
    }
}
