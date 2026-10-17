namespace AVAImpossibleMission.Engine;

/// <summary>The controls: left and right, up and down (up the screen is -1), and the button.</summary>
public readonly record struct Controls(int Dx = 0, int Dy = 0, bool Fire = false);

/// <summary>
/// The screen: a room of 32 by 20 squares between the score bar and the message strip. A room has four levels of
/// floor, at rows 4, 9, 14 and 19; a player stands with their feet on the top of a floor row.
/// </summary>
public static class Field
{
    public const double Step = 1.0 / 60;
    public const double Width = 1024, Height = 768, Hud = 80;
    public const int Cols = 32, Rows = 20, Levels = 4;
    public const double Tile = 30;
    public const double Left = (Width - Cols * Tile) / 2, Top = Hud + 8;
    public const double Bottom = Top + Rows * Tile;

    /// <summary>The row whose top a level's floor is.</summary>
    public static int FloorRow(int level) => 4 + level * 5;

    /// <summary>The level whose floor is at this height, or -1.</summary>
    public static int LevelAt(double y)
    {
        for (int k = 0; k < Levels; k++)
            if (Math.Abs(y - FloorRow(k)) < 0.02) return k;
        return -1;
    }
}

/// <summary>What there is to search in a room.</summary>
public enum Furn { Desk, Bookshelf, Cabinet, Console, Sofa, Locker, Vending, Crate, Workbench, Jukebox, Terminal }

/// <summary>A piece of furniture on a floor: searched by standing at it and holding up. The terminal is the security computer.</summary>
public sealed class Furniture
{
    public Furn Kind;
    public int X, Level, Width = 2;
    /// <summary>What's in it: a puzzle piece (its number), or a snooze code, or nothing.</summary>
    public int Piece = -1;
    public bool Snooze;
    public bool Searched;
    public double Progress;

    public double Feet => Field.FloorRow(Level);
    public double Middle => X + Width / 2.0;
    public bool Covers(double x) => x >= X && x < X + Width;

    /// <summary>How long a search takes, in seconds: bigger things take longer.</summary>
    public double SearchTime => Kind switch
    {
        Furn.Bookshelf or Furn.Workbench or Furn.Console => 2.6,
        Furn.Crate or Furn.Jukebox or Furn.Vending => 2.0,
        _ => 1.6,
    };
}

/// <summary>
/// A lift: a platform two squares wide that carries a player between the levels in its range. It stays where it's
/// left until the player rides it again (or a terminal sends every lift home, or the player leaves the room).
/// </summary>
public sealed class Lift
{
    public int X, Lo, Hi, Home;
    /// <summary>Where it is, as a level (between two levels while moving).</summary>
    public double At;
    public int Toward;
    public bool Moving => Math.Abs(At - Toward) > 1e-6;
    public int Level => Moving ? -1 : Toward;
    public double Row => Field.FloorRow(0) + At * 5;
    public bool Covers(double x) => x >= X && x < X + 2;
}

public enum RobotKind
{
    /// <summary>Walks its floor end to end.</summary>
    Patrol,
    /// <summary>Comes after you along its floor.</summary>
    Hunter,
    /// <summary>Walks, and fires a bolt when you're on its floor in front of it.</summary>
    Shooter,
    /// <summary>Stands its ground, turning now and then, and fires.</summary>
    Sentry,
}

/// <summary>A robot: it keeps to its own stretch of floor. Touching one is death.</summary>
public sealed class Robot
{
    public RobotKind Kind;
    public int Level;
    public double X, Lo, Hi;
    public int Facing = 1;
    public double Cool, Turn, Aim, Walk;
    public double Feet => Field.FloorRow(Level);
    public const double HalfWidth = 0.42, Height = 2.0;
}

/// <summary>A robot's bolt, flying level across the room.</summary>
public sealed class Zap
{
    public double X, Y, Travelled;
    public int Dir;
    public bool Dead;
}

/// <summary>The orb: a floating mine at chest height over one level, gliding after an agent on that level through anything in its way.</summary>
public sealed class Orb
{
    public double X, Home, Wait;
    public int Level;
    public double Y => Field.FloorRow(Level) - 1.4;
    public const double Radius = 0.5;
}

public enum Pose { Standing, Flipping, Falling, Riding, Searching, Dead }

/// <summary>The agent: across in squares (the middle of the body), down in rows (the feet).</summary>
public sealed class Player
{
    public double X, Y, Vy, Walk;
    public int Facing = 1;
    public Pose Pose;
    /// <summary>Through a somersault, 0 to 1, from where, and which way.</summary>
    public double Flip, FlipFrom;
    public int FlipDir;
    public double DeadTime;
    public bool FirePrev, UpPrev;
    public const double HalfWidth = 0.3, Height = 2.6, Curled = 1.4;
    public const double FlipDistance = 4, FlipHeight = 3, FlipSeconds = 0.7;

    /// <summary>How high off the floor the somersault has carried the body.</summary>
    public double Lift => Pose == Pose.Flipping ? FlipHeight * Math.Sin(Math.PI * Flip) : 0;
    /// <summary>The body, top and bottom, in rows.</summary>
    public (double Top, double Bottom) Body => Pose == Pose.Flipping ? (Y - Lift - Curled, Y - Lift) : (Y - Height, Y);
}

public enum EffectKind { Score, Word, Spark, Puff }

public sealed class Effect
{
    public EffectKind Kind;
    public double X, Y, Age, Life = 0.8;
    public string Text = "";
    public uint Colour = 0xFFFFE14A;
    public bool Done => Age >= Life;
}
