# Order Processing Service

A simplified e-commerce order processing API built for the Backend Developer Technical Assessment. ASP.NET Core Web API on .NET 8, with Order, Inventory, and Payment controllers, EF Core
on the In-Memory provider, and genuine HTTP-based communication between them.

## Running it

```bash
dotnet restore
dotnet run --project src/OrderProcessing.Api
```

The API listens on `http://localhost:5173` and launches straight into Swagger UI at
`http://localhost:5173/swagger` in Development. Inventory is seeded automatically on startup (see
[Seed data](#seed-data) below) — there's nothing else to set up before you can exercise every
endpoint.

Run the test suite with:

```bash
dotnet test
```

## Architecture

### One project, three controllers, real HTTP between them

The assessment's wording is ambiguous between "one service with three controllers" and "three
separate microservices." This is built as a single ASP.NET Core project — one process, one
`dotnet run` — but `OrderService` doesn't call `InventoryService`/`PaymentService` directly as
in-process methods. Instead, `OrdersController` depends on `IInventoryClient`/`IPaymentClient`,
which are typed `HttpClient`s (registered via `IHttpClientFactory`) that make real loopback HTTP
calls back into the same running process's Inventory and Payment endpoints.

This was a deliberate choice, not an accident: the assessment explicitly lists "HTTP Client for
inter-service communication" as a mandatory technology and "service unavailability" as an error
scenario to handle. Neither of those means anything if Order just calls Inventory's C# method
directly — there's no HTTP client involved, and a "service" that's just a method call can't be
unavailable. Routing through real HTTP gives both requirements genuine teeth: `IInventoryClient`/
`IPaymentClient` really do issue `HttpRequestException`s (mapped to `Unavailable`, HTTP 503) when
something goes wrong, and Task 8's integration tests really do exercise the full network round trip,
JSON included — which is exactly what caught a real serialization bug during development (see
[Trade-offs](#trade-offs-and-known-limitations) below).

The cost of this choice is that the app calls itself: `ServiceEndpoints:BaseUrl` in
`appsettings.json` has to match whatever address Kestrel is actually listening on
(`launchSettings.json`'s `applicationUrl`). Change one, change the other.

### Order creation flow

`OrderService.CreateAsync` implements the assessment's flow directly:

1. Validate the request (`[ApiController]` model binding handles field-level rules; a
   cross-field check rejects duplicate `productId`s within one order).
2. Reserve inventory for each line item, one HTTP call per item. If any item can't be reserved,
   everything already reserved for this order is released before returning an error — a
   partially-reservable order never holds stock hostage.
3. Persist the order as `Pending` once inventory is secured, so a payment failure below still has
   a real, retrievable order to attach the outcome to.
4. Process payment over HTTP.
5. Confirm the order (`Confirmed`) on a successful payment, or release inventory and cancel it
   (`Cancelled`) on a decline.

A cancelled-due-to-declined-payment order is still a **successful** API response (`201 Created`,
`OperationOutcome.Success`) — the request was carried all the way through the flow correctly; the
caller reads the order's `status` field to see which branch it took. The same reasoning applies one
level down: `PaymentService.ProcessAsync` treats a *declined* payment as `Success` too (a
`PaymentTransaction` with `Status: "failed"`, correctly recorded), reserving `ValidationFailed` for
genuinely malformed input (missing order id, non-positive amount). Only a request that couldn't be
carried out at all — bad input, a product that doesn't exist, a downstream service that couldn't be
reached — is a non-2xx error.

### Concurrency

`InventoryItem.RowVersion` is an EF Core concurrency token. Two requests reserving the same product
at the same time both read the same starting quantities, but only the first `SaveChangesAsync`
succeeds — the second gets a `DbUpdateConcurrencyException`, which `InventoryService` catches and
retries (up to 3 attempts) against a freshly re-read row rather than failing outright. This is what
makes "concurrent order processing" actually safe under load instead of just not crashing under a
single request. `InventoryServiceTests.ReserveAsync_UnderConcurrentRequests_NeverOversellsStock`
fires 20 simultaneous reservations against 10 units of stock and asserts exactly 10 succeed and
final stock is exact.

### Error handling

Every non-2xx response — a business rule violation, a model-validation failure, or a genuinely
unhandled exception — comes back as the same RFC 7807 `ProblemDetails` shape, tagged with a
correlation id:

- Services return an `OperationResult<T>` (`Success` / `NotFound` / `ValidationFailed` / `Conflict`
  / `Unavailable`) for expected, non-exceptional outcomes; controllers map each outcome to the
  matching HTTP status via a shared `ApiControllerBase.ProblemResult` helper.
- `[ApiController]`'s automatic model-validation failures are already `ValidationProblemDetails`;
  a small customization in `Program.cs` just adds the same correlation id to them.
- `ExceptionHandlingMiddleware` catches anything else and returns a generic 500, logging the real
  exception server-side without leaking internals to the caller.

### Logging and correlation IDs

`CorrelationIdMiddleware` reads (or generates) an `X-Correlation-Id` per request, echoes it back in
the response header, and pushes it into an `ILogger` scope for the lifetime of the request. Every
log line for that request — across Order, Inventory, and Payment, even across the internal HTTP
hop — carries the same id, and every error response includes it, so a support/debugging conversation
can go straight from "here's the correlation id from the response" to "here are all the log lines
for that request."

### Configuration and caching

The simulated payment gateway's failure rate is configuration-driven
(`Payment:FailureRate` in `appsettings.json`, overridden to a more visible `0.25` in
`appsettings.Development.json`) rather than a hardcoded constant, bound via the Options pattern
(`Configuration/PaymentOptions.cs`). Inventory reads (`GET /api/inventory/{productId}`) go through
an `IMemoryCache` cache-aside layer with a short TTL as a safety net, actively invalidated on every
successful reserve/release — a read immediately after a write is never stale.

## API

### Order Controller

| Method | Route | Description |
|---|---|---|
| POST | `/api/orders` | Create an order — runs the full reserve → pay → confirm/cancel flow |
| GET | `/api/orders/{id}` | Retrieve order details |
| GET | `/api/orders?page=&pageSize=` | List orders, paginated (`pageSize` clamped to 1–100) |
| PUT | `/api/orders/{id}/status` | Update order status, subject to the state machine below |

Order status is a simple state machine: `Pending → Confirmed | Cancelled`, `Confirmed → Shipped |
Cancelled`. `Cancelled` and `Shipped` are terminal. Setting a status equal to the current one is a
no-op; any other transition is a `409 Conflict`.

### Inventory Controller

| Method | Route | Description |
|---|---|---|
| GET | `/api/inventory/{productId}` | Check availability |
| POST | `/api/inventory/{productId}/reserve` | Reserve `{ "quantity": n }` units |
| POST | `/api/inventory/{productId}/release` | Release `{ "quantity": n }` previously-reserved units |

### Payment Controller

| Method | Route | Description |
|---|---|---|
| POST | `/api/payments/process` | Process `{ "orderId", "amount" }` — simulated gateway |
| GET | `/api/payments/{transactionId}` | Look up a previously processed transaction |

Full request/response schemas are in Swagger (`/swagger`) once the app is running — enums are
documented there as the actual camelCase strings they serialize as (`"pending"`, not `0`), via a
custom `EnumSchemaFilter`.

### Seed data

`DbSeeder` loads five products on startup so the API is exercisable immediately:

| ProductId | Available | Notes |
|---|---|---|
| SKU-001 | 50 | |
| SKU-002 | 25 | |
| SKU-003 | 100 | |
| SKU-004 | 5 | Deliberately low, for triggering insufficient-stock |
| SKU-005 | 0 | Deliberately zero, for triggering insufficient-stock immediately |

## Error scenarios

The assessment calls out five specific scenarios. Here's where each is handled:

| Scenario | Handling |
|---|---|
| Insufficient inventory | `InventoryService.ReserveAsync` returns `Conflict` when requested quantity exceeds `AvailableQuantity`; surfaces as `409` with a message naming the product, requested, and available quantities. A multi-item order rolls back everything already reserved before returning. |
| Payment processing failures | `RandomPaymentGatewaySimulator` declines at a configurable rate. A decline is a successful `PaymentTransaction` (`Status: "failed"`); `OrderService` reacts by releasing inventory and cancelling the order — the order-creation request itself still returns `201`. |
| Service unavailability | `IInventoryClient`/`IPaymentClient` catch `HttpRequestException`/timeout and return `Unavailable` (`503`), distinct from a business-rule `Conflict`. `OrderService` releases any already-reserved inventory before returning. |
| Invalid input data | DataAnnotations on every request DTO (`[Required]`, `[Range]`, `[MinLength]`) plus a cross-field duplicate-`productId` check in `OrderService`. Model-validation failures return `400` with field-level detail via `ValidationProblemDetails`. |
| Concurrent order processing | EF Core optimistic concurrency (`RowVersion`) with a bounded retry loop in `InventoryService`, covered by a dedicated concurrency test firing 20 simultaneous reservations at 10 units of stock. |

## Testing strategy

`dotnet test` runs 33 tests across two layers:

- **Unit tests** (`InventoryServiceTests`, `PaymentServiceTests`, `OrderServiceTests`) mock
  `IInventoryClient`/`IPaymentClient`/`IPaymentGatewaySimulator` with Moq and exercise each service
  in isolation against a real EF Core InMemory `DbContext`. Coverage includes every happy path, the
  assessment's named edge cases (insufficient inventory, payment decline, invalid input, concurrent
  reservation), rollback behavior on partial failures, and the state-machine/pagination rules.
- **Integration tests** (`OrdersEndToEndTests`) drive the full create-order flow through real HTTP
  requests via `WebApplicationFactory<Program>` — confirmed-and-committed on approval,
  cancelled-and-released on decline, and a `409` with the real `ProblemDetails` error text on
  insufficient stock. Because Order's HTTP clients make genuine loopback calls (see
  [Architecture](#one-project-three-controllers-real-http-between-them) above), and the default
  in-memory `TestServer` doesn't listen on a real socket for those internal calls to land on,
  `IInventoryClient`/`IPaymentClient`'s primary handler is re-pointed at that same `TestServer`'s own
  handler for the duration of the test — Order's internal calls reach
  `InventoryController`/`PaymentsController` in-process, no sockets involved. The payment gateway is
  swapped for a deterministic fake per test so approval/decline is a controlled precondition rather
  than a chance of flaking against the real random simulator.

These integration tests earned their complexity during development: they're what caught a real bug
where a genuinely successful payment response failed to deserialize correctly over the wire (see
below) — something the mocked unit tests structurally cannot catch, since they never serialize
anything to begin with.

## Manual verification

Beyond the automated suite, every endpoint and each of the assessment's five named error scenarios
was also exercised live against a running instance through Swagger UI (`/swagger`), confirming the
same behavior holds outside the test harness:

| Endpoint | Scenario | Result |
|---|---|---|
| `POST /api/orders` | Full happy path (reserve → charge → confirm) | `201`; inventory correctly moved from available to reserved |
| `POST /api/orders` | Invalid input (empty `customerId`, zero quantity, negative price) | `400` `ValidationProblemDetails` listing all three field errors at once |
| `POST /api/orders` | Order referencing a nonexistent product | `400`, "Product '...' does not exist" |
| `GET /api/inventory/{productId}` | Nonexistent product | `404`, "Product '...' was not found in inventory" |
| `PUT /api/orders/{id}/status` | Valid transition (Confirmed → Shipped) | `200`, order updated |
| `PUT /api/orders/{id}/status` | Invalid transition (Shipped → Confirmed; Shipped is terminal) | `409`, "Cannot change order status from 'Shipped' to 'Confirmed'" |
| `POST /api/inventory/{productId}/reserve` | Valid reservation | `200`, available/reserved quantities updated |
| `POST /api/inventory/{productId}/reserve` | Insufficient stock | `409`, "Insufficient stock for '...': requested N, only M available" |
| `POST /api/inventory/{productId}/release` | Valid release | `200`, quantities updated |
| `POST /api/inventory/{productId}/release` | Releasing more than currently reserved | `409`, "Cannot release N units... only M are currently reserved" |
| `GET /api/orders` | List with pagination | `200`; `page`/`pageSize`/`totalCount`/`totalPages` all correct, including with `pageSize=1` |
| `GET /api/orders/{id}` | Valid id, then a nonexistent id | `200` full order / `404` |
| `POST /api/payments/process` | Direct payment call (both approved and declined outcomes observed) | `200` in both cases — a decline is still a successfully processed request |
| `GET /api/payments/{transactionId}` | Valid id, then a nonexistent id | `200` / `404` |

Every response also carried its own `x-correlation-id`, confirming `CorrelationIdMiddleware` threads
through every code path — not just the ones the automated tests happen to exercise.

## Bonus features implemented

- **Swagger/OpenAPI** — full interactive docs at `/swagger`, enums documented as their real
  camelCase wire format.
- **Configuration management & environment-specific settings** — the payment failure rate lives in
  `appsettings.json`/`appsettings.Development.json`, not a hardcoded constant.
- **Structured logging with correlation IDs** — see [Logging](#logging-and-correlation-ids) above.
- **Caching** — `IMemoryCache` cache-aside on inventory reads, actively invalidated on writes.

**Deliberately not implemented:** JWT authentication and metrics/monitoring endpoints. This was a
scoping decision made up front (see the commit history) to keep the bonus surface realistic rather
than spreading thin — the assessment's own evaluation criteria weight business logic, edge cases,
and testing strategy (40%) alongside code quality (60%), and those three took priority.

## Trade-offs and known limitations

- **The self-referencing `BaseUrl`.** Because Order talks to Inventory/Payment over real HTTP
  within the same process, `appsettings.json`'s `ServiceEndpoints:BaseUrl` has to match wherever
  Kestrel actually ends up listening. This is a direct, accepted cost of making "HTTP Client for
  inter-service communication" and "service unavailability" real rather than nominal; a true
  microservices split would remove this coupling at the cost of needing three deployable processes,
  which felt like the wrong trade for an assessment scoped as one service.
- **A real bug this design caught.** `HttpResponseMapper` (used by both internal HTTP clients to
  parse responses) originally relied on `System.Net.Http.Json`'s implicit default
  `JsonSerializerOptions` — convenient, but silently missing the enum-as-string converter the API's
  own controllers use. Every real, successful payment response would have thrown a `JsonException`
  parsing its `status` field, meaning the assessment's core happy path never actually worked
  end-to-end despite every mocked unit test passing. Only `dotnet test` running the real integration
  tests surfaced it (twice — the first fix traded that bug for a second one, explicit
  `JsonSerializerOptions` silently dropping the case-insensitive property matching the implicit
  default had been providing). Both are fixed; see the commit history for the full story. It's a
  good illustration of why the integration tests were worth the extra complexity.
- **EF Core InMemory is not persistent.** State resets on every restart, by design — it's the
  assessment's mandated persistence technology, not a production choice.
- **No authentication.** Every endpoint is open, consistent with the bonus-scope decision above.
- **Build/test verification.** This was developed in an environment without registry access to
  restore NuGet packages, so verification leaned on careful manual review plus the user (Corne)
  running `dotnet build`/`dotnet test` locally after each task — which is exactly what caught the
  bug described above.

## Pre-submission checklist

- [x] Service starts successfully — `dotnet run --project src/OrderProcessing.Api`, seeded and
      ready immediately.
- [x] End-to-end order flow works correctly — verified via `OrdersEndToEndTests` and manually
      through Swagger.
- [x] Error scenarios are handled gracefully — see [Error scenarios](#error-scenarios) above, all
      covered by tests.
