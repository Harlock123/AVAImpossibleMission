namespace AVAImpossibleMission.Engine;

public sealed partial class GameEngine
{
    public const double WalkSpeed = 6.5, Gravity = 70, MaxFallSpeed = 20, LiftSpeed = 1.3, CarSpeed = 1.8;
    public const double HunterTurn = 0.4;
    public const double PatrolSpeed = 2.2, HunterSpeed = 2.9, ZapSpeed = 11, ZapRange = 18, ShootRange = 14, AimSeconds = 0.45, ShotGap = 2.4;
    public const double OrbSpeed = 1.5, OrbWait = 1.5, SnoozeSeconds = 20, DeadSeconds = 2.0;
    public const double StartSeconds = 3.2, WonSeconds = 7, TimeUpSeconds = 5;
    /// <summary>The mission clock, in game seconds: six hours, running ten times faster than real time.</summary>
    public const double MissionSeconds = 6 * 3600, ClockRate = 10, DeathPenalty = 10 * 60;
    public const int SearchPoints = 50, PiecePoints = 200, CodePoints = 100, CardPoints = 1000, WinPoints = 5000;
    public const int SnoozeCodes = 8;

    public Complex Complex { get; private set; } = null!;
    public Puzzle Puzzle { get; private set; } = null!;
    public Pilot Pilot { get; } = new();
    public int Score { get; private set; }
    /// <summary>Game seconds left on the mission clock.</summary>
    public double Clock { get; private set; }
    public Where Where { get; private set; }
    public int Deaths { get; private set; }
    public string Cause { get; private set; } = "";

    // In the shafts.
    public int Shaft { get; private set; }
    /// <summary>The lift car's place in its shaft (a stop number, or between two).</summary>
    public double CarAt { get; private set; }
    public int CarToward { get; private set; }
    public int Stop => (int)Math.Round(CarAt);
    public bool CarMoving => Math.Abs(CarAt - CarToward) > 1e-6;
    private bool _shaftHold;
    /// <summary>Which way the agent last walked off the car (for the picture).</summary>
    public int ShaftFacing { get; private set; } = 1;

    // In a room.
    public Room? Room { get; private set; }
    public Player Player { get; } = new();
    public List<Robot> Robots { get; } = [];
    public List<Zap> Zaps { get; } = [];
    public Orb? Orb { get; private set; }
    public double Snoozed { get; private set; }
    /// <summary>The side the agent came in by (-1 the left door), to start again from.</summary>
    public int EntrySide { get; private set; } = -1;
    private Lift? _riding;
    /// <summary>The level the lift being ridden is heading for (-1 when not on one).</summary>
    public int RidingTo => _riding?.Toward ?? -1;
    /// <summary>The furniture being searched (for the search bar).</summary>
    public Furniture? Searching { get; private set; }

    // What's been found.
    public List<Piece> Held { get; } = [];
    public bool[] CardDone { get; } = new bool[Puzzle.Cards];
    public int CardsDone => CardDone.Count(d => d);
    public int Codes { get; private set; }

    // The pocket computer.
    public int Cursor { get; private set; }
    private Controls _computerPrev;
    /// <summary>The pocket computer's list: the pieces still in hand (by card), then the way out.</summary>
    public List<Piece> Slots => Held.OrderBy(p => p.Card).ThenBy(p => p.Index).ToList();

    private void NewGame(int seed)
    {
        var rng = new Random(seed);
        Complex = Complex.Build();
        Puzzle = Puzzle.Deal(rng);
        // The pieces and the snooze codes, hidden about the complex.
        var places = Complex.Rooms.SelectMany(r => r.Searchable).OrderBy(_ => rng.Next()).ToList();
        for (int i = 0; i < Puzzle.Pieces.Count; i++) places[i].Piece = i;
        for (int i = 0; i < SnoozeCodes; i++) places[Puzzle.Pieces.Count + i].Snooze = true;
        Score = 0;
        Clock = MissionSeconds;
        Deaths = 0;
        Held.Clear();
        Array.Clear(CardDone);
        Codes = 0;
        Room = null;
        Robots.Clear();
        Zaps.Clear();
        Orb = null;
        Where = Where.Shaft;
        Shaft = 0;
        CarAt = CarToward = 0;
        _shaftHold = true;
        ShaftFacing = 1;
        Searching = null;
        SetPhase(Phase.Start);
        Say("STOP PROFESSOR VEX", "SIX CARDS OPEN HIS CONTROL ROOM", StartSeconds);
    }

