namespace AVAImpossibleMission.Engine;

/// <summary>
/// The computer agent, for the demonstration and the tests. In a room it finds its way square by square with the
/// <see cref="Planner"/> to the nearest thing it hasn't searched, somersaulting over robots and their bolts as
/// they come; when the room is done it leaves by the door nearer the next room worth searching. In the shafts it
/// rides to that room. Holding all three pieces of a card, it goes to the pocket computer and puts them together;
/// with all six cards, it makes for the control room.
/// </summary>
public sealed class Pilot
{
    private bool _press;
    private Room? _room;
    private int _roomTicks;
    private readonly HashSet<int> _snoozedIn = [];
    private List<(Planner.Act Act, Planner.Spot To)>? _plan;
    private Planner.Spot _planFrom;
    private object? _planGoal;

    /// <summary>What it's doing now, for the probe.</summary>
    public string Doing { get; private set; } = "";

    public void Reset()
    {
        _room = null;
        _roomTicks = 0;
        _snoozedIn.Clear();
        _givenUp.Clear();
        _diedIn.Clear();
        _deathsSeen = 0;
        _plan = null;
        _planGoal = null;
        Doing = "";
    }

    public Controls Decide(GameEngine g)
    {
        if (g.State is not (GameState.Playing or GameState.Attract) || g.Phase != Phase.Play) return default;
        _press = !_press;
        return g.Where switch
        {
            Where.Computer => Compute(g),
            Where.Shaft => InShaft(g),
            _ => InRoom(g),
        };
    }

    // ───────────────────────────── where to go next ─────────────────────────────

    /// <summary>Holding all three pieces of a card not yet made.</summary>
    private static bool HasSet(GameEngine g) =>
        g.Held.GroupBy(p => p.Card).Any(k => k.Count() == Card.PiecesPer && !g.CardDone[k.Key]);

    /// <summary>How far each shaft stop is from here, riding and walking through rooms already searched.</summary>
    /// <summary>How long it takes to get through a room from one door to the other (lifts at home), in the same units as a stop of riding.</summary>
    private static int Crossing(Room r)
    {
        if (Crossings.TryGetValue(r, out int c)) return c;
        var lifts = r.Lifts.Select(l => (l.At, l.Toward)).ToList();
        r.ResetLifts();
        var path = Planner.Path(r, new Planner.Spot(1, r.LeftDoor, Planner.Code(r)), s => s.Col == Field.Cols - 1 && s.Level == r.RightDoor);
        for (int i = 0; i < lifts.Count; i++) (r.Lifts[i].At, r.Lifts[i].Toward) = lifts[i];
        return Crossings[r] = 2 + (path?.Count ?? 60) / 3;
    }

    private static readonly Dictionary<Room, int> Crossings = new(ReferenceEqualityComparer.Instance);

    private static Dictionary<(int Shaft, int Stop), (int Cost, (int, int)? Prev)> Distances(GameEngine g, int shaft, int stop)
    {
        var dist = new Dictionary<(int, int), (int, (int, int)?)> { [(shaft, stop)] = (0, null) };
        var todo = new PriorityQueue<(int S, int T), int>();
        todo.Enqueue((shaft, stop), 0);
        while (todo.TryDequeue(out var n, out int d))
        {
            if (d > dist[n].Item1) continue;
            var next = new List<((int, int) To, int Cost)>();
            if (n.T > 0) next.Add(((n.S, n.T - 1), 1));
            if (n.T < Complex.Stops - 1) next.Add(((n.S, n.T + 1), 1));
            foreach (int side in new[] { -1, 1 })
                if (g.Complex.Beside(n.S, n.T, side) is { } room)
                    next.Add(((n.S + side, n.T), Crossing(room) + (room.Done ? 0 : 30)));
            foreach (var (to, cost) in next)
            {
                int nd = d + cost;
                if (dist.TryGetValue(to, out var old) && old.Item1 <= nd) continue;
                dist[to] = (nd, n);
                todo.Enqueue(to, nd);
            }
        }
        return dist;
    }

