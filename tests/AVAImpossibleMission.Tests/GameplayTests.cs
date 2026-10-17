using Avalonia.Input;
using AVAImpossibleMission.Engine;

namespace AVAImpossibleMission.Tests;

public class GameplayTests
{
    private static string TempFolder() => Path.Combine(Path.GetTempPath(), "avaimpossiblemission-tests-" + Guid.NewGuid().ToString("N"));

    /// <summary>A mission under way, past the briefing, at the top of the first shaft.</summary>
    private static GameEngine Game(int seed = 1)
    {
        var g = new GameEngine(HighScoreTable.Load(TempFolder()), seed) { Silent = true };
        g.Go();
        return g;
    }

    /// <summary>A room for a test: solid floors on every level, doors at the given levels, nothing in it.</summary>
    private static Room Plain(int left = 3, int right = 3)
    {
        var r = new Room { Id = 99, Name = "TEST ROOM", LeftDoor = left, RightDoor = right };
        for (int x = 0; x < Field.Cols; x++)
        for (int k = 0; k < Field.Levels; k++)
            r.Floor[x, k] = true;
        return r;
    }

    /// <summary>A game standing in a test room, the robots and orb added after.</summary>
    private static GameEngine In(Room room, double x = 5.5, int level = 3)
    {
        var g = Game();
        g.Visit(room);
        g.Put(x, level);
        // (Coming in, a button or up still held from the doorway counts for nothing: let go first.)
        g.Tick(default);
        return g;
    }

    private static readonly Controls Left = new(-1, 0), Right = new(1, 0), Up = new(0, -1), Down = new(0, 1), Button = new(0, 0, true);

    private static void Press(GameEngine g, Controls c) { g.Tick(c); g.Tick(default); }

    // ───────────────────────────── the mission ─────────────────────────────

    [Fact]
    public void A_mission_starts_at_the_top_of_the_first_shaft_with_six_hours_on_the_clock()
    {
        var g = new GameEngine(HighScoreTable.Load(TempFolder()), 1) { Silent = true };
        g.StartGame();
        Assert.Equal(GameState.Playing, g.State);
        Assert.Equal(Phase.Start, g.Phase);
        Assert.Equal((Where.Shaft, 0, 0), (g.Where, g.Shaft, g.Stop));
        Assert.Equal(6 * 3600, g.Clock);
        g.Run((int)(60 * GameEngine.StartSeconds) + 5);
        Assert.Equal(Phase.Play, g.Phase);
    }

    [Fact]
    public void The_clock_runs_ten_game_seconds_to_the_second()
    {
        var g = Game();
        g.Run(60);
        Assert.Equal(6 * 3600 - 10, g.Clock, 1);
    }

    [Fact]
    public void When_time_runs_out_the_mission_is_lost_and_a_good_score_goes_on_the_table()
    {
        var g = Game();
        g.SetScore(99999);
        g.SetClock(5);
        g.Run(60);
        Assert.Equal(Phase.TimeUp, g.Phase);
        g.Run((int)(60 * (GameEngine.TimeUpSeconds + 4.5)));
        Assert.Equal(GameState.EnterInitials, g.State);
    }

    // ───────────────────────────── the shafts ─────────────────────────────

    [Fact]
    public void The_car_rides_down_a_stop_at_a_time_and_stops_where_it_is_let_go()
    {
        var g = Game();
        g.Tick(Down);
        g.Run(10, Down);
        g.Run(60);
        Assert.Equal(1, g.Stop);
        Assert.False(g.CarMoving);
        g.Run(120, Down);
        g.Run(60);
        Assert.True(g.Stop >= 2);
        g.Run(400, Up);
        g.Run(60);
        Assert.Equal(0, g.Stop);
    }

    [Fact]
    public void Stepping_off_the_car_goes_in_by_the_door_of_the_room_beside_it()
    {
        var g = Game();
        g.Tick(default);
        g.Tick(Right);
        Assert.Equal(Where.Room, g.Where);
        var room = g.Complex.Room(0, 0);
        Assert.Same(room, g.Room);
        Assert.True(room.Seen);
        Assert.Equal(Field.FloorRow(room.LeftDoor), g.Player.Y);
        Assert.True(g.Player.X < 1.5);
    }

