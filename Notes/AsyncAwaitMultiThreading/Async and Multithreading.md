# Async and Multithreading in C#: Study Notes, Exercises and Interview Q&A

Target: mid-level .NET interviews | Plan: about 4-5 hours (trimmed) | Format: notes, then exercises, then interview answers

## How to Use This Document

| Part | Time | What to do |
| --- | --- | --- |
| Notes 1-2 | 1.5 hrs | Read, then code the exercises |
| Notes 3 | 45 min | Read, then code |
| Notes 4 | 1 hr | Read, reproduce the bugs on purpose |
| Notes 5 | 45 min | Read, build the pipeline |
| Interview Q&A | 45 min | Answer out loud before reading the answer |

Tip: keep one console project and add one method per exercise.

## Glossary: Read This First

Plain-language meanings of the terms used in this doc. Come back to this table whenever a word feels unclear.

| Term | Plain meaning |
| --- | --- |
| Thread | A single path of execution inside a program. A program with several threads can do several things at once, like several cooks working in one kitchen. |
| Process | A running program. It owns memory and contains one or more threads. |
| Thread pool | A ready-made team of threads that are reused for many jobs, so the program does not create (and pay for) a new thread each time. |
| Context switching | The CPU pausing one thread and running another. It has a cost, so too many threads makes a program slower, not faster. |
| CPU-bound | Work limited by the processor's speed: calculations, image processing, compression. More cores help. |
| I/O-bound | Work that mostly waits for something outside the CPU: a web request, a database, a file. Waiting needs no thread, which is why async helps. |
| Synchronous | Each step finishes before the next begins, and the calling thread waits. |
| Asynchronous | A step is started, the thread is released to do other work, and the program continues when the step finishes. |
| Blocking | A thread is stuck waiting and can do nothing else. `Thread.Sleep`, `.Result` and `.Wait()` block. |
| Concurrency | Several tasks in progress during the same period (they may take turns on one core). |
| Parallelism | Several tasks literally running at the same moment on different cores. |
| Task | An object that represents work that will finish later and may produce a result. It is a promise, not a thread. |
| Continuation | The code that runs after an awaited operation finishes, meaning "the rest of the method after the await". |
| State machine | The code the compiler generates from an async method. It remembers where the method paused so it can resume there. |
| Race condition | A bug where the result depends on which thread happens to run first. |
| Shared mutable state | Data that several threads can read and change, such as a counter. It is the source of most threading bugs. |
| Thread-safe | Code that stays correct when several threads use it at once. |
| Critical section | A piece of code that only one thread should run at a time, such as the lines that update a shared counter. |
| Atomic operation | An operation that happens as one indivisible step, so no other thread can see it half-done. |
| Lock / mutual exclusion | A rule that lets only one thread into a critical section while others wait their turn. |
| Deadlock | Two or more things each wait for the other, so none can ever continue. The program hangs. |
| Thread-pool starvation | All pool threads are blocked waiting, so new work cannot start. Requests hang or slow down even though the CPU is idle. |
| Sync-over-async | Calling async code and blocking on it with `.Result` or `.Wait()`. It causes deadlocks and starvation. |
| SynchronizationContext | A rule for where a continuation runs after an await. UI apps use it to return to the UI thread. |
| Cooperative cancellation | Cancelling by asking. The running code checks a token and chooses to stop, instead of being killed. |
| AggregateException | An exception that wraps several exceptions inside it. Blocking calls like `.Result` throw this, and `await` usually unwraps it. |
| Producer / consumer | One side creates work items (producer) and another side processes them (consumer), usually through a queue. |
| Backpressure | Slowing the producer down when the queue is full, so the consumer is not overwhelmed and memory does not fill up. |
| Degree of parallelism | How many operations are allowed to run at the same time, for example "at most 5". |
| Throttling | Deliberately limiting how many operations run at once, for example to avoid overloading an API. |
| Streaming | Handling items one at a time as they arrive, instead of waiting for the whole collection. |

---

## 1. Threading Foundations

### Key ideas

- **Thread**: an OS-level unit of execution. Each has its own stack (about 1 MB by default), and creating or switching between many threads is expensive.
- **ThreadPool**: a managed pool of reusable worker threads. You queue work, the pool runs it. Avoids the cost of creating threads per job. The pool starts with a minimum number of threads (roughly the CPU core count) and injects extra threads slowly when all are busy.
- **CPU-bound work**: the CPU is busy computing (image processing, hashing, big loops). Parallelism (more threads or cores) helps.
- **I/O-bound work**: waiting on something external (HTTP, database, disk). No thread needs to be blocked while waiting. This is what `async`/`await` is for.
- **Race condition**: result depends on timing of threads accessing shared mutable state.
- **Thread safety**: code that behaves correctly when called from multiple threads at once.

