namespace AVAImpossibleMission.Engine;

/// <summary>Where a robot starts in its room (it goes back there whenever the player comes in).</summary>
public sealed record RobotSpec(RobotKind Kind, int Level, double X, double Lo, double Hi, int Facing);

/// <summary>
/// A room of the complex: four levels of floor, lifts between them, furniture to search, and robots. A door on
/// the left leads to the elevator shaft on its left, and one on the right to the shaft on its right.
/// </summary>
public sealed class Room
{
    public int Id, Shaft, Stop;
    public string Name = "";
    /// <summary>Solid floor, by column and level (lifts not included).</summary>
    public bool[,] Floor = new bool[Field.Cols, Field.Levels];
    public List<Lift> Lifts { get; } = [];
    public List<Furniture> Furniture { get; } = [];
    public List<RobotSpec> Robots { get; } = [];
    /// <summary>Where the orb waits, if the room has one: across, and over which level.</summary>
    public (double X, int Level)? OrbStart;
    /// <summary>The level of each door.</summary>
    public int LeftDoor, RightDoor;
    public bool Seen;

    public IEnumerable<Furniture> Searchable => Furniture.Where(f => f.Kind != Furn.Terminal);
    public bool Done => Searchable.All(f => f.Searched);
    public int Left => Searchable.Count(f => !f.Searched);

    /// <summary>Whether a column at a level has something to stand on: floor, or a lift that's there.</summary>
    public bool Walkable(int col, int level) =>
        col >= 0 && col < Field.Cols && level >= 0 && level < Field.Levels &&
        (Floor[col, level] || Lifts.Any(l => l.Level == level && l.Covers(col + 0.5)));

    public Lift? LiftUnder(double x, int level) => Lifts.FirstOrDefault(l => l.Level == level && l.Covers(x));

    public void ResetLifts()
    {
        foreach (var l in Lifts) l.At = l.Toward = l.Home;
    }
}

/// <summary>
/// The professor's complex: five elevator shafts, each with four stops, and between each pair of shafts a room at
/// every stop: sixteen rooms. The rooms are laid out from a fixed seed (so the complex is always the same), and
/// every one is checked as it's made: from either door, every piece of furniture and the other door can be reached.
/// </summary>
public sealed class Complex
{
    public const int Shafts = 5, Stops = 4, Across = Shafts - 1;
    public const int DefaultSeed = 1984;
    /// <summary>The control room is off the last shaft's bottom stop.</summary>
    public const int ControlStop = Stops - 1;

    private static readonly string[] Names =
    [
        "RECEPTION", "SERVER HALL", "CRYO LAB", "ARCHIVE",
        "GENERATOR ROOM", "BARRACKS", "WORKSHOP", "OBSERVATORY",
        "GREENHOUSE", "ARMOURY", "LABORATORY", "LIBRARY",
        "ROBOT BAY", "VAULT ANNEX", "RADIO ROOM", "THE LOUNGE",
    ];

    public List<Room> Rooms { get; } = [];

    /// <summary>The room between shaft <paramref name="shaft"/> and the next, at a stop.</summary>
    public Room Room(int shaft, int stop) => Rooms[stop * Across + shaft];

    /// <summary>The room through a side of a shaft at a stop (side -1 left, 1 right), or null for a wall.</summary>
    public Room? Beside(int shaft, int stop, int side)
    {
        int s = side < 0 ? shaft - 1 : shaft;
        return s >= 0 && s < Across ? Room(s, stop) : null;
    }

    public static Complex Build(int seed = DefaultSeed)
    {
        var rng = new Random(seed);
        var c = new Complex();
        for (int stop = 0; stop < Stops; stop++)
        for (int shaft = 0; shaft < Across; shaft++)
        {
            int id = stop * Across + shaft;
            Room? room = null;
            for (int attempt = 0; room == null; attempt++)
            {
                if (attempt > 5000) throw new InvalidOperationException($"room {id} could not be laid out");
                room = Lay(rng, id, shaft, stop);
            }
            room.Name = Names[id];
            c.Rooms.Add(room);
        }
        return c;
    }

