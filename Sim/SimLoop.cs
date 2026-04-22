namespace CowColonySim.Sim;

public sealed class SimLoop
{
    private const int MaxStepsPerFrame = 8;

    private readonly Action<int> _step;
    private double _accumulator;

    public int Tick { get; private set; }
    public SimSpeed Speed { get; set; } = SimSpeed.X1;
    public bool IsPaused { get; set; }
    public int LastStepsExecuted { get; private set; }
    public double Alpha { get; private set; }

    public SimLoop(Action<int> step)
    {
        _step = step ?? throw new ArgumentNullException(nameof(step));
    }

    public void Advance(double realDeltaSeconds)
    {
        if (realDeltaSeconds <= 0 || IsPaused) { LastStepsExecuted = 0; return; }

        _accumulator += realDeltaSeconds * (int)Speed;

        var steps = 0;
        while (_accumulator >= SimConstants.SimDt && steps < MaxStepsPerFrame)
        {
            _step(Tick);
            Tick++;
            _accumulator -= SimConstants.SimDt;
            steps++;
        }

        if (_accumulator > SimConstants.SimDt * MaxStepsPerFrame)
        {
            _accumulator = 0;
        }

        LastStepsExecuted = steps;
        Alpha = Speed == SimSpeed.X1 ? _accumulator / SimConstants.SimDt : 1.0;
    }

    public void Reset()
    {
        Tick = 0;
        _accumulator = 0;
        LastStepsExecuted = 0;
        Alpha = 0;
    }
}