**Analogy:** A thread is a cook in a kitchen. Hiring and training a new cook for every single order is slow and expensive. A thread pool is a fixed team of cooks who take orders from a queue and reuse their skills for the next order. Put far more cooks in the kitchen than there are stoves (CPU cores) and they spend their time getting in each other's way. That is context switching.

### Thread vs ThreadPool vs Task

|  | Thread | ThreadPool | Task |
| --- | --- | --- | --- |
| What it is | OS thread you create | Shared pool of threads | Abstraction for an operation that completes later |
| Cost | High | Low (reused) | Low |
| Returns result | No (manual) | No | Yes (`Task<T>`) |
| Exceptions, cancellation, continuations | Manual | Manual | Built in |
| Use when | Long-running dedicated work, special priority/apartment state | Rarely used directly | Default choice |

### Example: race condition

```csharp
int counter = 0;
var t1 = new Thread(() => { for (int i = 0; i < 1_000_000; i++) counter++; });
var t2 = new Thread(() => { for (int i = 0; i < 1_000_000; i++) counter++; });
t1.Start(); t2.Start();
t1.Join(); t2.Join();
Console.WriteLine(counter); // expected 2,000,000, but usually less
```

`counter++` is three steps (read, add, write). Two threads can read the same value and both write back the same result, losing an increment.

---

## 2. Task and async/await

### Core concepts

- A `Task` represents an operation that will complete in the future. **It is not a thread.**
- `async` marks a method that can use `await`. `await` pauses the method **without blocking the thread** and returns control to the caller. When the awaited task finishes, the rest of the method continues.
- For I/O (HttpClient, EF Core, file streams), there is **no thread waiting** during the operation. The OS signals completion and a thread picks up the continuation.
- `Task.Run` queues CPU-bound work to the thread pool. Do not wrap I/O in `Task.Run`.

**Analogy:** A waiter takes your order and passes it to the kitchen. Instead of standing at the kitchen door until the food is ready, the waiter serves other tables, and comes back when the dish is done. `await` is the waiter walking away to do other work. Blocking with `.Result` is the waiter standing at the kitchen door doing nothing, so the restaurant needs many more waiters to serve the same number of guests.

### Return types

- `Task`: async method with no result.
- `Task<T>`: async method returning `T`.
- `ValueTask<T>`: struct alternative that avoids allocation when the result is often available synchronously (for example a cache hit). Rules: await it once, do not await it multiple times, do not use `.Result` before completion. Default to `Task<T>` unless profiling shows allocation pressure.
- `async void`: only for event handlers. Exceptions cannot be caught by the caller and can crash the process. Callers cannot await it.

### Basic example

```csharp
public async Task<string> GetPageAsync(string url)
{
    using var client = new HttpClient();
    string html = await client.GetStringAsync(url); // thread is freed here
    return html.Substring(0, 100);                   // resumes when done
}
```

### Exception handling

```csharp
try
{
    await DoWorkAsync();
}
catch (HttpRequestException ex)
{
    // exceptions from awaited tasks are rethrown at the await point
}
```

- With `await`, you get the original exception.
- With `.Result` or `.Wait()`, exceptions are wrapped in `AggregateException`.
- An un-awaited ("fire and forget") task swallows its exception silently. Avoid it, or handle exceptions inside it.

### Internals: the state machine

The compiler rewrites every `async` method into a **state machine** (a struct implementing `IAsyncStateMachine`):

1. The method body is split at each `await` into numbered states.
2. When execution reaches an `await` on an incomplete task, the machine saves local variables into its fields, registers a **continuation** on the task, and returns to the caller.
3. When the task completes, the continuation calls `MoveNext()`, which jumps to the saved state and continues.
4. If the awaited task is already complete, execution continues synchronously with no suspension.

To see it: paste a small async method into **sharplab.io** and view the decompiled C#.

### Does async create a new thread?

No. `async` itself creates no thread. The method runs synchronously on the caller's thread until the first `await` of an incomplete task. After that, the continuation usually runs on a thread-pool thread (in ASP.NET Core) or on the captured context (UI apps).

---

## 3. Composition, Cancellation and ConfigureAwait

### Task.WhenAll

Waits for all tasks to finish. Use it to run independent I/O calls concurrently.

```csharp
var t1 = GetUserAsync(1);
var t2 = GetOrdersAsync(1);
var t3 = GetPricesAsync();
await Task.WhenAll(t1, t2, t3);
var user = t1.Result; // safe: already completed (or use await t1)
```

