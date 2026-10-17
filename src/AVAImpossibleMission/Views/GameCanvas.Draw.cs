using System.Globalization;
using Avalonia;
using Avalonia.Media;
using AVAImpossibleMission.Engine;
using Effect = AVAImpossibleMission.Engine.Effect;

namespace AVAImpossibleMission.Views;

public sealed partial class GameCanvas
{
    private static readonly Dictionary<uint, IBrush> BrushCache = [];
    private static readonly Dictionary<(uint, double), Pen> PenCache = [];
    private static readonly Typeface Retro = new(new FontFamily("avares://AVAImpossibleMission/Assets/Fonts#VT323"));

    public static IBrush Brush(uint argb)
    {
        if (!BrushCache.TryGetValue(argb, out var b))
            BrushCache[argb] = b = new SolidColorBrush(Color.FromUInt32(argb)).ToImmutable();
        return b;
    }

    private static Pen Pen(uint argb, double width = 1)
    {
        if (!PenCache.TryGetValue((argb, width), out var p))
            PenCache[(argb, width)] = p = new Pen(Brush(argb), width, lineCap: PenLineCap.Round);
        return p;
    }

    private const uint White = 0xFFF2F2FF, Yellow = 0xFFFFE14A, Red = 0xFFFF4A3A, Dim = 0xFF8A9AC8, Stone = 0xFF3A3448, Moss = 0xFF6AA05A;

