using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using NUnit.Framework;
using TheMovies.Core;
using TheMovies.Core.Network;
using TheMovies.Core.Streaming;

namespace TheMovies.Tests
{
    public class NetworkCryptoTests
    {
        static byte[] Hex(string hex) => Enumerable.Range(0, hex.Length / 2).Select(i => Convert.ToByte(hex.Substring(i * 2, 2), 16)).ToArray();

        [TestCase("", "31d6cfe0d16ae931b73c59d7e0c089c0")]
        [TestCase("abc", "a448017aaf21d8525fc10ae87aa6729d")]
        [TestCase("12345678901234567890123456789012345678901234567890123456789012345678901234567890", "e33b4ddc9c38f2199c3e7b164fcc0536")]
        public void Md4(string input, string expected)
        {
            Assert.AreEqual(Hex(expected), TheMovies.Core.Network.Md4.Hash(Encoding.ASCII.GetBytes(input)));
        }

        [Test]
        public void Rc4()
        {
            Assert.AreEqual(Hex("bbf316e8d940af0ad3"), NtlmAuthenticator.Rc4(Encoding.ASCII.GetBytes("Key"), Encoding.ASCII.GetBytes("Plaintext")));
        }

        [Test]
        public void NtowfV2MatchesSpecExample()
        {
            // MS-NLMP 4.2.4.1.1
            Assert.AreEqual(Hex("0c868a403bfd7a93a3001ef22ef02e3f"), NtlmAuthenticator.NtowfV2("Password", "User", "Domain"));
        }

        [TestCase("", "bb1d6929e95937287fa37d129b756746")]
        [TestCase("6bc1bee22e409f96e93d7e117393172a", "070a16b46b4d4144f79bdd9dd04a287c")]
        [TestCase("6bc1bee22e409f96e93d7e117393172aae2d8a571e03ac9c9eb76fac45af8e5130c81c46a35ce411", "dfa66747de9ae63030ca32611497c827")]
        public void AesCmacMatchesRfc4493(string message, string expected)
        {
            Assert.AreEqual(Hex(expected), Smb2Client.AesCmac(Hex("2b7e151628aed2a6abf7158809cf4f3c"), Hex(message)));
        }

        [Test]
        public void NtlmMessagesHaveTheRightShape()
        {
            var ntlm = new NtlmAuthenticator("user", "pass", "", n => Enumerable.Repeat((byte)0xAA, n).ToArray());
            byte[] negotiate = ntlm.CreateNegotiate();
            Assert.AreEqual("NTLMSSP\0", Encoding.ASCII.GetString(negotiate, 0, 8));
            Assert.AreEqual(1, BitConverter.ToInt32(negotiate, 8));

            // Minimal challenge: flags with key exchange, no target info
            var challenge = new byte[48];
            Encoding.ASCII.GetBytes("NTLMSSP\0").CopyTo(challenge, 0);
            BitConverter.GetBytes(2).CopyTo(challenge, 8);
            BitConverter.GetBytes(0x628A8215u).CopyTo(challenge, 20);
            byte[] auth = ntlm.CreateAuthenticate(challenge);
            Assert.AreEqual(3, BitConverter.ToInt32(auth, 8));
            int userLength = BitConverter.ToUInt16(auth, 36);
            int userOffset = BitConverter.ToInt32(auth, 40);
            Assert.AreEqual("user", Encoding.Unicode.GetString(auth, userOffset, userLength));
            Assert.AreEqual(16, ntlm.SessionKey.Length);
        }
    }

    public class NetworkAddressTests
    {
        [TestCase(@"\\nas\filme", "nas", 445, "filme", "")]
        [TestCase("smb://192.168.1.10/Video/Filme/3D", "192.168.1.10", 445, "Video", "Filme/3D")]
        [TestCase("nas:4455/share/", "nas", 4455, "share", "")]
        [TestCase("smb://user@nas/share", "nas", 445, "share", "")]
        [TestCase("nas", "nas", 445, "", "")]
        [TestCase("smb://[fe80::1]:446/s", "fe80::1", 446, "s", "")]
        public void ParsesSmbAddresses(string input, string host, int port, string share, string path)
        {
            Assert.IsTrue(SmbAddress.TryParse(input, out var a));
            Assert.AreEqual(host, a.Host);
            Assert.AreEqual(port, a.Port);
            Assert.AreEqual(share, a.Share);
            Assert.AreEqual(path, a.Path);
            Assert.IsTrue(SmbAddress.TryParse(a.ToString(), out var again));
            Assert.AreEqual(a.ToString(), again.ToString());
        }

