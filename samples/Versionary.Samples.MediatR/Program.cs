using MediatR;
using Microsoft.AspNetCore.OpenApi;
using Versionary;
using Versionary.DependencyInjection;
using Versionary.Graph;
using Versionary.MediatR;
using Versionary.Samples.MediatR;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<ICurrencyLookup, InMemoryCurrencyLookup>();
builder.Services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<GetOrderHandler>());
builder.Services.AddOpenApi(options =>
{
    // Every generation declares a type called Order, and another called GetOrder. The default
    // scheme names a schema after the bare type, so all three versions collapse into one component
    // and the UI shows v1 and v2 the current shape — precisely the confusion this API exists to
    // avoid. Qualifying by declaring type keeps them apart.
    options.CreateSchemaReferenceId = type => type.Type.DeclaringType is { } declaring
        ? $"{declaring.Name}.{type.Type.Name}"
        : OpenApiOptions.CreateDefaultSchemaReferenceId(type);
});

// ── The whole integration ────────────────────────────────────────────────────
//
// Scanning finds every IMigrator<,> in the assembly. The graph is built and validated here, at
// startup, so a cycle or a duplicated hop fails the application now rather than on the first
// request from a v1 client.
builder.Services
    .AddVersionary(cfg => cfg.RegisterFromAssemblyContaining<V1OrderMigrator>())
    .AddMediatRPipeline(options =>
    {
        // SinglePass (the default) walks every hop at once and dispatches the terminal contract
        // once. Switch to Reentrant if you have MediatR behaviours registered against specific
        // older contracts — a validator written for V2.GetOrder, say — and you need them to run.
        options.Strategy = builder.Configuration.GetValue("Versionary:Strategy", MigrationStrategy.SinglePass);
    });

var app = builder.Build();

// Swagger UI at the root, served against the document the built-in OpenAPI support generates.
// Each endpoint is tagged with its version, so the UI groups the three generations separately and
// the schema list shows how the response shape changed between them.
app.MapOpenApi();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/openapi/v1.json", "Orders API — all versions");
    options.RoutePrefix = string.Empty;
    options.DocumentTitle = "Versionary — Orders API sample";
});

// Versions are chosen by the transport, not by Versionary. Separate routes keep this sample
// readable; a `Api-Version` header or Asp.Versioning would work identically, because all the
// transport has to do is decide which contract to bind and send.
//
// TypedResults rather than Results, so that OpenAPI picks up the response contract of each version
// and you can see V1.Order, V2.Order and Current.Order side by side in the schema list.

app.MapGet("/v1/orders/{id:int}", async (int id, ISender sender) =>
        TypedResults.Ok(await sender.Send(new V1.GetOrder(id))))
    .WithTags("Orders (v1)")
    .WithSummary("Get an order, oldest contract")
    .WithDescription(
        "Migrated two hops to Current.GetOrder before it reaches the handler, and the response is "
        + "migrated two hops back. No v1 handler exists.");

app.MapGet("/v2/orders/{id:int}", async (int id, ISender sender, bool includeTax = false) =>
        TypedResults.Ok(await sender.Send(new V2.GetOrder(id, includeTax))))
    .WithTags("Orders (v2)")
    .WithSummary("Get an order, with tax broken out")
    .WithDescription(
        "One hop each way. The currency the current contract needs is looked up during the "
        + "migration, because a v2 client had no way to send one.");

app.MapGet("/orders/{id:int}", async (int id, ISender sender, bool includeTax = false, string currency = "USD") =>
        TypedResults.Ok(await sender.Send(new Current.GetOrder(id, includeTax, currency))))
    .WithTags("Orders (current)")
    .WithSummary("Get an order, current contract")
    .WithDescription("Terminal: nothing to migrate, so it goes straight to the handler.");

// Cancelling is the pinned case: V1.CancelOrder has no migrator, so it is terminal and reaches its
// own handler untouched. Nothing about the wiring above changes to allow that.
app.MapPost("/v1/orders/{id:int}/cancel", async (int id, ISender sender) =>
        TypedResults.Ok(await sender.Send(new V1.CancelOrder(id))))
    .WithTags("Orders (v1)")
    .WithSummary("Cancel an order — a PINNED version")
    .WithDescription(
        "Not migrated. v1 promised the refund had already happened and the current contract only "
        + "queues one, which is a behaviour change rather than a reshaping. Leaving V1.CancelOrder "
        + "without a migrator makes it terminal, so it reaches its own handler untouched.");

app.MapPost("/orders/{id:int}/cancel", async (int id, string reason, ISender sender) =>
        TypedResults.Ok(await sender.Send(new Current.CancelOrder(id, reason))))
    .WithTags("Orders (current)")
    .WithSummary("Cancel an order, current contract")
    .WithDescription("Queues a refund and returns a reference to follow it up.");

// The generated version map. Handy in a sample; in production this is more useful asserted in a
// test than exposed on an endpoint.
app.MapGet("/migrations", (IMigrationGraph graph) => Results.Text(graph.Explain()))
    .WithTags("Diagnostics")
    .WithSummary("The version map, generated from the graph")
    .WithDescription("Graph.Explain(). Requests run left to right; responses run the other way.");

// When a handler throws, the stack trace will not mention that the request arrived as a v1 contract
// and was reshaped twice on the way in. IMigrationContext is what closes that gap.
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
