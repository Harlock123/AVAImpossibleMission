using AVAImpossibleMission.Engine;

namespace AVAImpossibleMission.Tests;

/// <summary>The punch cards and their pieces.</summary>
public class PuzzleTests
{
    [Fact]
    public void Six_cards_of_nine_holes_each_different_and_different_turned_over()
    {
        var p = Puzzle.Deal(new Random(5));
        Assert.Equal(6, p.All.Count);
        foreach (var c in p.All)
        {
            Assert.Equal(Card.Holes, c.Cells.Count);
            foreach (var (h, v) in new[] { (true, false), (false, true), (true, true) })
                Assert.False(c.Cells.Select(x => Piece.Turn(x, h, v)).ToHashSet().SetEquals(c.Cells));
        }
        Assert.Equal(6, p.All.Select(c => string.Join(",", c.Cells.Order())).Distinct().Count());
    }

    [Fact]
    public void Each_card_is_torn_into_three_pieces_of_three_holes()
    {
        var p = Puzzle.Deal(new Random(5));
        Assert.Equal(18, p.Pieces.Count);
        foreach (var c in p.All)
        {
            var pieces = p.Pieces.Where(x => x.Card == c.Index).ToList();
            Assert.Equal(3, pieces.Count);
            Assert.All(pieces, x => Assert.Equal(3, x.Cells.Count));
            Assert.True(pieces.SelectMany(x => x.Cells).ToHashSet().SetEquals(c.Cells));
        }
        // Plenty of them turn up the wrong way round.
        Assert.True(p.Pieces.Count(x => !x.Right) >= 6);
    }

    [Fact]
    public void A_card_is_made_by_its_three_pieces_all_the_right_way_round()
    {
        var p = Puzzle.Deal(new Random(5));
        var mine = p.Pieces.Where(x => x.Card == 2).ToList();
        foreach (var x in mine) x.FlipH = x.FlipV = false;
        Assert.Equal(2, Puzzle.Completes(mine));
        Assert.Equal(-1, Puzzle.Completes(mine.Take(2)));
        // One turned over: not unless it looks the same that way.
        var turned = mine.First(x => !x.Cells.Select(c => Piece.Turn(c, true, false)).ToHashSet().SetEquals(x.Cells));
        turned.FlipH = true;
        Assert.Equal(-1, Puzzle.Completes(mine));
        turned.FlipH = false;
        // A piece of another card in among them: no.
        Assert.Equal(-1, Puzzle.Completes(mine.Take(2).Append(p.Pieces.First(x => x.Card == 3))));
    }

    [Fact]
    public void Turning_a_piece_mirrors_its_holes()
    {
        Assert.Equal((4, 0), Piece.Turn((0, 0), true, false));
        Assert.Equal((0, 3), Piece.Turn((0, 0), false, true));
        Assert.Equal((3, 1), Piece.Turn((1, 2), true, true));
    }
}
