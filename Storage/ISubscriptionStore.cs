using PriceWatcher.Models;

namespace PriceWatcher.Storage;

public interface ISubscriptionStore
{
    Task<IReadOnlyList<Subscription>> GetAllAsync(CancellationToken ct);
    Task<Subscription> AddAsync(string listingUrl, string email, CancellationToken ct);
    Task UpdateAsync(Subscription subscription, CancellationToken ct);
}

