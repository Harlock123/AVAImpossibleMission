using AVAImpossibleMission.Engine;

namespace AVAImpossibleMission.Tests;

/// <summary>The complex: always laid out the same, and every room fair to get round.</summary>
public class ComplexTests
{
    private static string Layout(Complex c) => string.Join("|", c.Rooms.Select(r =>
        $"{r.Name}:{r.LeftDoor}{r.RightDoor}:" +
        string.Concat(Enumerable.Range(0, Field.Levels).SelectMany(k => Enumerable.Range(0, Field.Cols).Select(x => r.Floor[x, k] ? '=' : ' '))) +
        string.Join(",", r.Lifts.Select(l => $"{l.X}/{l.Lo}-{l.Hi}@{l.Home}")) +
        string.Join(",", r.Furniture.Select(f => $"{f.Kind}{f.X}/{f.Level}")) +
        string.Join(",", r.Robots.Select(o => $"{o.Kind}{o.X}/{o.Level}"))));

    [Fact]
    public void The_complex_is_laid_out_the_same_every_time()
    {
        Assert.Equal(Layout(Complex.Build()), Layout(Complex.Build()));
        Assert.NotEqual(Layout(Complex.Build()), Layout(Complex.Build(7)));
    }

    [Fact]
    public void Sixteen_rooms_between_five_shafts_every_one_named()
    {
        var c = Complex.Build();
        Assert.Equal(16, c.Rooms.Count);
        Assert.Equal(16, c.Rooms.Select(r => r.Name).Distinct().Count());
        for (int stop = 0; stop < Complex.Stops; stop++)
        for (int shaft = 0; shaft < Complex.Across; shaft++)
            Assert.Equal((shaft, stop), (c.Room(shaft, stop).Shaft, c.Room(shaft, stop).Stop));
        // The first shaft has nothing on its left, the last nothing on its right (but the control room).
        Assert.Null(c.Beside(0, 2, -1));
        Assert.Null(c.Beside(Complex.Shafts - 1, 1, 1));
        Assert.Same(c.Room(1, 2), c.Beside(1, 2, 1));
        Assert.Same(c.Room(0, 2), c.Beside(1, 2, -1));
    }

    [Fact]
    public void Every_room_can_be_got_round_from_either_door_and_never_traps_anyone()
    {
        foreach (var r in Complex.Build().Rooms)
        {
            Assert.True(Complex.Checked(r), $"{r.Name} has something out of reach, or a place with no way out");
            Assert.True(r.Searchable.Count() >= 2, $"{r.Name} has too little in it");
        }
    }

    [Fact]
    public void A_room_that_could_shut_the_agent_in_is_not_allowed()
    {
        // A floor at the top only reachable by a lift that goes down without coming back: no.
        var r = new Room { LeftDoor = 0, RightDoor = 0 };
        for (int x = 0; x < Field.Cols; x++) r.Floor[x, 0] = true;
        r.Floor[10, 0] = r.Floor[11, 0] = false;
        r.Lifts.Add(new Lift { X = 10, Lo = 0, Hi = 3, Home = 0 });
        // Down the lift to the bottom, where there's no floor to walk off onto, and the lift can come back up: fine.
        Assert.True(Complex.Checked(r));
        // But a hole in the top floor over a stretch of bottom floor the lift doesn't reach: whoever drops down it
        // is shut in.
        for (int x = 20; x < Field.Cols - 4; x++) r.Floor[x, 3] = true;
        r.Floor[22, 0] = false;
        Assert.False(Complex.Checked(r));
    }

    [Fact]
    public void Robots_keep_clear_of_the_doorways_and_sentries_of_the_furniture()
    {
        foreach (var r in Complex.Build().Rooms)
        {
            foreach (var o in r.Robots)
            {
                if (o.Level == r.LeftDoor) Assert.True(o.X >= 10, $"{r.Name}: a robot at {o.X} by the left door");
                if (o.Level == r.RightDoor) Assert.True(o.X <= Field.Cols - 10, $"{r.Name}: a robot at {o.X} by the right door");
                Assert.InRange(o.X, o.Lo, o.Hi);
                for (double x = o.Lo; x <= o.Hi; x += 0.5) Assert.True(r.Floor[(int)x, o.Level], $"{r.Name}: a robot's beat runs off its floor");
                if (o.Kind == RobotKind.Sentry)
                    Assert.DoesNotContain(r.Furniture, f => f.Level == o.Level && Math.Abs(f.Middle - o.X) < 5);
            }
            if (r.OrbStart is { } orb) Assert.True(orb.Level != r.LeftDoor && orb.Level != r.RightDoor);
        }
    }

    [Fact]
    public void The_pieces_and_codes_are_hidden_in_different_furniture_and_never_in_a_terminal()
    {
        var g = new GameEngine(HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "avaimpossiblemission-tests-" + Guid.NewGuid().ToString("N"))), 3) { Silent = true };
        g.Go();
        var all = g.Complex.Rooms.SelectMany(r => r.Furniture).ToList();
        Assert.Equal(Puzzle.Cards * Card.PiecesPer, all.Count(f => f.Piece >= 0));
        Assert.Equal(Enumerable.Range(0, 18), all.Where(f => f.Piece >= 0).Select(f => f.Piece).Order());
        Assert.Equal(GameEngine.SnoozeCodes, all.Count(f => f.Snooze));
        Assert.DoesNotContain(all, f => f.Piece >= 0 && f.Snooze);
        Assert.DoesNotContain(all, f => f.Kind == Furn.Terminal && (f.Piece >= 0 || f.Snooze));
    }

    [Fact]
    public void The_planner_walks_falls_somersaults_and_rides()
    {
        var r = new Room { LeftDoor = 0, RightDoor = 0 };
        for (int x = 0; x < Field.Cols; x++) { r.Floor[x, 0] = true; r.Floor[x, 3] = true; }
        // A two-square gap in the top floor, and a lift from the bottom up to the top.
        r.Floor[8, 0] = r.Floor[9, 0] = false;
        r.Floor[20, 0] = r.Floor[21, 0] = r.Floor[20, 3] = r.Floor[21, 3] = false;
        r.Lifts.Add(new Lift { X = 20, Lo = 0, Hi = 3, Home = 3, At = 3, Toward = 3 });
        var start = new Planner.Spot(5, 0, Planner.Code(r));
        // Over the gap: a somersault, not a fall (which is longer by walking).
        var over = Planner.Path(r, start, s => s.Col == 12 && s.Level == 0)!;
        Assert.Contains(over, m => m.Act == Planner.Act.FlipRight);
        // Down: walking off into the gap drops to the bottom floor.
        Assert.Contains(Planner.Moves(r, new Planner.Spot(7, 0, start.Lifts)), m => m.Act == Planner.Act.Right && m.To.Level == 3);
        // And back up by the lift.
        var up = Planner.Path(r, new Planner.Spot(10, 3, start.Lifts), s => s.Col == 25 && s.Level == 0)!;
        Assert.Contains(up, m => m.Act == Planner.Act.Up);
    }
}