    private void Simulate(double dt, Controls c)
    {
        PhaseTime += dt;
        switch (Phase)
        {
            case Phase.Start:
                if (PhaseTime > StartSeconds) SetPhase(Phase.Play);
                return;
            case Phase.Won:
                // The professor's cry, then the fanfare.
                if (PhaseTime - dt < 2.6 && PhaseTime >= 2.6) Play(Sfx.Win);
                if (PhaseTime > WonSeconds) GameOver();
                return;
            case Phase.TimeUp:
                if (PhaseTime > TimeUpSeconds) GameOver();
                return;
        }
        Clock -= dt * ClockRate;
        if (Clock <= 0)
        {
            Clock = 0;
            SetPhase(Phase.TimeUp);
            Say("TIME'S UP", "THE PROFESSOR'S PLAN GOES AHEAD", TimeUpSeconds);
            Play(Sfx.GameOver);
            return;
        }
        switch (Where)
        {
            case Where.Shaft: Ride(c, dt); break;
            case Where.Room: InRoom(c, dt); break;
            case Where.Computer: Compute(c); break;
        }
    }

    // ───────────────────────────── the shafts ─────────────────────────────

    private void Ride(Controls c, double dt)
    {
        if (c.Dx == 0) _shaftHold = false;
        if (!CarMoving)
        {
            if (c.Dy != 0)
            {
                int to = Math.Clamp(CarToward + c.Dy, 0, Complex.Stops - 1);
                if (to != CarToward) { CarToward = to; Play(Sfx.Elevator); }
            }
            else if (c.Fire && !Player.FirePrev) OpenComputer();
            else if (c.Dx != 0 && !_shaftHold) Leave(c.Dx);
        }
        Player.FirePrev = c.Fire;
        if (CarMoving)
        {
            CarAt += Math.Clamp(CarToward - CarAt, -CarSpeed * dt, CarSpeed * dt);
            if (!CarMoving)
            {
                // Held on: on to the next stop.
                int next = CarToward + c.Dy;
                if (c.Dy != 0 && next >= 0 && next < Complex.Stops) CarToward = next;
                else Play(Sfx.ElevatorStop);
            }
        }
    }

    /// <summary>Off the car, sideways: into a room, the control room, or a wall.</summary>
    private void Leave(int side)
    {
        _shaftHold = true;
        ShaftFacing = side;
        if (Shaft == Complex.Shafts - 1 && side > 0 && Stop == Complex.ControlStop)
        {
            if (CardsDone == Puzzle.Cards) Win();
            else
            {
                Play(Sfx.Locked);
                Say("THE CONTROL ROOM", $"NEEDS ALL SIX CARDS ({CardsDone} OF 6)", 2);
            }
            return;
        }
        if (Complex.Beside(Shaft, Stop, side) is { } room) Enter(room, -side);
    }

    /// <summary>Into a room by one of its doors (-1 the left): the robots back where they started, the lifts home.</summary>
    private void Enter(Room room, int side)
    {
        Where = Where.Room;
        Room = room;
        EntrySide = side;
        room.Seen = true;
        room.ResetLifts();
        Robots.Clear();
        foreach (var s in room.Robots)
            Robots.Add(new Robot { Kind = s.Kind, Level = s.Level, X = s.X, Lo = s.Lo, Hi = s.Hi, Facing = s.Facing });
        Zaps.Clear();
        Orb = room.OrbStart is { } o ? new Orb { X = o.X, Home = o.X, Level = o.Level, Wait = OrbWait } : null;
        Snoozed = 0;
        _riding = null;
        Searching = null;
        var p = Player;
        p.X = side < 0 ? 0.9 : Field.Cols - 0.9;
        p.Y = Field.FloorRow(side < 0 ? room.LeftDoor : room.RightDoor);
        p.Facing = -side;
        p.Pose = Pose.Standing;
        p.Vy = 0;
        p.UpPrev = true;
        p.FirePrev = true;
        Play(Sfx.Door);
    }

