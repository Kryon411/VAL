using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VAL.Continuum.Pipeline;
using VAL.Continuum.Pipeline.Truth;

namespace TruthStress
{
    internal static class Program
    {
        private sealed class Options
        {
            public string Path { get; init; } = string.Empty;
            public int Writers { get; init; } = 4;
            public int Rate { get; init; } = 200;
            public int Seconds { get; init; } = 10;
            public bool TruncateTailOnce { get; init; }
        }

        private static async Task<int> Main(string[] args)
        {
            var options = ParseArgs(args);
            var truthPath = string.IsNullOrWhiteSpace(options.Path)
                ? CreateDefaultPath()
                : options.Path;

            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(truthPath) ?? System.IO.Path.GetTempPath());

            Console.WriteLine($"TruthStress starting: {truthPath}");
            Console.WriteLine($"writers={options.Writers} rate={options.Rate}/s seconds={options.Seconds} truncateTailOnce={options.TruncateTailOnce}");

            var exceptions = new ConcurrentQueue<Exception>();
            var written = 0L;
            var parsed = 0L;
            var repairs = 0L;

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(options.Seconds));
            var stopwatch = Stopwatch.StartNew();

            var writerTasks = new List<Task>();
            for (var i = 0; i < options.Writers; i++)
            {
                var writerId = i;
                writerTasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        var interval = options.Rate > 0 ? TimeSpan.FromSeconds(1.0 / options.Rate) : TimeSpan.Zero;
                        var localCount = 0;

                        while (!cts.IsCancellationRequested)
                        {
                            var line = $"A|w{writerId}:{localCount}\r\n";
                            if (AtomicFile.TryAppendAllText(truthPath, line, durable: false))
                                Interlocked.Increment(ref written);
                            localCount++;

                            if (interval > TimeSpan.Zero)
                                await Task.Delay(interval, cts.Token);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        // expected during shutdown
                    }
                    catch (Exception ex)
                    {
                        exceptions.Enqueue(ex);
                    }
                }, cts.Token));
            }

            var readerTask = Task.Run(async () =>
            {
                try
                {
                    while (!cts.IsCancellationRequested)
                    {
                        if (TruthFile.TryRepairTruncatedTail(truthPath, out var removed) && removed > 0)
                            Interlocked.Increment(ref repairs);

                        var count = TruthReader.Read(truthPath, repairTailFirst: false).Count();
                        Interlocked.Exchange(ref parsed, count);

                        await Task.Delay(250, cts.Token);
                    }
                }
                catch (OperationCanceledException)
                {
                    // expected during shutdown
                }
                catch (Exception ex)
                {
                    exceptions.Enqueue(ex);
                }
            }, cts.Token);

            var truncateTask = Task.Run(async () =>
            {
                if (!options.TruncateTailOnce)
                    return;

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, options.Seconds / 2.0)), cts.Token);
                    if (File.Exists(truthPath))
                    {
                        using var fs = new FileStream(truthPath, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
                        if (fs.Length > 0)
                        {
                            fs.SetLength(fs.Length - 1);
                            fs.Flush(flushToDisk: true);
                            Console.WriteLine("Tail truncation applied.");
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    // expected during shutdown
                }
                catch (Exception ex)
                {
                    exceptions.Enqueue(ex);
                }
            }, cts.Token);

            await Task.WhenAll(writerTasks.Concat(new[] { readerTask, truncateTask }));
            stopwatch.Stop();

            Console.WriteLine("\nSummary");
            Console.WriteLine($"Elapsed: {stopwatch.Elapsed}");
            Console.WriteLine($"Lines written: {written}");
            Console.WriteLine($"Parsed entries: {parsed}");
            Console.WriteLine($"Repairs applied: {repairs}");
            Console.WriteLine($"Exceptions: {exceptions.Count}");

            if (!exceptions.IsEmpty)
            {
                foreach (var ex in exceptions)
                    Console.WriteLine(ex);
                return 1;
            }

            return 0;
        }

        private static Options ParseArgs(string[] args)
        {
            string? GetValue(string name)
            {
                var index = Array.FindIndex(args, a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));
                if (index < 0 || index + 1 >= args.Length)
                    return null;
                return args[index + 1];
            }

            var path = GetValue("--path");
            var writers = ParseInt(GetValue("--writers"), 4);
            var rate = ParseInt(GetValue("--rate"), 200);
            var seconds = ParseInt(GetValue("--seconds"), 10);
            var truncate = args.Any(a => string.Equals(a, "--truncateTailOnce", StringComparison.OrdinalIgnoreCase));

            return new Options
            {
                Path = path ?? string.Empty,
                Writers = Math.Max(1, writers),
                Rate = Math.Max(0, rate),
                Seconds = Math.Max(1, seconds),
                TruncateTailOnce = truncate
            };
        }

        private static int ParseInt(string? value, int fallback)
        {
            return int.TryParse(value, out var parsed) ? parsed : fallback;
        }

        private static string CreateDefaultPath()
        {
            var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "TruthStress", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return System.IO.Path.Combine(dir, "Truth.log");
        }
    }
}
