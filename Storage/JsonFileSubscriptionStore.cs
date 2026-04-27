using System.Text.Json;
using PriceWatcher.Models;

namespace PriceWatcher.Storage;

public sealed class JsonFileSubscriptionStore : ISubscriptionStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _filePath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public JsonFileSubscriptionStore(IWebHostEnvironment env)
    {
        var dir = Path.Combine(env.ContentRootPath, "App_Data");
        Directory.CreateDirectory(dir);
        _filePath = Path.Combine(dir, "subscriptions.json");
    }

    public async Task<IReadOnlyList<Subscription>> GetAllAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            return (await ReadAllUnsafeAsync(ct)).OrderBy(x => x.CreatedAt).ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<Subscription> AddAsync(string listingUrl, string email, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var all = await ReadAllUnsafeAsync(ct);

            var sub = new Subscription
            {
                Id = Guid.NewGuid(),
                ListingUrl = listingUrl,
                Email = email,
                CreatedAt = DateTimeOffset.UtcNow
            };

            all.Add(sub);
            await WriteAllUnsafeAsync(all, ct);
            return sub;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task UpdateAsync(Subscription subscription, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var all = await ReadAllUnsafeAsync(ct);
            var idx = all.FindIndex(x => x.Id == subscription.Id);
            if (idx < 0)
                return;

            all[idx] = subscription;
            await WriteAllUnsafeAsync(all, ct);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<List<Subscription>> ReadAllUnsafeAsync(CancellationToken ct)
    {
        if (!File.Exists(_filePath))
            return [];

        await using var fs = new FileStream(_filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            var data = await JsonSerializer.DeserializeAsync<List<Subscription>>(fs, JsonOptions, ct);
            return data ?? [];
        }
        catch
        {
            return [];
        }
    }

    private async Task WriteAllUnsafeAsync(List<Subscription> all, CancellationToken ct)
    {
        var temp = _filePath + ".tmp";
        await using (var fs = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await JsonSerializer.SerializeAsync(fs, all, JsonOptions, ct);
        }

        File.Copy(temp, _filePath, overwrite: true);
        File.Delete(temp);
    }
}