        [TestCase("")]
        [TestCase("nas:abc/share")]
        [TestCase("ftp://nas/share")]
        public void RejectsBadSmbAddresses(string input)
        {
            Assert.IsFalse(SmbAddress.TryParse(input, out _));
        }

        [Test]
        public void ChildPaths()
        {
            SmbAddress.TryParse("smb://nas", out var a);
            Assert.AreEqual("smb://nas/filme", a.Child("filme").ToString());
            Assert.AreEqual("smb://nas/filme/Mein Film.mkv", a.Child("filme").Child("Mein Film.mkv").ToString());
        }

        [TestCase(@"\\nas\filme", NetworkSourceKind.Smb, "smb://nas/filme")]
        [TestCase("https://nas:5006/video", NetworkSourceKind.WebDav, "https://nas:5006/video/")]
        [TestCase("davs://nas/video/", NetworkSourceKind.WebDav, "https://nas/video/")]
        [TestCase("192.168.0.5", NetworkSourceKind.Smb, "smb://192.168.0.5")]
        public void ParsesUserInput(string input, NetworkSourceKind kind, string address)
        {
            Assert.IsTrue(NetworkSource.TryParse(input, out var s, out string error), error);
            Assert.AreEqual(kind, s.Kind);
            Assert.AreEqual(address, s.Address);
        }

        [TestCase("192.168.1.2", true)]
        [TestCase("10.0.0.1", true)]
        [TestCase("172.20.1.1", true)]
        [TestCase("172.32.1.1", false)]
        [TestCase("8.8.8.8", false)]
        [TestCase("diskstation", true)]
        [TestCase("nas.local", true)]
        [TestCase("fritz.box", true)]
        [TestCase("example.com", false)]
        public void LocalHosts(string host, bool local)
        {
            Assert.AreEqual(local, NetAddress.IsLocalHost(host));
        }

        [Test]
        public void StoreRoundTripsAndSkipsDiscovered()
        {
            var sources = new[]
            {
                new NetworkSource { Kind = NetworkSourceKind.Smb, Name = "NAS\tFilme", Address = "smb://nas/filme", User = "ich", Password = "p\nä%ß" },
                new NetworkSource { Kind = NetworkSourceKind.WebDav, Name = "Cloud", Address = "https://x/dav/" },
                new NetworkSource { Kind = NetworkSourceKind.Dlna, Name = "Gefunden", Address = "http://1.2.3.4/d.xml", Discovered = true },
            };
            var back = NetworkSourceStore.Deserialize(NetworkSourceStore.Serialize(sources));
            Assert.AreEqual(2, back.Count);
            Assert.AreEqual("NAS\tFilme", back[0].Name);
            Assert.AreEqual("p\nä%ß", back[0].Password);
            Assert.AreEqual(NetworkSourceKind.WebDav, back[1].Kind);
            Assert.AreEqual(string.Empty, back[1].User);
            Assert.AreEqual(0, NetworkSourceStore.Deserialize("garbage\nv1\t9\ta\tb\tc\td").Count);
        }

        [Test]
        public void NetworkEntriesMatchByName()
        {
            var e = new MediaEntry { Location = "smb://nas/filme/Film.3D.SBS.mkv", SourceId = "smb:smb://nas/filme", Size = 5 };
            Assert.IsTrue(e.IsNetwork);
            Assert.IsFalse(e.IsUrl);
            Assert.AreEqual("Film.3D.SBS.mkv", e.MatchKey);
            var dlna = new MediaEntry { Location = "http://nas:8200/MediaItems/22.mkv", Name = "Titel.mkv", SourceId = "x" };
            Assert.AreEqual("Titel.mkv", dlna.FileName);
        }
    }

