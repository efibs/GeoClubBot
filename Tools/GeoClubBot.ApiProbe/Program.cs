using System.Text;
using System.Text.Json;
using GeoClubBot.ApiProbe;

// GeoClubBot.ApiProbe - a read-only console tool for inspecting what the GeoGuessr API actually
// returns. See README.md for why it exists and how to point it at your account.

var arguments = ProbeArguments.Parse(args);
if (arguments is null)
{
    ProbeArguments.PrintUsage();
    return 1;
}

var settings = ProbeSettings.Resolve();
if (settings is null)
{
    return 1;
}

var clubId = arguments.ClubId ?? settings.ClubId;

using var client = new GeoGuessrProbeClient(settings.NcfaToken);
using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cancellation.Cancel();
};

var output = new StringBuilder();

try
{
    switch (arguments.Command)
    {
        case "activities":
            await ProbeActivitiesAsync().ConfigureAwait(false);
            break;

        case "missions":
            await ProbeSingleAsync("/v4/missions", itemsProperty: "missions").ConfigureAwait(false);
            break;

        case "members":
            await ProbeSingleAsync($"/v4/clubs/{RequireClub()}/members", itemsProperty: null).ConfigureAwait(false);
            break;

        case "club":
            await ProbeSingleAsync($"/v4/clubs/{RequireClub()}", itemsProperty: "members").ConfigureAwait(false);
            break;

        case "user":
            if (arguments.Target is null)
            {
                Console.Error.WriteLine("The 'user' command needs a user id: user <userId>");
                return 1;
            }

            await ProbeSingleAsync($"/v3/users/{arguments.Target}", itemsProperty: null).ConfigureAwait(false);
            break;

        case "raw":
            if (arguments.Target is null)
            {
                Console.Error.WriteLine("The 'raw' command needs a path: raw /v4/some/endpoint");
                return 1;
            }

            await ProbeSingleAsync(arguments.Target, itemsProperty: null).ConfigureAwait(false);
            break;

        default:
            Console.Error.WriteLine($"Unknown command '{arguments.Command}'.");
            ProbeArguments.PrintUsage();
            return 1;
    }
}
catch (ProbeRequestException ex)
{
    Console.Error.WriteLine(ex.Message);
    if (ex.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
    {
        Console.Error.WriteLine(
            "The _ncfa token is missing, expired or not authorised for this club. "
            + "Grab a fresh one from a logged-in browser session (README.md).");
    }

    return 1;
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine("Cancelled.");
    return 130;
}

if (arguments.OutputPath is not null)
{
    await File.WriteAllTextAsync(arguments.OutputPath, output.ToString(), cancellation.Token).ConfigureAwait(false);
    Console.WriteLine($"\nWritten to {arguments.OutputPath}");
}

return 0;

Guid RequireClub()
{
    if (clubId is null)
    {
        throw new InvalidOperationException(
            "No club id. Pass --club <guid>, set GEOGUESSR_CLUB_ID, or put GeoGuessr:ClubId in appsettings.Local.json.");
    }

    return clubId.Value;
}

// The activity feed is the endpoint the bot's daily-mission logic depends on, so it gets the
// pagination loop and the cross-tab against xpReward.
async Task ProbeActivitiesAsync()
{
    var collected = new List<JsonElement>();
    var documents = new List<JsonDocument>();
    string? paginationToken = null;

    try
    {
        for (var page = 1; page <= arguments.Pages; page++)
        {
            var query = $"limit={arguments.Limit}";
            if (paginationToken is not null)
            {
                query += $"&paginationToken={Uri.EscapeDataString(paginationToken)}";
            }

            var document = await client
                .GetAsync($"/v4/clubs/{RequireClub()}/activities?{query}", cancellation.Token)
                .ConfigureAwait(false);
            documents.Add(document);

            var items = ExtractItems(document.RootElement, "items");
            Console.WriteLine($"  page {page}: {items.Count} item(s)");
            Emit($"=== page {page} ===");
            Emit(Pretty(document.RootElement));

            collected.AddRange(items);

            paginationToken = ReadPaginationToken(document.RootElement);
            if (paginationToken is null || items.Count == 0)
            {
                break;
            }
        }

        if (!arguments.NoCensus)
        {
            Emit(string.Empty);
            Emit(FieldCensus.Build(collected, crossTabAgainst: "xpReward"));
        }
    }
    finally
    {
        foreach (var document in documents)
        {
            document.Dispose();
        }
    }
}

async Task ProbeSingleAsync(string path, string? itemsProperty)
{
    using var document = await client.GetAsync(path, cancellation.Token).ConfigureAwait(false);

    Emit(Pretty(document.RootElement));

    if (arguments.NoCensus)
    {
        return;
    }

    var items = ExtractItems(document.RootElement, itemsProperty);
    if (items.Count > 0)
    {
        Emit(string.Empty);
        Emit(FieldCensus.Build(items));
    }
}

// Finds the array of records inside a response: the root when it is already an array, the named
// property when there is one, otherwise the first array-valued property.
List<JsonElement> ExtractItems(JsonElement root, string? itemsProperty)
{
    if (root.ValueKind == JsonValueKind.Array)
    {
        return root.EnumerateArray().ToList();
    }

    if (root.ValueKind != JsonValueKind.Object)
    {
        return [];
    }

    if (itemsProperty is not null
        && TryGetPropertyIgnoreCase(root, itemsProperty, out var named)
        && named.ValueKind == JsonValueKind.Array)
    {
        return named.EnumerateArray().ToList();
    }

    foreach (var property in root.EnumerateObject())
    {
        if (property.Value.ValueKind == JsonValueKind.Array)
        {
            return property.Value.EnumerateArray().ToList();
        }
    }

    return [];
}

string? ReadPaginationToken(JsonElement root) =>
    root.ValueKind == JsonValueKind.Object
    && TryGetPropertyIgnoreCase(root, "paginationToken", out var token)
    && token.ValueKind == JsonValueKind.String
        ? token.GetString()
        : null;

bool TryGetPropertyIgnoreCase(JsonElement element, string name, out JsonElement value)
{
    foreach (var property in element.EnumerateObject())
    {
        if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
        {
            value = property.Value;
            return true;
        }
    }

    value = default;
    return false;
}

string Pretty(JsonElement element) =>
    JsonSerializer.Serialize(element, new JsonSerializerOptions { WriteIndented = true });

void Emit(string text)
{
    var safe = TokenRedactor.Redact(text);
    Console.WriteLine(safe);
    output.AppendLine(safe);
}
