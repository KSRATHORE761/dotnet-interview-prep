using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Formats.Tar;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.VisualBasic;

internal class Program
{
    private static readonly HttpClient Http = new();
    private static int _counter;
    private static readonly object Gate = new();
    private static int _inFlight;
    private static async Task Main(string[] args)
    {
        string? choice = args.Length > 0 ? args[0] : null;
        if (choice is null)
        {
            Console.Write("Excercise number (1-10): ");
            choice = Console.ReadLine();
        }
        switch (choice)
        {
            case "1": EX1_RaceCondition(); break;
            case "2" : EX2_LockVsInterLocked(); break;
            case "3" : await EX3_SequentialVsWhenAll(); break;
            case "4" : await Ex4_AsyncVoid(); break;
            case "5" : await Ex5_WithTimeout(); break;
            case "6" : await EX6_CancelAfter(); break;
            case "7" : await EX7_SemaphoreThrottle(); break;
            case "8" : await Ex8_ThreadPoolStarvation(); break;
            case "9" : await EX9_ProducerConsumer(); break;
            case "10" : await Ex10_AsyncEnumerable(); break;
            default: Console.WriteLine("Choose a number from 1 to 10."); break;
        }
    }

    #region Block 1: Threads

    /// <summary>
    /// QUESTION - BLOCK 1: Threads
    /// 1. Run the two-thread counter 5 times. [Result differs and is below 2,000,000]
    /// 2. Fix it with lock, then with Interlocked.Increment. 
    /// 3. Time both with Stopwatch. [Interlocked is typically faster]
    /// </summary>
    private static void RunTwoThreads(Action work)
    {
        var t1 = new Thread(() => work());
        var t2 = new Thread(() => work());
        t1.Start();
        t2.Start();
        t1.Join();
        t2.Join();
    }

    // Exercise 1: two threads incrementing a shared counter without protection.
    // Expected: the result differs between runs and is usually below 2,000,00
    private static void EX1_RaceCondition()
    {
        for (int run = 1; run <= 5; run++)
        {
            _counter = 0;
            RunTwoThreads(() =>
            {
                for (int i = 0; i < 1_000_000; i++)
                {
                    _counter++;
                }
            });
            Console.WriteLine($"Run {run} : {_counter} (expected 2,000,000)");
        }
    }
    // Exercise 2: fix with lock, then with Interlocked, and time both.
    // Expected: both give exactly 2,000,000; Interlocked is typically faster.
    private static void EX2_LockVsInterLocked()
    {
        _counter = 0;
        Stopwatch sw = new Stopwatch();
        sw.Start();
        RunTwoThreads(() =>
        {
           for(int i = 0; i < 1000000; i++)
            {
                lock (Gate)
                {
                    _counter++;
                }
            } 
        });
        sw.Stop();
        Console.WriteLine($"Lock: {_counter:N0} in {sw.ElapsedMilliseconds} ms");

        //Interlocked
        _counter = 0;
        sw.Restart();
        RunTwoThreads(() =>
        {
            for(int i = 0; i < 1000000; i++)
            {
                Interlocked.Increment(ref _counter);
            }
        });
        sw.Stop();
        Console.WriteLine($"InterLock: {_counter:N0} in {sw.ElapsedMilliseconds} ms");
    }
    #endregion
    #region Block 2: async/await
    
    /// Write DownloadAsync(string url) using HttpClient. Download 5 URLs sequentially, then with Task.WhenAll. Compare times. [Parallel is about the slowest single request]
    /// Write an async void method that throws and call it inside a try/catch. Observe it is not caught. Change it to async Task and compare.
    
    private static readonly string[] Urls =
    {
        "https://example.com",
        "https://www.bing.com",
        "https://learn.microsoft.com",
        "https://github.com",
        "https://httpbin.org/delay/1",
    };
    private static async Task<int> DownloadAsync(string url)
    {
        string html = await Http.GetStringAsync(url);
        return html.Length;
    }