    /// <summary>The shaft stop to make for, and which way to step off there (0 to stay and use the computer).</summary>
    private static ((int Shaft, int Stop) Node, int Side, int Cost)? Goal(GameEngine g, Dictionary<(int, int), (int Cost, (int, int)? Prev)> dist, Dictionary<int, int> given)
    {
        if (g.CardsDone == Puzzle.Cards)
        {
            var node = (Complex.Shafts - 1, Complex.ControlStop);
            return dist.TryGetValue(node, out var d) ? (node, 1, d.Cost) : null;
        }
        if (HasSet(g)) return (dist.MinBy(d => d.Value.Cost).Key, 0, 0);
        ((int, int), int, int)? best = null;
        foreach (var r in g.Complex.Rooms.Where(r => !r.Done))
            foreach (var (node, side) in new[] { ((r.Shaft, r.Stop), 1), ((r.Shaft + 1, r.Stop), -1) })
            {
                // A room given up on comes after the others.
                int cost = dist.TryGetValue(node, out var d) ? d.Cost + 200 * given.GetValueOrDefault(r.Id) : int.MaxValue;
                if (cost < int.MaxValue && (best == null || cost < best.Value.Item3)) best = (node, side, cost);
            }
        return best;
    }

    // ───────────────────────────── in the shafts ─────────────────────────────

    private Controls InShaft(GameEngine g)
    {
        _room = null;
        if (g.CarMoving)
        {
            Doing = "riding";
            return new Controls(0, _rideTo == g.CarToward ? 0 : Math.Sign(_rideTo - g.CarToward));
        }
        var dist = Distances(g, g.Shaft, g.Stop);
        if (Goal(g, dist, _givenUp) is not { } goal) { Doing = "lost"; return default; }
        if (goal.Side == 0)
        {
            Doing = "to the computer";
            return new Controls(0, 0, _press);
        }
        // The first leg of the way there: up or down this shaft, or through a room to the next.
        var at = goal.Node;
        while (dist[at].Prev is { } prev && prev != (g.Shaft, g.Stop)) at = prev;
        int side;
        if (at == (g.Shaft, g.Stop)) side = goal.Side;
        else if (at.Item1 == g.Shaft) { _rideTo = at.Item2; Doing = "riding"; return new Controls(0, Math.Sign(at.Item2 - g.Stop)); }
        else side = Math.Sign(at.Item1 - g.Shaft);
        Doing = "into a room";
        return new Controls(_press ? side : 0, 0);
    }

    private int _rideTo;

    // ───────────────────────────── the pocket computer ─────────────────────────────

    private Controls Compute(GameEngine g)
    {
        Doing = "puzzling";
        if (!_press) return default;
        var slots = g.Slots;
        int card = slots.GroupBy(p => p.Card).Where(k => k.Count() == Card.PiecesPer && !g.CardDone[k.Key]).Select(k => k.Key).DefaultIfEmpty(-1).First();
        // What wants doing: take off a piece of another card; turn a piece the right way; lay it down.
        int want = -1;
        Controls act = default;
        for (int i = 0; i < slots.Count && want < 0; i++)
        {
            var p = slots[i];
            if (p.Card != card && p.Placed) { want = i; act = new Controls(0, 0, true); }
            else if (p.Card == card && !p.Right)
            {
                want = i;
                p.FlipH = !p.FlipH;
                bool h = p.Right;
                p.FlipH = !p.FlipH;
                act = new Controls(0, h ? -1 : 1);
            }
            else if (p.Card == card && !p.Placed) { want = i; act = new Controls(0, 0, true); }
        }
        if (want < 0) want = slots.Count;
        if (g.Cursor != want) return new Controls(Math.Sign(want - g.Cursor), 0);
        return want == slots.Count ? new Controls(0, 0, true) : act;
    }

    // ───────────────────────────── in a room ─────────────────────────────

