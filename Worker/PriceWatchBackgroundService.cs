using Microsoft.Extensions.Options;
using PriceWatcher.Email;
using PriceWatcher.Price;
using PriceWatcher.Storage;

namespace PriceWatcher.Worker;

public sealed class PriceWatchBackgroundService : BackgroundService
{
    private readonly ISubscriptionStore _store;
    private readonly IListingPriceClient _prices;
    private readonly IEmailSender _email;
    private readonly ILogger<PriceWatchBackgroundService> _logger;
    private readonly PriceWatchOptions _opt;

    public PriceWatchBackgroundService(
        ISubscriptionStore store,
        IListingPriceClient prices,
        IEmailSender email,
        IOptions<PriceWatchOptions> opt,
        ILogger<PriceWatchBackgroundService> logger)
    {
        _store = store;
        _prices = prices;
        _email = email;
        _logger = logger;
        _opt = opt.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var delay = TimeSpan.FromSeconds(Math.Max(10, _opt.PollIntervalSeconds));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await TickAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Background tick failed");
            }

            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task TickAsync(CancellationToken ct)
    {
        var subs = await _store.GetAllAsync(ct);
        if (subs.Count == 0)
            return;

        foreach (var sub in subs)
        {
            ct.ThrowIfCancellationRequested();

            var price = await _prices.TryGetPriceRubAsync(sub.ListingUrl, ct);
            sub.LastCheckedAt = DateTimeOffset.UtcNow;

            if (price is null)
            {
                await _store.UpdateAsync(sub, ct);
                continue;
            }

            var prev = sub.LastKnownPriceRub;
            // Compatibility: if previous version stored data-price with ".00" as x100.
            if (prev is not null && prev.Value % 100 == 0 && prev.Value / 100 == price.Value)
            {
                prev = price.Value;
            }

            if (prev is not null && prev.Value != price.Value)
            {
                var subject = "Изменилась цена квартиры на prinzip.su";
                var body =
                    $"Ссылка: {sub.ListingUrl}{Environment.NewLine}" +
                    $"Было: {prev.Value} ₽{Environment.NewLine}" +
                    $"Стало: {price.Value} ₽{Environment.NewLine}" +
                    $"Время проверки (UTC): {sub.LastCheckedAt:O}{Environment.NewLine}";

                await _email.SendAsync(sub.Email, subject, body, ct);
            }

            sub.LastKnownPriceRub = price.Value;
            await _store.UpdateAsync(sub, ct);
        }
    }
}

