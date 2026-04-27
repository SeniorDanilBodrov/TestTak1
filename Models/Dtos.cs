namespace PriceWatcher.Models;

public sealed record SubscribeRequest(string ListingUrl, string Email);

public sealed record SubscribeResponse(Guid Id);

public sealed record SubscriptionPriceDto(
    Guid Id,
    string ListingUrl,
    long? CurrentPriceRub,
    long? LastKnownPriceRub,
    DateTimeOffset? LastCheckedAt,
    DateTimeOffset CreatedAt
);

public sealed record SubscriptionPricePrettyDto(
    Guid Id,
    string ListingUrl,
    long? CurrentPriceRub,
    string CurrentPriceText,
    long? LastKnownPriceRub,
    string LastKnownPriceText,
    DateTimeOffset? LastCheckedAt,
    string LastCheckedAtText,
    DateTimeOffset CreatedAt,
    string CreatedAtText
);

public sealed record ApartmentPriceDto(
    string ListingUrl,
    long? CurrentPriceRub,
    string CurrentPriceText
);

