namespace PriceWatcher.Worker;

public sealed class PriceWatchOptions
{
    public int PollIntervalSeconds { get; init; } = 600;
}

