using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VAL.Continuum.Pipeline;
using VAL.Continuum.Pipeline.Truth;
using Xunit;

namespace VAL.Tests.Truth
{
    public sealed class TruthConcurrencyTests
    {
        [Fact(Timeout = 15000)]
        public async Task ConcurrentWritersAndReaders_DoNotThrow_AndMaintainMonotonicLineNumbers()
        {
            var (path, cleanup) = CreateTempFile();
            var exceptions = new ConcurrentQueue<Exception>();
            try
            {
                var writers = 4;
                var loops = 200;
                var cts = new CancellationTokenSource();

                var writerTasks = new List<Task>();
                for (var i = 0; i < writers; i++)
                {
                    var writerId = i;
                    writerTasks.Add(Task.Run(() =>
                    {
                        try
                        {
                            for (var j = 0; j < loops; j++)
                            {
                                AtomicFile.TryAppendAllText(path, $"A|w{writerId}:{j}\r\n", durable: false);
                            }
                        }
                        catch (Exception ex)
                        {
                            exceptions.Enqueue(ex);
                        }
                    }, cts.Token));
                }

                var writersAll = Task.WhenAll(writerTasks);

                var readerTask = Task.Run(() =>
                {
                    try
                    {
                        while (!writersAll.IsCompleted)
                        {
                            var _ = TruthReader.Read(path, repairTailFirst: true).Count();
                            Thread.Sleep(5);
                        }
                    }
                    catch (Exception ex)
                    {
                        exceptions.Enqueue(ex);
                    }
                }, cts.Token);

                var truncateTask = Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(100, cts.Token);
                        if (File.Exists(path))
                        {
                            using var fs = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
                            if (fs.Length > 0)
                            {
                                fs.SetLength(fs.Length - 1);
                                fs.Flush(flushToDisk: true);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        exceptions.Enqueue(ex);
                    }
                }, cts.Token);

                await writersAll;
                await readerTask;
                await truncateTask;

                if (!exceptions.IsEmpty)
                    throw new AggregateException(exceptions);

                var entries = TruthReader.Read(path, repairTailFirst: true).ToList();

                Assert.NotEmpty(entries);

                var lastLine = 0;
                foreach (var entry in entries)
                {
                    Assert.True(entry.LineNumber > lastLine);
                    lastLine = entry.LineNumber;
                }
            }
            finally
            {
                cleanup();
            }
        }

        private static (string path, Action cleanup) CreateTempFile()
        {
            var dir = Path.Combine(Path.GetTempPath(), "VAL.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "Truth.log");

            return (path, () =>
            {
                try
                {
                    if (Directory.Exists(dir))
                        Directory.Delete(dir, recursive: true);
                }
                catch
                {
                    // best-effort cleanup
                }
            });
        }
    }
}