    /// <summary>One try at a room; null if it came out with something out of reach.</summary>
    private static Room? Lay(Random rng, int id, int shaft, int stop)
    {
        int depth = shaft + stop;
        var r = new Room { Id = id, Shaft = shaft, Stop = stop };
        for (int x = 0; x < Field.Cols; x++)
        for (int k = 0; k < Field.Levels; k++)
            r.Floor[x, k] = true;
        r.LeftDoor = rng.Next(2) == 0 ? 0 : 3;
        r.RightDoor = rng.Next(3) == 0 ? r.LeftDoor : 3 - r.LeftDoor;

        // Lifts, at least five squares apart.
        int lifts = 1 + rng.Next(3);
        var taken = new bool[Field.Cols];
        for (int i = 0; i < lifts; i++)
        {
            for (int tries = 0; tries < 30; tries++)
            {
                int x = 5 + rng.Next(Field.Cols - 11);
                if (Enumerable.Range(x - 4, 10).Any(c => c >= 0 && c < Field.Cols && taken[c])) continue;
                int lo = rng.Next(3), hi = lo + 1 + rng.Next(3 - lo);
                int home = lo + rng.Next(hi - lo + 1);
                r.Lifts.Add(new Lift { X = x, Lo = lo, Hi = hi, Home = home, At = home, Toward = home });
                for (int c = x; c < x + 2; c++)
                {
                    taken[c] = true;
                    for (int k = lo; k <= hi; k++) r.Floor[c, k] = false;
                }
                break;
            }
        }

        // Gaps in the floors (at the bottom, pits); and now and then a long stretch of floor missing.
        bool NearLift(int c) => r.Lifts.Any(l => c >= l.X - 1 && c <= l.X + 2);
        for (int k = 0; k < Field.Levels; k++)
        {
            if (rng.NextDouble() < (k == 3 ? 0.5 : 0.55))
            {
                int w = 2 + rng.Next(k == 3 ? 2 : 3);
                int x = 5 + rng.Next(Field.Cols - 10 - w);
                if (!Enumerable.Range(x, w).Any(NearLift))
                    for (int c = x; c < x + w; c++) r.Floor[c, k] = false;
            }
        }
        if (rng.NextDouble() < 0.35)
        {
            int k = 1 + rng.Next(2), w = 6 + rng.Next(5);
            int x = 5 + rng.Next(Field.Cols - 10 - w);
            for (int c = x; c < x + w; c++)
                if (!taken[c]) r.Floor[c, k] = false;
        }
        // The doorways have floor in front of them.
        for (int c = 0; c < 4; c++)
        {
            r.Floor[c, r.LeftDoor] = true;
            r.Floor[Field.Cols - 1 - c, r.RightDoor] = true;
        }

        // Furniture, two squares wide on solid floor, a square apart, clear of the doorways.
        int pieces = 3 + rng.Next(3) + (rng.NextDouble() < 0.45 ? 1 : 0);
        bool terminal = pieces > 3 && rng.NextDouble() < 0.6;
        for (int i = 0; i < pieces; i++)
        {
            for (int tries = 0; tries < 60; tries++)
            {
                int k = rng.Next(Field.Levels), x = 3 + rng.Next(Field.Cols - 6);
                if (!r.Floor[x, k] || !r.Floor[x + 1, k]) continue;
                if (k == r.LeftDoor && x < 5 || k == r.RightDoor && x > Field.Cols - 7) continue;
                if (r.Furniture.Any(f => f.Level == k && x + 2 >= f.X - 1 && x <= f.X + f.Width)) continue;
                var kind = terminal && i == 0 ? Furn.Terminal : (Furn)rng.Next((int)Furn.Terminal);
                r.Furniture.Add(new Furniture { Kind = kind, X = x, Level = k });
                break;
            }
        }
        if (r.Searchable.Count() < 2) return null;

        // Robots, on stretches of solid floor long enough to jump them, and not on top of a doorway.
        int robots = 1 + Math.Min(2, (depth + rng.Next(3)) / 3);
        for (int i = 0; i < robots; i++)
        {
            for (int tries = 0; tries < 40; tries++)
            {
                int k = rng.Next(Field.Levels), x = 2 + rng.Next(Field.Cols - 4);
                if (!r.Floor[x, k]) continue;
                int a = x, b = x;
                while (a > 0 && r.Floor[a - 1, k]) a--;
                while (b < Field.Cols - 1 && r.Floor[b + 1, k]) b++;
                if (b - a < 7) continue;
                if (k == r.LeftDoor && x < 10 || k == r.RightDoor && x > Field.Cols - 11) continue;
                if (r.Robots.Any(o => o.Level == k && Math.Abs(o.X - (x + 0.5)) < 5)) continue;
                double roll = rng.NextDouble();
                var kind = roll < 0.4 - depth * 0.03 ? RobotKind.Patrol
                    : roll < 0.65 ? RobotKind.Hunter
                    : roll < 0.9 ? RobotKind.Shooter
                    : RobotKind.Sentry;
                // A sentry never moves: not where it would stand guard over something to search.
                if (kind == RobotKind.Sentry && r.Furniture.Any(f => f.Level == k && Math.Abs(f.Middle - (x + 0.5)) < 5)) continue;
                r.Robots.Add(new RobotSpec(kind, k, Math.Clamp(x + 0.5, a + 0.6, b + 0.4), a + 0.6, b + 0.4, rng.Next(2) == 0 ? -1 : 1));
                break;
            }
        }
        if (depth >= 3 && rng.NextDouble() < 0.3)
        {
            int k = rng.Next(Field.Levels);
            if (k != r.LeftDoor && k != r.RightDoor) r.OrbStart = (Field.Cols / 2.0, k);
        }

        return Checked(r) ? r : null;
    }

