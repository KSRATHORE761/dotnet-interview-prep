# ASP.NET Core Internals: Notes and Interview Q&A

**Audience:** .NET developers preparing for senior-level interviews\
**Topics:** Request pipeline and middleware, dependency injection,
configuration and Options, filters and exception handling,
`IHttpClientFactory` and health checks, Minimal APIs and controllers\
**Suggested study time:** 5--6 hours for a first pass, followed by
hands-on practice

------------------------------------------------------------------------

## How to Use These Notes

For each topic:

1.  Read the explanation.
2.  Explain the concept aloud without looking.
3.  Run or adapt the code example in a small ASP.NET Core application.
4.  Answer the interview questions before reading the answers.
5.  Practise the scenario questions at the end.

These notes use modern ASP.NET Core patterns. Some APIs vary by .NET
version; check the target framework of the application before copying an
example.

## Contents

1.  [Glossary](#glossary)
2.  [Request Pipeline and
    Middleware](#1-request-pipeline-and-middleware)
3.  [Dependency Injection Lifetimes](#2-dependency-injection-lifetimes)
4.  [Configuration and the Options
    Pattern](#3-configuration-and-the-options-pattern)
5.  [Filters and Global Exception
    Handling](#4-filters-and-global-exception-handling)
6.  [`IHttpClientFactory` and Health
    Checks](#5-ihttpclientfactory-and-health-checks)
7.  [Minimal APIs vs. Controllers](#6-minimal-apis-vs-controllers)
8.  [Senior-Level Scenario
    Questions](#7-senior-level-scenario-questions)
9.  [Cheat Sheet](#8-cheat-sheet)
10. [60-Second Interview Answer](#9-60-second-interview-answer)

------------------------------------------------------------------------

## Glossary

  -----------------------------------------------------------------------
  Term                                Simple meaning
  ----------------------------------- -----------------------------------
  Kestrel                             ASP.NET Core's built-in
                                      cross-platform web server.

  Host                                Starts and manages the application,
                                      configuration, logging, services,
                                      and server integration.

  Request pipeline                    The ordered chain of middleware
                                      that processes an HTTP request and
                                      response.

  Middleware                          A component that can inspect or
                                      change a request/response, call the
                                      next component, or stop the
                                      pipeline.

  `next`                              The delegate used to invoke the
                                      next middleware.

  Short-circuit                       Returning a response without
                                      invoking the next middleware.

  Endpoint                            The selected handler for a request,
                                      such as a controller action or
                                      Minimal API handler.

  Routing                             Matches the request to an endpoint
                                      based on route information and
                                      constraints.

  `HttpContext`                       Represents the current HTTP request
                                      and response, along with user and
                                      request-specific information.

  Dependency injection (DI)           Supplying a class with the
                                      dependencies it needs instead of
                                      having it create them itself.

  DI container                        The framework component that
                                      resolves registered services and
                                      manages their lifetimes.

  Lifetime                            How long a service instance is
                                      used: transient, scoped, or
                                      singleton.

  Scope                               A lifetime boundary for scoped
                                      services. In a typical web app, a
                                      request creates a scope.

  Captive dependency                  A long-lived service holds a
                                      shorter-lived dependency beyond its
                                      intended lifetime.

  Configuration                       Key/value settings loaded from
                                      sources such as JSON, environment
                                      variables, and command-line
                                      arguments.

  Options pattern                     Binding a configuration section to
                                      a typed C# class and injecting it.

  Filter                              Code that runs at a stage of the
                                      MVC or endpoint execution pipeline.

  `ProblemDetails`                    A standard structure for
                                      communicating API errors.

  Typed HTTP client                   A purpose-specific class that
                                      receives an `HttpClient` and wraps
                                      calls to a downstream service.

  Health check                        A check used to report whether an
                                      application or a dependency is
                                      healthy.

  Liveness/readiness                  Liveness asks whether a process
                                      should continue running; readiness
                                      asks whether it should receive
                                      traffic.

  Minimal API                         An endpoint style that maps
                                      handlers directly, without
                                      requiring controller classes.

  Controller                          A class that groups MVC actions and
                                      uses MVC features such as action
                                      filters and model binding.
  -----------------------------------------------------------------------

------------------------------------------------------------------------

# 1. Request Pipeline and Middleware

## 1.1 What happens when a request arrives?

A simplified request flow is:

1.  A client sends an HTTP request.
2.  Kestrel receives the request (possibly behind a reverse proxy or
    load balancer).
3.  The request passes through middleware in the order registered.
4.  Routing selects an endpoint.
5.  Authentication, authorization, and other middleware or endpoint
    logic run as configured.
6.  The endpoint executes.
7.  The response unwinds through middleware in reverse order.

Middleware can run code **before** and **after** calling the next
middleware. If a component does not call `next`, later middleware and
the endpoint do not run.

## 1.2 `Use`, `Run`, and `Map`

-   `app.Use(...)`: middleware can perform work and call the next
    middleware.
-   `app.Run(...)`: terminal middleware; it does not call the next
    middleware.
-   `app.Map(...)`: creates a branch based on a path prefix.
-   `app.MapWhen(...)`: creates a branch when a predicate is true.
-   `app.UseWhen(...)`: creates a conditional branch that can rejoin the
    main pipeline if the branch does not terminate it.

Example:

``` csharp
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

The timing middleware runs before and after the rest of the pipeline.
The `/admin` branch terminates in this example.

## 1.3 Custom middleware

``` csharp
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

Register it in `Program.cs`:

``` csharp
app.UseMiddleware<RequestIdMiddleware>();
```

In a real application, consider whether to use an existing
trace/correlation identifier rather than generating an unrelated ID for
every request. Avoid exposing sensitive information in response headers.

### Scoped dependencies in middleware

Conventional middleware created through `app.UseMiddleware<T>()` is
generally constructed once for the application. Do not inject a scoped
service into its constructor. Instead, inject it into `InvokeAsync`:

``` csharp
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

The framework resolves method-injected scoped services from the current
request scope.

**Qualification:** Middleware implementing `IMiddleware` is activated
through DI. It can use constructor injection when registered with an
appropriate lifetime, such as scoped.

## 1.4 Middleware ordering

There is no single order that is correct for every application. A common
explicit arrangement is:

``` csharp
app.UseExceptionHandler();

app.UseHttpsRedirection();

app.UseRouting();

app.UseCors("FrontendPolicy");

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapGet("/health", () => Results.Ok("Healthy"));
```

Static files, response compression, rate limiting, antiforgery, output
caching, and other middleware may be needed depending on the
application. Their placement matters.

Important principles:

-   Exception handling should be early enough to catch failures from
    middleware and endpoints registered after it.
-   Authentication should run before authorization.
-   CORS must be placed so that it can handle the relevant endpoint and
    preflight behavior.
-   When explicitly using `UseRouting`, place middleware that needs
    endpoint metadata after routing.
-   Middleware registered after a terminal endpoint mapping may not
    participate in the endpoint request flow as expected; follow the
    framework's documented ordering for the target version.

## 1.5 Common mistakes

-   Forgetting to call `next`, unintentionally stopping the pipeline.
-   Calling `next` more than once.
-   Writing to the response after it has already started.
-   Registering authentication after authorization.
-   Putting exception handling too late.
-   Injecting scoped services into conventional middleware constructors.
-   Logging sensitive headers, tokens, or personal data.
-   Assuming every request reaches the endpoint; middleware can
    short-circuit.

## 1.6 Interview questions

**Q1. What is middleware?**\
Middleware is a component in the HTTP request pipeline. It can inspect
or modify the request, call the next component, and inspect or modify
the response as control returns.

**Q2. Why does middleware order matter?**\
Middleware runs in registration order on the way in and unwinds in
reverse order on the way out. For example, authentication should precede
authorization, and exception handling should cover the components whose
errors it needs to handle.

**Q3. What is the difference between `Use`, `Run`, and `Map`?**\
`Use` can call the next component; `Run` is terminal; `Map` branches
based on a path prefix.

**Q4. What is short-circuiting?**\
A middleware returns a response without calling the next middleware.
Static-file middleware can serve a file this way, for example.

**Q5. How do you use scoped services in middleware?**\
For conventional middleware, inject them into `InvokeAsync` parameters.
For `IMiddleware`, constructor injection is possible when its DI
lifetime is configured correctly.

**Q6. Middleware vs. MVC filters?**\
Middleware runs in the HTTP pipeline and is not specific to MVC. MVC
filters run at stages inside the MVC action pipeline and can access
MVC-specific context, such as action arguments and model state.

------------------------------------------------------------------------

# 2. Dependency Injection Lifetimes

## 2.1 The three lifetimes

  -----------------------------------------------------------------------
  Lifetime                Meaning                 Typical use
  ----------------------- ----------------------- -----------------------
  Transient               A new instance is       Small, stateless
                          created each time the   helpers.
                          service is resolved.    

  Scoped                  One instance is used    `DbContext`,
                          within a DI scope. In a unit-of-work services.
                          typical web app, this   
                          is one request.         

  Singleton               One instance is shared  Thread-safe shared
                          for the lifetime of the services and caches.
                          application service     
                          provider.               
  -----------------------------------------------------------------------

Example registrations:

``` csharp
builder.Services.AddTransient<IFormatter, Formatter>();
builder.Services.AddScoped<IOrderService, OrderService>();
builder.Services.AddSingleton<IClock, SystemClock>();
```

A singleton is shared across requests, so it must be designed for
concurrent access. A transient is not automatically thread-safe merely
because it is transient.

## 2.2 Captive dependencies

A captive dependency occurs when a long-lived service holds a
shorter-lived service beyond its intended lifetime.

Problematic example:

``` csharp
builder.Services.AddScoped<AppDbContext>();
builder.Services.AddSingleton<ReportService>();
```

If `ReportService` directly receives `AppDbContext` in its constructor,
the singleton can retain a scoped context for the lifetime of the
singleton. This can cause incorrect lifetime behavior and concurrency
problems. `DbContext` is not thread-safe.

The general lesson is not simply that every dependency must have an
equal or longer lifetime. A transient dependency can be injected into a
singleton, but that transient instance will then be retained by the
singleton and effectively live as long as it does. Whether that is safe
depends on the dependency's state, resources, and concurrency behavior.

Enable scope validation in development and tests so invalid
scoped-from-singleton dependencies can be detected.

## 2.3 Using scoped services in a background service

A hosted service is long-lived. Create a scope for each unit of work,
rather than resolving a scoped service from the root provider.

``` csharp
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

This example creates and disposes a scope for each processing iteration.
Adapt retry, delay, and shutdown behavior to the actual workload. Avoid
swallowing failures without logging or making an explicit recovery
decision.

## 2.4 Disposal

-   The DI container disposes disposable services that it creates when
    their scope or provider is disposed.
-   Scoped services are normally disposed at the end of the request
    scope.
-   Singleton services are normally disposed when the application
    service provider shuts down.
-   If you pass an already-created instance to a registration such as
    `AddSingleton(instance)`, ownership and disposal are different; the
    container does not assume it created that instance.
-   Use `CreateAsyncScope` when scoped services may require asynchronous
    disposal.

## 2.5 Multiple registrations

``` csharp
builder.Services.AddScoped<INotificationSender, EmailSender>();
builder.Services.AddScoped<INotificationSender, SmsSender>();
```

For the default Microsoft DI container:

-   Injecting a single `INotificationSender` resolves the last
    registration.
-   Injecting `IEnumerable<INotificationSender>` resolves all registered
    implementations in registration order.
-   `TryAdd...` registration methods add a service only if an
    appropriate registration is not already present.

Keyed services are supported in modern .NET, starting with .NET 8, for
selecting among named/keyed registrations.

## 2.6 Constructor injection vs. service locator

Prefer constructor injection for normal application dependencies:

``` csharp
public sealed class OrderService(IOrderRepository repository)
{
    public Task<Order?> GetAsync(int id, CancellationToken ct) =>
        repository.GetAsync(id, ct);
}
```

The dependency is explicit and easy to replace in tests. Repeatedly
resolving services from `IServiceProvider` throughout business logic
hides dependencies and is often called the service locator pattern.
`IServiceProvider` is appropriate in infrastructure scenarios such as
creating scopes in hosted services.

## 2.7 Interview questions

**Q1. What are the three DI lifetimes?**\
Transient creates an instance per resolution; scoped shares an instance
within a scope; singleton shares an instance for the application's
service-provider lifetime.

**Q2. What is a captive dependency?**\
A long-lived service holds a shorter-lived dependency beyond the
dependency's intended lifetime. A singleton holding a scoped `DbContext`
is a common example.

**Q3. How do you use a scoped service in a `BackgroundService`?**\
Inject `IServiceScopeFactory`, create a scope for each unit of work,
resolve the scoped service from that scope, and dispose the scope when
the work is complete.

**Q4. What lifetime should `DbContext` usually have?**\
Scoped is the common default for web requests. It gives a unit of work
its own context and allows disposal at the end of the scope. Do not use
the same context concurrently across threads.

**Q5. Who disposes DI-created services?**\
The container disposes the disposable services it creates when their
scope or provider is disposed. Pre-created instances registered with the
container have different ownership rules.

**Q6. What happens if an interface is registered twice?**\
A single resolution returns the last registration in the default
container; `IEnumerable<T>` returns all registered implementations.

**Q7. Why prefer constructor injection?**\
It makes dependencies explicit, supports testing, and improves
readability. Use service-provider resolution deliberately in
infrastructure code where dynamic resolution or scope creation is
required.

------------------------------------------------------------------------

# 3. Configuration and the Options Pattern

## 3.1 Configuration providers

ASP.NET Core's default application configuration commonly includes
providers such as:

1.  `appsettings.json`
2.  `appsettings.{Environment}.json`
3.  User secrets in Development
4.  Environment variables
5.  Command-line arguments

Later providers generally override earlier providers for the same key.
Applications can add, remove, or reorder providers, so this is the
default pattern rather than a universal rule.

Nested environment-variable keys commonly use double underscores:

``` text
Smtp__Host=smtp.example.com
Smtp__Port=587
```

These map to keys such as `Smtp:Host` and `Smtp:Port`.

Do not commit production secrets to source control. Use a secret store
such as Azure Key Vault, managed identity where supported, or securely
injected deployment secrets. User Secrets is intended for local
development and is not an encrypted production secret store.

## 3.2 The Options pattern

Instead of scattering raw configuration lookups across the application,
bind a section to a strongly typed class.

Example configuration:

``` json
{
  "Smtp": {
    "Host": "smtp.example.com",
    "Port": 587
  }
}
```

Options class:

``` csharp
public sealed class SmtpOptions
{
    public string Host { get; set; } = "";
    public int Port { get; set; }
}
```

Registration with validation:

``` csharp
builder.Services
    .AddOptions<SmtpOptions>()
    .Bind(builder.Configuration.GetSection("Smtp"))
    .Validate(options => !string.IsNullOrWhiteSpace(options.Host),
        "Smtp:Host is required.")
    .Validate(options => options.Port is > 0 and <= 65535,
        "Smtp:Port must be a valid TCP port.")
    .ValidateOnStart();
```

Consumer:

``` csharp
public sealed class Mailer(IOptions<SmtpOptions> options)
{
    private readonly SmtpOptions _smtp = options.Value;

    public string GetServer() => _smtp.Host;
}
```

`ValidateOnStart()` makes configured validation run during startup
rather than waiting for the options value to be requested later.

## 3.3 `IOptions<T>` vs. `IOptionsSnapshot<T>` vs. `IOptionsMonitor<T>`

  -----------------------------------------------------------------------
  Type                    Lifetime / behavior     Use when
  ----------------------- ----------------------- -----------------------
  `IOptions<T>`           Can be injected         Settings that are
                          broadly; its options    effectively stable.
                          value is cached and     
                          does not provide        
                          per-request refresh     
                          semantics.              

  `IOptionsSnapshot<T>`   Scoped snapshot,        Request-scoped code
                          commonly refreshed for  that needs updated
                          each new request scope. configuration
                                                  snapshots.

  `IOptionsMonitor<T>`    Singleton-friendly; can Singletons or hosted
                          observe updated values  services that need to
                          and supports change     react to reloadable
                          notifications through   settings.
                          `OnChange`.             
  -----------------------------------------------------------------------

These types do not make every configuration source reloadable. Refresh
depends on the source and its reload support. For example, JSON
configuration commonly supports reload when configured with
`reloadOnChange`.

`IOptionsSnapshot<T>` is scoped and should not be injected directly into
a singleton. Use `IOptionsMonitor<T>` when a singleton needs to observe
changes.

## 3.4 Interview questions

**Q1. How does configuration precedence work?**\
In the default setup, later providers override earlier providers for the
same key. Environment variables and command-line arguments commonly
override JSON settings.

**Q2. What is the Options pattern?**\
It binds configuration to typed classes that can be injected, making
settings easier to validate, test, and maintain.

**Q3. What is the difference between the three Options interfaces?**\
`IOptions<T>` provides a cached value, `IOptionsSnapshot<T>` provides a
scoped snapshot, and `IOptionsMonitor<T>` supports current values and
change notifications when the source supports reload.

**Q4. Which can be injected into a singleton?**\
`IOptions<T>` and `IOptionsMonitor<T>` are suitable.
`IOptionsSnapshot<T>` is scoped and should not be injected directly into
a singleton.

**Q5. How do you validate settings at startup?**\
Use Options validation, such as `Validate`, `ValidateDataAnnotations`,
and `ValidateOnStart`.

**Q6. Where should secrets live?**\
Use User Secrets for local development and an appropriate managed secret
store or securely injected secrets for production. Avoid committing
secrets to configuration files.

------------------------------------------------------------------------

# 4. Filters and Global Exception Handling

## 4.1 MVC filter types

MVC filters run at particular stages in the MVC action pipeline. The
broad order is:

1.  Authorization filters
2.  Resource filters
3.  Action filters
4.  Exception filters
5.  Result filters

The exact execution includes before/after behavior and short-circuiting.
Result filters wrap result execution; exception filters handle eligible
exceptions within MVC execution.

  -----------------------------------------------------------------------
  Filter                              Typical purpose
  ----------------------------------- -----------------------------------
  Authorization filter                MVC-specific authorization
                                      decisions; framework authorization
                                      middleware/attributes are often
                                      preferred.

  Resource filter                     Logic around much of the MVC
                                      pipeline, such as specialized
                                      caching or resource management.

  Action filter                       Logic before/after an action
                                      method, such as action-level
                                      validation or logging.

  Exception filter                    Handling eligible exceptions thrown
                                      during MVC action execution.

  Result filter                       Logic before/after MVC result
                                      execution.
  -----------------------------------------------------------------------

MVC filters do not apply to Minimal API endpoints. Minimal APIs support
endpoint filters through `AddEndpointFilter`.

## 4.2 Middleware vs. filters

-   Middleware can run for requests across the application and does not
    depend on MVC.
-   MVC filters run inside MVC and can access action-specific
    information, model state, and result context.
-   Endpoint filters are designed for Minimal API endpoint execution.

For application-wide exception handling, middleware is generally the
better central mechanism because it can cover failures outside MVC
actions as well.

## 4.3 Global exception handling with `IExceptionHandler`

In .NET 8 and later, `IExceptionHandler` provides a structured way to
implement centralized exception handling. Register the handler and
exception middleware:

``` csharp
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

var app = builder.Build();

app.UseExceptionHandler();
```

The exception middleware should be early enough to cover the pipeline
components whose exceptions it needs to catch.

Example handler:

``` csharp
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
            // Fallback for cases where no registered writer can handle the response.
            context.Response.ContentType = "application/problem+json";
            await context.Response.WriteAsJsonAsync(
                problemDetails,
                cancellationToken);
        }

        return true;
    }
}
```

This is an illustrative pattern. Define application-specific exception
types such as `OrderNotFoundException` and `OrderConflictException` in
your own code. Ensure your fallback response uses the expected content
type and status code for your target framework and API contract.

Important points:

-   Log diagnostic details on the server.
-   Do not return stack traces, SQL statements, filesystem paths,
    secrets, or internal exception messages to clients.
-   Map known application exceptions to appropriate status codes.
-   Return a generic 500 response for unexpected failures.
-   Include a trace/correlation identifier so support teams can find the
    corresponding logs.
-   Do not try to replace a response after it has already started;
    headers may already have been sent.

### Choosing status codes

  -----------------------------------------------------------------------
  Status                              Common use
  ----------------------------------- -----------------------------------
  400 Bad Request                     Invalid request syntax or input
                                      that the API classifies as a client
                                      error.

  401 Unauthorized                    Authentication is required or
                                      failed.

  403 Forbidden                       The caller is authenticated but not
                                      allowed to perform the action.

  404 Not Found                       The requested resource does not
                                      exist or is intentionally not
                                      disclosed.

  409 Conflict                        The request conflicts with current
                                      resource state.

  500 Internal Server Error           Unexpected server-side failure.
  -----------------------------------------------------------------------

Do not map every `ArgumentException` to 400 or every
`KeyNotFoundException` to 404 automatically. Exception type alone may
not tell you whether the client caused the error. Use explicit
application exceptions or a deliberate mapping policy.

### Background-service exceptions

HTTP exception middleware does not wrap
`BackgroundService.ExecuteAsync`. Background processing needs its own
logging, retry, cancellation, and failure policy. Decide whether a
failed item should be retried, dead-lettered, skipped, or cause the
worker to stop.

## 4.4 Interview questions

**Q1. How do you handle exceptions globally?**\
Use exception-handling middleware, such as `UseExceptionHandler` with an
`IExceptionHandler` implementation in .NET 8+, or a well-tested custom
middleware. Log the failure and return a safe, consistent error
response.

**Q2. What is `ProblemDetails`?**\
A standard structure for API errors, commonly containing fields such as
`type`, `title`, `status`, `detail`, and `instance`, with room for
extensions.

**Q3. Exception filter vs. exception middleware?**\
An exception filter operates within MVC's execution pipeline. Exception
middleware can cover a wider portion of the HTTP pipeline, including
failures outside MVC actions.

**Q4. Name the MVC filter types.**\
Authorization, resource, action, exception, and result filters.

**Q5. What should not be returned to a client on an error?**\
Stack traces, internal exception details, SQL, file paths, credentials,
and other sensitive implementation details. Log useful diagnostics
securely and return a safe message with a trace identifier.

**Q6. Can HTTP middleware catch an exception from a background
worker?**\
No. A background worker does not execute as part of an HTTP request.
Handle and log failures in the worker's own execution flow.

**Q7. Why might an exception handler fail to change a response?**\
If the response has already started, headers and status may have been
sent. The handler cannot reliably replace them with a new error
response.

------------------------------------------------------------------------

# 5. `IHttpClientFactory` and Health Checks

## 5.1 Why use `IHttpClientFactory`?

Repeatedly creating and disposing `HttpClient` instances can cause
connection-management problems, including socket exhaustion under load.
A single long-lived client can also retain connections that do not
promptly reflect DNS changes.

`IHttpClientFactory` centralizes client configuration and pools/recycles
underlying message handlers. This supports connection reuse and helps
clients respond to DNS changes as handlers and connections are
refreshed.

Handler lifetime is configurable; do not assume that DNS changes are
observed immediately.

## 5.2 Three client styles

1.  **Basic client:** use `IHttpClientFactory.CreateClient()`.
2.  **Named client:** register a client under a name and request it by
    that name.
3.  **Typed client:** create a purpose-specific class that receives
    `HttpClient`.

Typed-client example:

``` csharp
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

Registration:

``` csharp
builder.Services
    .AddHttpClient<WeatherClient>(client =>
    {
        client.BaseAddress = new Uri("https://api.example.com");
        client.Timeout = TimeSpan.FromSeconds(10);
    });
```

This example explicitly checks the response status and passes
cancellation through the request and content-reading operations.

## 5.3 Resilience: timeout, retry, and circuit breaker

For applications targeting a compatible modern .NET version, the HTTP
resilience extensions provide a standard resilience pipeline:

``` csharp
builder.Services
    .AddHttpClient<WeatherClient>(client =>
    {
        client.BaseAddress = new Uri("https://api.example.com");
        client.Timeout = TimeSpan.FromSeconds(10);
    })
    .AddStandardResilienceHandler();
```

The standard handler is provided by the .NET HTTP resilience extensions
and uses Polly-based resilience infrastructure. Confirm the required
package and target-framework support in your application.

Understand the concepts:

-   **Timeout:** limits how long an operation is allowed to take.
-   **Retry:** repeats an operation after selected transient failures.
-   **Backoff and jitter:** spread retries out to avoid synchronized
    retry storms.
-   **Circuit breaker:** temporarily stops calls when failures cross a
    configured threshold.
-   **Cancellation:** allows a caller to stop waiting or stop work when
    its request is cancelled.

Retries are not automatically safe. Retrying a non-idempotent operation,
such as a payment or order creation, can duplicate effects unless the
API supports idempotency or the operation is otherwise protected. Avoid
layering multiple retry policies without understanding the combined
number of attempts and total timeout.

## 5.4 Health checks

Health checks can report application and dependency status to monitoring
systems and orchestrators.

``` csharp
builder.Services
    .AddHealthChecks()
    .AddCheck(
        "self",
        () => HealthCheckResult.Healthy(),
        tags: new[] { "live" });
```

A database check can be added using the appropriate provider package or
a custom `IHealthCheck` implementation:

``` csharp
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

Map endpoints:

``` csharp
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("live")
});

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
});
```

`DatabaseHealthCheck` is a custom class that you must implement, or
replace with a health-check package suited to your database.

### Liveness vs. readiness

-   **Liveness:** Is the process alive and making progress? A failed
    liveness probe may cause the orchestrator to restart it.
-   **Readiness:** Should this instance receive traffic now? A failed
    readiness probe should normally remove it from service until it is
    ready again.

Keep liveness checks lightweight. If every pod's liveness check depends
on a shared database, a temporary database outage can cause unnecessary
restarts. Readiness checks may include critical dependencies required to
serve traffic, with short timeouts and careful failure behavior.

Health-check endpoints may reveal operational details. Restrict or
protect them as appropriate for your environment.

## 5.5 Interview questions

**Q1. Why not create and dispose a new `HttpClient` for every call?**\
Repeated creation and disposal can lead to connection-management issues,
including socket exhaustion under load.

**Q2. Why can a single static `HttpClient` be problematic?**\
Long-lived connections may not promptly observe DNS changes. A
long-lived client can still be configured correctly in some designs, but
its connection lifetime needs to be considered.

**Q3. How does `IHttpClientFactory` help?**\
It manages configured clients and pools/recycles underlying message
handlers, supporting connection reuse and helping with DNS changes.

**Q4. Named vs. typed clients?**\
Named clients are configured and retrieved by string name. Typed clients
wrap a purpose-specific API and provide a clearer abstraction for
application code.

**Q5. How do you add retries and a circuit breaker?**\
Use a supported HTTP resilience pipeline, configure timeouts, and retry
only when the operation and failure are safe to retry. Consider
idempotency, backoff, jitter, and total timeout.

**Q6. What is the difference between liveness and readiness?**\
Liveness helps determine whether a process should be restarted;
readiness determines whether it should receive traffic.

**Q7. What belongs in a readiness check?**\
Dependencies that are essential for the instance to serve requests, such
as a required database, with bounded timeouts. Avoid making readiness
depend on optional services if the app can still serve useful traffic
without them.

------------------------------------------------------------------------

# 6. Minimal APIs vs. Controllers

## 6.1 Examples

Minimal API:

``` csharp
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

``` csharp
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

These examples assume that `IOrderService` and `Order` exist in the
application.

## 6.2 Comparison

  -----------------------------------------------------------------------
  Area                    Minimal APIs            Controllers
  ----------------------- ----------------------- -----------------------
  Setup                   Often less ceremony for Uses controller
                          simple endpoints.       classes, actions, and
                                                  attributes.

  Organization            Route groups and        Controllers group
                          endpoint-mapping        related actions into
                          methods can organize    classes.
                          features.               

  Filters                 Endpoint filters.       MVC filter pipeline.

  Model binding           Parameter-based binding MVC model binding and
                          with supported          extensibility.
                          conventions.            

  Validation              Depends on framework    `[ApiController]`
                          version and libraries;  provides automatic 400
                          validation can be added responses for
                          explicitly or through   model-state validation
                          supported integrations. failures.

  Features                Supports routing, DI,   Offers MVC-specific
                          authorization, and      features and
                          other endpoint          conventions.
                          capabilities.           

  Performance             May have lower overhead May have somewhat more
                          in some scenarios.      framework overhead in
                                                  some scenarios.

  Best fit                Simple services,        APIs that benefit from
                          focused APIs, and teams MVC conventions,
                          that prefer             filters, model binders,
                          endpoint-oriented code. and controller
                                                  organization.
  -----------------------------------------------------------------------

Do not choose solely on a presumed performance difference. Database
time, network latency, serialization, and application logic often
dominate. Both styles can be organized for large applications, and both
can be used in the same application.

## 6.3 Organizing Minimal APIs

Use route groups to share prefixes and endpoint conventions:

``` csharp
var orders = app.MapGroup("/api/orders")
    .RequireAuthorization();

orders.MapGet("/{id:int}", GetOrderAsync);
orders.MapPost("/", CreateOrderAsync);
```

The handler methods shown here would be defined separately. Route groups
can also help apply shared authorization, metadata, and endpoint
filters.

## 6.4 What `[ApiController]` provides

Common behaviors include:

-   Automatic 400 responses when model-state validation fails.
-   Binding-source inference for action parameters.
-   API-oriented conventions for binding and validation errors.

It does not mean that every possible exception or error automatically
becomes a standardized `ProblemDetails` response. Configure API error
behavior and global exception handling deliberately.

## 6.5 Interview questions

**Q1. When would you choose Minimal APIs vs. controllers?**\
Choose based on the application's conventions, endpoint complexity, team
preferences, and required features. Minimal APIs reduce ceremony;
controllers provide MVC-specific organization and extensibility.

**Q2. Are Minimal APIs faster?**\
They can have lower framework overhead in some benchmarks, but the real
difference depends on the workload. Measure the application rather than
assuming a meaningful performance improvement.

**Q3. What does `[ApiController]` provide?**\
Automatic model-state validation responses, binding-source inference,
and API-oriented conventions.

**Q4. How do you organize a large Minimal API?**\
Use route groups, feature-specific mapping methods, clear service
boundaries, and shared endpoint conventions.

**Q5. Do MVC filters work on Minimal APIs?**\
No. Minimal APIs use endpoint filters for endpoint-level cross-cutting
behavior.

**Q6. Can an application use both approaches?**\
Yes. They share the hosting environment, DI container, middleware
pipeline, and routing infrastructure.

------------------------------------------------------------------------

# 7. Senior-Level Scenario Questions

Try answering each question aloud in two to three minutes. Explain how
you would diagnose the issue, not only the likely cause.

## Scenario 1: The request is slow, but the endpoint code appears fast. Where do you look?

**Approach:**

1.  Measure total request duration and break it down by dependency.
2.  Check structured logs and distributed traces using a
    trace/correlation ID.
3.  Inspect database query duration, query plans, connection-pool waits,
    and downstream HTTP calls.
4.  Look for synchronous blocking such as `.Result` or `.Wait()` in
    asynchronous request paths.
5.  Check middleware, serialization, locks, thread-pool starvation, and
    excessive retries.
6.  Reproduce under realistic load and compare measurements before
    changing code.

Do not assume middleware or `HttpClient` is the cause without evidence.

## Scenario 2: Configuration changed, but the service still uses the old value. Why?

**Approach:**

-   Check whether the configuration provider supports reload and whether
    reload is enabled.
-   Determine whether the consumer uses `IOptions<T>`,
    `IOptionsSnapshot<T>`, or `IOptionsMonitor<T>`.
-   Remember that `IOptions<T>` does not provide automatic per-request
    refresh semantics.
-   Use `IOptionsMonitor<T>` for singleton consumers that need current
    values, where the source supports reload.
-   Check whether the application actually received the new
    configuration value.

## Scenario 3: Data from one user appears in another user's response. What do you investigate?

**Approach:**

-   Look for static fields or singleton services holding
    request-specific mutable state.
-   Check for singleton services capturing scoped dependencies.
-   Verify cache keys include all necessary tenant/user/authorization
    dimensions.
-   Inspect authorization and tenant-isolation logic.
-   Check whether objects are reused or mutated across concurrent
    requests.
-   Treat this as a potential security incident; contain and investigate
    before assuming it is only a lifetime bug.

## Scenario 4: How do you make API errors consistent and safe?

**Approach:**

-   Centralize unexpected-exception handling with middleware.
-   Use a consistent error format such as Problem Details.
-   Map known application exceptions intentionally to appropriate HTTP
    statuses.
-   Log diagnostics server-side with trace/correlation identifiers.
-   Never expose stack traces, SQL, secrets, or internal exception
    messages to clients.
-   Add tests for expected error responses and unexpected failures.

## Scenario 5: A Kubernetes service is healthy, but pods keep restarting during a database outage. Why?

**Likely cause:** The liveness probe may depend on the database. When
the database is unavailable, liveness fails and the orchestrator
restarts otherwise functioning processes.

**Better design:** Keep liveness focused on whether the process should
continue running. Use readiness to indicate whether the instance can
serve traffic, including required dependencies where appropriate. Set
sensible timeouts and thresholds.

## Scenario 6: A singleton needs a scoped repository. What do you do?

Do not inject the scoped repository directly into the singleton
constructor. Inject `IServiceScopeFactory`, create a scope for each unit
of work, resolve the repository inside it, and dispose the scope
afterward. Consider whether the design should instead move the work into
a scoped service.

## Scenario 7: A downstream service is failing, and retries make the outage worse. What do you change?

-   Add or review bounded timeouts.
-   Retry only transient failures and operations that are safe to
    repeat.
-   Use exponential backoff and jitter where appropriate.
-   Add a circuit breaker or other resilience behavior.
-   Limit total attempts and total elapsed time.
-   Avoid duplicate retry layers.
-   Use idempotency protection for operations that can create side
    effects.
-   Log attempts and outcomes without leaking sensitive payloads.

## Scenario 8: The API returns 500 for a missing order. How do you improve it?

Represent the expected absence explicitly in the application flow and
return 404 at the API boundary. Avoid relying on a generic exception for
ordinary control flow unless the application's design intentionally uses
a typed exception. Test both the missing-resource case and unexpected
server failures.

## Scenario 9: Would you migrate every controller to Minimal APIs for performance?

No. First measure a meaningful bottleneck. Both approaches support
production APIs. Consider maintainability, team familiarity, validation,
filters, organization, testing, and the application's actual latency
profile. Migrate only when there is a clear benefit.

## Scenario 10: A background worker stops processing after one failed item. What do you inspect?

-   Whether an exception escaped the worker's execution loop.
-   Whether the host's background-service exception behavior causes the
    host to stop.
-   Whether cancellation is being handled correctly.
-   Whether transient failures should be retried with limits.
-   Whether poison messages need dead-letter handling.
-   Whether failures are logged and observable.
-   Whether scopes and resources are disposed for every unit of work.

------------------------------------------------------------------------

# 8. Cheat Sheet

-   **Pipeline:** Kestrel → middleware in configured order → selected
    endpoint → response unwinds through middleware.
-   **`Use` / `Run` / `Map`:** continue / terminal / path branch.
-   **Ordering:** authentication before authorization; exception
    handling early enough to cover downstream components.
-   **Middleware DI:** conventional middleware usually receives scoped
    dependencies in `InvokeAsync`; `IMiddleware` has different
    activation behavior.
-   **Lifetimes:** transient per resolution, scoped per scope, singleton
    per application provider.
-   **Captive dependency:** a long-lived service retains a shorter-lived
    dependency beyond its intended lifetime.
-   **`DbContext`:** usually scoped in web apps; not thread-safe.
-   **Background workers:** create a scope for each unit of work when
    scoped dependencies are needed.
-   **Configuration:** default providers commonly use JSON,
    environment-specific JSON, user secrets in Development, environment
    variables, then command-line arguments; later providers override
    earlier ones.
-   **Options:** `IOptions<T>` cached value, `IOptionsSnapshot<T>`
    scoped snapshot, `IOptionsMonitor<T>` current values/change
    notifications when supported by the source.
-   **Validation:** use Options validation and `ValidateOnStart()` for
    early failure.
-   **Filters:** MVC has authorization, resource, action, exception, and
    result filters; Minimal APIs use endpoint filters.
-   **Global errors:** use exception middleware and a consistent error
    contract such as Problem Details.
-   **HTTP clients:** use `IHttpClientFactory` or another deliberate
    connection-lifetime strategy; configure timeouts and safe resilience
    policies.
-   **Retries:** consider idempotency, backoff, jitter, total time, and
    duplicate side effects.
-   **Health checks:** liveness asks whether the process should run;
    readiness asks whether it should receive traffic.
-   **Minimal APIs vs. controllers:** choose based on features,
    conventions, maintainability, and measurements---not assumptions
    alone.
-   **Production troubleshooting:** measure first, trace dependencies,
    inspect logs, and verify the fix under realistic conditions.

------------------------------------------------------------------------

# 9. 60-Second Interview Answer: "Walk Me Through the ASP.NET Core Request Pipeline"

"An HTTP request is received by Kestrel, often behind a reverse proxy or
load balancer. It passes through the middleware pipeline in the order
configured in `Program.cs`. Middleware can inspect or modify the
request, call the next component, and then inspect or modify the
response as control returns. Routing selects the endpoint, and
authentication and authorization determine whether the caller can access
it. The selected endpoint may be a controller action or a Minimal API
handler, with its dependencies provided through dependency injection.
Middleware ordering matters: for example, authentication should run
before authorization, and exception handling should cover the components
whose failures it needs to handle. In production, I would also use
structured logging, tracing, health checks, safe error responses, and
appropriate timeouts for downstream calls."

------------------------------------------------------------------------

## Suggested Practice Checklist

-   [ ] Draw the request pipeline and explain the order.
-   [ ] Write one custom middleware component.
-   [ ] Demonstrate transient, scoped, and singleton lifetimes.
-   [ ] Reproduce and fix a captive dependency.
-   [ ] Use `IServiceScopeFactory` in a background worker.
-   [ ] Bind and validate a configuration section.
-   [ ] Compare all three Options interfaces in a running app.
-   [ ] Implement global exception handling with a safe error response.
-   [ ] Register and call a typed HTTP client.
-   [ ] Add separate liveness and readiness endpoints.
-   [ ] Implement one endpoint using a Minimal API and another using a
    controller.
-   [ ] Answer the scenario questions aloud without reading the notes.