    private Controls InRoom(GameEngine g)
    {
        var room = g.Room!;
        var p = g.Player;
        if (_room != room) { _room = room; _roomTicks = 0; _plan = null; _blocked = null; }
        // Twice killed in a room: leave it for now, and come back when the rest are done.
        if (g.Deaths != _deathsSeen)
        {
            _deathsSeen = g.Deaths;
            _diedIn[room.Id] = _diedIn.GetValueOrDefault(room.Id) + 1;
            if (_diedIn[room.Id] % 2 == 0 && _roomTicks < 60 * 120) _roomTicks = 60 * 120 - 1;
        }
        if (++_roomTicks == 60 * 120 && !room.Done) _givenUp[room.Id] = _givenUp.GetValueOrDefault(room.Id) + 1;
        switch (p.Pose)
        {
            case Pose.Dead or Pose.Falling or Pose.Flipping:
                if (p.Pose == Pose.Flipping) _stalled++;
                Doing = p.Pose.ToString().ToLowerInvariant();
                _plan = null;
                return default;
            case Pose.Riding:
                // Let go once the lift's on its way to the level wanted, so it stops there.
                Doing = "on a lift";
                return g.RidingTo == _liftTo ? default : new Controls(0, _liftDy);
        }
        int level = Field.LevelAt(p.Y);
        int col = (int)Math.Floor(p.X);
        if (level < 0) return default;
        var spot = new Planner.Spot(col, level, Planner.Code(room));
        var want = Want(g, room, p, level, col, spot);
        // Kept from getting anywhere for twenty seconds: leave this room for now.
        _stalled = Doing is "dodging" or "waiting" ? _stalled + 1 : Math.Max(0, _stalled - 2);
        if (_stalled > 60 * 20 && _roomTicks < 60 * 120) { _roomTicks = 60 * 120 - 1; _stalled = 0; }
        if (!Threatened(g, p, level)) return want;
        // Something dangerous about: try the move out first, and if it ends badly, the first of the others that doesn't.
        if (Lives(g, want)) return want;
        int on = want.Dx != 0 ? want.Dx : p.Facing;
        foreach (var other in new[] { Flip(on), default, Flip(-on), new Controls(-on, 0) })
        {
            if (!Lives(g, other)) continue;
            Doing = "dodging";
            _plan = null;
            return other.Fire ? (_press ? other : default) : other;
        }
        return want;
    }

    private static Controls Flip(int dir) => new(dir, 0, true);

    /// <summary>A robot, bolt or the orb near enough to matter.</summary>
    private static bool Threatened(GameEngine g, Player p, int level)
    {
        if (g.Zaps.Count > 0) return true;
        if (g.Orb is { } o && Math.Abs(o.X - p.X) < 8 && Math.Abs(o.Level - level) <= 1) return true;
        return g.Snoozed < 2 && g.Robots.Any(r => Math.Abs(r.X - p.X) < 8 && Math.Abs(r.Level - level) <= 1);
    }

    /// <summary>
    /// Whether these controls (a somersault pressed once, anything else held a while), then standing still, leave the
    /// agent alive a second on.
    /// </summary>
    private static bool Lives(GameEngine g, Controls c)
    {
        // (Pressed now or on the next step, whichever it turns out to be: it must come out alive either way.)
        if (c.Fire) return g.Survives(i => i == 0 ? c : default, 52) && g.Survives(i => i == 1 ? c : default, 53);
        if (c.Dy != 0) return g.Survives(i => i < 70 ? c : default, 76);
        return g.Survives(i => i < 14 ? c : default, 36);
    }