    [Fact]
    public void There_is_no_going_back_out_the_way_in()
    {
        var g = Game();
        g.Tick(default);
        g.Tick(Left);
        Assert.Equal(Where.Shaft, g.Where);
    }

    [Fact]
    public void Walking_out_of_a_door_leads_into_the_shaft_beyond_it()
    {
        var g = In(Plain(right: 3), x: 29.5);
        g.Run(40, Right);
        Assert.Equal(Where.Shaft, g.Where);
        Assert.Equal(1, g.Shaft);
        // Still holding the way out: not straight on into the next room until it's pressed again.
        g.Run(10, Right);
        Assert.Equal(Where.Shaft, g.Where);
    }

    [Fact]
    public void Away_from_a_door_the_room_has_walls()
    {
        var g = In(Plain(right: 3), x: 29.5, level: 0);
        g.Run(40, Right);
        Assert.Equal(Where.Room, g.Where);
        Assert.True(g.Player.X <= Field.Cols - 0.4);
    }

    // ───────────────────────────── moving about ─────────────────────────────

    [Fact]
    public void Walking_off_a_ledge_drops_to_the_floor_below()
    {
        var room = Plain();
        for (int x = 10; x < 14; x++) room.Floor[x, 1] = false;
        var g = In(room, x: 8.5, level: 1);
        g.Run(40, Right);
        g.Run(60);
        Assert.Equal(Field.FloorRow(2), g.Player.Y);
        Assert.Equal(Pose.Standing, g.Player.Pose);
        Assert.Equal(0, g.Deaths);
    }

    [Fact]
    public void Falling_out_of_the_bottom_of_a_room_costs_ten_minutes_and_starts_again_at_the_door()
    {
        var room = Plain();
        for (int x = 10; x < 13; x++) room.Floor[x, 3] = false;
        var g = In(room, x: 8.5);
        double clock = g.Clock;
        g.Run(40, Right);
        g.Run(60);
        Assert.Equal(1, g.Deaths);
        Assert.Equal("fell", g.Cause);
        g.Run((int)(60 * GameEngine.DeadSeconds) + 5);
        Assert.Equal(Pose.Standing, g.Player.Pose);
        Assert.True(g.Player.X < 1.5);
        Assert.Equal(clock - 600 - 10 * (100 + 60 * GameEngine.DeadSeconds + 5) / 60.0, g.Clock, 0);
    }

    [Fact]
    public void A_somersault_carries_four_squares_over_a_gap_and_lands()
    {
        var room = Plain();
        room.Floor[10, 3] = room.Floor[11, 3] = false;
        var g = In(room, x: 8.5);
        g.Tick(new Controls(1, 0, true));
        Assert.Equal(Pose.Flipping, g.Player.Pose);
        g.Run(60);
        Assert.Equal(Pose.Standing, g.Player.Pose);
        Assert.Equal(12.5, g.Player.X, 1);
        Assert.Equal(0, g.Deaths);
    }

    [Fact]
    public void A_lift_carries_the_agent_up_a_level_and_stays_there()
    {
        var room = Plain();
        room.Floor[10, 2] = room.Floor[11, 2] = room.Floor[10, 3] = room.Floor[11, 3] = false;
        room.Lifts.Add(new Lift { X = 10, Lo = 2, Hi = 3, Home = 3 });
        var g = In(room, x: 10.9);
        g.Tick(Up);
        Assert.Equal(Pose.Riding, g.Player.Pose);
        g.Run(10, Up);
        g.Run(80);
        Assert.Equal(Field.FloorRow(2), g.Player.Y);
        Assert.Equal(2, room.Lifts[0].Level);
        // Walk off: the lift stays up.
        g.Run(30, Right);
        Assert.Equal(2, room.Lifts[0].Level);
        // Going out and coming back sends it home.
        g.Visit(room);
        Assert.Equal(3, room.Lifts[0].Level);
    }

    // ───────────────────────────── searching ─────────────────────────────