    private void ExitRoom(int side)
    {
        var room = Room!;
        Where = Where.Shaft;
        Shaft = side < 0 ? room.Shaft : room.Shaft + 1;
        CarAt = CarToward = room.Stop;
        ShaftFacing = -side;
        _shaftHold = true;
        Player.FirePrev = true;
        Robots.Clear();
        Zaps.Clear();
        Orb = null;
        Searching = null;
        Room = null;
        Play(Sfx.Door);
    }

    // ───────────────────────────── in a room ─────────────────────────────

    private void InRoom(Controls c, double dt)
    {
        var room = Room!;
        var p = Player;
        Snoozed = Math.Max(0, Snoozed - dt);
        bool fire = c.Fire && !p.FirePrev, up = c.Dy < 0 && !p.UpPrev;
        p.FirePrev = c.Fire;
        p.UpPrev = c.Dy < 0;
        switch (p.Pose)
        {
            case Pose.Dead:
                if ((p.DeadTime -= dt) <= 0)
                {
                    Clock = Math.Max(1, Clock - DeathPenalty);
                    Say("TEN MINUTES LOST", null, 1.6);
                    Enter(room, EntrySide);
                }
                return;
            case Pose.Standing:
                Stand(room, p, c, fire, up, dt);
                break;
            case Pose.Searching:
                SearchOn(room, p, c, dt);
                break;
            case Pose.Riding:
                RideLift(p, c, dt);
                break;
            case Pose.Flipping:
                Somersault(room, p, dt);
                break;
            case Pose.Falling:
                Drop(room, p, dt);
                break;
        }
        if (Where != Where.Room) return;
        if (Snoozed <= 0) Machines(room, dt);
        Hazards(p);
    }

    private void Stand(Room room, Player p, Controls c, bool fire, bool up, double dt)
    {
        int level = Field.LevelAt(p.Y);
        if (level < 0 || !room.Walkable(Col(p.X), level)) { Fall(p); return; }
        if (fire)
        {
            p.FlipDir = c.Dx != 0 ? c.Dx : p.Facing;
            p.Facing = p.FlipDir;
            p.FlipFrom = p.X;
            p.Flip = 0;
            p.Pose = Pose.Flipping;
            Play(Sfx.Flip);
            return;
        }
        if (c.Dy != 0 && room.LiftUnder(p.X, level) is { } lift)
        {
            int to = level + c.Dy;
            if (to >= lift.Lo && to <= lift.Hi)
            {
                lift.Toward = to;
                _riding = lift;
                p.Pose = Pose.Riding;
                Play(Sfx.LiftMove);
                return;
            }
        }
        if (c.Dy < 0 && room.Furniture.FirstOrDefault(f => f.Level == level && f.Covers(p.X)) is { } thing)
        {
            if (thing.Kind == Furn.Terminal) { if (up) UseTerminal(room); }
            else if (!thing.Searched)
            {
                p.Pose = Pose.Searching;
                Searching = thing;
                return;
            }
        }
        if (c.Dx != 0)
        {
            p.Facing = c.Dx;
            p.Walk += dt;
            p.X += c.Dx * WalkSpeed * dt;
            if (level == room.LeftDoor && p.X < 0.35 || level == room.RightDoor && p.X > Field.Cols - 0.35)
            {
                if (_forecast) _forecastOut = true;
                else ExitRoom(p.X < 1 ? -1 : 1);
                return;
            }
            p.X = Math.Clamp(p.X, 0.4, Field.Cols - 0.4);
            if (!room.Walkable(Col(p.X), level)) Fall(p);
        }
    }