    /// <summary>What the agent would do in a room if nothing were in the way.</summary>
    private Controls Want(GameEngine g, Room room, Player p, int level, int col, Planner.Spot spot)
    {
        // Searching: hold up until it's done.
        if (p.Pose == Pose.Searching) { Doing = "searching"; return new Controls(0, -1); }

        // Snooze the robots first in a busy room, if there's a terminal and a code.
        var terminal = room.Furniture.FirstOrDefault(f => f.Kind == Furn.Terminal);
        bool snooze = terminal != null && g.Codes > 0 && g.Snoozed <= 0 && !_snoozedIn.Contains(room.Id) && !room.Done
                      && (g.Robots.Count >= 2 || g.Orb != null || g.Deaths > 0 && g.Robots.Count > 0);
        Furniture? target = null;
        Func<Planner.Spot, bool> goal;
        object goalKey;
        if (snooze)
        {
            target = terminal;
            goal = s => s.Level == terminal!.Level && terminal.Covers(s.Col + 0.5);
            goalKey = terminal!;
        }
        else
        {
            var todo = room.Searchable.Where(f => !f.Searched).ToList();
            // (When what's left can't be got at from here, the lifts being in the wrong places: out and back in.)
            if (todo.Count > 0 && _roomTicks < 60 * 120 && _blocked != room)
            {
                goal = s => todo.Any(f => f.Level == s.Level && f.Covers(s.Col + 0.5));
                goalKey = ("search", todo.Count);
            }
            else
            {
                int side = ExitSide(g, room, spot, _givenUp);
                int door = side < 0 ? room.LeftDoor : room.RightDoor, edge = side < 0 ? 0 : Field.Cols - 1;
                if (level == door && col == edge) { Doing = "leaving"; return new Controls(side, 0); }
                goal = s => s.Level == door && s.Col == edge;
                goalKey = ("exit", side);
            }
        }

        // At what it came for?
        var here = room.Furniture.FirstOrDefault(f => f.Level == level && f.Covers(p.X) && (f == target || target == null && f.Kind != Furn.Terminal && !f.Searched));
        if (here != null)
        {
            if (here.Kind == Furn.Terminal)
            {
                Doing = "at the terminal";
                if (!_press) return default;
                _snoozedIn.Add(room.Id);
                return new Controls(0, -1);
            }
            Doing = "searching";
            return new Controls(0, -1);
        }

        if (_plan == null || _planFrom != spot || !Equals(_planGoal, goalKey) || _plan.Count == 0)
        {
            _plan = Planner.Path(room, spot, goal);
            _planFrom = spot;
            _planGoal = goalKey;
        }
        if (_plan == null && goalKey is ("search", _)) { _blocked = room; Doing = "blocked"; return default; }
        if (_plan is not { Count: > 0 }) { Doing = "stuck"; return default; }
        var (act, to) = _plan[0];
        Doing = act.ToString().ToLowerInvariant();
        double centre = col + 0.5;
        switch (act)
        {
            case Planner.Act.Left or Planner.Act.Right:
            {
                double aim = to.Col + 0.5;
                // Dropping to a floor below: not onto a robot.
                if (to.Level != level && !Clear(g, to.Level, aim, 0.8, margin: 1.1)) { Doing = "waiting"; return default; }
                return new Controls(Math.Sign(aim - p.X), 0);
            }
            case Planner.Act.FlipLeft or Planner.Act.FlipRight:
            {
                if (Math.Abs(p.X - centre) > 0.11) return new Controls(Math.Sign(centre - p.X), 0);
                int dir = act == Planner.Act.FlipLeft ? -1 : 1;
                if (!Clear(g, to.Level, to.Col + 0.5, Player.FlipSeconds + (to.Level != level ? 0.4 : 0))) { Doing = "waiting"; return default; }
                if (!_press) return default;
                return new Controls(dir, 0, true);
            }
            default:
                // Not up (or down) into a robot's way.
                if (!Clear(g, to.Level, p.X, 1 / GameEngine.LiftSpeed + 0.5, margin: 1.1)) { Doing = "waiting"; return default; }
                _liftDy = act == Planner.Act.Up ? -1 : 1;
                _liftTo = to.Level;
                return new Controls(0, _liftDy);
        }
    }

    private int _liftDy, _liftTo;

    /// <summary>
    /// Nowhere a robot on that level could be within <paramref name="t"/> seconds (going either way, as far as its
    /// floor allows), with room to spare; one robot can be left out (the one being jumped).
    /// </summary>
    private static bool Clear(GameEngine g, int level, double x, double t, Robot? over = null, double margin = 1.8)
    {
        if (g.Snoozed > t + 0.3) return true;
        foreach (var r in g.Robots)
        {
            if (r == over || r.Level != level) continue;
            double v = r.Kind switch { RobotKind.Hunter => GameEngine.HunterSpeed, RobotKind.Sentry => 0, _ => GameEngine.PatrolSpeed };
            // (The last stretch of a somersault comes down low enough to touch a robot, so keep well clear.)
            if (x > Math.Max(r.Lo, r.X - v * t) - margin && x < Math.Min(r.Hi, r.X + v * t) + margin) return false;
        }
        return true;
    }
    private Room? _blocked;
    /// <summary>Rooms given up on for now (too long in them), and how often.</summary>
    private readonly Dictionary<int, int> _givenUp = [], _diedIn = [];
    private int _deathsSeen, _stalled;

    /// <summary>The door to leave by: the one nearer (counting the walk to it) the next place to go.</summary>
    private static int ExitSide(GameEngine g, Room room, Planner.Spot spot, Dictionary<int, int> given)
    {
        int best = -1, bestCost = int.MaxValue;
        foreach (int side in new[] { -1, 1 })
        {
            int door = side < 0 ? room.LeftDoor : room.RightDoor, edge = side < 0 ? 0 : Field.Cols - 1;
            var path = Planner.Path(room, spot, s => s.Level == door && s.Col == edge);
            if (path == null) continue;
            int shaft = side < 0 ? room.Shaft : room.Shaft + 1;
            var dist = Distances(g, shaft, room.Stop);
            int after = Goal(g, dist, given)?.Cost ?? 0;
            // (On a tie, onwards rather than back the way it came.)
            int cost = 2 * (2 + path.Count / 3 + after) + (side == g.EntrySide ? 1 : 0);
            if (cost < bestCost) { bestCost = cost; best = side; }
        }
        return best;
    }
}
