namespace CodexMeter;

/// <summary>One immediate step, then delayed repeating steps until release or boundary.</summary>
sealed class RepeatScrollController : IDisposable
{
    readonly System.Windows.Forms.Timer timer = new();
    readonly Func<int, bool> move;
    int direction;
    public RepeatScrollController(Func<int, bool> move)
    {
        this.move = move;
        timer.Tick += (_, _) => { if (!move(direction)) Stop(); else timer.Interval = 90; };
    }
    public void Start(int direction)
    {
        Stop();
        this.direction = direction;
        if (!move(direction)) return;
        timer.Interval = 320;
        timer.Start();
    }
    public void Stop() => timer.Stop();
    public void Dispose() => timer.Dispose();
}
