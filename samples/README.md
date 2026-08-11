# Samples

Three runnable samples. Start with the default one, which is the shortest complete example. The
tour explains the engine, and the MediatR one shows the same thing wired through a mediator.

| | |
| --- | --- |
| [`Versionary.Samples.Default`](Versionary.Samples.Default) | **Start here.** The default path: `IVersionarySender` and one handler, no mediator at all. Two files. |
| [`Versionary.Samples.Tour`](Versionary.Samples.Tour) | Console. The engine explained, printing what it does at every step. |
| [`Versionary.Samples.MediatR`](Versionary.Samples.MediatR) | The same idea wired through MediatR, for teams that already have it. |

They use two worked examples, both three generations deep: a quotes API in the minimal sample, and
an orders API in the other two, where v1 knew nothing about tax and v2 knew nothing about currency.

Whichever you read, the shape is the same. Requests run left to right until they reach the contract
the one handler accepts, and results run back the other way.

```
V1.Request ──► V2.Request ──► Request ──► [ the only handler ]
                                                  │
V1.Result ◄──── V2.Result ◄──── Result ◄──────────┘
```

## The default sample

```bash
dotnet run --project samples/Versionary.Samples.Default
```

Then open **<http://localhost:5090>** for Swagger UI.

The shortest complete example there is. `Program.cs` holds the registration and three endpoints,
`Quotes.cs` holds the contracts, the one handler and the two migrators. Nothing else, and no mediator
anywhere.

```bash
# v1: two hops out, two hops back. There is no v1 handler.
curl localhost:5090/v1/quotes/MSFT
# {"symbol":"MSFT","price":130.0}

# v2: one hop each way. The currency the current contract needs is looked up
# during the migration, because a v2 caller had no way to send one.
curl "localhost:5090/v2/quotes/VOD.L?includeChange=true"
# {"symbol":"VOD.L","price":137.5,"change":2.06}

# current: nothing to migrate, straight to the handler.
curl "localhost:5090/quotes/AAPL?includeChange=true&currency=EUR"
# {"symbol":"AAPL","price":130.0,"change":1.95,"currency":"EUR"}
```

The thing to look at is how little each endpoint does:

```csharp
app.MapGet("/v1/quotes/{symbol}", async (string symbol, IVersionarySender sender, CancellationToken ct) =>
    TypedResults.Ok(await sender.SendAsync(new Quotes.V1.GetQuote(symbol), ct)));
```

One call, no migration in sight, and no response type either. `Quotes.V1.GetQuote` declares it returns
a `Quotes.V1.Quote`, so the sender works it out. The endpoint names only v1 types. Add a fourth version
tomorrow and that line does not move.

## The tour

```bash
dotnet run --project samples/Versionary.Samples.Tour
```

Eight sections, each one runnable code rather than prose:

1. **A request two versions behind reaches one handler.** The whole pattern, with the applied hops printed at the end.
2. **Three ways to register the same hops.** Assembly scanning, explicit `AddMigrator<T>()`, and inline delegates, all producing the same graph.
3. **`Explain()`.** The version map, generated rather than drawn.
4. **Validation.** What a cycle looks like when it fails your startup, and how to assert the same thing in a unit test.
5. **Response fan-out.** One current contract migrating down to several older shapes, with the requested target picking the branch — and the same shape on a request path, where it is an error.
6. **Every option.** `TrackAppliedMigrations`, `MigratorLifetime`, `HandlerLifetime`, `TreatValidationWarningsAsErrors` and `ValidateOnBuild`, each shown by running with it both ways.
7. **Upcasting.** The same forward walk applied to a stored event, with no handler and no response.

## The MediatR sample

```bash
dotnet run --project samples/Versionary.Samples.MediatR
```

Then open **<http://localhost:5080>** for Swagger UI. Endpoints are grouped by version, and the
schema list is worth a look on its own:

```
Current.CancelResult   orderId, status, refundReference
Current.Order          orderId, subtotal, tax, total, currency
V1.CancelResult        orderId, status
V1.Order               orderId, total
V2.Order               orderId, total, tax
```

Five distinct response contracts, one handler behind them. Each endpoint's description says what
migration it goes through, so you can send a request and read what happened to it.

Or from the command line:

```bash
# v1: two hops out, two hops back. Never touches a v1 handler.
curl localhost:5080/v1/orders/7
# {"orderId":7,"total":107}

# v2: one hop each way. The currency the current contract needs gets looked up,
# because a v2 client had no way to send one.
curl "localhost:5080/v2/orders/8?includeTax=true"
# {"orderId":8,"total":129.6,"tax":21.6}

# current: nothing to migrate, straight to the handler.
curl "localhost:5080/orders/9?includeTax=true&currency=GBP"
# {"orderId":9,"subtotal":109,"tax":21.8,"total":130.8,"currency":"GBP"}

# the generated version map
curl localhost:5080/migrations
# Migration graph: 4 hops across 6 contracts
#   Current.Order -> V2.Order -> V1.Order
#   V1.GetOrder -> V2.GetOrder -> Current.GetOrder
```

Worth reading for:

- **`Program.cs`.** The entire integration is `AddVersionary(...).AddMediatRPipeline(...)`. Versions are selected by the route, because choosing a version is the transport's job. A header or `Asp.Versioning` would drop in unchanged.
- **A pinned version.** `V1.CancelOrder` has no migrator, which makes it terminal, so it reaches its own handler untouched. It earns that because the behaviour genuinely changed. v1 said the money was already refunded, and no reshaping of the current response makes that true. Nothing about the wiring changes to allow it.

  ```bash
  curl -X POST localhost:5080/v1/orders/5/cancel
  # {"orderId":5,"status":"Refunded"}            <- the pinned v1 handler

  curl -X POST "localhost:5080/orders/5/cancel?reason=duplicate"
  # {"orderId":5,"status":"RefundQueued","refundReference":"..."}
  ```

- **An asynchronous migrator that does real work.** `V2OrderMigrator` injects `ICurrencyLookup` to fill in a field v2 clients could not have sent, which is the case that justifies migrations being asynchronous at all.
- **Diagnostics.** The middleware at the bottom of `Program.cs` logs `IMigrationContext` when a handler throws, so a failure tells you the request arrived as a v1 contract and was reshaped twice on the way in.
- **Switching strategy.** `Versionary:Strategy` in `appsettings.json` flips between `SinglePass` and `Reentrant`.
