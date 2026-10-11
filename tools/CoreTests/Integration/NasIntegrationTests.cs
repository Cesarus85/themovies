using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using NUnit.Framework;
using TheMovies.Core.Network;
using TheMovies.Core.Streaming;

namespace TheMovies.Tests.Integration
{
    /// <summary>
    /// Whole NAS path: browse a source, stream a file through <see cref="SourceStreamer"/> and the local HTTP
    /// server, read it like the video player would. Needs THEMOVIES_SMB ("host:port", share "filme" with
    /// tester/geheim) and/or THEMOVIES_DAV (WebDAV folder URL with tester/geheim containing Filme/film.mp4);
    /// THEMOVIES_SMB_FILE is the local copy of film.mp4 for comparison.
    /// </summary>
    [TestFixture, Category("Integration")]
    public class NasIntegrationTests
    {
        static string Env(string name) => Environment.GetEnvironmentVariable(name);

        static void PlayThroughProxy(INetworkBrowser browser, NetworkItem file)
        {
            using (var streamer = new SourceStreamer(() => browser.OpenFile(file.Location), file.Size))
            using (var server = new LocalStreamServer())
            using (var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) })
            {
                string url = server.Serve("/nas/1/" + file.Name, streamer, LocalStreamServer.ContentTypeFor(file.Name));
                byte[] all = http.GetByteArrayAsync(url).GetAwaiter().GetResult();
                Assert.AreEqual(file.Size, all.Length);
                string local = Env("THEMOVIES_SMB_FILE");
                if (!string.IsNullOrEmpty(local)) Assert.AreEqual(File.ReadAllBytes(local), all);

                var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Range = new RangeHeaderValue(2_000_000, null);
                var response = http.SendAsync(request).GetAwaiter().GetResult();
                Assert.AreEqual(206, (int)response.StatusCode);
                byte[] tail = response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
                Assert.AreEqual(all.Skip(2_000_000).ToArray(), tail);
                Assert.IsNull(streamer.Error);
            }
        }

        [Test]
        public void SmbBrowseAndStream()
        {
            string smb = Env("THEMOVIES_SMB");
            if (string.IsNullOrEmpty(smb)) Assert.Ignore("THEMOVIES_SMB not set");
            Assert.IsTrue(NetworkSource.TryParse(smb, out var source, out _));
            source.User = "tester";
            source.Password = "geheim";
            using (var browser = NetworkBrowser.Create(source))
            {
                var shares = browser.List(source.RootLocation);
                var share = shares.Single(s => s.Name == "filme");
                Assert.IsTrue(share.IsFolder);
                var items = browser.List(share.Location);
                var folder = items.Single(i => i.Name == "Ordner mit Ä");
                Assert.AreEqual("datei.txt", browser.List(folder.Location).Single().Name);
                var file = items.Single(i => i.Name == "film.mp4");
                Assert.AreEqual(share.Location, browser.FolderOf(file.Location));

                byte[] srt = browser.ReadSmallFile(items.Single(i => i.Name == "film.srt").Location, 100000);
                StringAssert.Contains("Hallo", System.Text.Encoding.UTF8.GetString(srt));
                Assert.IsNull(browser.ReadSmallFile(file.Location, 1000));
                Assert.IsNull(browser.DirectUrl(file.Location));

                PlayThroughProxy(browser, file);
            }
        }

        [Test]
        public void SmbWrongPasswordIsReported()
        {
            string smb = Env("THEMOVIES_SMB");
            if (string.IsNullOrEmpty(smb)) Assert.Ignore("THEMOVIES_SMB not set");
            NetworkSource.TryParse(smb + "/filme", out var source, out _);
            source.User = "tester";
            source.Password = "falsch";
            using (var browser = NetworkBrowser.Create(source))
            {
                var e = Assert.Throws<SmbException>(() => browser.List(source.RootLocation));
                StringAssert.Contains("Anmeldung abgelehnt", e.Message);
            }
        }

        [Test]
        public void WebDavBrowseAndStream()
        {
            string dav = Env("THEMOVIES_DAV");
            if (string.IsNullOrEmpty(dav)) Assert.Ignore("THEMOVIES_DAV not set");
            Assert.IsTrue(NetworkSource.TryParse(dav, out var source, out _));
            source.User = "tester";
            source.Password = "geheim";
            using (var browser = NetworkBrowser.Create(source))
            {
                var folder = browser.List(source.RootLocation).Single(i => i.Name == "Filme");
                Assert.IsTrue(folder.IsFolder);
                var items = browser.List(folder.Location);
                var file = items.Single(i => i.Name == "film.mp4");
                Assert.AreEqual(3000000, file.Size);
                Assert.IsNull(browser.DirectUrl(file.Location), "WebDAV files go through the proxy");
                StringAssert.Contains("Hallo", System.Text.Encoding.UTF8.GetString(browser.ReadSmallFile(items.Single(i => i.Name == "film.srt").Location, 1000)));
                PlayThroughProxy(browser, file);
            }

            source.Password = "falsch";
            using (var browser = NetworkBrowser.Create(source))
            {
                var e = Assert.Throws<IOException>(() => browser.List(source.RootLocation));
                StringAssert.Contains("Anmeldung abgelehnt", e.Message);
            }
        }

        [Test]
        public void DlnaBrowseAndRead()
        {
            string dlna = Env("THEMOVIES_DLNA");
            if (string.IsNullOrEmpty(dlna)) Assert.Ignore("THEMOVIES_DLNA not set (device description URL)");
            var source = new NetworkSource { Kind = NetworkSourceKind.Dlna, Name = "Test", Address = dlna };
            using (var browser = NetworkBrowser.Create(source))
            {
                // Walk down the containers until a video shows up.
                NetworkItem file = null;
                var queue = new System.Collections.Generic.Queue<string>();
                queue.Enqueue(source.RootLocation);
                int visited = 0;
                while (file == null && queue.Count > 0 && visited++ < 30)
                {
                    foreach (var item in browser.List(queue.Dequeue()))
                    {
                        if (item.IsFolder) queue.Enqueue(item.Location);
                        else if (file == null) file = item;
                    }
                }
                Assert.IsNotNull(file, "no video found");
                TestContext.WriteLine($"{file.Name} {file.Size} {file.Location}");
                Assert.AreEqual(file.Location, browser.DirectUrl(file.Location));
                using (var stream = browser.OpenFile(file.Location))
                {
                    stream.Open();
                    Assert.AreEqual(file.Size, stream.Length);
                    var head = new byte[1000];
                    Assert.AreEqual(1000, SourceReader.ReadFully(stream, 0, head, 0, 1000));
                }
            }
        }

        [Test]
        public void SmbCopyToDisk()
        {
            string smb = Env("THEMOVIES_SMB");
            if (string.IsNullOrEmpty(smb)) Assert.Ignore("THEMOVIES_SMB not set");
            NetworkSource.TryParse(smb + "/filme", out var source, out _);
            source.User = "tester";
            source.Password = "geheim";
            using (var browser = NetworkBrowser.Create(source))
            {
                var file = browser.List(source.RootLocation).Single(i => i.Name == "film.mp4");
                string target = Path.Combine(Path.GetTempPath(), "themovies-copy-" + Guid.NewGuid().ToString("N"), FileDownload.SafeFileName(file.Name));
                var copy = new FileDownload(() => NetworkBrowser.OpenFile(source, file.Location), target, file.Size);
                var watch = System.Diagnostics.Stopwatch.StartNew();
                Assert.IsTrue(copy.Start().Wait(60000));
                Assert.IsNull(copy.Error);
                Assert.IsTrue(copy.Finished);
                Assert.AreEqual(file.Size, new FileInfo(target).Length);
                string local = Env("THEMOVIES_SMB_FILE");
                if (!string.IsNullOrEmpty(local)) Assert.IsTrue(File.ReadAllBytes(local).AsSpan().SequenceEqual(File.ReadAllBytes(target)));
                TestContext.WriteLine($"{file.Size / 1e6:0.0} MB in {watch.ElapsedMilliseconds} ms");
                Directory.Delete(Path.GetDirectoryName(target), true);
            }
        }
    }
}
