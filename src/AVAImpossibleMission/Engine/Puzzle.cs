namespace AVAImpossibleMission.Engine;

/// <summary>
/// The professor's password is on six punch cards, torn into three pieces each and hidden about the complex. A
/// card is five holes across by four down, nine of them punched; each piece carries three of its holes. The
/// pieces turn up the wrong way round as often as not: in the pocket computer they're flipped and laid over
/// one another until the three pieces of a card show its holes the right way round.
/// </summary>
public sealed class Card
{
    public const int Cols = 5, Rows = 4, Holes = 9, PiecesPer = 3;
    public int Index;
    public HashSet<(int C, int R)> Cells = [];
    public uint Colour;
}

public sealed class Piece
{
    public int Index, Card;
    /// <summary>Its holes, the right way round.</summary>
    public HashSet<(int C, int R)> Cells = [];
    public bool FlipH, FlipV;
    /// <summary>On the pocket computer's work area.</summary>
    public bool Placed;

    public static (int C, int R) Turn((int C, int R) cell, bool h, bool v) =>
        (h ? Engine.Card.Cols - 1 - cell.C : cell.C, v ? Engine.Card.Rows - 1 - cell.R : cell.R);

    /// <summary>Its holes the way it's lying now.</summary>
    public IEnumerable<(int C, int R)> Showing => Cells.Select(c => Turn(c, FlipH, FlipV));

    /// <summary>Lying the right way round (or a way that looks just the same).</summary>
    public bool Right => Showing.ToHashSet().SetEquals(Cells);
}

public sealed class Puzzle
{
    public const int Cards = 6;
    public static readonly uint[] Colours = [0xFFE85A4F, 0xFF4FB0E8, 0xFF6BD86B, 0xFFE8C84F, 0xFFC27BE8, 0xFFE89A4F];
    public static readonly string Password = "VORTEX";

    public List<Card> All { get; } = [];
    public List<Piece> Pieces { get; } = [];

    /// <summary>The cards and their pieces for a game: each card's holes chosen so it looks different turned over.</summary>
    public static Puzzle Deal(Random rng)
    {
        var p = new Puzzle();
        var cells = Enumerable.Range(0, Card.Cols * Card.Rows).Select(i => (C: i % Card.Cols, R: i / Card.Cols)).ToList();
        for (int k = 0; k < Cards; k++)
        {
            HashSet<(int C, int R)> holes;
            do holes = cells.OrderBy(_ => rng.Next()).Take(Card.Holes).ToHashSet();
            while (Lopsided(holes) is false || p.All.Any(c => c.Cells.SetEquals(holes)));
            p.All.Add(new Card { Index = k, Cells = holes, Colour = Colours[k] });
            var order = holes.OrderBy(_ => rng.Next()).ToList();
            for (int i = 0; i < Card.PiecesPer; i++)
            {
                var piece = new Piece
                {
                    Index = k * Card.PiecesPer + i,
                    Card = k,
                    Cells = order.Skip(i * 3).Take(3).ToHashSet(),
                    FlipH = rng.Next(2) == 0,
                    FlipV = rng.Next(2) == 0,
                };
                p.Pieces.Add(piece);
            }
        }
        return p;
    }

    /// <summary>Whether a card looks different every way it can be turned.</summary>
    private static bool Lopsided(HashSet<(int C, int R)> holes) =>
        new[] { (true, false), (false, true), (true, true) }.All(t => !holes.Select(c => Piece.Turn(c, t.Item1, t.Item2)).ToHashSet().SetEquals(holes));

    /// <summary>
    /// The card the work area makes, if it makes one: exactly the three pieces of a card, all the right way round.
    /// </summary>
    public static int Completes(IEnumerable<Piece> placed)
    {
        var list = placed.ToList();
        if (list.Count != Card.PiecesPer || list.Select(p => p.Card).Distinct().Count() != 1) return -1;
        return list.All(p => p.Right) ? list[0].Card : -1;
    }
}