    public class DlnaTests
    {
        const string Description = @"<?xml version=""1.0""?>
<root xmlns=""urn:schemas-upnp-org:device-1-0"">
  <device>
    <deviceType>urn:schemas-upnp-org:device:MediaServer:1</deviceType>
    <friendlyName>DiskStation</friendlyName>
    <serviceList>
      <service><serviceType>urn:schemas-upnp-org:service:ConnectionManager:1</serviceType><controlURL>/cm</controlURL></service>
      <service><serviceType>urn:schemas-upnp-org:service:ContentDirectory:1</serviceType><controlURL>/ctl/ContentDir</controlURL></service>
    </serviceList>
  </device>
</root>";

        [Test]
        public void ParsesDescription()
        {
            var d = Dlna.ParseDescription(Description, "http://192.168.1.5:50001/desc/device.xml");
            Assert.AreEqual("DiskStation", d.FriendlyName);
            Assert.AreEqual("http://192.168.1.5:50001/ctl/ContentDir", d.ControlUrl);
            Assert.AreEqual("urn:schemas-upnp-org:service:ContentDirectory:1", d.ServiceType);
        }

        [Test]
        public void ParsesBrowseResult()
        {
            string didl = "<DIDL-Lite xmlns=\"urn:schemas-upnp-org:metadata-1-0/DIDL-Lite/\" xmlns:dc=\"http://purl.org/dc/elements/1.1/\" xmlns:upnp=\"urn:schemas-upnp-org:metadata-1-0/upnp/\">" +
                          "<container id=\"64$1\" parentID=\"64\" childCount=\"3\"><dc:title>Filme &amp; Serien</dc:title><upnp:class>object.container.storageFolder</upnp:class></container>" +
                          "<item id=\"64$2\"><dc:title>Avatar 3D SBS</dc:title><upnp:class>object.item.videoItem</upnp:class>" +
                          "<res protocolInfo=\"http-get:*:video/x-matroska:DLNA.ORG_OP=01\" size=\"123456\">http://192.168.1.5:8200/MediaItems/22.mkv</res></item>" +
                          "<item id=\"64$3\"><dc:title>Lied</dc:title><upnp:class>object.item.audioItem</upnp:class><res protocolInfo=\"http-get:*:audio/mpeg:*\">http://x/a.mp3</res></item>" +
                          "</DIDL-Lite>";
            string soap = "<?xml version=\"1.0\"?><s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\"><s:Body>" +
                          "<u:BrowseResponse xmlns:u=\"urn:schemas-upnp-org:service:ContentDirectory:1\"><Result>" +
                          System.Security.SecurityElement.Escape(didl) +
                          "</Result><NumberReturned>3</NumberReturned><TotalMatches>3</TotalMatches><UpdateID>1</UpdateID></u:BrowseResponse></s:Body></s:Envelope>";
            var items = Dlna.ParseBrowseResponse(soap, out int returned, out int total);
            Assert.AreEqual(3, returned);
            Assert.AreEqual(3, total);
            Assert.AreEqual(2, items.Count);
            Assert.IsTrue(items[0].IsFolder);
            Assert.AreEqual("Filme & Serien", items[0].Name);
            Assert.AreEqual("64$1", items[0].Location);
            Assert.IsFalse(items[1].IsFolder);
            Assert.AreEqual("Avatar 3D SBS.mkv", items[1].Name);
            Assert.AreEqual(123456, items[1].Size);
            Assert.AreEqual("http://192.168.1.5:8200/MediaItems/22.mkv", items[1].Location);
        }

        [Test]
        public void SsdpLocation()
        {
            string response = "HTTP/1.1 200 OK\r\nCACHE-CONTROL: max-age=1800\r\nLocation: http://192.168.1.5:50001/desc.xml\r\nST: urn:schemas-upnp-org:device:MediaServer:1\r\n\r\n";
            Assert.AreEqual("http://192.168.1.5:50001/desc.xml", Dlna.ParseSsdpLocation(response));
            Assert.IsNull(Dlna.ParseSsdpLocation("HTTP/1.1 200 OK\r\n\r\n"));
        }

        [Test]
        public void FileNames()
        {
            Assert.AreEqual("Film.mp4", Dlna.FileNameFor("Film", new Uri("http://x/stream?id=1"), "video/mp4"));
            Assert.AreEqual("Film.mkv", Dlna.FileNameFor("Film", new Uri("http://x/22.mkv"), "video/x-matroska"));
            Assert.AreEqual("Film.ts", Dlna.FileNameFor("Film.ts", new Uri("http://x/1"), "video/mp4"));
        }
    }

