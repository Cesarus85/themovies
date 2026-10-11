using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using NUnit.Framework;
using TheMovies.Core.Streaming;

namespace TheMovies.Tests
{
    public class HttpRangeTests
    {
        [TestCase(null, RangeResult.Full, 0, 999)]
        [TestCase("bytes=0-", RangeResult.Partial, 0, 999)]
        [TestCase("bytes=100-199", RangeResult.Partial, 100, 199)]
        [TestCase("bytes=900-5000", RangeResult.Partial, 900, 999)]
        [TestCase("bytes=-100", RangeResult.Partial, 900, 999)]
        [TestCase("bytes=-5000", RangeResult.Partial, 0, 999)]
        [TestCase("bytes=1000-", RangeResult.Unsatisfiable, 1000, 999)]
        [TestCase("bytes=10-20, 30-40", RangeResult.Partial, 10, 20)]
        [TestCase("items=1-2", RangeResult.Full, 0, 999)]
        public void Parses(string header, RangeResult expected, long start, long end)
        {
            var result = HttpRange.Parse(header, 1000, out long s, out long e);
            Assert.AreEqual(expected, result);
            if (expected != RangeResult.Unsatisfiable)
            {
                Assert.AreEqual(start, s);
                Assert.AreEqual(end, e);
            }
        }
    }

    public class RemoteBlockCacheTests
    {
        [Test]
        public void BlockGeometry()
        {
            var cache = new RemoteBlockCache(totalSize: 1000, blockSize: 300);
            Assert.AreEqual(4, cache.BlockCount);
            Assert.AreEqual(300, cache.ExpectedLength(0));
            Assert.AreEqual(100, cache.ExpectedLength(3));
            Assert.AreEqual(3, cache.BlockIndexOf(999));
            Assert.AreEqual(3, cache.BlockIndexOf(5000));
        }

        [Test]
        public void WantedBlocksComeFirstThenReadAhead()
        {
            var cache = new RemoteBlockCache(totalSize: 100 * 10, blockSize: 10, capacityBlocks: 50, readAheadBlocks: 3);
            var waiter = new Thread(() => cache.WaitForBlock(40, 2000));
            waiter.Start();
            Thread.Sleep(100);

            var requests = cache.TakeRequests(now: 0, maxInFlight: 10, retryAfter: 5);
            CollectionAssert.AreEqual(new[] { 40, 41, 42, 43 }, requests);
            Assert.AreEqual(4, cache.InFlightCount);

            Assert.IsTrue(cache.OnBlockReceived(40, new byte[10]));
            waiter.Join();
            Assert.IsEmpty(cache.TakeRequests(now: 1, maxInFlight: 10, retryAfter: 5), "41-43 still in flight");
            CollectionAssert.AreEqual(new[] { 41, 42, 43 }, cache.TakeRequests(now: 10, maxInFlight: 10, retryAfter: 5), "retried after timeout");
        }

        [Test]
        public void RespectsMaxInFlight()
        {
            var cache = new RemoteBlockCache(totalSize: 1000, blockSize: 10, readAheadBlocks: 20);
            Assert.AreEqual(4, cache.TakeRequests(0, 4, 5).Count);
            Assert.AreEqual(0, cache.TakeRequests(0, 4, 5).Count);
        }

        [Test]
        public void RejectsWrongLength()
        {
            var cache = new RemoteBlockCache(totalSize: 25, blockSize: 10);
            Assert.IsFalse(cache.OnBlockReceived(2, new byte[10]));
            Assert.IsTrue(cache.OnBlockReceived(2, new byte[5]));
            Assert.IsFalse(cache.OnBlockReceived(7, new byte[10]));
        }

