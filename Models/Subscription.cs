namespace PriceWatcher.Models;

public sealed class Subscription
{
    public required Guid Id { get; init; }
    public required string ListingUrl { get; init; }
    public required string Email { get; init; }

    public long? LastKnownPriceRub { get; set; }
    public DateTimeOffset? LastCheckedAt { get; set; }
    public DateTimeOffset CreatedAt { get; init; }
}

