# ASP.NET Core Internals: Notes and Interview Q&A

**Audience:** .NET developers preparing for senior-level interviews\
**Topics:** Request pipeline and middleware, dependency injection, configuration and Options, filters and exception handling, `IHttpClientFactory` and health checks, Minimal APIs and controllers\
**Suggested study time:** about 5 hours for a first pass, then hands-on practice

---

## How to Use These Notes

For each topic:

1. Read **Remember This** first. It gives you the analogy and the one-line answer.
2. Read the detailed notes.
3. Optional: run the **Try it** exercise in a small ASP.NET Core app.
4. Cover the answers and say the interview questions out loud.
5. At the end, practise the scenario questions.

Answers in the Q&A sections are kept short on purpose, so you can say them in an interview. The notes above them hold the detail for follow-up questions.

Some APIs vary by .NET version, so check your target framework before copying an example.

## Contents

- [Glossary](#glossary)
- [1. Request Pipeline and Middleware](#1-request-pipeline-and-middleware)
- [2. Dependency Injection Lifetimes](#2-dependency-injection-lifetimes)
- [3. Configuration and the Options Pattern](#3-configuration-and-the-options-pattern)
- [4. Filters and Global Exception Handling](#4-filters-and-global-exception-handling)
- [5. IHttpClientFactory and Health Checks](#5-ihttpclientfactory-and-health-checks)
- [6. Minimal APIs vs. Controllers](#6-minimal-apis-vs-controllers)
- [7. Senior-Level Scenario Questions](#7-senior-level-scenario-questions)
- [8. Cheat Sheet](#8-cheat-sheet)
- [9. 60-Second Interview Answer](#9-60-second-interview-answer)
- [Practice Checklist](#practice-checklist)

---

## Glossary

| Term | Simple meaning |
|---|---|
| Kestrel | ASP.NET Core's built-in, cross-platform web server. |
| Host | Starts and manages the app, configuration, logging, services and server. |
| Request pipeline | The ordered chain of middleware that processes a request and response. |
| Middleware | A component that can inspect or change a request/response, call the next component, or stop the pipeline. |
| `next` | The delegate that runs the next middleware. |
| Short-circuit | Returning a response without calling the next middleware. |
| Endpoint | The handler chosen for a request, such as a controller action or Minimal API handler. |
| Routing | Matching the request to an endpoint using route information. |
| `HttpContext` | The current HTTP request and response, plus user and request-specific data. |
| Dependency injection (DI) | Giving a class the dependencies it needs, instead of it creating them. |
| DI container | The framework component that creates registered services and manages their lifetimes. |
| Lifetime | How long a service instance lives: transient, scoped or singleton. |
| Scope | A boundary for scoped services. In a web app, one request creates one scope. |
| Captive dependency | A long-lived service holds a shorter-lived dependency beyond its intended lifetime. |
| Configuration | Key/value settings loaded from JSON, environment variables, command line and other sources. |
| Options pattern | Binding a configuration section to a typed C# class and injecting it. |
| Filter | Code that runs at a stage of the MVC or endpoint execution pipeline. |
| `ProblemDetails` | A standard structure for API error responses. |
| Typed HTTP client | A purpose-specific class that receives an `HttpClient` and wraps calls to one downstream service. |
| Health check | A check that reports whether the app or one of its dependencies is healthy. |
| Liveness / readiness | Liveness: should the process keep running? Readiness: should it receive traffic? |
| Minimal API | An endpoint style that maps handlers directly, with no controller classes. |
| Controller | A class that groups MVC actions and uses MVC features such as filters and model binding. |

---

# 1. Request Pipeline and Middleware

## Remember This

**Analogy:** airport security. You pass passport control, then the bag scan, then the gate, and your bags come back through the same checks on the way out. If passport control rejects you, you never reach the bag scan.

**One-line answer:** the pipeline is a chain of middleware that runs in the order registered on the way in and in reverse on the way out, and any middleware can stop the chain.

## 1.1 What happens when a request arrives?

1. A client sends an HTTP request.
2. Kestrel receives it (possibly behind a reverse proxy or load balancer).
3. The request passes through middleware in the order registered.
4. Routing selects an endpoint.
5. Authentication, authorization and other middleware or endpoint logic run as configured.
6. The endpoint executes.
7. The response unwinds through the middleware in reverse order.

Middleware can run code **before** and **after** calling the next middleware. If a component does not call `next`, later middleware and the endpoint do not run.

## 1.2 `Use`, `Run` and `Map`

- `app.Use(...)`: can do work and call the next middleware.
- `app.Run(...)`: terminal. It does not call the next middleware.
- `app.Map(...)`: creates a branch based on a path prefix.
- `app.MapWhen(...)`: creates a branch when a predicate is true.
- `app.UseWhen(...)`: creates a conditional branch that can rejoin the main pipeline.

```csharp
app.Use(async (context, next) =>
{
    var startedAt = Stopwatch.GetTimestamp();

    await next(context);

    var elapsed = Stopwatch.GetElapsedTime(startedAt);
    Console.WriteLine(
        $"{context.Request.Method} {context.Request.Path} took {elapsed.TotalMilliseconds} ms");
});

app.Map("/admin", adminApp =>
{
    adminApp.Run(async context =>
    {
        await context.Response.WriteAsync("Admin branch");
    });
});
```

The timing middleware runs before and after the rest of the pipeline. The `/admin` branch ends the request in this example.

## 1.3 Custom middleware

```csharp
public sealed class RequestIdMiddleware
{
    private readonly RequestDelegate _next;

    public RequestIdMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var requestId = Guid.NewGuid().ToString("N");
        context.Response.Headers["X-Request-Id"] = requestId;

        await _next(context);
    }
}
```

Register it:

```csharp
app.UseMiddleware<RequestIdMiddleware>();
```

In a real app, consider reusing an existing trace or correlation ID instead of generating an unrelated one. Don't expose sensitive information in response headers.

### Scoped dependencies in middleware

Conventional middleware (added with `UseMiddleware<T>()`) is created once for the application. Don't inject a scoped service into its constructor. Inject it into `InvokeAsync`:

```csharp
public sealed class AuditMiddleware
{
    private readonly RequestDelegate _next;

    public AuditMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(
        HttpContext context,
        IAuditService auditService)
    {
        await auditService.RecordRequestAsync(
            context.Request.Path,
            context.RequestAborted);

        await _next(context);
    }
}
```

The framework resolves method-injected scoped services from the current request scope.

**Qualification:** middleware that implements `IMiddleware` is created through DI, so it can use constructor injection when registered with a suitable lifetime, such as scoped.

## 1.4 Middleware ordering

No single order fits every app. A common arrangement:

```csharp
app.UseExceptionHandler();

app.UseHttpsRedirection();

app.UseRouting();

app.UseCors("FrontendPolicy");

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapGet("/health", () => Results.Ok("Healthy"));
```

Static files, response compression, rate limiting, antiforgery and output caching may also be needed, and their placement matters.

Principles to remember:

- Exception handling goes early so it can catch failures from everything registered after it.
- Authentication runs before authorization.
- CORS must be placed so it can handle the endpoint and preflight requests.
- When you call `UseRouting` explicitly, middleware that needs endpoint information goes after it.
- Follow the documented ordering for your target .NET version.

## 1.5 Common mistakes

- Forgetting to call `next`, which stops the pipeline without anyone noticing.
- Calling `next` more than once.
- Writing to the response after it has started.
- Registering authentication after authorization.
- Putting exception handling too late.
- Injecting scoped services into a conventional middleware constructor.
- Logging sensitive headers, tokens or personal data.
- Assuming every request reaches the endpoint. Middleware can short-circuit.

## 1.6 Try it (optional, 15 minutes)

1. Add the timing middleware and call any endpoint. Check the console output.
2. Move the `await next(context)` line out, or remove it. Watch the endpoint stop responding.
3. Swap `UseAuthentication` and `UseAuthorization` in a secured app and see what changes.

## 1.7 Interview questions

**Q1. What is middleware?**\
A component in the request pipeline. It can inspect or change the request, call the next component, and inspect or change the response on the way back.

**Q2. Why does middleware order matter?**\
Middleware runs in registration order on the way in and in reverse on the way out. For example, authentication must come before authorization, and exception handling must come early to cover what follows.

**Q3. What is the difference between `Use`, `Run` and `Map`?**\
`Use` can call the next middleware, `Run` is terminal, and `Map` branches by path prefix.

**Q4. What is short-circuiting?**\
A middleware returns a response without calling `next`. Static file middleware can do this when it serves a file.

**Q5. How do you use scoped services in middleware?**\
For conventional middleware, inject them as `InvokeAsync` parameters. With `IMiddleware`, constructor injection works if its lifetime is set up correctly.

**Q6. Middleware vs. MVC filters?**\
Middleware runs in the HTTP pipeline and knows nothing about MVC. Filters run inside the MVC action pipeline and can see action arguments and model state.

---

# 2. Dependency Injection Lifetimes

## Remember This

**Analogy:** a restaurant. A transient service is a fresh napkin each time. A scoped service is the bill for your table, shared during your visit only. A singleton is the one kitchen everyone shares.

**One-line answer:** transient is a new instance each time, scoped is one per request, singleton is one for the whole app, and a long-lived service must not hold on to a short-lived one.

## 2.1 The three lifetimes

| Lifetime | Meaning | Typical use |
|---|---|---|
| Transient | A new instance each time the service is resolved. | Small, stateless helpers. |
| Scoped | One instance within a DI scope. In a web app, one request. | `DbContext`, unit-of-work services. |
| Singleton | One instance for the lifetime of the application's service provider. | Thread-safe shared services and caches. |

```csharp
builder.Services.AddTransient<IFormatter, Formatter>();
builder.Services.AddScoped<IOrderService, OrderService>();
builder.Services.AddSingleton<IClock, SystemClock>();
```

A singleton is shared across requests, so it must be safe for concurrent use. A transient is not automatically thread-safe just because it is transient.

## 2.2 Captive dependencies

A captive dependency happens when a long-lived service holds a shorter-lived one beyond its intended lifetime.

```csharp
builder.Services.AddScoped<AppDbContext>();
builder.Services.AddSingleton<ReportService>();
```

If `ReportService` takes `AppDbContext` in its constructor, the singleton keeps that scoped context for the life of the app. This causes wrong lifetime behavior and concurrency bugs, because `DbContext` is not thread-safe.

The lesson is not simply "dependencies must live at least as long". A transient injected into a singleton is also kept by the singleton, so it effectively lives as long as the singleton does. Whether that is safe depends on that object's state, resources and thread safety.

Turn on scope validation in development and tests, so invalid scoped-from-singleton dependencies are caught early.

## 2.3 Using scoped services in a background service

A hosted service lives for the whole app. Create a scope for each unit of work instead of resolving scoped services from the root provider.

```csharp
public sealed class OrderWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OrderWorker> _logger;

    public OrderWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<OrderWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();

                var processor = scope.ServiceProvider
                    .GetRequiredService<IOrderProcessor>();

                await processor.ProcessNextBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException)
                when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Order processing failed");
            }

            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }
}
```

This creates and disposes a scope on each loop. Adapt the retry, delay and shutdown behavior to your workload, and don't swallow failures without logging or a deliberate recovery decision.

## 2.4 Disposal

- The container disposes the disposable services it creates when their scope or provider is disposed.
- Scoped services are normally disposed at the end of the request.
- Singletons are normally disposed when the app shuts down.
- If you register an already-created instance (`AddSingleton(instance)`), the container does not assume it owns it, so you handle disposal.
- Use `CreateAsyncScope` when scoped services may need asynchronous disposal.

## 2.5 Multiple registrations

```csharp
builder.Services.AddScoped<INotificationSender, EmailSender>();
builder.Services.AddScoped<INotificationSender, SmsSender>();
```

With the default container:

- Injecting one `INotificationSender` gives the **last** registration.
- Injecting `IEnumerable<INotificationSender>` gives **all** of them, in registration order.
- `TryAdd...` adds a service only if one is not already registered.
- Keyed services (.NET 8 and later) let you pick among named registrations.

## 2.6 Constructor injection vs. service locator

Prefer constructor injection:

```csharp
public sealed class OrderService(IOrderRepository repository)
{
    public Task<Order?> GetAsync(int id, CancellationToken ct) =>
        repository.GetAsync(id, ct);
}
```

The dependency is visible and easy to replace in tests. Resolving services from `IServiceProvider` all over your business code hides dependencies. That is the service locator pattern. `IServiceProvider` is fine in infrastructure code, such as creating scopes in hosted services.

## 2.7 Common mistakes

- Injecting a scoped service into a singleton.
- Sharing a `DbContext` across threads.
- Storing per-request data in a singleton or static field.
- Resolving services from `IServiceProvider` throughout business code.
- Assuming a transient is safe to share between threads.

## 2.8 Try it (optional, 15 minutes)

1. Register one transient, one scoped and one singleton service, each with a `Guid` created in its constructor.
2. Inject all three into an endpoint and print the Guids. Call it twice and compare which change.
3. Inject the scoped service into the singleton. Run in Development and read the startup error, then fix it with `IServiceScopeFactory`.

## 2.9 Interview questions

**Q1. What are the three DI lifetimes?**\
Transient creates a new instance each time, scoped shares one per scope (one request in web apps), and singleton shares one for the app's lifetime.

**Q2. What is a captive dependency?**\
A long-lived service holds a shorter-lived dependency beyond its intended lifetime, such as a singleton holding a scoped `DbContext`.

**Q3. How do you use a scoped service in a `BackgroundService`?**\
Inject `IServiceScopeFactory`, create a scope for each unit of work, resolve the service from it, and dispose the scope afterward.

**Q4. What lifetime should `DbContext` have?**\
Usually scoped, so each request gets its own context, which is disposed at the end. Never use one context from several threads at once.

**Q5. Who disposes DI-created services?**\
The container, when their scope or provider is disposed. Instances you create yourself and pass in have different ownership rules.

**Q6. What if an interface is registered twice?**\
A single injection gets the last registration. `IEnumerable<T>` gets all of them.

**Q7. Why prefer constructor injection?**\
Dependencies are explicit, readable and easy to test. Use the service provider directly only in infrastructure code, such as creating scopes.

---

# 3. Configuration and the Options Pattern

## Remember This

**Analogy:** `IOptions` is a printed menu from opening day. `IOptionsSnapshot` is a fresh menu handed to each customer. `IOptionsMonitor` is a live display board.

**One-line answer:** configuration comes from layered sources where later ones win, and the Options pattern turns a section into a typed, validated class, with three interfaces that differ in how they handle changes.

## 3.1 Configuration providers

The default setup commonly includes:

1. `appsettings.json`
2. `appsettings.{Environment}.json`
3. User secrets (Development)
4. Environment variables
5. Command-line arguments

Later providers generally override earlier ones for the same key. Apps can add, remove or reorder providers, so this is the default pattern, not a universal rule.

Nested environment-variable keys use double underscores:

```text
Smtp__Host=smtp.example.com
Smtp__Port=587
```

These map to `Smtp:Host` and `Smtp:Port`.

Don't commit production secrets. Use a secret store such as Azure Key Vault, managed identity where supported, or securely injected deployment secrets. User Secrets is for local development and is not an encrypted production store.

## 3.2 The Options pattern

Bind a section to a strongly typed class instead of scattering raw lookups.

```json
{
  "Smtp": {
    "Host": "smtp.example.com",
    "Port": 587
  }
}
```

```csharp
public sealed class SmtpOptions
{
    public string Host { get; set; } = "";
    public int Port { get; set; }
}
```

```csharp
builder.Services
    .AddOptions<SmtpOptions>()
    .Bind(builder.Configuration.GetSection("Smtp"))
    .Validate(options => !string.IsNullOrWhiteSpace(options.Host),
        "Smtp:Host is required.")
    .Validate(options => options.Port is > 0 and <= 65535,
        "Smtp:Port must be a valid TCP port.")
    .ValidateOnStart();
```

```csharp
public sealed class Mailer(IOptions<SmtpOptions> options)
{
    private readonly SmtpOptions _smtp = options.Value;

    public string GetServer() => _smtp.Host;
}
```

`ValidateOnStart()` runs validation at startup instead of waiting for the value to be requested later, so a bad setting stops the app early.

## 3.3 `IOptions<T>` vs. `IOptionsSnapshot<T>` vs. `IOptionsMonitor<T>`

| Type | Behavior | Use when |
|---|---|---|
| `IOptions<T>` | Can be injected anywhere. The value is cached and has no per-request refresh. | Settings that are effectively stable. |
| `IOptionsSnapshot<T>` | Scoped snapshot, commonly refreshed for each new request scope. | Request-scoped code that needs updated settings. |
| `IOptionsMonitor<T>` | Singleton-friendly. Gives current values and supports `OnChange`. | Singletons or hosted services that react to reloadable settings. |

These types don't make every source reloadable. Refresh depends on the configuration source and its reload support. JSON files, for example, reload when `reloadOnChange` is enabled.

`IOptionsSnapshot<T>` is scoped and must not be injected into a singleton. Use `IOptionsMonitor<T>` there.

## 3.4 Common mistakes

- Reading settings with string keys everywhere instead of a typed class.
- Using `IOptions<T>` and expecting live changes.
- Injecting `IOptionsSnapshot<T>` into a singleton.
- Skipping validation, so a bad value fails later in production.
- Committing secrets in `appsettings.json`.

## 3.5 Try it (optional, 15 minutes)

1. Bind a setting and read it through all three option types in one endpoint.
2. Edit `appsettings.json` while the app is running and call the endpoint again.
3. Note which of the three values changed and which did not.
4. Add `.ValidateOnStart()` with a rule, break the setting, and watch the app refuse to start.

## 3.6 Interview questions

**Q1. How does configuration precedence work?**\
In the default setup, later providers override earlier ones, so environment variables and command-line arguments usually override JSON.

**Q2. What is the Options pattern?**\
It binds configuration to typed classes that you inject, which makes settings easy to validate, test and maintain.

**Q3. What is the difference between the three Options interfaces?**\
`IOptions<T>` is a cached value, `IOptionsSnapshot<T>` is a scoped snapshot per request, and `IOptionsMonitor<T>` gives current values with change notifications, where the source supports reload.

**Q4. Which can be injected into a singleton?**\
`IOptions<T>` and `IOptionsMonitor<T>`. `IOptionsSnapshot<T>` is scoped, so it cannot go into a singleton directly.

**Q5. How do you validate settings at startup?**\
Use Options validation (`Validate`, `ValidateDataAnnotations`) with `ValidateOnStart()`.

**Q6. Where should secrets live?**\
User Secrets for local development, and a managed secret store or securely injected secrets in production. Never in files committed to source control.

---

# 4. Filters and Global Exception Handling

## Remember This

**Analogy:** a safety net under a trapeze. Whatever goes wrong above, the net catches it, and the audience sees a clean landing, not the accident.

**One-line answer:** put one central exception handler at the start of the pipeline, log the details on the server, and return a safe, consistent `ProblemDetails` response.

## 4.1 MVC filter types

MVC filters run at stages of the MVC action pipeline. The broad order:

1. Authorization filters
2. Resource filters
3. Action filters
4. Exception filters
5. Result filters

Execution includes before/after behavior and short-circuiting. Result filters wrap result execution, and exception filters handle eligible exceptions inside MVC execution.

| Filter | Typical purpose |
|---|---|
| Authorization filter | MVC-specific authorization decisions. Authorization middleware and attributes are often preferred. |
| Resource filter | Logic around much of the MVC pipeline, such as specialized caching. |
| Action filter | Logic before and after an action method, such as logging. |
| Exception filter | Handling eligible exceptions thrown during MVC action execution. |
| Result filter | Logic before and after the result is executed. |

MVC filters do not apply to Minimal API endpoints. Minimal APIs use **endpoint filters** (`AddEndpointFilter`).

## 4.2 Middleware vs. filters

- Middleware runs across the application and does not depend on MVC.
- MVC filters run inside MVC and can see action details, model state and the result.
- Endpoint filters are for Minimal API endpoint execution.

For application-wide exception handling, middleware is usually the better central place, because it also covers failures outside MVC actions.

## 4.3 Global exception handling with `IExceptionHandler`

In .NET 8 and later, `IExceptionHandler` gives a structured way to centralize exception handling.

```csharp
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

var app = builder.Build();

app.UseExceptionHandler();
```

Register the exception middleware early enough to cover the components whose exceptions it needs to catch.

```csharp
public sealed class GlobalExceptionHandler(
    ILogger<GlobalExceptionHandler> logger,
    IProblemDetailsService problemDetailsService) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext context,
        Exception exception,
        CancellationToken cancellationToken)
    {
        logger.LogError(
            exception,
            "Unhandled exception while processing request {TraceIdentifier}",
            context.TraceIdentifier);

        var (status, title) = exception switch
        {
            OrderNotFoundException =>
                (StatusCodes.Status404NotFound, "Order not found"),

            OrderConflictException =>
                (StatusCodes.Status409Conflict, "Order conflict"),

            InvalidOrderException =>
                (StatusCodes.Status400BadRequest, "Invalid order"),

            _ =>
                (StatusCodes.Status500InternalServerError,
                    "An unexpected error occurred")
        };

        context.Response.StatusCode = status;

        var problemDetails = new ProblemDetails
        {
            Status = status,
            Title = title,
            Instance = context.Request.Path
        };

        problemDetails.Extensions["traceId"] = context.TraceIdentifier;

        var written = await problemDetailsService.TryWriteAsync(
            new ProblemDetailsContext
            {
                HttpContext = context,
                ProblemDetails = problemDetails
            });

        if (!written)
        {
            // Fallback when no registered writer can handle the response.
            context.Response.ContentType = "application/problem+json";
            await context.Response.WriteAsJsonAsync(
                problemDetails,
                cancellationToken);
        }

        return true;   // true = handled, stop looking for other handlers
    }
}
```

This is an illustrative pattern. Define the exception types (`OrderNotFoundException` and so on) in your own code, and check the fallback content type and status against your API contract and target framework.

Key points:

- Log diagnostic details on the server.
- Never return stack traces, SQL, file paths, secrets or internal exception messages to clients.
- Map known application exceptions to the right status code.
- Return a generic 500 for unexpected failures.
- Include a trace or correlation ID so support can find the matching logs.
- Don't try to replace a response that has already started. The headers may already be sent.

### Choosing status codes

| Status | Common use |
|---|---|
| 400 Bad Request | Invalid input that the API treats as a client error. |
| 401 Unauthorized | Authentication is required or failed. |
| 403 Forbidden | The caller is authenticated but not allowed. |
| 404 Not Found | The resource does not exist, or is intentionally not disclosed. |
| 409 Conflict | The request conflicts with the current state of the resource. |
| 500 Internal Server Error | Unexpected server-side failure. |

Don't automatically map every `ArgumentException` to 400 or every `KeyNotFoundException` to 404. The exception type alone does not tell you whether the client caused the error. Use explicit application exceptions or a deliberate mapping policy.

### Background-service exceptions

HTTP exception middleware does not wrap `BackgroundService.ExecuteAsync`. Background work needs its own logging, retry, cancellation and failure policy. Decide whether a failed item should be retried, dead-lettered, skipped, or should stop the worker.

## 4.4 Common mistakes

- Registering the exception handler late, so earlier middleware errors escape it.
- Returning `ex.Message` or a stack trace to the client.
- Empty `catch` blocks that swallow errors.
- Using exceptions for normal control flow, such as an expected "not found".
- Expecting HTTP middleware to catch background-worker failures.

## 4.5 Try it (optional, 15 minutes)

1. Add an endpoint that throws an exception.
2. Without a handler, call it in Development and in Production mode and compare the output.
3. Add the global handler and call it again. Check that the response is clean `ProblemDetails` with a `traceId`, and the details appear only in the server log.

## 4.6 Interview questions

**Q1. How do you handle exceptions globally?**\
Use exception middleware, such as `UseExceptionHandler` with an `IExceptionHandler` (.NET 8+), registered early. Log the failure and return a safe, consistent error response.

**Q2. What is `ProblemDetails`?**\
A standard structure for API errors, with fields like `type`, `title`, `status`, `detail` and `instance`, plus room for extensions.

**Q3. Exception filter vs. exception middleware?**\
A filter works only inside the MVC pipeline. Middleware covers more of the HTTP pipeline, including failures outside MVC actions.

**Q4. Name the MVC filter types.**\
Authorization, resource, action, exception and result filters.

**Q5. What should never be returned to a client on error?**\
Stack traces, internal messages, SQL, file paths and credentials. Log the details securely and return a safe message with a trace ID.

**Q6. Can HTTP middleware catch an exception from a background worker?**\
No, because a background worker doesn't run inside an HTTP request. Handle and log failures in the worker itself.

**Q7. Why might a handler fail to change a response?**\
If the response has already started, the status and headers may be sent, so they can't be replaced.

---

# 5. IHttpClientFactory and Health Checks

## Remember This

**Analogy:** a taxi stand. The factory keeps a pool of cars and swaps them regularly. You don't buy a new car for each ride, and you don't use one car forever.

**One-line answer:** `IHttpClientFactory` reuses and recycles connections, so you avoid socket exhaustion and stale DNS. Health checks tell the platform whether to restart the app (liveness) or send it traffic (readiness).

## 5.1 Why use `IHttpClientFactory`?

- Creating and disposing an `HttpClient` for every call can cause connection problems, including **socket exhaustion** under load.
- One long-lived client can hold connections that don't promptly reflect **DNS changes**.
- `IHttpClientFactory` centralizes client setup and pools and recycles the underlying handlers, so connections are reused and refreshed.

The handler lifetime is configurable. Don't assume DNS changes show up immediately.

## 5.2 Three client styles

1. **Basic client:** `IHttpClientFactory.CreateClient()`.
2. **Named client:** registered under a name and requested by that name.
3. **Typed client:** a purpose-specific class that receives an `HttpClient`.

```csharp
public sealed class WeatherClient(HttpClient httpClient)
{
    public async Task<string> GetAsync(
        string city,
        CancellationToken cancellationToken)
    {
        var encodedCity = Uri.EscapeDataString(city);

        using var response = await httpClient.GetAsync(
            $"/weather?city={encodedCity}",
            cancellationToken);

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadAsStringAsync(
            cancellationToken);
    }
}
```

```csharp
builder.Services
    .AddHttpClient<WeatherClient>(client =>
    {
        client.BaseAddress = new Uri("https://api.example.com");
        client.Timeout = TimeSpan.FromSeconds(10);
    });
```

The example checks the response status and passes cancellation through both the request and the content read.

## 5.3 Resilience: timeout, retry and circuit breaker

On a compatible modern .NET version, the HTTP resilience extensions provide a standard pipeline:

```csharp
builder.Services
    .AddHttpClient<WeatherClient>(client =>
    {
        client.BaseAddress = new Uri("https://api.example.com");
    })
    .AddStandardResilienceHandler();
```

The standard handler uses Polly-based infrastructure. Check the required package and target framework support in your app.

**Timeout note:** the resilience handler has its own per-attempt and total timeouts. If you also set a short `HttpClient.Timeout`, it can cut off the whole retry sequence, so don't combine them without deciding how they fit together. That is why the example above leaves `Timeout` out.

Know the concepts:

- **Timeout:** limits how long an operation may take.
- **Retry:** repeats an operation after selected transient failures.
- **Backoff and jitter:** spread retries out to avoid a synchronized retry storm.
- **Circuit breaker:** stops calls for a while after failures cross a threshold.
- **Cancellation:** lets a caller stop waiting when its own request is cancelled.

Retries are not automatically safe. Retrying a non-idempotent operation, such as a payment or an order creation, can duplicate effects unless the API supports idempotency. Avoid stacking several retry layers without working out the total attempts and the total timeout.

## 5.4 Health checks

Health checks report the status of the app and its dependencies to monitoring tools and orchestrators.

```csharp
builder.Services
    .AddHealthChecks()
    .AddCheck(
        "self",
        () => HealthCheckResult.Healthy(),
        tags: new[] { "live" })
    .AddCheck<DatabaseHealthCheck>(
        "database",
        tags: new[] { "ready" });
```

`DatabaseHealthCheck` is a custom `IHealthCheck` class you must write, or you can use a health-check package for your database.

```csharp
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("live")
});

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
});
```

### Liveness vs. readiness

- **Liveness:** is the process alive and making progress? A failure may make the orchestrator restart it.
- **Readiness:** should this instance receive traffic now? A failure should normally remove it from service until it recovers, without restarting it.

Keep liveness checks light. If every pod's liveness check depends on a shared database, a short database outage restarts all of them for no reason. Readiness checks can include the dependencies that are truly required to serve traffic, with short timeouts.

Health endpoints can reveal operational details, so restrict or protect them for your environment.

## 5.5 Common mistakes

- `new HttpClient()` for every request.
- No timeout on outgoing calls.
- Retrying calls that are not safe to repeat.
- Stacking retries, so one failure becomes dozens of calls.
- Checking the database in the liveness probe.
- Leaving health endpoints open to the public internet.

## 5.6 Try it (optional, 15 minutes)

1. Register a typed client and call it from an endpoint.
2. Add `/health/live` and `/health/ready`, and make the readiness check fail on purpose.
3. Confirm that liveness still returns healthy while readiness returns unhealthy.

## 5.7 Interview questions

**Q1. Why not create a new `HttpClient` for every call?**\
It can lead to connection-management problems, including socket exhaustion under load.

**Q2. Why can a single static `HttpClient` be a problem?**\
Its long-lived connections may not notice DNS changes. It can still work if you manage the connection lifetime deliberately.

**Q3. How does `IHttpClientFactory` help?**\
It manages configured clients and recycles the underlying handlers, so connections are reused and DNS changes are picked up.

**Q4. Named vs. typed clients?**\
Named clients are looked up by a string. Typed clients wrap one API in your own class, which is clearer and easier to test.

**Q5. How do you add retries and a circuit breaker?**\
Use a supported HTTP resilience pipeline, set timeouts, and retry only safe operations and transient failures, with backoff and jitter. Watch idempotency and the total time.

**Q6. Liveness vs. readiness?**\
Liveness decides whether the process should be restarted. Readiness decides whether it should receive traffic.

**Q7. What belongs in a readiness check?**\
The dependencies the instance really needs to serve requests, such as a required database, with short timeouts. Leave out optional services.

---

# 6. Minimal APIs vs. Controllers

## Remember This

**Analogy:** a food truck vs. a full restaurant. The truck is quick to set up and has a small menu. The restaurant takes more setup but offers more built-in service.

**One-line answer:** both run on the same pipeline, routing and DI. Minimal APIs have less ceremony, and controllers give you the MVC feature set, such as filters and automatic validation.

## 6.1 Examples

Minimal API:

```csharp
app.MapGet(
    "/orders/{id:int}",
    async (int id, IOrderService service, CancellationToken ct) =>
    {
        var order = await service.GetAsync(id, ct);

        return order is null
            ? Results.NotFound()
            : Results.Ok(order);
    });
```

Controller:

```csharp
[ApiController]
[Route("orders")]
public sealed class OrdersController(IOrderService service)
    : ControllerBase
{
    [HttpGet("{id:int}")]
    public async Task<ActionResult<Order>> Get(
        int id,
        CancellationToken ct)
    {
        var order = await service.GetAsync(id, ct);

        return order is null ? NotFound() : Ok(order);
    }
}
```

Both examples assume `IOrderService` and `Order` exist in your app.

## 6.2 Comparison

| Area | Minimal APIs | Controllers |
|---|---|---|
| Setup | Often less ceremony for simple endpoints. | Controller classes, actions and attributes. |
| Organization | Route groups and endpoint-mapping methods. | Controllers group related actions. |
| Filters | Endpoint filters. | The MVC filter pipeline. |
| Model binding | Parameter-based binding. | MVC model binding and extensibility. |
| Validation | Depends on the framework version and libraries, and can be added explicitly. | `[ApiController]` gives automatic 400 responses on model-state failures. |
| Features | Routing, DI, authorization and other endpoint features. | MVC-specific features and conventions. |
| Performance | May have lower overhead in some scenarios. | May have a little more framework overhead. |
| Best fit | Simple or focused services. | APIs that benefit from MVC conventions, filters and model binders. |

Don't choose only on a presumed performance gap. Database time, network latency, serialization and business logic usually dominate. Both styles can be organized for large apps, and one app can use both.

## 6.3 Organizing Minimal APIs

Use route groups to share a prefix and endpoint conventions:

```csharp
var orders = app.MapGroup("/api/orders")
    .RequireAuthorization();

orders.MapGet("/{id:int}", GetOrderAsync);
orders.MapPost("/", CreateOrderAsync);
```

The handler methods are defined separately. Route groups can also share authorization, metadata and endpoint filters.

## 6.4 What `[ApiController]` provides

- Automatic 400 responses when model-state validation fails.
- Binding-source inference for action parameters.
- API-oriented conventions for binding and validation errors.

It does **not** turn every exception into a standard `ProblemDetails` response. Set up API error behavior and global exception handling on purpose.

## 6.5 Common mistakes

- Choosing Minimal APIs "for speed" without measuring anything.
- Putting every endpoint in `Program.cs` until it becomes unreadable.
- Expecting MVC filters to run on Minimal API endpoints.
- Assuming automatic validation exists in every version of Minimal APIs.

## 6.6 Interview questions

**Q1. When would you choose Minimal APIs vs. controllers?**\
Choose based on conventions, endpoint complexity, team preference and the features you need. Minimal APIs reduce ceremony, and controllers give MVC organization and extensibility.

**Q2. Are Minimal APIs faster?**\
Sometimes slightly, because of less framework overhead. The real difference depends on the workload, so measure before deciding.

**Q3. What does `[ApiController]` provide?**\
Automatic model-state validation responses, binding-source inference and API-oriented conventions.

**Q4. How do you organize a large Minimal API?**\
Use route groups, feature-specific mapping methods and shared endpoint conventions.

**Q5. Do MVC filters work on Minimal APIs?**\
No. Minimal APIs use endpoint filters.

**Q6. Can one app use both?**\
Yes. They share hosting, DI, middleware and routing.

---

# 7. Senior-Level Scenario Questions

Try each aloud in two to three minutes. Explain how you would diagnose the problem, not only the likely cause.

## Scenario 1: A request is slow, but the endpoint code looks fast. Where do you look?

1. Measure total request time and break it down by dependency.
2. Use structured logs and distributed traces with a correlation ID.
3. Check database query time, query plans, connection-pool waits and downstream HTTP calls.
4. Look for blocking calls like `.Result` or `.Wait()` in async paths.
5. Check middleware, serialization, locks, thread-pool starvation and excessive retries.
6. Reproduce under realistic load and compare measurements before changing code.

Don't blame middleware or `HttpClient` without evidence.

## Scenario 2: A config value changed, but the service still uses the old one. Why?

- Does the provider support reload, and is it enabled?
- Which type does the consumer use: `IOptions<T>`, `IOptionsSnapshot<T>` or `IOptionsMonitor<T>`?
- `IOptions<T>` has no automatic per-request refresh.
- Use `IOptionsMonitor<T>` for singletons that need current values, where the source supports reload.
- Check that the app actually received the new value.

## Scenario 3: One user's data appears in another user's response. What do you investigate?

- Static fields or singletons holding per-request mutable state.
- Singletons that captured scoped dependencies.
- Cache keys missing the tenant, user or authorization dimension.
- Authorization and tenant-isolation logic.
- Objects reused or changed across concurrent requests.
- Treat it as a possible security incident. Contain it and investigate before assuming it's only a lifetime bug.

## Scenario 4: How do you make API errors consistent and safe?

- Centralize unexpected-exception handling in middleware.
- Use one error format, such as `ProblemDetails`.
- Map known application exceptions to the right statuses on purpose.
- Log details on the server with trace IDs.
- Never expose stack traces, SQL, secrets or internal messages.
- Add tests for both expected errors and unexpected failures.

## Scenario 5: Pods keep restarting during a database outage. Why?

**Likely cause:** the liveness probe depends on the database. When it goes down, liveness fails and the orchestrator restarts processes that were otherwise fine.

**Better design:** keep liveness about the process itself. Use readiness for dependencies that are required to serve traffic, with sensible timeouts and thresholds.

## Scenario 6: A singleton needs a scoped repository. What do you do?

Don't inject the scoped repository into the singleton constructor. Inject `IServiceScopeFactory`, create a scope for each unit of work, resolve the repository inside it, and dispose the scope afterward. Also ask whether the work belongs in a scoped service instead.

## Scenario 7: A downstream service is failing, and retries make it worse. What do you change?

- Add or review bounded timeouts.
- Retry only transient failures and operations that are safe to repeat.
- Use exponential backoff and jitter.
- Add a circuit breaker.
- Limit total attempts and total elapsed time.
- Remove duplicate retry layers.
- Protect side-effecting operations with idempotency.
- Log attempts and outcomes without leaking sensitive payloads.

## Scenario 8: The API returns 500 for a missing order. How do you improve it?

Represent the expected absence explicitly and return 404 at the API boundary. Don't use a generic exception for ordinary control flow unless your design deliberately uses a typed exception. Test both the missing-resource case and unexpected failures.

## Scenario 9: Would you move every controller to Minimal APIs for performance?

No. First find a real bottleneck by measuring. Both styles work in production. Weigh maintainability, team familiarity, validation, filters, organization and testing, and migrate only for a clear benefit.

## Scenario 10: A background worker stops after one failed item. What do you inspect?

- Did an exception escape the worker's loop?
- Does the host stop when a background service throws?
- Is cancellation handled correctly?
- Should transient failures be retried with limits?
- Do poison messages need a dead-letter path?
- Are failures logged and visible?
- Are scopes and resources disposed for every unit of work?

---

# 8. Cheat Sheet

| Topic | Remember |
|---|---|
| Pipeline | Kestrel, then middleware in configured order, then the endpoint, then the response unwinds in reverse. (Airport security.) |
| `Use` / `Run` / `Map` | Continue / terminal / path branch. |
| Ordering | Exception handling early. Authentication before authorization. |
| Middleware DI | Conventional middleware takes scoped services in `InvokeAsync`. `IMiddleware` is activated through DI. |
| Lifetimes | Transient per resolution, scoped per scope, singleton per app. (Napkin, bill, kitchen.) |
| Captive dependency | A long-lived service holds a shorter-lived one. Fix with `IServiceScopeFactory`. |
| `DbContext` | Usually scoped. Not thread-safe. |
| Config order | JSON, environment JSON, user secrets, environment variables, command line. Later wins. |
| Options | `IOptions` cached, `IOptionsSnapshot` per request, `IOptionsMonitor` live. Use `ValidateOnStart()`. (Menu, fresh menu, display board.) |
| Filters | Authorization, resource, action, exception, result. Minimal APIs use endpoint filters. |
| Global errors | `UseExceptionHandler` early, `IExceptionHandler`, `ProblemDetails`. Log details, never return them. (Safety net.) |
| HTTP clients | `IHttpClientFactory`, typed clients, timeouts, safe retries. (Taxi stand.) |
| Retries | Think idempotency, backoff, jitter and total time. |
| Health checks | Liveness: should it run? Readiness: should it get traffic? Keep liveness light. |
| Minimal vs. controllers | Same pipeline. Minimal has less ceremony, controllers have more built-in features. (Food truck, restaurant.) |
| Troubleshooting | Measure first, trace dependencies, inspect logs, verify the fix under realistic load. |

---

# 9. 60-Second Interview Answer

**"Walk me through the ASP.NET Core request pipeline."**

"An HTTP request is received by Kestrel, often behind a reverse proxy or load balancer. It passes through the middleware pipeline in the order configured in `Program.cs`. Each middleware can inspect or change the request, call the next component, and then inspect or change the response as control returns. Routing selects the endpoint, and authentication and authorization decide whether the caller may access it. The endpoint, which may be a controller action or a Minimal API handler, gets its dependencies through dependency injection. Order matters: authentication runs before authorization, and exception handling comes early enough to cover what follows. In production I would also use structured logging, tracing, health checks, safe error responses and timeouts on downstream calls."

---

## Practice Checklist

- [ ] Draw the request pipeline and explain the order.
- [ ] Write one custom middleware component.
- [ ] Demonstrate transient, scoped and singleton lifetimes.
- [ ] Reproduce and fix a captive dependency.
- [ ] Use `IServiceScopeFactory` in a background worker.
- [ ] Bind and validate a configuration section.
- [ ] Compare all three Options interfaces in a running app.
- [ ] Implement global exception handling with a safe error response.
- [ ] Register and call a typed HTTP client.
- [ ] Add separate liveness and readiness endpoints.
- [ ] Write one endpoint as a Minimal API and another as a controller.
- [ ] Answer the scenario questions aloud without reading the notes.