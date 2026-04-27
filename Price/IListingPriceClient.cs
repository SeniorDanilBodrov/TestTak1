namespace PriceWatcher.Price;

public interface IListingPriceClient
{
    Task<long?> TryGetPriceRubAsync(string listingUrl, CancellationToken ct);
}