- If several tasks fail, `await` rethrows only the **first** exception. To see all, inspect `whenAllTask.Exception.InnerExceptions`.
- Start all tasks first, then await. Awaiting each in sequence runs them one after another.

### Task.WhenAny

Completes when the **first** task completes. Common uses: timeouts, redundancy (fastest of several sources).

```csharp
var work = LongOperationAsync();
var finished = await Task.WhenAny(work, Task.Delay(TimeSpan.FromSeconds(2)));
if (finished != work) throw new TimeoutException();
await work; // propagate result or exception
```

Note: this does not cancel `work`. It keeps running. Pass a `CancellationToken` to actually stop it.

### CancellationToken (cooperative cancellation)

- `CancellationTokenSource` creates and triggers cancellation. `CancellationToken` is passed to the work.
- Cancellation is **cooperative**: the work must check the token.

```csharp
using var cts = new CancellationTokenSource();
cts.CancelAfter(TimeSpan.FromSeconds(2));

try
{
    await LongLoopAsync(cts.Token);
}
catch (OperationCanceledException)
{
    Console.WriteLine("Cancelled");
}

async Task LongLoopAsync(CancellationToken ct)
{
    for (int i = 0; i < 100; i++)
    {
        ct.ThrowIfCancellationRequested();
        await Task.Delay(100, ct); // pass token down
    }
}
```

Best practices:

- Accept a `CancellationToken` as the last parameter of async methods and pass it down the whole chain.
- In ASP.NET Core, controller actions can accept a `CancellationToken` that fires when the client disconnects.
- Use `CancellationTokenSource.CreateLinkedTokenSource` to combine tokens (for example a request token plus a timeout).
- Dispose `CancellationTokenSource`.

### SynchronizationContext and ConfigureAwait

- A `SynchronizationContext` decides **where** a continuation runs after `await`. UI apps (WinForms, WPF) have one that returns to the UI thread. Legacy ASP.NET (.NET Framework) had one too.
- `await task.ConfigureAwait(false)` says: do not resume on the captured context, any thread is fine.
- **ASP.NET Core has no SynchronizationContext**, so `ConfigureAwait(false)` makes no practical difference there.
- Use it in **library code** (which may be used by UI or legacy apps) to avoid deadlocks and gain a little performance. In UI code, do **not** use it where you update controls afterwards.

---

## 4. Deadlocks and Synchronization

### The classic deadlock

```csharp
// Runs on a UI thread or legacy ASP.NET request thread
public string GetData()
{
    return GetDataAsync().Result; // blocks the context thread
}

async Task<string> GetDataAsync()
{
    await Task.Delay(100);  // continuation wants to resume on the captured context
    return "data";
}
```

Why it deadlocks:

1. The caller blocks the context thread waiting for the task.
2. The task finishes its delay and tries to resume on that same context thread.
3. That thread is blocked, so the continuation never runs. Each waits for the other.

**Analogy:** You lock yourself in a room and refuse to leave until a package arrives. The courier is outside, but needs you to open the door to hand it over. You wait for the courier, the courier waits for you, and nobody moves.

Fixes:

- **Async all the way**: make callers async and `await` instead of `.Result`/`.Wait()`/`.GetAwaiter().GetResult()`.
- `ConfigureAwait(false)` in library code (a mitigation, not the primary fix).

ASP.NET Core has no context, so this exact deadlock does not occur there, but blocking still causes **thread-pool starvation** (see Q&A).

**Thread-pool starvation in one picture:** imagine a pool of 10 threads and 10 requests that each call `.Result` on an async operation. All 10 threads are now blocked waiting. The async operations need a free pool thread to finish, but there are none, so they wait for the very threads that are waiting for them. New requests queue up while the CPU sits almost idle. The pool slowly adds extra threads, so the app recovers slowly, if at all.

### lock and Monitor

```csharp
private readonly object _gate = new();
private int _counter;

public void Increment()
{
    lock (_gate)
    {
        _counter++;
    }
}
```

- `lock` compiles to `Monitor.Enter`/`Monitor.Exit` in a try/finally.
- Lock on a private `object` field. Never `lock(this)`, `lock(typeof(X))`, or lock on a string.
- **You cannot `await` inside a `lock`** (compile error). Use `SemaphoreSlim` instead.

### SemaphoreSlim

Limits how many callers can enter a section at once. `SemaphoreSlim(1,1)` acts as an async-friendly lock.

**Analogy:** A restaurant with 3 tables. The first 3 groups sit down immediately. Group 4 waits at the door until a table frees up. `WaitAsync` is waiting at the door, and `Release` is a table becoming free. You must always release the table in `finally`, otherwise a crashed group keeps its table forever.