    private void SearchOn(Room room, Player p, Controls c, double dt)
    {
        var f = Searching!;
        if (_forecast && c.Dy < 0 && c.Dx == 0 && !c.Fire) return;
        if (c.Dy >= 0 || c.Dx != 0 || c.Fire)
        {
            // Let go: what's been gone through so far stays gone through.
            p.Pose = Pose.Standing;
            Searching = null;
            return;
        }
        double before = f.Progress;
        f.Progress += dt;
        if ((int)(before / 0.4) != (int)(f.Progress / 0.4)) Play(Sfx.Search);
        if (f.Progress < f.SearchTime) return;
        f.Searched = true;
        p.Pose = Pose.Standing;
        Searching = null;
        Score += SearchPoints;
        double y = f.Feet - 3;
        if (f.Piece >= 0)
        {
            Held.Add(Puzzle.Pieces[f.Piece]);
            Score += PiecePoints;
            Play(Sfx.Piece);
            Word("PUZZLE PIECE!", f.Middle, y, Puzzle.Colours[Puzzle.Pieces[f.Piece].Card]);
        }
        else if (f.Snooze)
        {
            Codes++;
            Score += CodePoints;
            Play(Sfx.Code);
            Word("SNOOZE CODE", f.Middle, y, 0xFF8FE8FF);
        }
        else
        {
            Play(Sfx.Nothing);
            Word("NOTHING HERE", f.Middle, y, 0xFFB0B0C0);
        }
        if (room.Done) Word("ROOM SEARCHED", Field.Cols / 2.0, 1.5, 0xFF8FFF8F, 1.6);
    }

    /// <summary>The security terminal: a snooze code puts the room's robots to sleep; and every lift goes home.</summary>
    private void UseTerminal(Room room)
    {
        if (_forecast) return;
        bool did = false;
        if (Codes > 0 && Snoozed <= 0 && (Robots.Count > 0 || Orb != null))
        {
            Codes--;
            Snoozed = SnoozeSeconds;
            Play(Sfx.Snooze);
            Word("ROBOTS SNOOZED", Field.Cols / 2.0, 1.5, 0xFF8FE8FF, 1.6);
            did = true;
        }
        if (room.Lifts.Any(l => l.Toward != l.Home))
        {
            room.ResetLifts();
            Play(Sfx.LiftMove);
            Word("LIFTS RESET", Field.Cols / 2.0, 2.4, 0xFFFFE14A, 1.6);
            did = true;
        }
        if (!did) { Play(Sfx.Click); Word(Codes == 0 ? "NO SNOOZE CODES" : "NOTHING TO DO", Field.Cols / 2.0, 1.5, 0xFFB0B0C0, 1.2); }
        // The agent may have been standing on a lift that just went home.
        int level = Field.LevelAt(Player.Y);
        if (level < 0 || !room.Walkable(Col(Player.X), level)) Fall(Player);
    }

    private void RideLift(Player p, Controls c, double dt)
    {
        var l = _riding!;
        l.At += Math.Clamp(l.Toward - l.At, -LiftSpeed * dt, LiftSpeed * dt);
        p.Y = l.Row;
        if (l.Moving) return;
        int next = l.Toward + c.Dy;
        if (c.Dy != 0 && next >= l.Lo && next <= l.Hi) { l.Toward = next; return; }
        p.Y = Field.FloorRow(l.Toward);
        p.Pose = Pose.Standing;
        _riding = null;
    }

    private void Somersault(Room room, Player p, double dt)
    {
        p.Flip = Math.Min(1, p.Flip + dt / Player.FlipSeconds);
        p.X = Math.Clamp(p.FlipFrom + p.FlipDir * Player.FlipDistance * p.Flip, 0.4, Field.Cols - 0.4);
        if (p.Flip < 1) return;
        p.Pose = Pose.Standing;
        Play(Sfx.Land);
        int level = Field.LevelAt(p.Y);
        if (level < 0 || !room.Walkable(Col(p.X), level)) Fall(p);
    }

    private static void Fall(Player p)
    {
        p.Pose = Pose.Falling;
        p.Vy = 0;
    }

    private void Drop(Room room, Player p, double dt)
    {
        p.Vy = Math.Min(MaxFallSpeed, p.Vy + Gravity * dt);
        double ny = p.Y + p.Vy * dt;
        for (int k = 0; k < Field.Levels; k++)
        {
            int row = Field.FloorRow(k);
            if (row > p.Y + 1e-9 && row <= ny && room.Walkable(Col(p.X), k))
            {
                p.Y = row;
                p.Pose = Pose.Standing;
                Play(Sfx.Land);
                return;
            }
        }
        p.Y = ny;
        if (p.Y > Field.Rows + 4) Die("fell");
    }