    [Fact]
    public void Searching_takes_a_while_and_turns_up_what_is_hidden()
    {
        var room = Plain();
        var desk = new Furniture { Kind = Furn.Desk, X = 6, Level = 3, Piece = 4 };
        room.Furniture.Add(desk);
        var g = In(room, x: 7);
        g.Run(30, Up);
        Assert.Equal(Pose.Searching, g.Player.Pose);
        Assert.False(desk.Searched);
        // Letting go keeps what's been gone through so far.
        g.Tick(default);
        double so = desk.Progress;
        Assert.True(so > 0.4);
        g.Run((int)(60 * (desk.SearchTime - so)) + 5, Up);
        Assert.True(desk.Searched);
        Assert.Contains(g.Held, p => p.Index == 4);
        Assert.Equal(GameEngine.SearchPoints + GameEngine.PiecePoints, g.Score);
        Assert.True(room.Done);
        // Nothing more to find there.
        g.Run(200, Up);
        Assert.Single(g.Held);
    }

    [Fact]
    public void A_snooze_code_used_at_a_terminal_puts_the_robots_to_sleep()
    {
        var room = Plain();
        room.Furniture.Add(new Furniture { Kind = Furn.Terminal, X = 6, Level = 3 });
        var g = In(room, x: 7);
        g.Robots.Add(new Robot { Kind = RobotKind.Hunter, Level = 3, X = 20, Lo = 1, Hi = 30, Facing = -1 });
        g.GiveCodes(1);
        g.Tick(Up);
        Assert.Equal(GameEngine.SnoozeSeconds, g.Snoozed, 1);
        Assert.Equal(0, g.Codes);
        double x = g.Robots[0].X;
        g.Run(120);
        Assert.Equal(x, g.Robots[0].X);
        // Asleep, a robot does no harm.
        g.Put(20, 3);
        g.Run(10);
        Assert.Equal(0, g.Deaths);
    }

    [Fact]
    public void The_terminal_sends_every_lift_home()
    {
        var room = Plain();
        room.Floor[20, 1] = room.Floor[21, 1] = room.Floor[20, 2] = room.Floor[21, 2] = false;
        room.Lifts.Add(new Lift { X = 20, Lo = 1, Hi = 2, Home = 1 });
        room.Furniture.Add(new Furniture { Kind = Furn.Terminal, X = 6, Level = 3 });
        var g = In(room, x: 7);
        room.Lifts[0].At = room.Lifts[0].Toward = 2;
        g.Tick(Up);
        Assert.Equal(1, room.Lifts[0].Level);
    }

    // ───────────────────────────── robots and the orb ─────────────────────────────

    [Fact]
    public void Touching_a_robot_is_death()
    {
        var g = In(Plain(), x: 10);
        g.Robots.Add(new Robot { Kind = RobotKind.Patrol, Level = 3, X = 13, Lo = 1, Hi = 30, Facing = -1 });
        g.Run(120);
        Assert.Equal(1, g.Deaths);
        Assert.Equal("electrocuted", g.Cause);
    }

    [Fact]
    public void A_somersault_clears_a_robot_coming_the_other_way()
    {
        var g = In(Plain(), x: 10.5);
        g.Robots.Add(new Robot { Kind = RobotKind.Patrol, Level = 3, X = 13.5, Lo = 1, Hi = 30, Facing = -1 });
        g.Tick(new Controls(1, 0, true));
        g.Run(50);
        Assert.Equal(0, g.Deaths);
        Assert.True(g.Player.X > g.Robots[0].X + 1);
    }

    [Fact]
    public void A_hunter_comes_after_the_agent_but_not_beyond_its_floor()
    {
        var room = Plain();
        for (int x = 14; x < 18; x++) room.Floor[x, 3] = false;
        var g = In(room, x: 25.5);
        g.Robots.Add(new Robot { Kind = RobotKind.Hunter, Level = 3, X = 21, Lo = 18.6, Hi = 31, Facing = -1 });
        g.Run(60);
        // It has turned round, after a moment, and come on.
        Assert.Equal(1, g.Robots[0].Facing);
        Assert.True(g.Robots[0].X > 21);
        // Across the gap, out of its reach: it goes back to its rounds.
        g.Put(8.5, 3);
        g.Run(400);
        Assert.True(g.Robots[0].Facing != 0);
        Assert.Equal(0, g.Deaths);
        var xs = new List<double>();
        for (int i = 0; i < 400; i++) { g.Tick(default); xs.Add(g.Robots[0].X); }
        Assert.True(xs.Max() - xs.Min() > 5, "it should be patrolling, not waiting at the edge");
    }

