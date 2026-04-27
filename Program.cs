var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<PriceWatcher.Worker.PriceWatchOptions>(builder.Configuration.GetSection("PriceWatch"));
builder.Services.Configure<PriceWatcher.Email.SmtpOptions>(builder.Configuration.GetSection("Smtp"));

builder.Services.AddSingleton<PriceWatcher.Storage.ISubscriptionStore, PriceWatcher.Storage.SqliteSubscriptionStore>();
builder.Services.AddHttpClient<PriceWatcher.Price.IListingPriceClient, PriceWatcher.Price.PrinzipHtmlPriceClient>()
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
    {
        AutomaticDecompression =
            System.Net.DecompressionMethods.GZip |
            System.Net.DecompressionMethods.Deflate |
            System.Net.DecompressionMethods.Brotli,
        ConnectCallback = async (context, cancellationToken) =>
        {
            var addresses = await System.Net.Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, cancellationToken);
            var ip = addresses.FirstOrDefault(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                     ?? addresses.First();

            var socket = new System.Net.Sockets.Socket(ip.AddressFamily, System.Net.Sockets.SocketType.Stream, System.Net.Sockets.ProtocolType.Tcp);
            await socket.ConnectAsync(ip, context.DnsEndPoint.Port, cancellationToken);
            return new System.Net.Sockets.NetworkStream(socket, ownsSocket: true);
        }
    });
builder.Services.AddHttpClient<PriceWatcher.Price.IPrinzipCatalogClient, PriceWatcher.Price.PrinzipCatalogClient>()
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
    {
        AutomaticDecompression =
            System.Net.DecompressionMethods.GZip |
            System.Net.DecompressionMethods.Deflate |
            System.Net.DecompressionMethods.Brotli,
        ConnectCallback = async (context, cancellationToken) =>
        {
            var addresses = await System.Net.Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, cancellationToken);
            var ip = addresses.FirstOrDefault(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                     ?? addresses.First();

            var socket = new System.Net.Sockets.Socket(ip.AddressFamily, System.Net.Sockets.SocketType.Stream, System.Net.Sockets.ProtocolType.Tcp);
            await socket.ConnectAsync(ip, context.DnsEndPoint.Port, cancellationToken);
            return new System.Net.Sockets.NetworkStream(socket, ownsSocket: true);
        }
    });
builder.Services.AddSingleton<PriceWatcher.Email.IEmailSender, PriceWatcher.Email.SmtpEmailSender>();
builder.Services.AddHostedService<PriceWatcher.Worker.PriceWatchBackgroundService>();

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { ok = true }));

app.MapPost("/api/subscriptions", async (
    PriceWatcher.Models.SubscribeRequest req,
    PriceWatcher.Storage.ISubscriptionStore store,
    CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(req.ListingUrl) || !Uri.TryCreate(req.ListingUrl, UriKind.Absolute, out var uri))
        return Results.BadRequest(new { error = "Invalid ListingUrl" });

    if (uri.Host is null || !uri.Host.Contains("prinzip", StringComparison.OrdinalIgnoreCase))
        return Results.BadRequest(new { error = "ListingUrl must point to prinzip.su" });

    if (string.IsNullOrWhiteSpace(req.Email) || !req.Email.Contains('@'))
        return Results.BadRequest(new { error = "Invalid Email" });

    var sub = await store.AddAsync(req.ListingUrl.Trim(), req.Email.Trim(), ct);
    return Results.Ok(new PriceWatcher.Models.SubscribeResponse(sub.Id));
});

app.MapGet("/api/subscriptions/prices", async (
    PriceWatcher.Storage.ISubscriptionStore store,
    PriceWatcher.Price.IListingPriceClient prices,
    CancellationToken ct) =>
{
    var subs = await store.GetAllAsync(ct);

    // "Актуальные цены" = свежий запрос прямо сейчас (без ожидания фонового цикла).
    var result = new List<PriceWatcher.Models.SubscriptionPriceDto>(subs.Count);

    foreach (var s in subs)
    {
        var current = await prices.TryGetPriceRubAsync(s.ListingUrl, ct);
        result.Add(new PriceWatcher.Models.SubscriptionPriceDto(
            s.Id,
            s.ListingUrl,
            current,
            s.LastKnownPriceRub,
            s.LastCheckedAt,
            s.CreatedAt
        ));
    }

    return Results.Ok(result);
});

app.MapGet("/api/subscriptions/prices/pretty", async (
    PriceWatcher.Storage.ISubscriptionStore store,
    PriceWatcher.Price.IListingPriceClient prices,
    CancellationToken ct) =>
{
    var subs = await store.GetAllAsync(ct);
    var ru = new System.Globalization.CultureInfo("ru-RU");

    var result = new List<PriceWatcher.Models.SubscriptionPricePrettyDto>(subs.Count);
    foreach (var s in subs)
    {
        var current = await prices.TryGetPriceRubAsync(s.ListingUrl, ct);
        var currentText = current is null ? "Не удалось получить цену" : $"{current.Value.ToString("N0", ru)} ₽";
        var knownText = s.LastKnownPriceRub is null ? "Нет данных" : $"{s.LastKnownPriceRub.Value.ToString("N0", ru)} ₽";
        var checkedText = s.LastCheckedAt is null ? "Еще не проверялась" : s.LastCheckedAt.Value.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss");

        result.Add(new PriceWatcher.Models.SubscriptionPricePrettyDto(
            s.Id,
            s.ListingUrl,
            current,
            currentText,
            s.LastKnownPriceRub,
            knownText,
            s.LastCheckedAt,
            checkedText,
            s.CreatedAt,
            s.CreatedAt.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss")
        ));
    }

    return Results.Ok(result);
});

app.MapGet("/api/prinzip/apartments/prices", async (
    PriceWatcher.Price.IPrinzipCatalogClient catalog,
    PriceWatcher.Price.IListingPriceClient prices,
    int? limit,
    CancellationToken ct) =>
{
    var take = Math.Clamp(limit ?? 100, 1, 1000);
    var urls = await catalog.GetApartmentUrlsAsync(take, ct);
    var ru = new System.Globalization.CultureInfo("ru-RU");

    var semaphore = new SemaphoreSlim(8, 8);
    var tasks = urls.Select(async url =>
    {
        await semaphore.WaitAsync(ct);
        try
        {
            var price = await prices.TryGetPriceRubAsync(url, ct);
            var text = price is null ? "Не удалось получить цену" : $"{price.Value.ToString("N0", ru)} ₽";
            return new PriceWatcher.Models.ApartmentPriceDto(url, price, text);
        }
        finally
        {
            semaphore.Release();
        }
    });

    var items = await Task.WhenAll(tasks);
    var ordered = items
        .OrderByDescending(x => x.CurrentPriceRub.HasValue)
        .ThenBy(x => x.CurrentPriceRub ?? long.MaxValue)
        .ToArray();

    return Results.Ok(new
    {
        totalRequested = take,
        totalFound = urls.Count,
        totalWithPrice = ordered.Count(x => x.CurrentPriceRub is not null),
        apartments = ordered
    });
});

app.Run();
