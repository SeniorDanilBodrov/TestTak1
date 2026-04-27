namespace PriceWatcher.Price;

public interface IPrinzipCatalogClient
{
    Task<IReadOnlyList<string>> GetApartmentUrlsAsync(int maxCount, CancellationToken ct);
}