    [Fact]
    public void A_shooter_fires_along_its_floor_and_the_bolt_kills()
    {
        var g = In(Plain(), x: 20.5);
        g.Robots.Add(new Robot { Kind = RobotKind.Shooter, Level = 3, X = 10, Lo = 9, Hi = 11, Facing = 1 });
        g.Run(40);
        Assert.Single(g.Zaps);
        g.Run(120);
        Assert.Equal(1, g.Deaths);
        Assert.Equal("zapped", g.Cause);
    }

    [Fact]
    public void A_bolt_can_be_somersaulted_over_but_not_outrun()
    {
        // Over it, towards it: it passes underneath.
        var g = In(Plain(), x: 20.5);
        g.Zaps.Add(new Zap { X = 17, Y = Field.FloorRow(3) - 1.3, Dir = 1 });
        g.Tick(new Controls(-1, 0, true));
        g.Run(60);
        Assert.Equal(0, g.Deaths);
        // Away from it: it catches up as the somersault comes down.
        g = In(Plain(), x: 20.5);
        g.Zaps.Add(new Zap { X = 17, Y = Field.FloorRow(3) - 1.3, Dir = 1 });
        g.Tick(new Controls(1, 0, true));
        g.Run(60);
        Assert.Equal(1, g.Deaths);
    }

    [Fact]
    public void A_shooter_on_another_floor_holds_its_fire()
    {
        var g = In(Plain(), x: 20.5);
        g.Robots.Add(new Robot { Kind = RobotKind.Shooter, Level = 1, X = 10, Lo = 9, Hi = 11, Facing = 1 });
        g.Run(200);
        Assert.Empty(g.Zaps);
    }

    [Fact]
    public void The_orb_glides_after_an_agent_on_its_own_floor_and_otherwise_drifts_home()
    {
        var room = Plain();
        room.OrbStart = (16, 2);
        var g = In(room, x: 25.5, level: 2);
        g.Run(60 * 2);
        Assert.True(g.Orb!.X > 16.5);
        g.Put(25.5, 3);
        g.Run(60 * 6);
        Assert.Equal(16, g.Orb.X, 1);
        Assert.Equal(0, g.Deaths);
        g.Put(18, 2);
        g.Run(60 * 3);
        Assert.Equal(1, g.Deaths);
        Assert.Equal("orb", g.Cause);
    }

    [Fact]
    public void Coming_in_again_puts_the_robots_back_where_they_started()
    {
        var g = Game();
        var room = g.Complex.Rooms.First(r => r.Robots.Count > 0);
        g.Visit(room);
        var start = g.Robots.Select(r => r.X).ToList();
        g.Put(1, room.LeftDoor);
        g.Run(120);
        g.Visit(room);
        Assert.Equal(start, g.Robots.Select(r => r.X));
    }

    // ───────────────────────────── the pocket computer ─────────────────────────────

    [Fact]
    public void The_pocket_computer_opens_in_a_shaft_and_turns_and_lays_down_pieces()
    {
        var g = Game();
        foreach (var p in g.Puzzle.Pieces.Where(p => p.Card == 1)) g.GivePiece(p.Index);
        g.Tick(default);
        g.Tick(Button);
        Assert.Equal(Where.Computer, g.Where);
        g.Tick(default);
        // Turn each piece the right way round and lay it down: the card is made.
        var pieces = g.Slots;
        for (int i = 0; i < pieces.Count; i++)
        {
            var p = pieces[i];
            while (g.Cursor < i) Press(g, Right);
            if (p.FlipH) Press(g, Up);
            if (p.FlipV) Press(g, Down);
            Press(g, Button);
        }
        Assert.True(g.CardDone[1]);
        Assert.Empty(g.Held);
        Assert.Equal(GameEngine.CardPoints, g.Score);
        // The way out is the last slot.
        Press(g, Button);
        Assert.Equal(Where.Shaft, g.Where);
    }