```csharp
var throttle = new SemaphoreSlim(3); // max 3 concurrent

async Task CallApiAsync(int id)
{
    await throttle.WaitAsync();
    try
    {
        await Task.Delay(500); // simulate API call
        Console.WriteLine($"Done {id}");
    }
    finally
    {
        throttle.Release(); // always release in finally
    }
}

await Task.WhenAll(Enumerable.Range(1, 20).Select(CallApiAsync));
```

### Interlocked

Atomic operations without a lock: `Interlocked.Increment(ref x)`, `Decrement`, `Add`, `Exchange`, `CompareExchange`. Faster than `lock` for simple counters.

```csharp
Interlocked.Increment(ref counter);
```

### volatile

- Guarantees **visibility**: reads always see the latest written value, and the compiler or CPU will not cache or reorder it in ways that hide updates.
- Does **not** guarantee atomicity. `volatile int count; count++` is still a race.
- Typical use: a simple stop flag (`volatile bool _stop`). In most code prefer `lock`, `Interlocked`, or `CancellationToken`.

### Choosing a tool

| Need | Use |
| --- | --- |
| Protect a short synchronous critical section | `lock` |
| Protect a section containing `await`, or limit concurrency | `SemaphoreSlim` |
| Atomic counter or flag | `Interlocked` |
| Cross-process exclusivity | `Mutex` |
| Thread-safe dictionary or queue | `Concurrent*` collections |
| Signal to stop | `CancellationToken` |

---

## 5. Parallelism and Pipelines

### Parallel.For / Parallel.ForEach

For **CPU-bound** data parallelism: splits a collection across threads.

```csharp
Parallel.ForEach(files, new ParallelOptions { MaxDegreeOfParallelism = 4 },
    file => ProcessImage(file));
```

- Blocks the calling thread until complete.
- Not suited to async work. Do not use `async` lambdas inside `Parallel.ForEach` (it does not await them).
- For async work with a degree limit, use **`Parallel.ForEachAsync`** (.NET 6+):

```csharp
await Parallel.ForEachAsync(urls,
    new ParallelOptions { MaxDegreeOfParallelism = 5, CancellationToken = ct },
    async (url, token) => await DownloadAsync(url, token));
```

### Parallel.ForEach vs Task.WhenAll

- `Parallel.ForEach`: CPU-bound, synchronous work, controlled degree of parallelism.
- `Task.WhenAll`: I/O-bound async work, many concurrent operations without blocking threads.

### Channel\<T>

An async, thread-safe producer/consumer queue (`System.Threading.Channels`).

**Analogy:** A conveyor belt in a packing warehouse. Producers put boxes on one end and consumers take them off the other. A bounded belt holds at most 10 boxes, so when it is full the producers must wait. That waiting is backpressure. An unbounded belt never stops the producers, so boxes can pile up until the warehouse runs out of space.

```csharp
var channel = Channel.CreateBounded<int>(10); // backpressure at 10 items

var producer = Task.Run(async () =>
{
    for (int i = 1; i <= 100; i++)
        await channel.Writer.WriteAsync(i); // waits when full
    channel.Writer.Complete();
});

var consumers = Enumerable.Range(1, 3).Select(id => Task.Run(async () =>
{
    await foreach (var item in channel.Reader.ReadAllAsync())
        Console.WriteLine($"Consumer {id} processed {item}");
})).ToArray();

await Task.WhenAll(consumers.Append(producer));
```

- **Bounded**: limits memory and applies backpressure (producer waits when full).
- **Unbounded**: never blocks the producer, but memory can grow without limit.
- Always call `Writer.Complete()` so consumers can finish.
- Compared to `BlockingCollection`: `Channel<T>` is fully async and does not block threads.

### IAsyncEnumerable\<T> and await foreach

Streams items asynchronously as they become available, instead of waiting for the full list.

```csharp
async IAsyncEnumerable<int> GenerateAsync(
    [EnumeratorCancellation] CancellationToken ct = default)
{
    for (int i = 0; i < 5; i++)
    {
        await Task.Delay(500, ct);
        yield return i;
    }
}

await foreach (var n in GenerateAsync(cts.Token))
    Console.WriteLine(n);
```

- `[EnumeratorCancellation]` lets the token passed via `.WithCancellation(token)` flow into the method.
- Typical uses: streaming database rows (EF Core `AsAsyncEnumerable()`), paged APIs, large file processing, ASP.NET Core endpoints that return `IAsyncEnumerable<T>`.

---

## 6. Practice Exercises

Code each one yourself. Expected outcomes are in brackets.

### Block 1: Threads

1. Run the two-thread counter 5 times. \[Result differs and is below 2,000,000\]
2. Fix it with `lock`, then with `Interlocked.Increment`. Time both with `Stopwatch`. \[Interlocked is typically faster\]