    /// <summary>
    /// Every piece of furniture, and the other door, can be reached from either door (with the lifts at home); and
    /// wherever the agent can get to, a door can still be reached from there, so nobody is ever shut in (going out
    /// and back in sends the lifts home).
    /// </summary>
    public static bool Checked(Room r)
    {
        r.ResetLifts();
        int home = Planner.Code(r);
        var all = new HashSet<Planner.Spot>();
        foreach (var start in new[] { new Planner.Spot(1, r.LeftDoor, home), new Planner.Spot(Field.Cols - 2, r.RightDoor, home) })
        {
            var reach = Planner.Reach(r, start);
            if (!reach.Any(s => s.Col == 0 && s.Level == r.LeftDoor)) return false;
            if (!reach.Any(s => s.Col == Field.Cols - 1 && s.Level == r.RightDoor)) return false;
            foreach (var f in r.Furniture)
                if (!reach.Any(s => s.Level == f.Level && f.Covers(s.Col + 0.5))) return false;
            all.UnionWith(reach);
        }
        return Planner.CanLeave(r, all).Count == all.Count;
    }
}

/// <summary>
/// The ways about a room, square by square: walking a square, falling where there's no floor, a somersault four
/// squares along, and riding a lift a level up or down. Used to check every room can be got round, and by the
/// computer player to find its way.
/// </summary>
public static class Planner
{
    public enum Act { Left, Right, FlipLeft, FlipRight, Up, Down }

    /// <summary>Standing in a column at a level, with the lifts where <see cref="Lifts"/> says (two bits each).</summary>
    public readonly record struct Spot(int Col, int Level, int Lifts);

    public static int Code(Room r)
    {
        int code = 0;
        for (int i = 0; i < r.Lifts.Count; i++) code |= r.Lifts[i].Toward << (2 * i);
        return code;
    }

    private static int LiftLevel(int code, int i) => (code >> (2 * i)) & 3;

    private static bool Stand(Room r, int code, int col, int level)
    {
        if (col < 0 || col >= Field.Cols) return false;
        if (r.Floor[col, level]) return true;
        for (int i = 0; i < r.Lifts.Count; i++)
            if (LiftLevel(code, i) == level && r.Lifts[i].Covers(col + 0.5)) return true;
        return false;
    }