    [Fact]
    public void Pieces_the_wrong_way_round_make_nothing()
    {
        var g = Game();
        var mine = g.Puzzle.Pieces.Where(p => p.Card == 0).ToList();
        foreach (var p in mine) { g.GivePiece(p.Index); p.FlipH = p.FlipV = false; }
        var odd = mine.First(p => !p.Cells.Select(c => Piece.Turn(c, false, true)).ToHashSet().SetEquals(p.Cells));
        odd.FlipV = true;
        g.Tick(default);
        g.Tick(Button);
        g.Tick(default);
        for (int i = 0; i < 3; i++) { while (g.Cursor < i) Press(g, Right); Press(g, Button); }
        Assert.False(g.CardDone[0]);
        Assert.Equal(3, g.Held.Count(p => p.Placed));
    }

    [Fact]
    public void Not_in_a_room()
    {
        var g = In(Plain());
        g.OpenComputer();
        Assert.Equal(Where.Room, g.Where);
    }

    // ───────────────────────────── the control room ─────────────────────────────

    [Fact]
    public void The_control_room_stays_shut_without_all_six_cards()
    {
        var g = Game();
        g.PutInShaft(Complex.Shafts - 1, Complex.ControlStop);
        g.Tick(default);
        g.Tick(Right);
        Assert.Equal(Where.Shaft, g.Where);
        Assert.Equal(Phase.Play, g.Phase);
    }

    [Fact]
    public void With_all_six_cards_the_control_room_ends_the_mission_with_a_bonus_for_time_left()
    {
        var g = Game();
        g.FinishCards();
        g.PutInShaft(Complex.Shafts - 1, Complex.ControlStop);
        double clock = g.Clock;
        g.Tick(default);
        g.Tick(Right);
        Assert.Equal(Phase.Won, g.Phase);
        Assert.InRange(g.Score, GameEngine.WinPoints + (int)clock - 30, GameEngine.WinPoints + (int)clock);
        g.Run((int)(60 * (GameEngine.WonSeconds + 4.5)));
        Assert.Equal(GameState.EnterInitials, g.State);
    }

    // ───────────────────────────── title, sounds, keys ─────────────────────────────

    [Fact]
    public void The_title_speaks_the_welcome_then_plays_its_tune_and_a_mission_starts_with_the_welcome()
    {
        var g = new GameEngine(HighScoreTable.Load(TempFolder()), 1);
        var heard = new List<Sfx>();
        g.Sound += heard.Add;
        g.Tick(default);
        Assert.Equal([Sfx.Welcome], heard);
        g.Run((int)(60 * GameEngine.WelcomeSeconds) + 5);
        Assert.Contains(Sfx.TitleTune, heard);
        heard.Clear();
        g.Tick(default, start: true);
        Assert.Equal(GameState.Playing, g.State);
        Assert.Contains(Sfx.Welcome, heard);
    }

    [Fact]
    public void Left_alone_the_title_runs_the_demonstration_then_the_high_scores()
    {
        var g = new GameEngine(HighScoreTable.Load(TempFolder()), 1) { Silent = true };
        g.Run((int)(60 * GameEngine.TitleIdleSeconds) + 5);
        Assert.Equal(GameState.Attract, g.State);
        g.Run((int)(60 * GameEngine.AttractSeconds) + 5);
        Assert.Equal(GameState.HighScores, g.State);
        Assert.True(g.Complex.Rooms.Any(r => r.Seen), "the demonstration agent should have gone into a room");
        g.Run((int)(60 * GameEngine.HighScoreSeconds) + 5);
        Assert.Equal(GameState.Title, g.State);
    }

    [Fact]
    public void The_keys_steer_the_agent_the_right_way()
    {
        var input = new InputHandler();
        input.OnKeyDown(Key.Up);
        Assert.Equal(-1, input.Player.Dy);
        input.OnKeyUp(Key.Up);
        input.OnKeyDown(Key.S);
        Assert.Equal(1, input.Player.Dy);
        input.OnKeyUp(Key.S);
        input.OnKeyDown(Key.A);
        Assert.Equal(-1, input.Player.Dx);
        input.OnKeyDown(Key.Space);
        Assert.True(input.Player.Fire);
        input.Dispose();
        // And up on the keys rides the car up, as the screen shows it.
        var g = Game();
        g.PutInShaft(0, 2);
        g.Run(10, new Controls(0, -1));
        Assert.True(g.CarAt < 2);
    }
}