    // Exercise 3: sequential vs Task.WhenAll.
    // Expected: parallel time is about the slowest single request.
    private static async Task EX3_SequentialVsWhenAll()
    {
        Stopwatch sw  = new Stopwatch();
        sw.Start();
        foreach(var url in Urls)
        {
            await DownloadAsync(url);
        }
        sw.Stop();
        Console.WriteLine($"Sequential : {sw.ElapsedMilliseconds} ms");

        sw.Restart();
        await Task.WhenAll(Urls.Select(DownloadAsync));
        sw.Stop();

        Console.WriteLine($"WhenAll: {sw.ElapsedMilliseconds} ms");
    }
     private static async Task ThrowsTask()
    {
        await Task.Delay(50);
        throw new InvalidOperationException("boom (async Task)");
    }
 
    private static async void ThrowsVoid()
    {
        await Task.Delay(50);
        throw new InvalidOperationException("boom (async void)");
    }
 
    // Exercise 4: async void exceptions cannot be caught by the caller.
    // WARNING: the second part crashes the process on purpose. That is the lesson.
    private static async Task Ex4_AsyncVoid()
    {
        try
        {
            await ThrowsTask();
        }
        catch(Exception ex)
        {
            Console.WriteLine($"async Task: caught -> {ex.Message}");
        }

        AppDomain.CurrentDomain.UnhandledException +=(_,e)=> Console.WriteLine($"async void : NOT Caught by try/catch; unhandled -> {((Exception)e.ExceptionObject).Message}");

        try
        {
            ThrowsVoid();
            Console.WriteLine("async void: the try block finished without any error");
            await Task.Delay(1000); // the exception fires on pool thread meanwhile
        }
        catch(Exception)
        {
            Console.WriteLine("never printed");
        }
    }
    #endregion

    #region Block 3: Composition and cancellation

    //Write WithTimeout<T>(Task<T> task, TimeSpan timeout) using Task.WhenAny. Throw TimeoutException if the delay wins
    private static async Task<T> WithTimeout<T>(Task<T> task, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource();
        Task delay = Task.Delay(timeout, cts.Token);

        Task finished = await Task.WhenAny(task, delay);
        if(finished == task)
        {
            cts.Cancel(); //stop the timer, we no longer need it. 
            return await task;
        }
        // Note: WhenAny does not cancel 'task'. It keeps running in the background.
        throw new TimeoutException($"Operation did not finish within {timeout.TotalSeconds} s.");
    }
    private static async Task<string> SlowWork(int seconds)
    {
        await Task.Delay(TimeSpan.FromSeconds(seconds));
        return $"finished after {seconds} s";
    }

    private static async Task Ex5_WithTimeout()
    {
        Console.WriteLine(await WithTimeout(SlowWork(1), TimeSpan.FromSeconds(3)));
        try
        {
            await WithTimeout(SlowWork(5), TimeSpan.FromSeconds(1));
        }
        catch(Exception ex)
        {
            Console.WriteLine($"TimeOutException:{ex.Message}");
        }
    }
    //Call a slow endpoint (for example https://httpbin.org/delay/5) with CancellationTokenSource.CancelAfter(2s). Catch OperationCanceledException.
    private static async Task EX6_CancelAfter()
    {
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromSeconds(2));

