namespace AVAImpossibleMission.Engine;

public enum GameState { Title, Attract, Playing, Paused, GameOver, EnterInitials, HighScores }

/// <summary>How the mission stands.</summary>
public enum Phase
{
    /// <summary>The briefing, at the top of the first shaft.</summary>
    Start,
    /// <summary>Searching the complex against the clock.</summary>
    Play,
    /// <summary>In the control room with every card: the professor is stopped.</summary>
    Won,
    /// <summary>The clock ran out.</summary>
    TimeUp,
}

/// <summary>Where the agent is.</summary>
public enum Where { Shaft, Room, Computer }

/// <summary>Sounds the engine asks for; the host decides how they sound.</summary>
public enum Sfx
{
    Flip, Land, Search, Nothing, Piece, Snooze, Code, LiftMove, Elevator, ElevatorStop, Door, Zap, Electrocute, Scream,
    CardDone, Computer, Click, Locked,
    Welcome, Defeat, Win,
    TitleTune, GameOver,
}

/// <summary>
/// The whole game with no UI. Call <see cref="Update"/> with the elapsed time and the controls; it runs in fixed
/// 60 Hz steps, so it plays the same at any frame rate.
/// </summary>
public sealed partial class GameEngine
{
    public const double TitleIdleSeconds = 20;
    public const double AttractSeconds = 70;
    public const double HighScoreSeconds = 9;
    /// <summary>How long the spoken welcome lasts before anything else is heard.</summary>
    public const double WelcomeSeconds = 6.5;

    private double _accumulator, _idle;
    private bool _startPrev;
    private bool _welcomePending = true, _tunePending = true;
    private readonly int? _seed;

    /// <summary>A game whose hiding places are dealt from <paramref name="seed"/> (a fresh deal every game if none).</summary>
    public GameEngine(HighScoreTable? table = null, int? seed = null)
    {
        HighScores = table ?? HighScoreTable.Load();
        _seed = seed;
        NewGame(seed ?? 1);
        State = GameState.Title;
    }

    public event Action<Sfx>? Sound;

    public GameState State { get; private set; } = GameState.Title;
    public Phase Phase { get; private set; } = Phase.Start;
    public double StateTime { get; private set; }
    public double PhaseTime { get; private set; }
    public HighScoreTable HighScores { get; }
    public InitialsEntry? Initials { get; private set; }
    public int NewEntryIndex { get; private set; } = -1;
    public bool Silent { get; set; }

    public List<Effect> Effects { get; } = [];

    /// <summary>A word across the screen, and for how much longer.</summary>
    public string? Message { get; private set; }
    public string? SubMessage { get; private set; }
    public double MessageTime { get; private set; }

    private void Play(Sfx s)
    {
        if (!Silent) Sound?.Invoke(s);
    }

    private void Say(string text, string? sub = null, double seconds = 2.5)
    {
        Message = text;
        SubMessage = sub;
        MessageTime = seconds;
    }

    public void Update(double dt, Controls c, bool start)
    {
        _accumulator += Math.Min(dt, 0.25);
        while (_accumulator >= Field.Step)
        {
            _accumulator -= Field.Step;
            Tick(c, start);
        }
    }

    /// <summary>One 1/60 s step.</summary>
    public void Tick(Controls c, bool start = false)
    {
        double dt = Field.Step;
        StateTime += dt;
        bool startPressed = start && !_startPrev;
        _startPrev = start;
        foreach (var e in Effects) e.Age += dt;
        Effects.RemoveAll(e => e.Done);
        if (MessageTime > 0) { MessageTime -= dt; if (MessageTime <= 0) Message = SubMessage = null; }

        switch (State)
        {
            case GameState.Title:
                if (_welcomePending) { _welcomePending = false; Play(Sfx.Welcome); }
                if (_tunePending && StateTime > WelcomeSeconds) { _tunePending = false; Play(Sfx.TitleTune); }
                _idle += dt;
                if (startPressed || c.Fire && StateTime > 0.5) StartGame();
                else if (_idle > TitleIdleSeconds) StartAttract();
                break;
            case GameState.Attract:
                Simulate(dt, Pilot.Decide(this));
                if (startPressed) { StartGame(); break; }
                if (State == GameState.Attract && StateTime > AttractSeconds) ShowHighScores(-1);
                break;
            case GameState.Playing:
                Simulate(dt, c);
                break;
            case GameState.GameOver:
                if (StateTime > 4)
                {
                    if (HighScores.Qualifies(Score))
                    {
                        State = GameState.EnterInitials;
                        StateTime = 0;
                        Initials = new InitialsEntry(HighScores.LastInitials);
                    }
                    else ShowHighScores(-1);
                }
                break;
            case GameState.EnterInitials:
                if (Initials is { IsDone: true } done)
                {
                    int place = HighScores.Insert(done.Initials, Score, CardsDone);
                    HighScores.Save();
                    ShowHighScores(place);
                }
                break;
            case GameState.HighScores:
                if (startPressed) StartGame();
                else if (StateTime > HighScoreSeconds) ReturnToTitle();
                break;
        }
    }

    /// <summary>A new mission: a fresh deal of the hiding places, from the top of the first shaft.</summary>
    public void StartGame()
    {
        NewGame(_seed ?? Environment.TickCount);
        State = GameState.Playing;
        StateTime = 0;
        NewEntryIndex = -1;
        Play(Sfx.Welcome);
    }

    /// <summary>The demonstration: the computer agent searches the complex.</summary>
    private void StartAttract()
    {
        NewGame(_seed ?? 7);
        State = GameState.Attract;
        StateTime = 0;
        Pilot.Reset();
    }

    public void ReturnToTitle()
    {
        State = GameState.Title;
        StateTime = 0;
        _idle = 0;
        _welcomePending = _tunePending = true;
        Effects.Clear();
        Message = SubMessage = null;
        NewGame(_seed ?? 1);
    }

    private void ShowHighScores(int place)
    {
        State = GameState.HighScores;
        StateTime = 0;
        NewEntryIndex = place;
    }

    public void Pause()
    {
        if (State == GameState.Playing) State = GameState.Paused;
    }

    public void Resume()
    {
        if (State == GameState.Paused) State = GameState.Playing;
    }

    private void GameOver()
    {
        if (State == GameState.Attract)
        {
            ShowHighScores(-1);
            return;
        }
        State = GameState.GameOver;
        StateTime = 0;
    }

    private void SetPhase(Phase p)
    {
        Phase = p;
        PhaseTime = 0;
    }
}
