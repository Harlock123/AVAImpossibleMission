using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using AVAImpossibleMission.Engine;
using AVAImpossibleMission.Views;
using Controls = AVAImpossibleMission.Engine.Controls;

namespace AVAImpossibleMission.Tests;

/// <summary>
/// Draws the game in a real (headless) window at the moments worth showing. Set IMPOSSIBLE_SHOTS to a folder to
/// save them as PNGs: the README's screenshots come from here.
/// </summary>
public sealed class ScreenshotTests
{
    private static (Window W, GameCanvas Canvas, GameEngine G) Open(int seed = 11)
    {
        var g = new GameEngine(HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "avaimpossiblemission-ui-" + Guid.NewGuid().ToString("N")))) { Silent = true };
        var canvas = new GameCanvas(g, new SoundManager(silent: true), startTimer: false);
        var w = new Window { Width = 1024, Height = 768, Content = canvas };
        w.Show();
        Pump();
        return (w, canvas, g);
    }

    private static void Pump()
    {
        for (int i = 0; i < 10; i++) Dispatcher.UIThread.RunJobs();
    }

    private static void Shot(Window w, GameCanvas c, string name)
    {
        c.InvalidateVisual();
        Pump();
        var frame = w.CaptureRenderedFrame();
        Assert.NotNull(frame);
        var dir = Environment.GetEnvironmentVariable("IMPOSSIBLE_SHOTS");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        frame.Save(Path.Combine(dir, $"AVAImpossibleMission-{name}.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }

    /// <summary>Lets the computer pilot play until something worth a picture is on screen.</summary>
    private static bool PlayUntil(GameEngine g, GameCanvas c, Func<GameEngine, bool> done, int maxSeconds = 150)
    {
        for (int t = 0; t < 60 * maxSeconds; t++)
        {
            if (done(g)) { c.Advance(0.016); return true; }
            var move = g.Pilot.Decide(g);
            g.Tick(move);
            if (t % 3 == 0) c.Advance(1 / 20.0);
        }
        return false;
    }
    [AvaloniaFact]
    public void The_title_and_the_high_scores()
    {
        var (w, c, g) = Open();
        g.Idle(30);
        c.Advance(2.1);
        Shot(w, c, "title");
        g.HighScores.Insert("VEX", 48300, 6);
        g.HighScores.Insert("AVA", 21500, 3);
        g.Idle((int)(60 * (GameEngine.TitleIdleSeconds + GameEngine.AttractSeconds)) + 10);
        for (int i = 0; i < 60 * 30 && g.State != GameState.HighScores; i++) g.Idle(1);
        Shot(w, c, "high-scores");
    }

    /// <summary>A mission played by the computer agent until something worth showing.</summary>
    private static void Mission(string name, Func<GameEngine, bool> moment, int seconds = 600)
    {
        var (w, c, g) = Open();
        g.Go();
        g.Pilot.Reset();
        Assert.True(PlayUntil(g, c, moment, seconds), $"never saw the moment for {name}");
        Shot(w, c, name);
    }

    [AvaloniaFact]
    public void The_shaft() => Mission("shaft", g => g.Where == Where.Shaft && g.CarMoving && g.Shaft == 1);

    [AvaloniaFact]
    public void Searching() => Mission("searching", g => g.Where == Where.Room && g.Searching is { } f && f.Progress > f.SearchTime * 0.6 && g.Robots.Count >= 2);

    [AvaloniaFact]
    public void A_somersault() => Mission("somersault", g => g.Where == Where.Room && g.Player.Pose == Pose.Flipping && g.Player.Flip is > 0.45 and < 0.55 && g.Robots.Any(r => r.Level == Field.LevelAt(g.Player.Y) && Math.Abs(r.X - g.Player.X) < 1.2));

    [AvaloniaFact]
    public void A_lift() => Mission("lift", g => g.Where == Where.Room && g.Player.Pose == Pose.Riding && g.Room!.Lifts.Count >= 2);

    [AvaloniaFact]
    public void A_bolt() => Mission("bolt", g => g.Where == Where.Room && g.Zaps.Any(z => Math.Abs(z.X - g.Player.X) < 6));

    [AvaloniaFact]
    public void The_orb() => Mission("orb", g => g.Where == Where.Room && g.Orb is { Wait: <= 0 } o && Math.Abs(o.X - g.Player.X) < 5 && o.Level == Field.LevelAt(g.Player.Y));

    [AvaloniaFact]
    public void A_piece_found() => Mission("piece", g => g.Effects.Any(e => e.Text == "PUZZLE PIECE!" && e.Age > 0.3));

    [AvaloniaFact]
    public void The_pocket_computer() => Mission("computer", g => g.Where == Where.Computer && g.Held.Count(p => p.Placed) == 2 && g.CardsDone >= 1);

    [AvaloniaFact]
    public void Mission_accomplished() => Mission("won", g => g.Phase == Phase.Won && g.PhaseTime > 0.6, seconds: 1300);

    [AvaloniaFact]
    public void A_fall()
    {
        var (w, c, g) = Open();
        g.Go();
        // Down the shaft of a lift that reaches the bottom floor, sent up out of the way.
        var room = g.Complex.Rooms.First(r => r.Lifts.Any(l => l.Hi == 3 && l.Lo < 3));
        g.Visit(room);
        var lift = room.Lifts.First(l => l.Hi == 3 && l.Lo < 3);
        lift.At = lift.Toward = lift.Lo;
        int pit = lift.X;
        g.Robots.Clear();
        g.Put(pit + 0.5, 3);
        for (int i = 0; i < 200 && g.Player.Pose != Pose.Dead; i++) g.Tick(default);
        g.Run(4);
        c.Advance(0.2);
        Shot(w, c, "fall");
    }
}