    private static int Col(double x) => (int)Math.Floor(x);

    /// <summary>On the same floor as a robot (standing there, or somersaulting along it).</summary>
    private static bool OnLevel(Player p, int level) =>
        p.Pose is Pose.Standing or Pose.Searching or Pose.Flipping && Math.Abs(p.Y - Field.FloorRow(level)) < 0.05;

    /// <summary>The robots, their bolts, and the orb.</summary>
    private void Machines(Room room, double dt)
    {
        var p = Player;
        bool alive = p.Pose != Pose.Dead;
        foreach (var r in Robots)
        {
            r.Cool -= dt;
            bool seen = alive && OnLevel(p, r.Level) && Math.Abs(p.X - r.X) < ShootRange;
            switch (r.Kind)
            {
                // (An agent off the end of its floor is out of its reach: it goes back to patrolling.)
                case RobotKind.Hunter when seen && p.X > r.Lo - 1 && p.X < r.Hi + 1:
                    // It loses sight of an agent somersaulting over it, and takes a moment to turn round.
                    int way = Math.Sign(p.X - r.X);
                    if (p.Pose != Pose.Flipping && way != 0 && way != r.Facing && (r.Turn += dt) > HunterTurn) { r.Facing = way; r.Turn = 0; }
                    Move(r, HunterSpeed, dt, turn: false);
                    break;
                case RobotKind.Shooter or RobotKind.Sentry when seen && Math.Sign(p.X - r.X) == r.Facing:
                    r.Aim += dt;
                    if (r.Aim >= AimSeconds && r.Cool <= 0)
                    {
                        Zaps.Add(new Zap { X = r.X + r.Facing * 0.6, Y = r.Feet - 1.3, Dir = r.Facing });
                        r.Cool = ShotGap;
                        r.Aim = 0;
                        Play(Sfx.Zap);
                    }
                    break;
                case RobotKind.Sentry:
                    r.Aim = 0;
                    if ((r.Turn += dt) > 2.6) { r.Turn = 0; r.Facing = -r.Facing; }
                    break;
                default:
                    r.Aim = 0;
                    Move(r, PatrolSpeed, dt, turn: true);
                    break;
            }
        }
        foreach (var z in Zaps)
        {
            double step = z.Dir * ZapSpeed * dt;
            z.X += step;
            z.Travelled += Math.Abs(step);
            if (z.X < 0 || z.X > Field.Cols || z.Travelled > ZapRange) z.Dead = true;
        }
        Zaps.RemoveAll(z => z.Dead);
        if (Orb is { } o)
        {
            if (o.Wait > 0) o.Wait -= dt;
            // It goes after an agent on its own floor (losing sight of one somersaulting over it, like the
            // robots); otherwise it drifts back to where it waits.
            else if (alive && p.Pose != Pose.Flipping && OnLevel(p, o.Level)) o.X += Math.Clamp(p.X - o.X, -OrbSpeed * dt, OrbSpeed * dt);
            else if (p.Pose != Pose.Flipping) o.X += Math.Clamp(o.Home - o.X, -OrbSpeed * dt / 2, OrbSpeed * dt / 2);
        }
    }

    private static void Move(Robot r, double speed, double dt, bool turn)
    {
        r.X += r.Facing * speed * dt;
        r.Walk += dt;
        if (r.X < r.Lo) { r.X = r.Lo; if (turn) r.Facing = 1; }
        if (r.X > r.Hi) { r.X = r.Hi; if (turn) r.Facing = -1; }
    }

    private void Hazards(Player p)
    {
        if (p.Pose == Pose.Dead) return;
        var (top, bottom) = p.Body;
        if (Snoozed <= 0)
        {
            foreach (var r in Robots)
                if (Math.Abs(p.X - r.X) < Robot.HalfWidth + Player.HalfWidth && bottom > r.Feet - Robot.Height && top < r.Feet)
                { Die("electrocuted"); return; }
            if (Orb is { Wait: <= 0 } o && Math.Abs(p.X - o.X) < Orb.Radius + Player.HalfWidth && bottom > o.Y - Orb.Radius && top < o.Y + Orb.Radius)
            { Die("orb"); return; }
        }
        foreach (var z in Zaps)
            if (Math.Abs(z.X - p.X) < Player.HalfWidth + 0.2 && z.Y > top && z.Y < bottom) { Die("zapped"); return; }
    }