### Block 2: async/await

3. Write `DownloadAsync(string url)` using `HttpClient`. Download 5 URLs sequentially, then with `Task.WhenAll`. Compare times. \[Parallel is about the slowest single request\]
4. Write an `async void` method that throws and call it inside a try/catch. Observe it is not caught. Change it to `async Task` and compare.

### Block 3: Composition and cancellation

5. Write `WithTimeout<T>(Task<T> task, TimeSpan timeout)` using `Task.WhenAny`. Throw `TimeoutException` if the delay wins.
6. Call a slow endpoint (for example `https://httpbin.org/delay/5`) with `CancellationTokenSource.CancelAfter(2s)`. Catch `OperationCanceledException`.

### Block 4: Deadlocks and synchronization

7. Throttle 20 fake API calls (`Task.Delay(500)`) to 3 at a time using `SemaphoreSlim`. Print timestamps to confirm batches of 3.
8. Call `GetDataAsync().Result` inside 200 parallel tasks and watch throughput drop (thread-pool starvation). Fix it with `await`.

### Block 5: Parallelism and pipelines

9. Build the producer/consumer pipeline: 1 producer, 3 consumers, bounded channel of 10. Log when the producer waits because the channel is full.
10. Write an `IAsyncEnumerable<int>` that yields a value every 500 ms. Consume it with `await foreach` and cancel after 2 seconds using `.WithCancellation(token)`.

---

## 7. Interview Questions and Model Answers

Try answering each out loud first.

### Fundamentals

**Q1. What is the difference between Task and Thread?** A `Thread` is an OS-level execution unit that you create and manage. A `Task` is a higher-level abstraction representing an operation that completes in the future. Tasks usually run on thread-pool threads, support return values, exception propagation, continuations, and cancellation, and I/O tasks do not occupy a thread at all while waiting.

**Q2. Does async/await create a new thread?** No. `async` does not create threads. The method runs on the caller's thread until it hits an `await` on an incomplete task, then returns. The continuation later runs on whichever thread is available (thread pool in ASP.NET Core, or the UI thread in UI apps). Only `Task.Run` explicitly queues work to the thread pool.

**Q3. What happens when you await a task?** If the task is already complete, execution continues synchronously. Otherwise the method's state is saved in a compiler-generated state machine, a continuation is registered, and control returns to the caller. When the task completes, the continuation resumes via `MoveNext()` from the saved point.

**Q4. CPU-bound vs I/O-bound, and how do you handle each?** I/O-bound work waits on external resources, so use true async APIs with `await` so no thread is blocked. CPU-bound work needs computation, so use `Task.Run` (in desktop apps), `Parallel.ForEach`, or PLINQ to spread it across cores. In ASP.NET Core you usually should not use `Task.Run` for request work, because it just moves work to another pool thread and adds overhead.

**Q5. What is wrong with async void?** Exceptions cannot be caught by the caller, and it can crash the process. The caller cannot await it or know when it completes, and it is hard to test. Use it only for event handlers. Otherwise return `Task`.

**Q6. Task vs ValueTask?** `ValueTask<T>` is a struct that avoids heap allocation when the result is often available synchronously (cache hits, buffered reads). Restrictions: await only once, do not await concurrently, do not read the result before completion. Use `Task<T>` by default and `ValueTask<T>` in hot paths where measurement shows allocation cost.

### Composition and cancellation

**Q8. Task.WhenAll vs Task.WhenAny?** `WhenAll` completes when all tasks finish, used for parallel independent work. `WhenAny` completes when the first finishes, used for timeouts or taking the fastest result. With `WhenAll`, if multiple tasks fail, `await` throws the first exception, and all are available on the task's `Exception.InnerExceptions`. `WhenAny` does not cancel the other tasks.

**Q9. How do you implement cancellation?** Create a `CancellationTokenSource`, pass its `Token` down through async methods, and have the work either pass it to cancellable APIs or call `ThrowIfCancellationRequested()`. Callers catch `OperationCanceledException`. Use `CancelAfter` for timeouts and linked tokens to combine sources. In ASP.NET Core, accept a `CancellationToken` in actions to handle client disconnects.

**Q10. What is ConfigureAwait(false) and when do you use it?** It tells the awaiter not to resume on the captured `SynchronizationContext`. It avoids deadlocks and small overhead in library code that may run under a UI or legacy ASP.NET context. ASP.NET Core has no `SynchronizationContext`, so it has no practical effect in an ASP.NET Core application, but library authors still use it. Do not use it where you must return to the UI thread.

### Deadlocks and synchronization