        var sw = Stopwatch.StartNew();
        try
        {
            //The Server waits 5s before answering. 
            string body = await Http.GetStringAsync("https://httpbin.org/delay/5", cts.Token);
            Console.WriteLine($"Got {body.Length} chars(not expected)");
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine($"Cancelled after {sw.ElapsedMilliseconds} ms");
        }
    }
    #endregion

    #region Block 4: Deadlocks and synchronization

    /// <summary>
    /// Throttle 20 fake API calls (Task.Delay(500)) to 3 at a time using SemaphoreSlim. Print timestamps to confirm batches of 3.
    /// </summary>
    private static async Task EX7_SemaphoreThrottle()
    {
        var throttle = new SemaphoreSlim(3);
        Stopwatch clock =  Stopwatch.StartNew();
        async Task CallApiAsync(int id)
        {
            await throttle.WaitAsync();
            try
            {
                int now = Interlocked.Increment(ref _inFlight);
                Console.WriteLine($"{clock.ElapsedMilliseconds,5} ms  start {id,2}  in-flight = {now}");
                await Task.Delay(500); //simulate an API Call;
            }
            finally
            {
                Interlocked.Decrement(ref _inFlight);
                throttle.Release();
            }
        }
        await Task.WhenAll(Enumerable.Range(1,20).Select(CallApiAsync));
        Console.WriteLine($"All done in {clock.ElapsedMilliseconds} ms (about 7 batches x 500 ms)");
    }
    ///<summary>
    /// Call GetDataAsync().Result inside 200 parallel tasks and watch 
    /// throughput drop (thread-pool starvation). Fix it with await.
    /// </summary>
    private static async Task<string> GetDataAsync()
    {
        await Task.Delay(100);
        return "data";
    }
    private static async Task Ex8_ThreadPoolStarvation()
    {
        const int Count = 200;
        Console.WriteLine($"Pool starts with min worker threads = {ThreadPool.ThreadCount}(cores = {Environment.ProcessorCount})");
        var sw = Stopwatch.StartNew();
        var blocking = Enumerable.Range(0,Count)
        .Select(_=> Task.Run(()=> GetDataAsync().Result))
        .ToArray(); // BAD: Blocks a pool thread;

        await Task.WhenAll(blocking);
        sw.Stop();
        Console.WriteLine($".Result version: {sw.ElapsedMilliseconds} ms, pool threads now = {ThreadPool.ThreadCount}");  
        
        sw.Restart();
        await Task.WhenAll(Enumerable.Range(0,Count).Select(_=>GetDataAsync())); //// GOOD: no thread blocked
        Console.WriteLine($"await version:   {sw.ElapsedMilliseconds} ms (about 100 ms)");
    }
    #endregion
    #region Block 5: Parallelism and pipelines
    
    //Build the producer/consumer pipeline: 1 producer, 3 consumers, bounded channel of 10. Log when the producer waits because the channel is full.
    private static async Task EX9_ProducerConsumer()
    {
        var channel = Channel.CreateBounded<int>(10);

        var producer = Task.Run(async() =>
        {
            for(int i = 1; i <= 50; i++)
            {
                if (!channel.Writer.TryWrite(i))
                {
                    Console.WriteLine($"Producer waiting: Channel full before item{i}");
                    await channel.Writer.WriteAsync(i); // waits until there is space(backpressure)
                }
            }
            channel.Writer.Complete(); // lets the consumer finish
        });

        var consumers = Enumerable.Range(1,3).Select(id => Task.Run(async () =>
        {
            await foreach(var item in channel.Reader.ReadAllAsync())
            {
                Console.WriteLine($"Consumer {id} processed {item}");
                await Task.Delay(200); //simulate work
            }
        })).ToArray();

        await Task.WhenAll(consumers.Append(producer));
        Console.WriteLine("Pipeline finished");
    }

    //Write an IAsyncEnumerable<int> that yields a value every 500 ms. Consume it with await foreach and cancel after 2 seconds using .WithCancellation(token).
    private static async IAsyncEnumerable<int> GenerateAsync([EnumeratorCancellation] CancellationToken ct = default)
    {
        int i=0;
        while (true)
        {
            await Task.Delay(2000,ct);
            yield return i++;
        }
    }

    private static async Task Ex10_AsyncEnumerable()
    {
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMinutes(2));
        try
        {
            await foreach(var n in GenerateAsync().WithCancellation(cts.Token))
            {
                Console.WriteLine($"Received {n}");
            }
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("Cancelled");
        }
    }

    #endregion
}