        [Test]
        public void EvictsOutsideReadWindow()
        {
            var cache = new RemoteBlockCache(totalSize: 1000, blockSize: 10, capacityBlocks: 10, readAheadBlocks: 2);
            for (int i = 0; i < 30; i++)
                cache.OnBlockReceived(i, new byte[10]);
            Assert.IsTrue(cache.HasBlock(0), "inside the window around the read position (0)");
            Assert.IsFalse(cache.HasBlock(10), "old block outside the window");
            Assert.IsTrue(cache.HasBlock(29), "most recent");
            Assert.AreEqual(1, cache.ContiguousBlocksFrom(29));
        }

        [Test]
        public void CloseReleasesWaiters()
        {
            var cache = new RemoteBlockCache(totalSize: 100, blockSize: 10);
            byte[] result = new byte[1];
            var waiter = new Thread(() => result = cache.WaitForBlock(3, 10000));
            waiter.Start();
            Thread.Sleep(50);
            cache.Close();
            Assert.IsTrue(waiter.Join(2000));
            Assert.IsNull(result);
        }
    }

    public class LocalStreamServerTests
    {
        static byte[] Source(int size)
        {
            var data = new byte[size];
            new Random(42).NextBytes(data);
            return data;
        }

        /// <summary>Plays the role of the main thread + network: answers block requests from the source.</summary>
        static Thread StartFetcher(RemoteBlockCache cache, byte[] source, CancellationToken token)
        {
            var t = new Thread(() =>
            {
                while (!token.IsCancellationRequested)
                {
                    foreach (int index in cache.TakeRequests(Environment.TickCount / 1000.0, 4, 5))
                    {
                        int length = cache.ExpectedLength(index);
                        var block = new byte[length];
                        Array.Copy(source, (long)index * cache.BlockSize, block, 0, length);
                        cache.OnBlockReceived(index, block);
                    }
                    Thread.Sleep(2);
                }
            }) { IsBackground = true };
            t.Start();
            return t;
        }

        [Test]
        public void ServesWholeFileAndRanges()
        {
            var source = Source(1_000_003);
            var cache = new RemoteBlockCache(source.Length, blockSize: 64 * 1024, readAheadBlocks: 4);
            using var cts = new CancellationTokenSource();
            StartFetcher(cache, source, cts.Token);
            using var server = new LocalStreamServer();
            string url = server.Serve("/media/1/movie.mp4", cache, "video/mp4");

            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };

            var full = http.GetAsync(url).Result;
            Assert.AreEqual(200, (int)full.StatusCode);
            Assert.AreEqual("video/mp4", full.Content.Headers.ContentType.MediaType);
            CollectionAssert.AreEqual(source, full.Content.ReadAsByteArrayAsync().Result);

            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Range = new RangeHeaderValue(500_000, 700_123);
            var partial = http.SendAsync(request).Result;
            Assert.AreEqual(206, (int)partial.StatusCode);
            Assert.AreEqual("bytes 500000-700123/1000003", partial.Content.Headers.ContentRange.ToString());
            CollectionAssert.AreEqual(source.Skip(500_000).Take(200_124).ToArray(), partial.Content.ReadAsByteArrayAsync().Result);

            var tail = new HttpRequestMessage(HttpMethod.Get, url);
            tail.Headers.Range = new RangeHeaderValue(null, 10);
            CollectionAssert.AreEqual(source.Skip(source.Length - 10).ToArray(), http.SendAsync(tail).Result.Content.ReadAsByteArrayAsync().Result);

            var head = http.SendAsync(new HttpRequestMessage(HttpMethod.Head, url)).Result;
            Assert.AreEqual(1_000_003, head.Content.Headers.ContentLength);

            Assert.AreEqual(404, (int)http.GetAsync($"http://127.0.0.1:{server.Port}/other").Result.StatusCode);
            cts.Cancel();
        }

        [Test]
        public void UnsatisfiableRange()
        {
            var cache = new RemoteBlockCache(100, blockSize: 10);
            using var server = new LocalStreamServer();
            string url = server.Serve("/m", cache, null);
            using var http = new HttpClient();
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Range = new RangeHeaderValue(200, null);
            Assert.AreEqual(416, (int)http.SendAsync(request).Result.StatusCode);
        }
    }
}