    public class WebDavTests
    {
        [Test]
        public void ParsesMultiStatus()
        {
            const string xml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<D:multistatus xmlns:D=""DAV:"">
  <D:response><D:href>/dav/filme/</D:href><D:propstat><D:prop><D:resourcetype><D:collection/></D:resourcetype></D:prop><D:status>HTTP/1.1 200 OK</D:status></D:propstat></D:response>
  <D:response><D:href>/dav/filme/3D%20Filme/</D:href><D:propstat><D:prop><D:resourcetype><D:collection/></D:resourcetype></D:prop><D:status>HTTP/1.1 200 OK</D:status></D:propstat></D:response>
  <D:response><D:href>http://nas/dav/filme/M%C3%BCnchen.mp4</D:href>
    <D:propstat><D:prop><D:resourcetype/><D:getcontentlength>4096</D:getcontentlength></D:prop><D:status>HTTP/1.1 200 OK</D:status></D:propstat>
    <D:propstat><D:prop><D:displayname/></D:prop><D:status>HTTP/1.1 404 Not Found</D:status></D:propstat>
  </D:response>
  <D:response><D:href>/dav/filme/.hidden</D:href><D:propstat><D:prop><D:resourcetype/></D:prop><D:status>HTTP/1.1 200 OK</D:status></D:propstat></D:response>
</D:multistatus>";
            var items = WebDav.ParseMultiStatus(xml, new Uri("http://nas/dav/filme/"));
            Assert.AreEqual(2, items.Count);
            Assert.AreEqual("3D Filme", items[0].Name);
            Assert.IsTrue(items[0].IsFolder);
            Assert.AreEqual("http://nas/dav/filme/3D%20Filme/", items[0].Location);
            Assert.AreEqual("München.mp4", items[1].Name);
            Assert.AreEqual(4096, items[1].Size);
            Assert.IsFalse(items[1].IsFolder);
        }
    }

    public class MdnsTests
    {
        static void Name(MemoryStream m, params string[] labels)
        {
            foreach (string l in labels)
            {
                m.WriteByte((byte)l.Length);
                byte[] b = Encoding.ASCII.GetBytes(l);
                m.Write(b, 0, b.Length);
            }
            m.WriteByte(0);
        }

        static void Record(MemoryStream m, ushort type, byte[] data)
        {
            m.Write(new byte[] { (byte)(type >> 8), (byte)type, 0x80, 1, 0, 0, 0x11, 0x94, (byte)(data.Length >> 8), (byte)data.Length }, 0, 10);
            m.Write(data, 0, data.Length);
        }

        [Test]
        public void ResolvesSmbServiceWithCompression()
        {
            var m = new MemoryStream();
            m.Write(new byte[] { 0, 0, 0x84, 0, 0, 0, 0, 1, 0, 0, 0, 2 }, 0, 12);
            // PTR _smb._tcp.local -> DiskStation._smb._tcp.local
            int serviceOffset = (int)m.Length;
            Name(m, "_smb", "_tcp", "local");
            var ptr = new MemoryStream();
            ptr.WriteByte(11);
            ptr.Write(Encoding.ASCII.GetBytes("DiskStation"), 0, 11);
            ptr.WriteByte(0xC0);
            ptr.WriteByte((byte)serviceOffset);
            Record(m, 12, ptr.ToArray());
            // SRV DiskStation._smb._tcp.local -> port 445, target ds.local (compressed instance name)
            int instanceOffset = serviceOffset + 17 + 10;
            m.WriteByte(0xC0);
            m.WriteByte((byte)instanceOffset);
            var srv = new MemoryStream();
            srv.Write(new byte[] { 0, 0, 0, 0, 0x01, 0xBD }, 0, 6);
            Name(srv, "ds", "local");
            Record(m, 33, srv.ToArray());
            // A ds.local
            Name(m, "ds", "local");
            Record(m, 1, new byte[] { 192, 168, 1, 5 });

            var records = new Mdns.Records();
            Mdns.Parse(m.ToArray(), records, "192.168.1.99");
            var services = records.Resolve("_smb._tcp.local");
            Assert.AreEqual(1, services.Count);
            Assert.AreEqual("DiskStation", services[0].Name);
            Assert.AreEqual("192.168.1.5", services[0].Address);
            Assert.AreEqual(445, services[0].Port);
        }