    /// <summary>Where a fall from a column at a level ends, or null if it's out of the bottom of the room.</summary>
    private static int? Land(Room r, int code, int col, int level)
    {
        for (int k = level + 1; k < Field.Levels; k++)
            if (Stand(r, code, col, k)) return k;
        return null;
    }

    public static IEnumerable<(Act Act, Spot To)> Moves(Room r, Spot s)
    {
        foreach (var (act, d) in new[] { (Act.Left, -1), (Act.Right, 1), (Act.FlipLeft, -4), (Act.FlipRight, 4) })
        {
            int c = s.Col + d;
            if (c < 0 || c >= Field.Cols) continue;
            if (Stand(r, s.Lifts, c, s.Level)) yield return (act, s with { Col = c });
            else if (Land(r, s.Lifts, c, s.Level) is { } k) yield return (act, s with { Col = c, Level = k });
        }
        for (int i = 0; i < r.Lifts.Count; i++)
        {
            var l = r.Lifts[i];
            if (LiftLevel(s.Lifts, i) != s.Level || !l.Covers(s.Col + 0.5)) continue;
            int clear = s.Lifts & ~(3 << (2 * i));
            if (s.Level > l.Lo) yield return (Act.Up, new Spot(s.Col, s.Level - 1, clear | (s.Level - 1) << (2 * i)));
            if (s.Level < l.Hi) yield return (Act.Down, new Spot(s.Col, s.Level + 1, clear | (s.Level + 1) << (2 * i)));
        }
    }

    public static HashSet<Spot> Reach(Room r, Spot from)
    {
        var seen = new HashSet<Spot> { from };
        var todo = new Queue<Spot>();
        todo.Enqueue(from);
        while (todo.Count > 0)
            foreach (var (_, to) in Moves(r, todo.Dequeue()))
                if (seen.Add(to)) todo.Enqueue(to);
        return seen;
    }

    /// <summary>The spots, of those given, from which a door can still be reached.</summary>
    public static HashSet<Spot> CanLeave(Room r, HashSet<Spot> spots)
    {
        var back = new Dictionary<Spot, List<Spot>>();
        foreach (var s in spots)
            foreach (var (_, to) in Moves(r, s))
            {
                if (!back.TryGetValue(to, out var list)) back[to] = list = [];
                list.Add(s);
            }
        var ok = spots.Where(s => s.Col == 0 && s.Level == r.LeftDoor || s.Col == Field.Cols - 1 && s.Level == r.RightDoor).ToHashSet();
        var todo = new Queue<Spot>(ok);
        while (todo.Count > 0)
            if (back.TryGetValue(todo.Dequeue(), out var from))
                foreach (var f in from)
                    if (ok.Add(f)) todo.Enqueue(f);
        return ok;
    }

    /// <summary>The shortest way (fewest moves, somersaults counting a little more) from a spot to one that suits.</summary>
    public static List<(Act Act, Spot To)>? Path(Room r, Spot from, Func<Spot, bool> goal, Func<Spot, Act, bool>? avoid = null)
    {
        if (goal(from)) return [];
        var prev = new Dictionary<Spot, (Spot From, Act Act)>();
        var cost = new Dictionary<Spot, int> { [from] = 0 };
        var todo = new PriorityQueue<Spot, int>();
        todo.Enqueue(from, 0);
        while (todo.TryDequeue(out var s, out int d))
        {
            if (d > cost[s]) continue;
            if (goal(s))
            {
                var path = new List<(Act, Spot)>();
                for (var at = s; at != from; at = prev[at].From) path.Add((prev[at].Act, at));
                path.Reverse();
                return path;
            }
            foreach (var (act, to) in Moves(r, s))
            {
                if (avoid != null && avoid(to, act)) continue;
                int nd = d + act switch { Act.FlipLeft or Act.FlipRight => 6, Act.Up or Act.Down => 5, _ => 1 };
                if (cost.TryGetValue(to, out int old) && old <= nd) continue;
                cost[to] = nd;
                prev[to] = (s, act);
                todo.Enqueue(to, nd);
            }
        }
        return null;
    }
}