    private static void Text(DrawingContext dc, string text, Point at, double size, uint colour, bool center = false, bool right = false, bool shadow = true)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(colour));
        double x = center ? at.X - ft.Width / 2 : right ? at.X - ft.Width : at.X;
        if (shadow) dc.DrawText(new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(0xC0000000)), new Point(x + 2, at.Y - ft.Height / 2 + 2));
        dc.DrawText(ft, new Point(x, at.Y - ft.Height / 2));
    }

    private static void Poly(DrawingContext dc, uint fill, uint? edge, double width, params Point[] pts)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(pts[0], true);
            foreach (var p in pts.Skip(1)) c.LineTo(p);
            c.EndFigure(true);
        }
        dc.DrawGeometry(fill == 0 ? null : Brush(fill), edge is { } e ? Pen(e, width) : null, g);
    }

    private static uint Hash(int x, int y, int salt)
    {
        uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(salt * 83492791);
        h ^= h >> 13;
        h *= 0x5bd1e995;
        h ^= h >> 15;
        return h;
    }

    private static uint Alpha(uint colour, double a) => (uint)(Math.Clamp(a, 0, 1) * 255) << 24 | (colour & 0x00FFFFFF);


    private bool InGame => Engine.State is GameState.Attract or GameState.Playing or GameState.Paused or GameState.GameOver;

    private const double T = Field.Tile;

    /// <summary>Each room's girders have a colour of their own.</summary>
    private static readonly uint[] RoomColours =
        [0xFF4A9AE0, 0xFFE0A040, 0xFF60C080, 0xFFC070D0, 0xFFE06A5A, 0xFF5AC8C8, 0xFFD0C050, 0xFF8A8AF0];

    private void DrawScreen(DrawingContext dc)
    {
        var g = Engine;
        dc.FillRectangle(Brush(0xFF06070E), new Rect(0, 0, Field.Width, Field.Height));
        if (InGame)
        {
            switch (g.Where)
            {
                case Where.Room: DrawRoom(dc, g.Room!); break;
                case Where.Shaft: DrawShaft(dc); break;
                case Where.Computer: DrawComputer(dc); break;
            }
            DrawStrip(dc);
        }
        DrawHud(dc);
        switch (g.State)
        {
            case GameState.Title: DrawTitle(dc); break;
            case GameState.HighScores: DrawHighScores(dc); break;
            case GameState.EnterInitials: DrawInitials(dc); break;
            default: DrawMessages(dc); break;
        }
        if (g.State == GameState.Paused)
        {
            dc.FillRectangle(Brush(0x90000000), new Rect(0, 0, Field.Width, Field.Height));
            Text(dc, "PAUSED", new Point(Field.Width / 2, 340), 80, Yellow, center: true);
            Text(dc, "ANY KEY TO CARRY ON - ESC TO QUIT", new Point(Field.Width / 2, 400), 26, White, center: true);
        }
        if (g.State == GameState.GameOver)
            Text(dc, g.Phase == Phase.Won ? "WELL DONE, AGENT" : "GAME OVER", new Point(Field.Width / 2, 360), 90, g.Phase == Phase.Won ? Yellow : Red, center: true);
        if (g.State == GameState.Attract)
            Text(dc, "DEMONSTRATION - PRESS ENTER TO PLAY", new Point(Field.Width / 2, Field.Height - 14), 24, Yellow, center: true);
    }

    // ───────────────────────────── a room ─────────────────────────────

    private void DrawRoom(DrawingContext dc, Room room)
    {
        var g = Engine;
        uint girder = RoomColours[room.Id % RoomColours.Length];
        using (dc.PushTransform(Matrix.CreateTranslation(Field.Left, Field.Top)))
        // (A little room above the top, for a somersault off the highest floor.)
        using (dc.PushClip(new Rect(0, -Field.Top + Field.Hud + 2, Field.Cols * T, Field.Rows * T + Field.Top - Field.Hud - 2)))
        {
            // A dark wall of panels.
            dc.FillRectangle(Brush(0xFF0C0F1E), new Rect(0, 0, Field.Cols * T, Field.Rows * T));
            for (int x = 0; x < Field.Cols; x += 4)
            for (int y = 0; y < Field.Rows; y += 5)
            {
                uint h = Hash(x, y, room.Id);
                dc.FillRectangle(Brush(h % 3 == 0 ? 0xFF10142A : 0xFF0E1224), new Rect(x * T + 2, y * T + 2, 4 * T - 4, 5 * T - 4), 3);
            }
            // The lift shafts: guide rails, and the platform.
            foreach (var l in room.Lifts)
            {
                double top = Field.FloorRow(l.Lo) * T - 6, bottom = Field.FloorRow(l.Hi) * T + 10;
                for (int s = 0; s < 2; s++)
                {
                    double rx = l.X * T + 3 + s * (2 * T - 6);
                    for (double y = top; y < bottom; y += 14) dc.DrawLine(Pen(0xFF3A4060, 2), new Point(rx, y), new Point(rx, Math.Min(bottom, y + 8)));
                }
            }
            // Floors: girders with rivets.
            for (int k = 0; k < Field.Levels; k++)
            {
                double y = Field.FloorRow(k) * T;
                for (int x = 0; x < Field.Cols; x++)
                {
                    if (!room.Floor[x, k]) continue;
                    dc.FillRectangle(Brush(girder), new Rect(x * T, y, T, 10));
                    dc.FillRectangle(Brush(Alpha(0xFF000000, 0.35)), new Rect(x * T, y + 7, T, 3));
                    dc.FillRectangle(Brush(0xFFFFFFFF & 0x60FFFFFF), new Rect(x * T, y, T, 2));
                    dc.DrawEllipse(Brush(0xFF1A1A26), null, new Point(x * T + T / 2, y + 5), 1.6, 1.6);
                }
            }
            foreach (var l in room.Lifts)
            {
                double y = l.Row * T;
                for (int i = 0; i < 6; i++)
                    dc.FillRectangle(Brush(i % 2 == 0 ? 0xFFF0C030 : 0xFF2A2A2A), new Rect(l.X * T + i * T / 3, y, T / 3, 10));
                dc.DrawRectangle(Pen(0xFF101010, 1.5), new Rect(l.X * T, y, 2 * T, 10));
            }
            DrawDoor(dc, 0, room.LeftDoor, -1);
            DrawDoor(dc, Field.Cols - 1, room.RightDoor, 1);
            foreach (var f in room.Furniture) DrawFurniture(dc, f, g.Searching == f);
            foreach (var r in g.Robots) DrawRobot(dc, r, g.Snoozed > 0);
            foreach (var z in g.Zaps) DrawZap(dc, z);
            if (g.Orb is { } o) DrawOrb(dc, o, g.Snoozed > 0);
            DrawAgent(dc, g.Player);
            foreach (var e in g.Effects) DrawEffect(dc, e);
        }
    }

    private void DrawDoor(DrawingContext dc, int col, int level, int side)
    {
        double y = Field.FloorRow(level) * T, x = col * T;
        double h = 3.4 * T;
        dc.FillRectangle(Brush(0xFF02030A), new Rect(x + (side < 0 ? 0 : 6), y - h, T - 6, h));
        dc.DrawRectangle(Pen(0xFF5A6A9A, 3), new Rect(x + (side < 0 ? 0 : 6), y - h, T - 6, h));
        Text(dc, side < 0 ? "<" : ">", new Point(x + T / 2, y - h - 12), 20, Dim, center: true);
    }

    private void DrawFurniture(DrawingContext dc, Furniture f, bool searching)
    {
        double x = f.X * T, w = f.Width * T, feet = f.Feet * T;
        double fade = f.Searched ? 0.45 : 1;
        uint C(uint c) => Alpha(c, fade);
        switch (f.Kind)
        {
            case Furn.Desk:
                dc.FillRectangle(Brush(C(0xFF8A5A30)), new Rect(x + 2, feet - 30, w - 4, 6));
                dc.FillRectangle(Brush(C(0xFF6A4220)), new Rect(x + 4, feet - 24, 18, 24));
                dc.FillRectangle(Brush(C(0xFF6A4220)), new Rect(x + w - 10, feet - 24, 6, 24));
                dc.FillRectangle(Brush(C(0xFF2A3A4A)), new Rect(x + 26, feet - 46, 22, 16), 2);
                dc.FillRectangle(Brush(C(0xFF6AE0A0)), new Rect(x + 28, feet - 44, 18, 11));
                break;
            case Furn.Bookshelf:
                dc.FillRectangle(Brush(C(0xFF5A3A20)), new Rect(x + 4, feet - 84, w - 8, 84));
                for (int s = 0; s < 3; s++)
                for (int b = 0; b < 6; b++)
                {
                    uint h = Hash(f.X * 7 + b, s, f.Level);
                    uint[] spines = [0xFFC04A3A, 0xFF3A6AC0, 0xFF4AA05A, 0xFFD0B040, 0xFF8A4AB0];
                    dc.FillRectangle(Brush(C(spines[h % 5])), new Rect(x + 8 + b * 8, feet - 80 + s * 27 + (h >> 8) % 6, 6, 20 - (h >> 8) % 6));
                }
                break;
            case Furn.Cabinet:
                dc.FillRectangle(Brush(C(0xFF7A8090)), new Rect(x + 10, feet - 64, w - 20, 64), 2);
                for (int d = 0; d < 3; d++)
                {
                    dc.DrawRectangle(Pen(C(0xFF4A5060), 1.5), new Rect(x + 13, feet - 61 + d * 20, w - 26, 17));
                    dc.FillRectangle(Brush(C(0xFFD0D0D8)), new Rect(x + w / 2 - 5, feet - 54 + d * 20, 10, 3));
                }
                break;
            case Furn.Console:
                dc.FillRectangle(Brush(C(0xFF3A4458)), new Rect(x + 2, feet - 72, w - 4, 72), 3);
                for (int i = 0; i < 2; i++)
                {
                    dc.FillEllipse(x + 16 + i * 28, feet - 52, 9, C(0xFF1A1A22));
                    double spin = _clock * (2 + i) + f.X;
                    dc.DrawLine(Pen(C(0xFF8A8AA0), 2), new Point(x + 16 + i * 28 + Math.Cos(spin) * 7, feet - 52 + Math.Sin(spin) * 7), new Point(x + 16 + i * 28 - Math.Cos(spin) * 7, feet - 52 - Math.Sin(spin) * 7));
                }
                for (int i = 0; i < 5; i++)
                    dc.FillRectangle(Brush(C((int)(_clock * 3 + i + f.X) % 3 == 0 ? 0xFFFF5A4A : 0xFF4AE07A)), new Rect(x + 8 + i * 9, feet - 24, 5, 5));
                break;
            case Furn.Sofa:
                dc.FillRectangle(Brush(C(0xFF8A3A4A)), new Rect(x + 2, feet - 34, w - 4, 26), 6);
                dc.FillRectangle(Brush(C(0xFFA04A5A)), new Rect(x + 6, feet - 22, w - 12, 12), 3);
                dc.FillRectangle(Brush(C(0xFF3A2A20)), new Rect(x + 6, feet - 8, 4, 8));
                dc.FillRectangle(Brush(C(0xFF3A2A20)), new Rect(x + w - 10, feet - 8, 4, 8));
                break;
            case Furn.Locker:
                for (int i = 0; i < 2; i++)
                {
                    dc.FillRectangle(Brush(C(0xFF4A6A8A)), new Rect(x + 6 + i * 25, feet - 76, 23, 76), 2);
                    for (int v = 0; v < 3; v++) dc.FillRectangle(Brush(C(0xFF2A3A4A)), new Rect(x + 11 + i * 25, feet - 70 + v * 5, 13, 2));
                }
                break;
            case Furn.Vending:
                dc.FillRectangle(Brush(C(0xFFC03A3A)), new Rect(x + 6, feet - 80, w - 12, 80), 4);
                dc.FillRectangle(Brush(C(0xFF1A2A3A)), new Rect(x + 11, feet - 72, w - 30, 46));
                for (int i = 0; i < 6; i++) dc.FillRectangle(Brush(C(0xFFE0E0A0)), new Rect(x + 14 + i % 3 * 9, feet - 68 + i / 3 * 20, 6, 12));
                dc.FillRectangle(Brush(C(0xFF101010)), new Rect(x + 13, feet - 18, w - 34, 8));
                break;
            case Furn.Crate:
                dc.FillRectangle(Brush(C(0xFFA07A40)), new Rect(x + 4, feet - 44, w - 8, 44));
                dc.DrawRectangle(Pen(C(0xFF6A4A20), 3), new Rect(x + 4, feet - 44, w - 8, 44));
                dc.DrawLine(Pen(C(0xFF6A4A20), 3), new Point(x + 4, feet - 44), new Point(x + w - 4, feet));
                dc.DrawLine(Pen(C(0xFF6A4A20), 3), new Point(x + w - 4, feet - 44), new Point(x + 4, feet));
                break;
            case Furn.Workbench:
                dc.FillRectangle(Brush(C(0xFF6A7080)), new Rect(x, feet - 32, w, 6));
                dc.FillRectangle(Brush(C(0xFF4A5060)), new Rect(x + 4, feet - 26, 5, 26));
                dc.FillRectangle(Brush(C(0xFF4A5060)), new Rect(x + w - 9, feet - 26, 5, 26));
                dc.FillRectangle(Brush(C(0xFFC0A030)), new Rect(x + 10, feet - 44, 14, 12));
                dc.DrawLine(Pen(C(0xFFB0B0C0), 3), new Point(x + 32, feet - 32), new Point(x + 44, feet - 50));
                break;
            case Furn.Jukebox:
                dc.FillRectangle(Brush(C(0xFF8A4AA0)), new Rect(x + 8, feet - 70, w - 16, 70), 18);
                dc.FillRectangle(Brush(C(0xFFF0D060)), new Rect(x + 16, feet - 58, w - 32, 22), 6);
                for (int i = 0; i < 4; i++)
                    dc.FillRectangle(Brush(C(Hash(i, (int)(_clock * 4), 3) % 2 == 0 ? 0xFF60E0F0 : 0xFFF060B0)), new Rect(x + 14 + i * 9, feet - 28, 6, 18));
                break;
            case Furn.Terminal:
                dc.FillRectangle(Brush(0xFF5A6070), new Rect(x + 24, feet - 30, 12, 30));
                dc.FillRectangle(Brush(0xFF3A4050), new Rect(x + 6, feet - 70, w - 12, 42), 4);
                dc.FillRectangle(Brush(0xFF082A14), new Rect(x + 11, feet - 65, w - 22, 32));
                for (int i = 0; i < 3; i++)
                    Text(dc, (int)(_clock * 2 + i) % 4 == 0 ? "_" : "SEC", new Point(x + 15, feet - 59 + i * 10), 12, 0xFF4AF07A, shadow: false);
                break;
        }
        if (searching || f.Progress > 0 && !f.Searched)
        {
            double frac = Math.Min(1, f.Progress / f.SearchTime);
            dc.FillRectangle(Brush(0xFF202030), new Rect(x + 4, feet - 100, w - 8, 6));
            dc.FillRectangle(Brush(Yellow), new Rect(x + 4, feet - 100, (w - 8) * frac, 6));
        }
    }

    private static uint RobotColour(RobotKind k) => k switch
    {
        RobotKind.Patrol => 0xFF8A9AB8,
        RobotKind.Hunter => 0xFFD85A4A,
        RobotKind.Shooter => 0xFFE0A040,
        _ => 0xFFA070D0,
    };

    private void DrawRobot(DrawingContext dc, Robot r, bool asleep)
    {
        double x = r.X * T, feet = r.Feet * T;
        uint body = RobotColour(r.Kind);
        // A dome on a drum, on a wheeled base.
        dc.FillRectangle(Brush(0xFF2A2A34), new Rect(x - 13, feet - 10, 26, 10), 4);
        for (int i = 0; i < 3; i++)
            dc.FillEllipse(x - 8 + i * 8, feet - 4, 3.5, 0xFF101014);
        dc.FillRectangle(Brush(body), new Rect(x - 12, feet - 44, 24, 34), 3);
        dc.FillRectangle(Brush(Alpha(0xFF000000, 0.25)), new Rect(x - 12, feet - 30, 24, 4));
        dc.DrawEllipse(Brush(body), Pen(Alpha(0xFF000000, 0.4), 1), new Point(x, feet - 44), 12, 14);
        dc.FillRectangle(Brush(body), new Rect(x - 12, feet - 46, 24, 4));
        // Its eye, scanning (shut when snoozing).
        double eye = asleep ? 0 : Math.Sin(_clock * 3 + r.X) * 5 + r.Facing * 3;
        if (asleep)
        {
            dc.DrawLine(Pen(0xFF101010, 2), new Point(x - 5, feet - 48), new Point(x + 5, feet - 48));
            Text(dc, "z", new Point(x + 14 + Math.Sin(_clock * 2) * 3, feet - 66 - (_clock * 10 % 10)), 20, 0xFF8FE8FF);
        }
        else dc.FillEllipse(x + eye, feet - 48, 4, r.Aim > 0 ? 0xFFFFFFFF : 0xFFFF3A2A);
        dc.DrawLine(Pen(0xFF6A6A7A, 2), new Point(x, feet - 58), new Point(x, feet - 66));
        dc.FillEllipse(x, feet - 67, 2.5, (int)(_clock * 4) % 2 == 0 ? 0xFFFF4A3A : 0xFF6A2A2A);
        if (r.Kind is RobotKind.Shooter or RobotKind.Sentry)
            dc.FillRectangle(Brush(0xFF3A3A44), new Rect(x + (r.Facing > 0 ? 10 : -20), feet - 34, 10, 5));
    }

    private void DrawZap(DrawingContext dc, Zap z)
    {
        double x = z.X * T, y = z.Y * T;
        var pts = new List<Point>();
        for (int i = 0; i <= 6; i++) pts.Add(new Point(x - z.Dir * i * 5, y + (i % 2 == 0 ? -4 : 4) * (Hash(i, (int)(_clock * 30), 5) % 2 == 0 ? 1 : -1)));
        for (int i = 0; i < pts.Count - 1; i++)
        {
            dc.DrawLine(Pen(0x80FFFF80, 6), pts[i], pts[i + 1]);
            dc.DrawLine(Pen(0xFFFFFFFF, 2), pts[i], pts[i + 1]);
        }
    }

    private void DrawOrb(DrawingContext dc, Orb o, bool asleep)
    {
        double x = o.X * T, y = o.Y * T, pulse = asleep ? 0 : Math.Sin(_clock * 6) * 2;
        dc.FillEllipse(x, y, Orb.Radius * T + 6 + pulse, Alpha(0xFFB04AF0, 0.25));
        dc.DrawEllipse(Brush(0xFF0A0610), Pen(0xFFB04AF0, 2.5), new Point(x, y), Orb.Radius * T, Orb.Radius * T);
        dc.FillEllipse(x - 5, y - 5, 3, 0x90FFFFFF);
    }

    private void DrawAgent(DrawingContext dc, Player p)
    {
        if (p.Pose == Pose.Dead && Engine.Cause != "fell") return;
        if (p.Pose == Pose.Dead) return;
        double x = p.X * T, feet = p.Y * T;
        if (p.Pose == Pose.Flipping)
        {
            // Tucked up in a ball, turning over.
            double cy = feet - p.Lift * T - Player.Curled * T / 2, a = p.Flip * Math.PI * 2 * p.FlipDir;
            Point R(double dx, double dy) => new(x + dx * Math.Cos(a) - dy * Math.Sin(a), cy + dx * Math.Sin(a) + dy * Math.Cos(a));
            dc.DrawLine(Pen(0xFF2A2A40, 9), R(-4, 4), R(10, 14));
            dc.DrawLine(Pen(0xFF2A2A40, 9), R(4, 4), R(14, 8));
            dc.FillEllipse(R(0, 0).X, R(0, 0).Y, 13, 0xFFF2F2F2);
            dc.DrawLine(Pen(0xFFF0C8A0, 4), R(4, -6), R(14, 4));
            var head = R(-6, -16);
            dc.FillEllipse(head.X, head.Y, 8, 0xFFF0C8A0);
            dc.FillEllipse(R(-8, -21).X, R(-8, -21).Y, 7, 0xFF3A2A1A);
            return;
        }
        double swing = p.Pose == Pose.Standing && Engine.Player.Walk > 0 ? Math.Sin(p.Walk * 14) * 7 : 0;
        if (p.Pose == Pose.Falling) swing = 0;
        double hip = feet - 38, shoulder = feet - 66;
        dc.DrawLine(Pen(0xFF2A2A40, 7), new Point(x - 3, hip), new Point(x - 3 + swing, feet - 3));
        dc.DrawLine(Pen(0xFF2A2A40, 7), new Point(x + 3, hip), new Point(x + 3 - swing, feet - 3));
        dc.FillRectangle(Brush(0xFF101018), new Rect(x - 7 + swing, feet - 4, 11, 4));
        dc.FillRectangle(Brush(0xFF101018), new Rect(x - 4 - swing, feet - 4, 11, 4));
        dc.FillRectangle(Brush(0xFFF2F2F2), new Rect(x - 9, shoulder, 18, 30), 4);
        dc.FillRectangle(Brush(0xFF2A2A40), new Rect(x - 9, hip - 4, 18, 5));
        bool reaching = p.Pose == Pose.Searching, falling = p.Pose == Pose.Falling;
        double rummage = reaching ? Math.Sin(_clock * 18) * 4 : 0;
        if (falling)
        {
            dc.DrawLine(Pen(0xFFF0C8A0, 4), new Point(x - 8, shoulder + 3), new Point(x - 14, shoulder - 16));
            dc.DrawLine(Pen(0xFFF0C8A0, 4), new Point(x + 8, shoulder + 3), new Point(x + 14, shoulder - 16));
        }
        else if (reaching)
        {
            dc.DrawLine(Pen(0xFFF0C8A0, 4), new Point(x - 6, shoulder + 4), new Point(x + p.Facing * 14 + rummage, shoulder + 14));
            dc.DrawLine(Pen(0xFFF0C8A0, 4), new Point(x + 6, shoulder + 4), new Point(x + p.Facing * 16 - rummage, shoulder + 20));
        }
        else
        {
            dc.DrawLine(Pen(0xFFF0C8A0, 4), new Point(x - 8, shoulder + 3), new Point(x - 10 - swing * 0.5, shoulder + 26));
            dc.DrawLine(Pen(0xFFF0C8A0, 4), new Point(x + 8, shoulder + 3), new Point(x + 10 + swing * 0.5, shoulder + 26));
        }
        dc.FillEllipse(x, shoulder - 10, 9, 0xFFF0C8A0);
        dc.FillRectangle(Brush(0xFF3A2A1A), new Rect(x - 9, shoulder - 21, 18, 8), 3);
        dc.FillEllipse(x + p.Facing * 4, shoulder - 10, 1.8, 0xFF101010);
    }

    private void DrawEffect(DrawingContext dc, Effect e)
    {
        double a = 1 - e.Age / e.Life, x = e.X * T, y = e.Y * T;
        switch (e.Kind)
        {
            case EffectKind.Word:
                Text(dc, e.Text, new Point(Math.Clamp(x, 110, Field.Cols * T - 110), y - e.Age * 30), 26, Alpha(e.Colour, Math.Min(1, a * 2)), center: true);
                break;
            case EffectKind.Spark:
                for (int i = 0; i < 10; i++)
                {
                    double ang = i * Math.PI / 5 + e.Age * 3, r = 10 + e.Age * 60;
                    dc.DrawLine(Pen(Alpha(0xFFFFFF80, a), 3), new Point(x + Math.Cos(ang) * r * 0.4, y + Math.Sin(ang) * r * 0.4), new Point(x + Math.Cos(ang) * r, y + Math.Sin(ang) * r));
                }
                dc.FillEllipse(x, y, 14 * a + 4, Alpha(0xFFFFFFFF, a));
                break;
            case EffectKind.Puff:
                Text(dc, "AAAAAH!", new Point(x, Math.Min(y, Field.Rows * T - 40) - e.Age * 20), 30, Alpha(White, a), center: true);
                break;
        }
    }

    // ───────────────────────────── an elevator shaft ─────────────────────────────

    private static double StopY(double stop) => Field.Top + 116 + stop * 148;

    private void DrawShaft(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2, half = 60;
        dc.FillRectangle(Brush(0xFF080A14), new Rect(0, Field.Top, Field.Width, Field.Rows * T));
        // The corridors off each stop.
        for (int stop = 0; stop < Complex.Stops; stop++)
        {
            double y = StopY(stop);
            foreach (int side in new[] { -1, 1 })
            {
                double x0 = side < 0 ? 0 : cx + half, x1 = side < 0 ? cx - half : Field.Width;
                var room = g.Complex.Beside(g.Shaft, stop, side);
                bool control = g.Shaft == Complex.Shafts - 1 && side > 0 && stop == Complex.ControlStop;
                bool wayIn = g.Shaft == 0 && side < 0 && stop == 0;
                if (room == null && !control && !wayIn)
                {
                    // Solid rock.
                    dc.FillRectangle(Brush(0xFF161420), new Rect(x0, y - 110, x1 - x0, 130));
                    for (int i = 0; i < 18; i++)
                    {
                        uint h = Hash(i, stop, side + 7);
                        dc.FillEllipse(x0 + h % (uint)(x1 - x0), y - 100 + (h >> 10) % 110, 3 + h % 4, 0xFF22202E);
                    }
                    continue;
                }
                dc.FillRectangle(Brush(0xFF10142A), new Rect(x0, y - 100, x1 - x0, 100));
                dc.FillRectangle(Brush(0xFF5A6A9A), new Rect(x0, y, x1 - x0, 10));
                for (double lx = x0 + 30; lx < x1 - 10; lx += 90) dc.FillRectangle(Brush(0xFF2A3050), new Rect(lx, y - 96, 40, 6), 2);
                double sx = side < 0 ? 40 : Field.Width - 40;
                if (control)
                {
                    bool open = g.CardsDone == Puzzle.Cards;
                    dc.FillRectangle(Brush(open ? 0xFF2A6A3A : 0xFF6A1A1A), new Rect(sx - 30, y - 96, 60, 96));
                    Text(dc, "CONTROL", new Point(sx - 160, y - 64), 28, open ? 0xFF8FFF8F : Red, center: true);
                    Text(dc, open ? "OPEN" : "LOCKED", new Point(sx - 160, y - 38), 24, open ? 0xFF8FFF8F : Red, center: true);
                }
                else if (wayIn)
                    Text(dc, "THE WAY IN", new Point(sx + 120, y - 50), 26, Dim, center: true);
                else
                {
                    var r = room!;
                    dc.FillRectangle(Brush(0xFF02030A), new Rect(sx - 26, y - 96, 52, 96));
                    dc.DrawRectangle(Pen(RoomColours[r.Id % RoomColours.Length], 3), new Rect(sx - 26, y - 96, 52, 96));
                    double lx = side < 0 ? (cx - half) / 2 + 20 : cx + half + (Field.Width - cx - half) / 2 - 20;
                    Text(dc, r.Name, new Point(lx, y - 64), 28, White, center: true);
                    string state = !r.Seen ? "NOT YET SEARCHED" : r.Done ? "SEARCHED" : $"{r.Left} LEFT TO SEARCH";
                    Text(dc, state, new Point(lx, y - 38), 22, r.Done ? 0xFF8FFF8F : r.Seen ? Yellow : Dim, center: true);
                }
            }
        }
        // The shaft, its cables, and the car.
        dc.FillRectangle(Brush(0xFF04050A), new Rect(cx - half, Field.Top, half * 2, Field.Rows * T));
        dc.DrawLine(Pen(0xFF3A4060, 3), new Point(cx - half, Field.Top), new Point(cx - half, Field.Bottom));
        dc.DrawLine(Pen(0xFF3A4060, 3), new Point(cx + half, Field.Top), new Point(cx + half, Field.Bottom));
        double car = StopY(g.CarAt);
        dc.DrawLine(Pen(0xFF6A6A7A, 2), new Point(cx - 20, Field.Top), new Point(cx - 20, car - 110));
        dc.DrawLine(Pen(0xFF6A6A7A, 2), new Point(cx + 20, Field.Top), new Point(cx + 20, car - 110));
        dc.DrawRectangle(Pen(0xFF8A8AA0, 2), new Rect(cx - half + 6, car - 110, half * 2 - 12, 110));
        dc.FillRectangle(Brush(0xFFF0C030), new Rect(cx - half + 4, car, half * 2 - 8, 10));
        for (int i = 0; i < 6; i++) dc.FillRectangle(Brush(0xFF2A2A2A), new Rect(cx - half + 8 + i * 19, car, 9, 10));
        var p = g.Player;
        p.Facing = g.ShaftFacing;
        double walk = p.Walk;
        p.Walk = 0;
        var keep = (p.X, p.Y, p.Pose);
        p.X = cx / T - Field.Left / T;
        p.Y = car / T - Field.Top / T;
        p.Pose = Pose.Standing;
        using (dc.PushTransform(Matrix.CreateTranslation(Field.Left, Field.Top))) DrawAgent(dc, p);
        (p.X, p.Y, p.Pose) = keep;
        p.Walk = walk;
        Text(dc, "UP/DOWN: RIDE   LEFT/RIGHT: GO IN   BUTTON: POCKET COMPUTER", new Point(cx, Field.Bottom - 12), 22, 0xFF6A7AA8, center: true);
    }

    // ───────────────────────────── the pocket computer ─────────────────────────────

    private void DrawComputer(DrawingContext dc)
    {
        var g = Engine;
        dc.FillRectangle(Brush(0xFF1A1810), new Rect(0, Field.Top, Field.Width, Field.Rows * T));
        var body = new Rect(40, Field.Top + 10, Field.Width - 80, Field.Rows * T - 20);
        dc.FillRectangle(Brush(0xFFB8AE90), body, 18);
        dc.FillRectangle(Brush(0xFF0A1A10), new Rect(body.X + 22, body.Y + 22, body.Width - 44, body.Height - 44), 8);
        uint green = 0xFF6AF08A, dimGreen = 0xFF2A6A3A;
        double left = body.X + 40, top = body.Y + 40;
        Text(dc, "POCKET COMPUTER", new Point(left, top), 28, green);

        // The complex: shafts and rooms.
        double mx = left, my = top + 40, cw = 64, ch = 44;
        for (int s = 0; s < Complex.Shafts; s++)
        {
            double sx = mx + s * (cw + 14);
            dc.FillRectangle(Brush(dimGreen), new Rect(sx, my, 6, Complex.Stops * (ch + 10)));
        }
        foreach (var r in g.Complex.Rooms)
        {
            var rect = new Rect(mx + r.Shaft * (cw + 14) + 10, my + r.Stop * (ch + 10) + 2, cw, ch);
            dc.FillRectangle(Brush(r.Done ? 0xFF1E5A2E : r.Seen ? 0xFF4A4A1A : 0xFF0E2A16), rect, 3);
            dc.DrawRectangle(Pen(green, 1), rect);
            if (r.Seen) Text(dc, r.Done ? "OK" : r.Left.ToString(), new Point(rect.Center.X, rect.Center.Y), 22, r.Done ? green : Yellow, center: true);
        }
        dc.FillRectangle(Brush(0xFF6A1A1A), new Rect(mx + (Complex.Shafts - 1) * (cw + 14) + 10, my + Complex.ControlStop * (ch + 10) + 2, 26, ch), 3);
        if ((int)(_clock * 3) % 2 == 0)
            dc.FillEllipse(mx + g.Shaft * (cw + 14) + 3, my + g.Stop * (ch + 10) + ch / 2, 7, Yellow);
        Text(dc, "YOU ARE HERE", new Point(mx, my + Complex.Stops * (ch + 10) + 16), 20, Yellow);

        // The password, letter by letter.
        double px = left + 420;
        Text(dc, "PASSWORD", new Point(px, top + 50), 24, green);
        for (int k = 0; k < Puzzle.Cards; k++)
        {
            var rect = new Rect(px + k * 52, top + 70, 44, 54);
            dc.FillRectangle(Brush(g.CardDone[k] ? Puzzle.Colours[k] : 0xFF0E2A16), rect, 4);
            dc.DrawRectangle(Pen(Puzzle.Colours[k], 2), rect);
            Text(dc, g.CardDone[k] ? Puzzle.Password[k].ToString() : "?", new Point(rect.Center.X, rect.Center.Y), 40, g.CardDone[k] ? 0xFF101010 : dimGreen, center: true);
        }

        // The work area: the pieces laid on it, over one another.
        double wx = px, wy = top + 160, hole = 26;
        Text(dc, "WORK AREA", new Point(wx, wy - 12), 22, green);
        var placed = g.Held.Where(p => p.Placed).ToList();
        DrawCard(dc, wx, wy, hole, placed, placed.Count > 0 ? Puzzle.Colours[placed[0].Card] : dimGreen, frame: true);

        // The pieces in hand.
        var slots = g.Slots;
        double sy = body.Bottom - 150, size = 9;
        Text(dc, slots.Count == 0 ? "NO PIECES IN HAND - SEARCH THE ROOMS" : "PIECES IN HAND", new Point(left, sy - 22), 22, green);
        for (int i = 0; i <= slots.Count; i++)
        {
            double x = left + i * 66;
            if (x > body.Right - 120) break;
            bool on = i == g.Cursor;
            if (on) dc.DrawRectangle(null, Pen(Yellow, 3), new Rect(x - 5, sy - 5, 60, 54), 4, 4);
            if (i == slots.Count)
            {
                Text(dc, "DONE", new Point(x + 25, sy + 22), 24, on ? Yellow : green, center: true);
                continue;
            }
            var piece = slots[i];
            DrawCard(dc, x, sy, size, [piece], Puzzle.Colours[piece.Card], frame: true);
            if (piece.Placed) Text(dc, "ON", new Point(x + 25, sy + 56), 18, Yellow, center: true);
        }
        Text(dc, "LEFT/RIGHT: CHOOSE   UP: TURN SIDEWAYS   DOWN: TURN OVER   BUTTON: LAY DOWN / PICK UP", new Point(Field.Width / 2, body.Bottom - 40), 20, green, center: true);
    }

    /// <summary>A punch card (or the pieces of one) with its holes.</summary>
    private static void DrawCard(DrawingContext dc, double x, double y, double hole, List<Piece> pieces, uint colour, bool frame)
    {
        double w = Card.Cols * hole * 1.0 + hole * 0.6, h = Card.Rows * hole + hole * 0.6;
        if (frame) dc.DrawRectangle(null, Pen(Alpha(colour, 0.6), Math.Max(1, hole / 10)), new Rect(x, y, w, h), hole / 4, hole / 4);
        var count = new Dictionary<(int, int), int>();
        foreach (var p in pieces)
        foreach (var c in p.Showing)
            count[c] = count.GetValueOrDefault(c) + 1;
        for (int c = 0; c < Card.Cols; c++)
        for (int r = 0; r < Card.Rows; r++)
        {
            var at = new Point(x + hole * 0.3 + c * hole + hole / 2, y + hole * 0.3 + r * hole + hole / 2);
            int n = count.GetValueOrDefault((c, r));
            if (n == 0) dc.DrawEllipse(null, Pen(Alpha(colour, 0.25), 1), at, hole * 0.3, hole * 0.3);
            else dc.DrawEllipse(Brush(n > 1 ? 0xFFFF4A3A : colour), null, at, hole * 0.36, hole * 0.36);
        }
    }

    // ───────────────────────────── around the edges ─────────────────────────────

    private void DrawStrip(DrawingContext dc)
    {
        var g = Engine;
        double y = Field.Bottom + 10;
        dc.FillRectangle(Brush(0xFF0E1020), new Rect(0, Field.Bottom, Field.Width, Field.Height - Field.Bottom));
        dc.DrawLine(Pen(0xFF3A3A6A, 2), new Point(0, Field.Bottom), new Point(Field.Width, Field.Bottom));
        string where = g.Where switch
        {
            Where.Room => g.Room!.Name,
            Where.Shaft => $"ELEVATOR SHAFT {g.Shaft + 1}",
            _ => "POCKET COMPUTER",
        };
        Text(dc, where, new Point(40, y + 22), 30, White);
        if (g.Where == Where.Room && g.Room is { } room)
            Text(dc, room.Done ? "SEARCHED" : $"{room.Left} TO SEARCH", new Point(40, y + 50), 22, room.Done ? 0xFF8FFF8F : Dim);
        // The pieces in hand, by card.
        double x = 420;
        Text(dc, "PIECES", new Point(x, y + 14), 20, Dim);
        for (int k = 0; k < Puzzle.Cards; k++)
        {
            int n = g.Held.Count(p => p.Card == k);
            for (int i = 0; i < Card.PiecesPer; i++)
            {
                var rect = new Rect(x + k * 44 + i * 13, y + 30, 11, 16);
                dc.FillRectangle(Brush(g.CardDone[k] ? Alpha(Puzzle.Colours[k], 0.35) : i < n ? Puzzle.Colours[k] : 0xFF1A1C2E), rect, 2);
            }
        }
        Text(dc, $"SNOOZE CODES {g.Codes}", new Point(Field.Width - 40, y + 22), 26, 0xFF8FE8FF, right: true);
        if (g.Snoozed > 0)
            Text(dc, $"ROBOTS ASLEEP {g.Snoozed:0}", new Point(Field.Width - 40, y + 50), 22, 0xFF8FE8FF, right: true);
    }

    private void DrawHud(DrawingContext dc)
    {
        var g = Engine;
        dc.FillRectangle(Brush(0xFF0E1020), new Rect(0, 0, Field.Width, Field.Hud));
        dc.DrawLine(Pen(0xFF3A3A6A, 2), new Point(0, Field.Hud), new Point(Field.Width, Field.Hud));
        Text(dc, "SCORE", new Point(24, 22), 24, Dim);
        Text(dc, g.Score.ToString("000000"), new Point(24, 52), 40, Yellow);
        Text(dc, "HI " + Math.Max(g.HighScores.TopScore, g.Score).ToString("000000"), new Point(24 + 150, 22), 24, Dim);
        if (!InGame) return;
        var left = TimeSpan.FromSeconds(Math.Ceiling(g.Clock));
        bool low = g.Clock < 3600;
        Text(dc, $"{(int)left.TotalHours}:{left.Minutes:00}:{left.Seconds:00}", new Point(Field.Width / 2, 40), 56, low && (int)(_clock * 2) % 2 == 0 ? Red : White, center: true);
        Text(dc, "MISSION TIME LEFT", new Point(Field.Width / 2, 70), 18, Dim, center: true);
        Text(dc, "CARDS", new Point(Field.Width - 24 - 6 * 30, 22), 22, Dim);
        for (int k = 0; k < Puzzle.Cards; k++)
        {
            var rect = new Rect(Field.Width - 24 - (6 - k) * 30, 38, 24, 32);
            dc.FillRectangle(Brush(g.CardDone[k] ? Puzzle.Colours[k] : 0xFF1A1C2E), rect, 3);
            if (g.CardDone[k]) Text(dc, Puzzle.Password[k].ToString(), new Point(rect.Center.X, rect.Center.Y), 26, 0xFF101010, center: true, shadow: false);
        }
    }

    private void DrawMessages(DrawingContext dc)
    {
        var g = Engine;
        if (g.Message is not { } msg) return;
        double cx = Field.Width / 2, cy = Field.Top + 230;
        dc.FillRectangle(Brush(0xB0000000), new Rect(cx - 400, cy - 52, 800, 110), 10);
        Text(dc, msg, new Point(cx, cy - 12), msg.Length > 16 ? 46 : 58, msg == "TIME'S UP" ? Red : Yellow, center: true);
        if (g.SubMessage is { } sub) Text(dc, sub, new Point(cx, cy + 34), 24, White, center: true);
    }

    // ───────────────────────────── title and scores ─────────────────────────────

    private void DrawTitle(DrawingContext dc)
    {
        double cx = Field.Width / 2;
        dc.FillRectangle(Brush(0xFF06070E), new Rect(0, Field.Hud, Field.Width, Field.Height - Field.Hud));
        // A girder floor, a robot on it, and the agent somersaulting over.
        double floor = 520;
        for (double x = 80; x < Field.Width - 80; x += T)
        {
            dc.FillRectangle(Brush(0xFF4A9AE0), new Rect(x, floor, T, 10));
            dc.FillRectangle(Brush(0x60FFFFFF), new Rect(x, floor, T, 2));
        }
        var robot = new Robot { Kind = RobotKind.Patrol, Level = 0, X = (cx + Math.Sin(_clock * 0.6) * 140) / T, Facing = Math.Cos(_clock * 0.6) > 0 ? 1 : -1 };
        using (dc.PushTransform(Matrix.CreateTranslation(0, floor - Field.FloorRow(0) * T))) DrawRobot(dc, robot, false);
        double cycle = _clock % 3.2 / 3.2;
        var agent = new Player { X = (cx - 330 + cycle * 660) / T, Y = floor / T, Facing = 1 };
        if (cycle > 0.4 && cycle < 0.6) { agent.Pose = Pose.Flipping; agent.Flip = (cycle - 0.4) / 0.2; agent.FlipDir = 1; }
        else { agent.Pose = Pose.Standing; agent.Walk = _clock; }
        DrawAgent(dc, agent);
        string title = "AVA IMPOSSIBLE MISSION";
        for (int i = 0; i < title.Length; i++)
            Text(dc, title[i].ToString(), new Point(cx - title.Length * 19 + 19 + i * 38, 150 + Math.Sin(_clock * 2 + i * 0.3) * 2), 72, i % 2 == 0 ? 0xFF8AC8FF : 0xFFE8F4FF, center: true);
        Text(dc, "AFTER THE 1984 COMMODORE 64 CLASSIC", new Point(cx, 200), 24, Dim, center: true);
        Text(dc, "PROFESSOR VEX MEANS TO HOLD THE WORLD TO RANSOM.", new Point(cx, 260), 28, White, center: true);
        Text(dc, "SEARCH HIS COMPLEX FOR THE SIX PUNCH CARDS OF HIS PASSWORD,", new Point(cx, 292), 28, White, center: true);
        Text(dc, "PUT THEM TOGETHER, AND REACH HIS CONTROL ROOM IN SIX HOURS.", new Point(cx, 324), 28, White, center: true);
        Text(dc, "MIND THE ROBOTS.", new Point(cx, 372), 30, Red, center: true);
        if ((int)(_clock * 2) % 2 == 0)
            Text(dc, "PRESS ENTER TO BEGIN", new Point(cx, 610), 42, Yellow, center: true);
        Text(dc, "ARROWS: WALK, RIDE LIFTS    HOLD UP: SEARCH    SPACE: SOMERSAULT", new Point(cx, 664), 22, 0xFF8AA0D0, center: true);
        Text(dc, "TAB OR THE BUTTON IN A SHAFT: POCKET COMPUTER    P: PAUSE    M: SOUND", new Point(cx, 692), 20, 0xFF8AA0D0, center: true);
    }

    private void DrawHighScores(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        dc.FillRectangle(Brush(0xFF06070E), new Rect(0, Field.Hud, Field.Width, Field.Height - Field.Hud));
        Text(dc, "THE BEST AGENTS", new Point(cx, 140), 64, 0xFF8AC8FF, center: true);
        Text(dc, "RANK NAME    SCORE  CARDS", new Point(cx, 204), 30, Dim, center: true);
        var entries = g.HighScores.Entries;
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            bool mine = i == g.NewEntryIndex && (int)(_clock * 4) % 2 == 0;
            Text(dc, $"{i + 1,2}.  {e.Initials}  {e.Score,8}   {e.Stage,4}", new Point(cx, 248 + i * 42), 34, mine ? Red : i == 0 ? Yellow : White, center: true);
        }
        Text(dc, "PRESS ENTER TO PLAY", new Point(cx, 710), 30, White, center: true);
    }

    private void DrawInitials(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        dc.FillRectangle(Brush(0xFF06070E), new Rect(0, Field.Hud, Field.Width, Field.Height - Field.Hud));
        Text(dc, "A NEW HIGH SCORE!", new Point(cx, 200), 64, Yellow, center: true);
        Text(dc, g.Score.ToString(), new Point(cx, 270), 52, White, center: true);
        Text(dc, "ENTER YOUR INITIALS", new Point(cx, 350), 34, Dim, center: true);
        if (g.Initials is { } entry)
        {
            string s = entry.Initials;
            for (int i = 0; i < s.Length; i++)
            {
                bool cur = i == entry.Cursor && (int)(_clock * 3) % 2 == 0;
                Text(dc, s[i].ToString(), new Point(cx - 60 + i * 60, 440), 80, cur ? Red : Yellow, center: true);
                dc.FillRectangle(Brush(i == entry.Cursor ? Red : Dim), new Rect(cx - 82 + i * 60, 480, 44, 4));
            }
        }
        Text(dc, "TYPE, OR UP/DOWN AND SPACE; ENTER WHEN DONE", new Point(cx, 560), 26, White, center: true);
    }
}

internal static class DrawingExtensions
{
    public static void FillEllipse(this DrawingContext dc, double x, double y, double r, uint colour) =>
        dc.DrawEllipse(GameCanvas.Brush(colour), null, new Point(x, y), r, r);
}
