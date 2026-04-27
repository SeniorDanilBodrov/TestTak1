using Microsoft.Data.Sqlite;
using PriceWatcher.Models;

namespace PriceWatcher.Storage;

public sealed class SqliteSubscriptionStore : ISubscriptionStore
{
    private readonly string _connectionString;
    private readonly SemaphoreSlim _initGate = new(1, 1);
    private bool _initialized;

    public SqliteSubscriptionStore(IWebHostEnvironment env)
    {
        var dir = Path.Combine(env.ContentRootPath, "App_Data");
        Directory.CreateDirectory(dir);
        var dbPath = Path.Combine(dir, "pricewatcher.db");
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        }.ToString();
    }

    public async Task<IReadOnlyList<Subscription>> GetAllAsync(CancellationToken ct)
    {
        await EnsureInitializedAsync(ct);

        var result = new List<Subscription>();
        await using var con = new SqliteConnection(_connectionString);
        await con.OpenAsync(ct);

        var cmd = con.CreateCommand();
        cmd.CommandText =
            """
            SELECT id, listing_url, email, last_known_price_rub, last_checked_at, created_at
            FROM subscriptions
            ORDER BY created_at ASC;
            """;

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var id = Guid.Parse(reader.GetString(0));
            var listingUrl = reader.GetString(1);
            var email = reader.GetString(2);

            long? lastKnownPrice = reader.IsDBNull(3) ? null : reader.GetInt64(3);
            DateTimeOffset? lastCheckedAt = reader.IsDBNull(4) ? null : DateTimeOffset.Parse(reader.GetString(4));
            var createdAt = DateTimeOffset.Parse(reader.GetString(5));

            result.Add(new Subscription
            {
                Id = id,
                ListingUrl = listingUrl,
                Email = email,
                LastKnownPriceRub = lastKnownPrice,
                LastCheckedAt = lastCheckedAt,
                CreatedAt = createdAt
            });
        }

        return result;
    }

    public async Task<Subscription> AddAsync(string listingUrl, string email, CancellationToken ct)
    {
        await EnsureInitializedAsync(ct);

        var sub = new Subscription
        {
            Id = Guid.NewGuid(),
            ListingUrl = listingUrl,
            Email = email,
            CreatedAt = DateTimeOffset.UtcNow
        };

        await using var con = new SqliteConnection(_connectionString);
        await con.OpenAsync(ct);

        var cmd = con.CreateCommand();
        cmd.CommandText =
            """
            INSERT INTO subscriptions(
              id, listing_url, email, last_known_price_rub, last_checked_at, created_at
            ) VALUES (
              $id, $listing_url, $email, $last_known_price_rub, $last_checked_at, $created_at
            );
            """;
        cmd.Parameters.AddWithValue("$id", sub.Id.ToString("D"));
        cmd.Parameters.AddWithValue("$listing_url", sub.ListingUrl);
        cmd.Parameters.AddWithValue("$email", sub.Email);
        cmd.Parameters.AddWithValue("$last_known_price_rub", DBNull.Value);
        cmd.Parameters.AddWithValue("$last_checked_at", DBNull.Value);
        cmd.Parameters.AddWithValue("$created_at", sub.CreatedAt.ToString("O"));

        await cmd.ExecuteNonQueryAsync(ct);
        return sub;
    }

    public async Task UpdateAsync(Subscription subscription, CancellationToken ct)
    {
        await EnsureInitializedAsync(ct);

        await using var con = new SqliteConnection(_connectionString);
        await con.OpenAsync(ct);

        var cmd = con.CreateCommand();
        cmd.CommandText =
            """
            UPDATE subscriptions
            SET
              listing_url = $listing_url,
              email = $email,
              last_known_price_rub = $last_known_price_rub,
              last_checked_at = $last_checked_at
            WHERE id = $id;
            """;

        cmd.Parameters.AddWithValue("$id", subscription.Id.ToString("D"));
        cmd.Parameters.AddWithValue("$listing_url", subscription.ListingUrl);
        cmd.Parameters.AddWithValue("$email", subscription.Email);
        cmd.Parameters.AddWithValue("$last_known_price_rub", subscription.LastKnownPriceRub is null ? DBNull.Value : subscription.LastKnownPriceRub.Value);
        cmd.Parameters.AddWithValue("$last_checked_at", subscription.LastCheckedAt is null ? DBNull.Value : subscription.LastCheckedAt.Value.ToString("O"));

        await cmd.ExecuteNonQueryAsync(ct);
    }

    private async Task EnsureInitializedAsync(CancellationToken ct)
    {
        if (_initialized)
            return;

        await _initGate.WaitAsync(ct);
        try
        {
            if (_initialized)
                return;

            await using var con = new SqliteConnection(_connectionString);
            await con.OpenAsync(ct);

            var cmd = con.CreateCommand();
            cmd.CommandText =
                """
                CREATE TABLE IF NOT EXISTS subscriptions(
                  id TEXT NOT NULL PRIMARY KEY,
                  listing_url TEXT NOT NULL,
                  email TEXT NOT NULL,
                  last_known_price_rub INTEGER NULL,
                  last_checked_at TEXT NULL,
                  created_at TEXT NOT NULL
                );

                CREATE INDEX IF NOT EXISTS idx_subscriptions_created_at ON subscriptions(created_at);
                """;

            await cmd.ExecuteNonQueryAsync(ct);
            _initialized = true;
        }
        finally
        {
            _initGate.Release();
        }
    }
}

