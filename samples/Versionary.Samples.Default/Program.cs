using Microsoft.AspNetCore.OpenApi;
using Versionary;
using Versionary.Execution;
using Versionary.Graph;
using Versionary.Samples.Default;

// ─────────────────────────────────────────────────────────────────────────────
//  The whole sample is this one file. Three versions of a quotes API, one
//  handler, no mediator.
// ─────────────────────────────────────────────────────────────────────────────

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<IPriceFeed, FakePriceFeed>();

// Finds the handler and both migrators in one pass, then builds and validates the
// graph. A cycle, a duplicate hop, or a handler stranded on an old contract fails
// the application here rather than on the first old request.
builder.Services.AddVersionary(cfg => cfg.RegisterFromAssemblyContaining<GetQuoteHandler>());

builder.Services.AddOpenApi(options =>
    // Every version declares a type called Quote, so the default naming would collapse
    // all three into one schema and show v1 callers the current shape.
    options.CreateSchemaReferenceId = type => type.Type.DeclaringType is { } declaring
        ? $"{declaring.Name}.{type.Type.Name}"
        : OpenApiOptions.CreateDefaultSchemaReferenceId(type));

var app = builder.Build();

app.MapOpenApi();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/openapi/v1.json", "Quotes API, all versions");
    options.RoutePrefix = string.Empty;
    options.DocumentTitle = "Versionary, minimal API sample";
});

// ─────────────────────────────────────────────────────────────────────────────
//  The endpoints.
//
//  Every one is a single call. No migration mentioned, and no response type
//  either: the contract declares what it returns, so asking a v1 request for a v2
//  response would not compile. Each endpoint names only its own version's types,
//  which is what makes them immune to a new version landing later.
// ─────────────────────────────────────────────────────────────────────────────

app.MapGet("/v1/quotes/{symbol}", async (string symbol, IVersionarySender sender, CancellationToken ct) =>
        TypedResults.Ok(await sender.SendAsync(new Quotes.V1.GetQuote(symbol), ct)))
    .WithTags("Quotes (v1)")
    .WithSummary("Get a quote, oldest contract")
    .WithDescription("Migrated two hops to the current contract on the way in, and two hops back on "
        + "the way out. There is no v1 handler.");

app.MapGet("/v2/quotes/{symbol}", async (
        string symbol, IVersionarySender sender, CancellationToken ct, bool includeChange = false) =>
            TypedResults.Ok(await sender.SendAsync(new Quotes.V2.GetQuote(symbol, includeChange), ct)))
    .WithTags("Quotes (v2)")
    .WithSummary("Get a quote, with the daily change")
    .WithDescription("One hop each way. The currency the current contract needs is looked up during "
        + "the migration, because a v2 caller had no way to send one.");

app.MapGet("/quotes/{symbol}", async (
        string symbol, IVersionarySender sender, CancellationToken ct,
        bool includeChange = false, string currency = "USD") =>
            TypedResults.Ok(await sender.SendAsync(
                new Quotes.GetQuote(symbol, includeChange, currency), ct)))
    .WithTags("Quotes (current)")
    .WithSummary("Get a quote, current contract")
    .WithDescription("Already current, so it goes straight to the handler with nothing to migrate.");

app.MapGet("/migrations", (IMigrationGraph graph) => Results.Text(graph.Explain()))
    .WithTags("Diagnostics")
    .WithSummary("The version map, generated from the graph");

// When a handler throws, nothing in the stack trace says the request arrived as a v1
// contract and was reshaped twice on the way in. This does.
app.Use(async (context, next) =>
{
    try
    {
        await next(context);
    }
    catch (Exception ex)
    {
        var migrations = context.RequestServices.GetRequiredService<IMigrationContext>();
        app.Logger.LogError(
            ex,
            "Request {Path} failed. Migrations applied:\n{Migrations}",
            context.Request.Path,
            string.Join(Environment.NewLine, migrations.Applied));
        throw;
    }
});

app.Run();