**Q12. How does a deadlock occur with .Result or .Wait()?** In an environment with a `SynchronizationContext` (UI, legacy ASP.NET), blocking on a task with `.Result` blocks the context thread. The task's continuation needs that same thread to resume, so neither can proceed. Fix: use async all the way up; in libraries use `ConfigureAwait(false)`.

**Q13. What is thread-pool starvation and how do you fix it?** Blocking calls like `.Result` or `.Wait()` tie up thread-pool threads. When all threads are blocked, new work (including the continuations that would unblock them) waits, and the pool injects threads slowly, so requests hang or slow down. Fix: async all the way, avoid blocking calls, and do not use `Task.Run` to wrap blocking I/O.

**Q14. lock vs SemaphoreSlim vs Mutex?** `lock` (Monitor) gives exclusive access in a synchronous section, within a process, and cannot contain `await`. `SemaphoreSlim` allows N concurrent entries and supports `WaitAsync`, so it works with `await`. `Mutex` is an OS-level primitive that works across processes and is slower.

**Q15. Why can't you await inside a lock?** A lock is owned by a thread. After an `await` the continuation may run on a different thread, which would not own the lock, so the compiler forbids it. Use `SemaphoreSlim.WaitAsync` instead.

**Q16. What does volatile do, and why isn't it enough for a counter?** `volatile` ensures reads and writes are visible across threads and are not cached or reordered. It does not make operations atomic, and `count++` is read-modify-write, so concurrent increments can still be lost. Use `Interlocked.Increment` or `lock`.

**Q19. How would you throttle 1,000 API calls to 10 at a time?** Options: `SemaphoreSlim(10)` with `WaitAsync`/`Release` in try/finally around each call and `Task.WhenAll` on all tasks; or `Parallel.ForEachAsync` with `MaxDegreeOfParallelism = 10`; or a bounded `Channel<T>` with 10 consumers. `Parallel.ForEachAsync` is the simplest on .NET 6+.

### Parallelism and pipelines

**Q20. Parallel.ForEach vs Task.WhenAll?** `Parallel.ForEach` is for CPU-bound synchronous work, blocks the calling thread, and controls the degree of parallelism. `Task.WhenAll` is for I/O-bound async work, does not block threads, and awaits many operations concurrently. For async work with a concurrency limit use `Parallel.ForEachAsync`.

**Q22. What is Channel\<T> and when do you use it?** A thread-safe, async producer/consumer queue. Producers use `Writer.WriteAsync`, consumers use `Reader.ReadAllAsync`. Bounded channels provide backpressure. Use it for pipelines, background processing, and decoupling producers from consumers. It is the modern, async-friendly alternative to `BlockingCollection`.

**Q23. What is IAsyncEnumerable and where have you used it?** It represents an asynchronous stream consumed with `await foreach`. Items are produced and processed as they arrive, avoiding loading everything into memory. Examples: streaming DB rows, paged API results, large files. Use `[EnumeratorCancellation]` so cancellation tokens flow in.

### Scenario questions

**Q24. Your API endpoint is slow and calls three downstream services one after another. How do you improve it?** If the calls are independent, start all three and `await Task.WhenAll`. Pass the cancellation token and add timeouts and retries (Polly). Handle partial failures explicitly.

**Q25. Two requests update the same in-memory cache entry at once. What can go wrong?** Lost updates or duplicate work from a race. Use `ConcurrentDictionary` with `AddOrUpdate`/`GetOrAdd`, `Lazy<T>` for single initialization, or a `SemaphoreSlim` around the load. For multiple instances, use a distributed lock or cache like Redis.

**Q26. A background job needs to process messages without overwhelming the database. Design it.** Use a `BackgroundService` reading from a bounded `Channel<T>` (or a queue), with a fixed number of consumer tasks, cancellation via the stopping token, and retry handling. The bound provides backpressure.

**Q27. A request hangs under load but CPU is low. What do you suspect?** Thread-pool starvation from blocking calls (`.Result`, `.Wait()`, sync-over-async) or a deadlock. Look for blocking calls, check thread counts and pool queue length (`dotnet-counters`), and capture a dump (`dotnet-dump`) to inspect stacks. Fix with async all the way.

---

## 8. Quick Revision Cheat Sheet