    private void Die(string cause)
    {
        var p = Player;
        if (p.Pose == Pose.Dead) return;
        if (_forecast) { p.Pose = Pose.Dead; ForecastCause = cause; return; }
        Cause = cause;
        Deaths++;
        if (_riding != null) { _riding.Toward = (int)Math.Round(_riding.At); _riding.At = _riding.Toward; _riding = null; }
        p.Pose = Pose.Dead;
        p.DeadTime = DeadSeconds;
        Searching = null;
        Effects.Add(new Effect { Kind = cause == "fell" ? EffectKind.Puff : EffectKind.Spark, X = p.X, Y = Math.Min(p.Y, Field.Rows) - 1.3, Life = 1.0 });
        Play(cause == "fell" ? Sfx.Scream : Sfx.Electrocute);
    }

    // ───────────────────────────── looking ahead (for the computer agent) ─────────────────────────────

    private bool _forecast, _forecastOut;
    /// <summary>What ended the last look ahead that ended badly (for tuning).</summary>
    public string ForecastCause { get; private set; } = "";

    /// <summary>
    /// Whether the agent would still be alive after <paramref name="ticks"/> steps of these controls in this room,
    /// tried out on the side: everything (the agent, the robots, their bolts, the orb, the lifts) is put back after.
    /// </summary>
    public bool Survives(Func<int, Controls> controls, int ticks)
    {
        if (Where != Where.Room || Room is not { } room) return true;
        var p = Player;
        var player = (p.X, p.Y, p.Vy, p.Walk, p.Facing, p.Pose, p.Flip, p.FlipFrom, p.FlipDir, p.DeadTime, p.FirePrev, p.UpPrev);
        var robots = Robots.Select(r => (r.X, r.Facing, r.Cool, r.Turn, r.Aim, r.Walk)).ToList();
        var zaps = Zaps.Select(z => new Zap { X = z.X, Y = z.Y, Travelled = z.Travelled, Dir = z.Dir }).ToList();
        var orb = Orb is { } o ? (o.X, o.Wait) : default;
        var lifts = room.Lifts.Select(l => (l.At, l.Toward)).ToList();
        var (riding, snoozed, searching, silent) = (_riding, Snoozed, Searching, Silent);
        _forecast = true;
        _forecastOut = false;
        Silent = true;
        bool alive = true;
        try
        {
            for (int i = 0; i < ticks && !_forecastOut; i++)
            {
                InRoom(controls(i), Field.Step);
                if (p.Pose == Pose.Dead) { alive = false; break; }
            }
        }
        finally
        {
            (p.X, p.Y, p.Vy, p.Walk, p.Facing, p.Pose, p.Flip, p.FlipFrom, p.FlipDir, p.DeadTime, p.FirePrev, p.UpPrev) = player;
            for (int i = 0; i < Robots.Count; i++)
                (Robots[i].X, Robots[i].Facing, Robots[i].Cool, Robots[i].Turn, Robots[i].Aim, Robots[i].Walk) = robots[i];
            Zaps.Clear();
            Zaps.AddRange(zaps);
            if (Orb is { } back) (back.X, back.Wait) = orb;
            for (int i = 0; i < lifts.Count; i++) (room.Lifts[i].At, room.Lifts[i].Toward) = lifts[i];
            (_riding, Snoozed, Searching, Silent) = (riding, snoozed, searching, silent);
            _forecast = false;
        }
        return alive;
    }

    // ───────────────────────────── the pocket computer ─────────────────────────────

    public void OpenComputer()
    {
        if (Where != Where.Shaft || CarMoving) return;
        Where = Where.Computer;
        Cursor = 0;
        _computerPrev = new Controls(1, 1, true);
        Play(Sfx.Computer);
    }

    public void CloseComputer()
    {
        if (Where != Where.Computer) return;
        Where = Where.Shaft;
        Player.FirePrev = true;
        _shaftHold = true;
        Play(Sfx.Click);
    }