        [Test]
        public void QueryShape()
        {
            byte[] q = Mdns.BuildQuery("_smb._tcp.local", 0x1234);
            Assert.AreEqual(0x12, q[0]);
            Assert.AreEqual(1, q[5]);
            Assert.AreEqual(12 + 17 + 4, q.Length);
        }
    }

    public class SourceStreamerTests
    {
        sealed class MemorySource : IRandomAccessSource
        {
            readonly byte[] m_Data;
            public int Opens;
            public int FailEvery;
            int m_Reads;

            public MemorySource(byte[] data) { m_Data = data; }
            public long Length => m_Data.Length;
            public void Open() => Opens++;

            public int Read(long offset, byte[] buffer, int index, int count)
            {
                if (FailEvery > 0 && ++m_Reads % FailEvery == 0) throw new IOException("Verbindung weg");
                if (offset >= m_Data.Length) return 0;
                int n = (int)Math.Min(count, Math.Min(100000, m_Data.Length - offset));
                Buffer.BlockCopy(m_Data, (int)offset, buffer, index, n);
                return n;
            }

            public void Dispose() { }
        }

        static byte[] Data(int size)
        {
            var data = new byte[size];
            new Random(7).NextBytes(data);
            return data;
        }

        [Test]
        public void ServesThroughLocalServerAndRecoversFromErrors()
        {
            byte[] data = Data(1_500_000);
            var source = new MemorySource(data) { FailEvery = 5 };
            using (var streamer = new SourceStreamer(() => source, data.Length))
            using (var server = new LocalStreamServer())
            using (var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) })
            {
                string url = server.Serve("/nas/1/film.mkv", streamer, "video/x-matroska");
                byte[] all = http.GetByteArrayAsync(url).GetAwaiter().GetResult();
                Assert.AreEqual(data, all);

                var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Range = new RangeHeaderValue(1_000_000, 1_000_099);
                var response = http.SendAsync(request).GetAwaiter().GetResult();
                byte[] part = response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
                Assert.AreEqual(data.Skip(1_000_000).Take(100).ToArray(), part);
                Assert.Greater(source.Opens, 1, "should have reconnected after the injected errors");
                Assert.AreEqual(long.MaxValue, streamer.BufferedBytesFrom(1_400_000));
            }
        }

        [Test]
        public void HttpRangeSourceReadsFromRangeServer()
        {
            byte[] data = Data(700_000);
            using (var streamer = new SourceStreamer(() => new MemorySource(data), data.Length))
            using (var server = new LocalStreamServer())
            {
                string url = server.Serve("/f.mp4", streamer, "video/mp4");
                using (var source = new HttpRangeSource(url))
                {
                    source.Open();
                    Assert.AreEqual(data.Length, source.Length);
                    byte[] block = SourceReader.ReadBlock(source, 2, RemoteBlockCache.DefaultBlockSize);
                    Assert.AreEqual(data.Length - 2 * RemoteBlockCache.DefaultBlockSize, block.Length);
                    Assert.AreEqual(data.Skip(2 * RemoteBlockCache.DefaultBlockSize).ToArray(), block);
                    Assert.IsNull(SourceReader.ReadBlock(source, 3, RemoteBlockCache.DefaultBlockSize));
                }
            }
        }

        [Test]
        public void DisposeStopsQuickly()
        {
            var streamer = new SourceStreamer(() => new MemorySource(Data(1000)), 1000);
            Assert.IsNotNull(streamer.WaitForBlock(0, 5000));
            streamer.Dispose();
            Assert.IsNull(streamer.WaitForBlock(0, 100));
        }
    }
}

namespace TheMovies.Tests
{
    using System;
    using System.IO;
    using NUnit.Framework;
    using TheMovies.Core.Network;