- Task is a promise, not a thread. `async` does not create threads.
- I/O-bound: `await`. CPU-bound: `Task.Run` or `Parallel`.
- Never `async void` (except event handlers). Never `.Result`/`.Wait()` on async code.
- Async all the way. Pass `CancellationToken` through every layer.
- `WhenAll` for parallel I/O, `WhenAny` for timeouts, but `WhenAny` does not cancel the loser.
- `ConfigureAwait(false)` in libraries. Irrelevant in ASP.NET Core apps.
- `lock`: sync only, no `await`. `SemaphoreSlim`: async-friendly, limits concurrency. `Interlocked`: atomic counters. `volatile`: visibility, not atomicity. `Mutex`: cross-process.
- Always `Release()` the semaphore in `finally`.
- `Parallel.ForEach`: CPU-bound. `Parallel.ForEachAsync`: async with a limit. PLINQ: measure first.
- `Channel<T>`: bounded for backpressure, always call `Complete()`.
- `IAsyncEnumerable`: streaming with `await foreach`, use `[EnumeratorCancellation]`.
- Thread-pool starvation = blocking calls exhaust the pool. Fix: async all the way.

### 60-second answer: "Explain async/await"

Async/await lets a method wait for I/O without blocking a thread. The compiler turns the method into a state machine. At an `await` on an incomplete task, the method saves its state, returns to the caller, and registers a continuation. When the operation finishes, the continuation resumes the method from where it stopped. It does not create threads by itself, which is why it scales well for I/O-heavy workloads like web APIs.

---

## 9. Senior Add-On (for 5+ years of experience)

At senior level, interviewers ask less "what is X" and more "what would you do when Y goes wrong". Read this after finishing Parts 1-8. Expected time: 30-40 minutes.

### 9.1 Diagnosing production problems

**Typical symptom:** requests hang or get slow under load, but CPU usage is low. That pattern usually means threads are waiting (blocked) rather than working.

Step-by-step approach:

1. **Define the symptom.** Hang, slow, high CPU, or high memory? Which endpoints? Since which deployment?
2. **Low CPU with slow requests** points to blocking, starvation, a deadlock, or a slow downstream dependency (database, HTTP service, connection pool exhausted).
3. **Watch the thread pool live** with `dotnet-counters monitor`. Look at *ThreadPool Thread Count*, *ThreadPool Queue Length* and *Work Items Completed*. A growing queue with a rising thread count means starvation.
4. **Capture a dump** with `dotnet-dump collect`, then `dotnet-dump analyze` and use `threads`, `clrstack` and `dumpasync`. Many threads parked in `Task.Wait`, `GetResult` or `.Result` confirm sync-over-async.
5. **For high CPU** use `dotnet-trace` to find the hot methods.
6. **Check the dependencies:** database connection pool exhaustion, `HttpClient` timeouts, and a downstream service that has become slow.
7. **Fix the cause:** async all the way, add timeouts and cancellation, limit concurrency, and use `IHttpClientFactory`. `ThreadPool.SetMinThreads` is only a temporary stopgap, not a fix.

### 9.2 Design scenarios with model answers

**Scenario A: call an external API 10,000 times without overloading it**

```csharp
var options = new ParallelOptions
{
    MaxDegreeOfParallelism = 10,   // throttle
    CancellationToken = ct         // allow shutdown/timeout
};

await Parallel.ForEachAsync(ids, options, async (id, token) =>
{
    // retry + circuit breaker via Polly / HttpClient resilience handler
    var result = await client.GetAsync($"/items/{id}", token);
    result.EnsureSuccessStatusCode();
});
```

Points to mention: a concurrency limit, per-request timeouts, retry with exponential backoff and jitter, a circuit breaker, honoring `429 Too Many Requests` and `Retry-After`, idempotency of the calls, and collecting failures for a retry queue instead of failing everything.

**Scenario B: background job processor**

```csharp
public class JobWorker : BackgroundService
{
    private readonly Channel<Job> _channel = Channel.CreateBounded<Job>(100);

    public ValueTask EnqueueAsync(Job job) => _channel.Writer.WriteAsync(job);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var consumers = Enumerable.Range(0, 4).Select(_ => Task.Run(async () =>
        {
            await foreach (var job in _channel.Reader.ReadAllAsync(stoppingToken))
            {
                try { await ProcessAsync(job, stoppingToken); }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // log, retry, or send to dead-letter store
                }
            }
        }, stoppingToken));

        await Task.WhenAll(consumers);
    }
}
```

Points to mention: the bounded channel gives backpressure, a fixed number of consumers limits load on the database, the `stoppingToken` gives graceful shutdown, one failed job must not kill the worker, and for durability across restarts a channel is not enough (use a persistent queue such as Service Bus, RabbitMQ or an outbox table).

**Scenario C: rate limiter**

- .NET 7+ has `System.Threading.RateLimiting` with four algorithms: **fixed window**, **sliding window**, **token bucket**, and **concurrency limiter**. ASP.NET Core has built-in rate limiting middleware (`AddRateLimiter`).
- Fixed window is simple but allows bursts at window edges. Sliding window smooths that. Token bucket allows controlled bursts. Concurrency limiter caps in-flight requests.
- In-memory limiters are per instance. With several instances behind a load balancer you need a shared store such as Redis, or a gateway-level limit.