    /// <summary>
    /// Left and right go along the pieces (and the way out at the end); up turns the piece over side to side, down
    /// top to bottom; the button lays it on the work area or takes it off (or, at the end, leaves).
    /// </summary>
    private void Compute(Controls c)
    {
        var prev = _computerPrev;
        _computerPrev = c;
        var slots = Slots;
        Cursor = Math.Clamp(Cursor, 0, slots.Count);
        if (c.Dx != 0 && prev.Dx != c.Dx) { Cursor = Math.Clamp(Cursor + c.Dx, 0, slots.Count); Play(Sfx.Click); }
        var piece = Cursor < slots.Count ? slots[Cursor] : null;
        if (c.Dy != 0 && prev.Dy != c.Dy && piece != null)
        {
            if (c.Dy < 0) piece.FlipH = !piece.FlipH;
            else piece.FlipV = !piece.FlipV;
            Play(Sfx.Click);
            Check();
        }
        if (c.Fire && !prev.Fire)
        {
            if (piece == null) { CloseComputer(); return; }
            piece.Placed = !piece.Placed;
            Play(Sfx.Click);
            Check();
        }
    }

    /// <summary>The work area makes a card: its letter of the password.</summary>
    private void Check()
    {
        int card = Puzzle.Completes(Held.Where(p => p.Placed));
        if (card < 0) return;
        CardDone[card] = true;
        Held.RemoveAll(p => p.Card == card);
        Score += CardPoints;
        Play(Sfx.CardDone);
        if (CardsDone == Puzzle.Cards) Say("ALL SIX CARDS!", "THE CONTROL ROOM IS OFF THE LAST SHAFT, AT THE BOTTOM", 4);
        else Say($"CARD {CardsDone} OF 6", $"PASSWORD LETTER {Puzzle.Password[card]}", 2.5);
    }

    private void Win()
    {
        int bonus = WinPoints + (int)Clock;
        Score += bonus;
        SetPhase(Phase.Won);
        Play(Sfx.Defeat);
        Say("MISSION ACCOMPLISHED", $"PROFESSOR VEX IS STOPPED   BONUS {bonus}", WonSeconds);
    }

    private void Word(string text, double x, double y, uint colour, double life = 1.4) =>
        Effects.Add(new Effect { Kind = EffectKind.Word, X = x, Y = y, Text = text, Colour = colour, Life = life });

    // ───────────────────────────── for tests ─────────────────────────────

    /// <summary>Runs a number of steps with the same controls.</summary>
    public void Run(int steps, Controls c = default)
    {
        for (int i = 0; i < steps; i++) Tick(c);
    }

    public void Idle(int steps) => Run(steps);

    /// <summary>Straight into a game, past the briefing.</summary>
    public void Go()
    {
        StartGame();
        SetPhase(Phase.Play);
        Message = null;
    }

    /// <summary>Into a room by a door (-1 the left).</summary>
    public void GoTo(int shaft, int stop, int side = -1) => Enter(Complex.Room(shaft, stop), side);

    /// <summary>Into a room made for a test.</summary>
    public void Visit(Room room, int side = -1) => Enter(room, side);

    /// <summary>The agent somewhere in the current room, standing on a level.</summary>
    public void Put(double x, int level)
    {
        Player.X = x;
        Player.Y = Field.FloorRow(level);
        Player.Pose = Pose.Standing;
        Player.Vy = 0;
    }

    /// <summary>Into a shaft at a stop.</summary>
    public void PutInShaft(int shaft, int stop)
    {
        Where = Where.Shaft;
        Room = null;
        Robots.Clear();
        Shaft = shaft;
        CarAt = CarToward = stop;
        _shaftHold = true;
    }

    public void GivePiece(int index) => Held.Add(Puzzle.Pieces[index]);
    public void GiveCodes(int n) => Codes += n;
    public void SetClock(double seconds) => Clock = seconds;
    public void SetScore(int score) => Score = score;
    public void Snooze() => Snoozed = SnoozeSeconds;
    public void FinishCards()
    {
        for (int i = 0; i < Puzzle.Cards; i++) CardDone[i] = true;
    }
}