    public class FileDownloadTests
    {
        sealed class FlakySource : IRandomAccessSource
        {
            readonly byte[] m_Data;
            readonly int m_FailEvery;
            static int s_Reads;
            public FlakySource(byte[] data, int failEvery) { m_Data = data; m_FailEvery = failEvery; }
            public long Length => m_Data.Length;
            public void Open() { }
            public int Read(long offset, byte[] buffer, int index, int count)
            {
                if (m_FailEvery > 0 && ++s_Reads % m_FailEvery == 0) throw new IOException("weg");
                int n = (int)Math.Min(count, Math.Min(3_000_000, m_Data.Length - offset));
                if (n <= 0) return 0;
                Buffer.BlockCopy(m_Data, (int)offset, buffer, index, n);
                return n;
            }
            public void Dispose() { }
        }

        static string TempDir()
        {
            string dir = Path.Combine(Path.GetTempPath(), "themovies-dl-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        [Test]
        public void CopiesWithRetriesAndResumes()
        {
            var data = new byte[11_000_000];
            new Random(3).NextBytes(data);
            string dir = TempDir();
            string target = Path.Combine(dir, "Film.mkv");

            // A half-finished earlier copy is continued, not restarted.
            File.WriteAllBytes(target + ".part", new ArraySegment<byte>(data, 0, 5_000_000).ToArray());
            var download = new FileDownload(() => new FlakySource(data, 4), target, data.Length);
            download.Start().Wait(20000);
            Assert.IsNull(download.Error);
            Assert.IsTrue(download.Finished);
            Assert.IsFalse(File.Exists(target + ".part"));
            Assert.AreEqual(data, File.ReadAllBytes(target));

            // Already there: finishes immediately.
            var again = new FileDownload(() => throw new InvalidOperationException("must not connect"), target, data.Length);
            again.Start().Wait(5000);
            Assert.IsTrue(again.Finished);
            Directory.Delete(dir, true);
        }

        [Test]
        public void CancelKeepsThePartFile()
        {
            var data = new byte[40_000_000];
            string dir = TempDir();
            string target = Path.Combine(dir, "Gross.mp4");
            var download = new FileDownload(() => new FlakySource(data, 0), target, data.Length);
            download.Cancel();
            download.Start().Wait(5000);
            Assert.IsFalse(download.Finished);
            Assert.AreEqual("Abgebrochen.", download.Error);
            Assert.IsFalse(File.Exists(target));
            Directory.Delete(dir, true);
        }

        [TestCase("Film: Teil 1/2?.mkv", "Film_ Teil 1_2_.mkv")]
        [TestCase("München 3D.mp4", "München 3D.mp4")]
        [TestCase("  ", "film.mp4")]
        public void SafeNames(string input, string expected)
        {
            Assert.AreEqual(expected, FileDownload.SafeFileName(input));
        }
    }
}

namespace TheMovies.Tests
{
    using System;
    using System.Diagnostics;
    using System.IO;
    using System.Threading;
    using NUnit.Framework;
    using TheMovies.Core.Network;
    using TheMovies.Core.Streaming;

    public class StreamerShutdownTests
    {
        /// <summary>Like a NAS connection whose read hangs until the socket is closed.</summary>
        sealed class HangingSource : IRandomAccessSource, IAbortable
        {
            readonly ManualResetEventSlim m_Aborted = new ManualResetEventSlim();
            public static int Disposed;
            public long Length => 10_000_000;
            public void Open() { }
            public int Read(long offset, byte[] buffer, int index, int count)
            {
                m_Aborted.Wait(30000);
                throw new IOException("Verbindung getrennt");
            }
            public void Abort() => m_Aborted.Set();
            public void Dispose()
            {
                // A graceful close would talk to the server: must never run on the caller of streamer.Dispose.
                Thread.Sleep(2000);
                Interlocked.Increment(ref Disposed);
            }
        }

        [Test]
        public void DisposeReturnsAtOnceEvenWithHangingReads()
        {
            var streamer = new SourceStreamer(() => new HangingSource(), 10_000_000);
            Thread.Sleep(200); // workers are now blocked in Read
            var clock = Stopwatch.StartNew();
            streamer.Dispose();
            Assert.Less(clock.ElapsedMilliseconds, 300, "Dispose must not block the main thread");
            Assert.IsNull(streamer.WaitForBlock(0, 100));
        }
    }
}
