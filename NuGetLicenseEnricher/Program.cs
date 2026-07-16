using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using NuGetLicenseEnricher.Models;
using NuGetLicenseEnricher.Services;

return await RunAsync(args).ConfigureAwait(false);

static async Task<int> RunAsync(string[] args)
{
    if (!TryParseArguments(args, out string? inputPath, out string? outputPath, out string? argumentError))
    {
        Console.Error.WriteLine(argumentError);
        PrintUsage();
        return 2;
    }

    using var cancellation = new CancellationTokenSource();
    ConsoleCancelEventHandler cancelHandler = (_, eventArgs) =>
    {
        eventArgs.Cancel = true;
        cancellation.Cancel();
    };
    Console.CancelKeyPress += cancelHandler;

    try
    {
        JsonNode? root;
        try
        {
            await using FileStream input = new(
                inputPath!,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 81920,
                useAsync: true);
            root = await JsonNode.ParseAsync(
                input,
                new JsonNodeOptions { PropertyNameCaseInsensitive = false },
                new JsonDocumentOptions
                {
                    AllowTrailingCommas = false,
                    CommentHandling = JsonCommentHandling.Disallow
                },
                cancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            Console.Error.WriteLine("Cancelled.");
            return 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            Console.Error.WriteLine($"Could not read input '{inputPath}': {ex.Message}");
            return 2;
        }

        if (!CycloneDxValidator.TryValidate(root, out JsonObject? bom, out string? validationError))
        {
            Console.Error.WriteLine($"Input is not valid CycloneDX 1.6 JSON: {validationError}");
            return 3;
        }

        using var httpClient = new HttpClient(new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        })
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("NuGetLicenseEnricher/1.0");

        using var licenseService = new NuGetLicenseService(
            httpClient,
            new NuGetPackageInspector(),
            maximumConcurrency: 5);
        var enrichmentService = new BomEnrichmentService(licenseService);

        EnrichmentSummary summary;
        try
        {
            summary = await enrichmentService.EnrichAsync(
                bom!,
                Console.Out,
                cancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            Console.Error.WriteLine("Cancelled.");
            return 0;
        }

        try
        {
            var serializerOptions = new JsonSerializerOptions
            {
                WriteIndented = true
            };
            string outputJson = bom!.ToJsonString(serializerOptions);
            await File.WriteAllTextAsync(outputPath!, outputJson + Environment.NewLine, cancellation.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            Console.Error.WriteLine("Cancelled.");
            return 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            Console.Error.WriteLine($"Could not write output '{outputPath}': {ex.Message}");
            return 4;
        }

        Console.WriteLine();
        Console.WriteLine($"Total NuGet components: {summary.TotalNuGetComponents}");
        Console.WriteLine($"Already licensed: {summary.AlreadyLicensed}");
        Console.WriteLine($"Successfully enriched: {summary.SuccessfullyEnriched}");
        Console.WriteLine($"Licence not found: {summary.LicenseNotFound}");
        Console.WriteLine($"Errors: {summary.Errors}");
        Console.WriteLine($"Output: {outputPath}");
        return 0;
    }
    finally
    {
        Console.CancelKeyPress -= cancelHandler;
    }
}

static bool TryParseArguments(
    string[] args,
    out string? inputPath,
    out string? outputPath,
    out string? error)
{
    inputPath = null;
    outputPath = null;
    error = null;

    for (int index = 0; index < args.Length; index++)
    {
        string argument = args[index];
        if (argument is not ("--input" or "--output"))
        {
            error = $"Unknown argument: {argument}";
            return false;
        }

        if (++index >= args.Length || string.IsNullOrWhiteSpace(args[index]))
        {
            error = $"Missing value for {argument}.";
            return false;
        }

        if (argument == "--input")
        {
            inputPath = args[index];
        }
        else
        {
            outputPath = args[index];
        }
    }

    if (inputPath is null || outputPath is null)
    {
        error = "Both --input and --output are required.";
        return false;
    }

    return true;
}

static void PrintUsage() =>
    Console.Error.WriteLine("Usage: dotnet run -- --input bom.json --output bom.enriched.json");