### 9.3 Common pitfalls interviewers probe

| Pitfall | Why it is a problem | Better approach |
| --- | --- | --- |
| Fire-and-forget (`_ = DoWorkAsync();`) | Exceptions are lost, work may be cut off at shutdown, request scope may be disposed | Use a `BackgroundService` and queue, or at least catch and log inside the task |
| `Task.Run` around async or I/O code in ASP.NET Core | Extra thread hop and overhead, no benefit | Call the async API directly with `await` |
| `.Result`, `.Wait()`, `.GetAwaiter().GetResult()` | Deadlocks and thread-pool starvation | Async all the way |
| `new HttpClient()` per request | Socket exhaustion and DNS issues | `IHttpClientFactory` or a shared instance |
| Not passing `CancellationToken` | Work continues after client disconnects or shutdown | Accept and forward the token through every layer |
| `lock` around code that awaits | Does not compile, and holding locks across awaits is a bug pattern | `SemaphoreSlim.WaitAsync` |
| Releasing `SemaphoreSlim` outside `finally` | A thrown exception leaks the permit and the app slowly freezes | Always `Release()` in `finally` |
| `async void` outside event handlers | Unhandled exceptions can crash the process | Return `Task` |
| Unbounded parallelism (`Task.WhenAll` on 10,000 tasks) | Overloads the target and your own memory | Throttle with `SemaphoreSlim` or `Parallel.ForEachAsync` |
| Sharing a `DbContext` across threads | `DbContext` is not thread-safe | One context per operation or scope |

### 9.4 Extra senior-level questions

**S1. Is fire-and-forget ever acceptable in ASP.NET Core?** Rarely. If the work must happen, queue it to a background worker (hosted service with a channel or a message broker). If it truly is best effort, wrap the body in try/catch and log failures, and do not touch request-scoped services such as `DbContext` because they are disposed when the request ends.

**S2. Why is Task.Run in a controller usually wrong?** The request is already running on a thread-pool thread. `Task.Run` queues the work to another pool thread, adding scheduling overhead and no extra capacity. For I/O use real async APIs. For heavy CPU work, consider moving it to a background worker so it does not compete with request handling.

**S3. How do you do async lazy initialization safely?** Use `Lazy<Task<T>>` (or `AsyncLazy<T>`) so the initialization runs once and every caller awaits the same task. A `SemaphoreSlim` with a double-check is the manual alternative.

**S4. What do you do if you must call async code from a synchronous method you cannot change?** First try to make the chain async. If impossible, isolate the blocking at one boundary, use `ConfigureAwait(false)` in the inner async code, never block inside a context that has a `SynchronizationContext`, and document it as technical debt. `GetAwaiter().GetResult()` is preferable to `.Result` because it does not wrap exceptions in `AggregateException`.

**S5. How do you make a long-running worker shut down cleanly?** Observe the `stoppingToken`, pass it into every awaited call, finish or requeue in-flight work, and let the host's shutdown timeout apply. Avoid `Thread.Abort` or ignoring the token.

**S6. How do you handle failures in `Task.WhenAll` where some tasks succeed?** Keep the task list, await `WhenAll` in try/catch, then inspect each task's `Status`, `Exception` and result individually. Alternatively catch exceptions inside each task and return a result object (success or error) so `WhenAll` itself never throws.

### 9.5 Your own project stories (prepare 2-3)

At 6+ years, interviewers expect real examples. Prepare each in this shape (about 60-90 seconds):

- **Situation:** what the system did and what was wrong or slow.
- **Problem:** the specific concurrency issue (sequential calls, race condition, no throttling, blocked threads, missing cancellation).
- **Action:** what you changed and which tool you used (`Task.WhenAll`, `SemaphoreSlim`, `Channel<T>`, `Interlocked`, `CancellationToken`) and why that one over the alternatives.
- **Result:** a measurable outcome (response time dropped from 6 s to 2 s, errors removed, load on the database reduced).
- **Trade-off or lesson:** what you would do differently, or what risk it introduced.

**Fill-in template**

| Story | Situation | Problem | Action | Result |
| --- | --- | --- | --- | --- |
| 1 |  |  |  |  |
| 2 |  |  |  |  |
| 3 |  |  |  |  |

If you have not hit one of these problems in real work, prepare the closest honest example (a code review you did, a bug you investigated, or a side project) and say clearly that it was that. Interviewers value honesty and clear reasoning more than a polished story that falls apart under follow-up questions.
