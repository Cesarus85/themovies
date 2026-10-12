using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Threading;
using NUnit.Framework;
using TheMovies.Core;
using TheMovies.Core.Network;

namespace TheMovies.Tests
{
    public class JellyfinTests
    {
        const string Base = "http://192.168.1.5:8096/";

        [Test]
        public void ParsesLoginAndServerInfo()
        {
            Assert.IsTrue(Jellyfin.ParseLogin(Json.Parse("{\"User\":{\"Name\":\"anna\",\"Id\":\"u1\"},\"AccessToken\":\"tok\",\"ServerId\":\"s\"}"), out string token, out string user));
            Assert.AreEqual("tok", token);
            Assert.AreEqual("u1", user);
            Assert.IsTrue(Jellyfin.IsServerInfo(Json.Parse("{\"LocalAddress\":\"http://x:8096\",\"ServerName\":\"Kino\",\"Version\":\"10.10.3\",\"ProductName\":\"Jellyfin Server\",\"Id\":\"abc\"}"), out string name));
            Assert.AreEqual("Kino", name);
            Assert.IsFalse(Jellyfin.IsServerInfo(Json.Parse("{\"status\":\"ok\"}"), out _));
            StringAssert.Contains("\"Pw\":\"p\\\"w\"", Jellyfin.LoginBody("anna", "p\"w"));
        }

        [Test]
        public void ParsesViewsAndItems()
        {
            var views = Jellyfin.ParseViews(Json.Parse(
                "{\"Items\":[{\"Name\":\"Filme\",\"Id\":\"v1\",\"CollectionType\":\"movies\",\"ImageTags\":{\"Primary\":\"t1\"}}," +
                "{\"Name\":\"Musik\",\"Id\":\"v2\",\"CollectionType\":\"music\"},{\"Name\":\"Serien\",\"Id\":\"v3\",\"CollectionType\":\"tvshows\"}]}"), Base);
            Assert.AreEqual(2, views.Count);
            Assert.AreEqual("view:movies:v1", views[0].Location);
            Assert.AreEqual(Base + "Items/v1/Images/Primary?fillHeight=360&quality=85&tag=t1", views[0].Poster);
            StringAssert.Contains("Recursive=true&IncludeItemTypes=Movie&", Jellyfin.ItemsQuery("u1", views[0].Location));
            StringAssert.Contains("ParentId=v1", Jellyfin.ItemsQuery("u1", views[0].Location));
            StringAssert.Contains("IncludeItemTypes=Series", Jellyfin.ItemsQuery("u1", views[1].Location));
            StringAssert.DoesNotContain("Recursive", Jellyfin.ItemsQuery("u1", "season7"));

            var items = Jellyfin.ParseItems(Json.Parse(
                "{\"Items\":[" +
                "{\"Name\":\"Avatar\",\"Id\":\"m1\",\"Type\":\"Movie\",\"MediaType\":\"Video\",\"IsFolder\":false,\"ProductionYear\":2009,\"ImageTags\":{\"Primary\":\"p\"}," +
                " \"MediaSources\":[{\"Id\":\"ms1\",\"Path\":\"/media/Filme/Avatar (2009)/Avatar (2009) 3D-SBS.mkv\",\"Container\":\"mkv\",\"Size\":12345678901}]}," +
                "{\"Name\":\"Pilot\",\"Id\":\"e1\",\"Type\":\"Episode\",\"MediaType\":\"Video\",\"IndexNumber\":1,\"ParentIndexNumber\":2,\"ImageTags\":{}," +
                " \"MediaSources\":[{\"Id\":\"ms2\",\"Path\":\"\\\\\\\\nas\\\\serien\\\\folge.avi\",\"Container\":\"avi\",\"Size\":5}]}," +
                "{\"Name\":\"Staffel 1\",\"Id\":\"s1\",\"Type\":\"Season\",\"IsFolder\":true}," +
                "{\"Name\":\"Song\",\"Id\":\"a1\",\"Type\":\"Audio\",\"MediaType\":\"Audio\"}]}"), Base, "tok");
            Assert.AreEqual(3, items.Count);
            Assert.AreEqual("Avatar (2009) 3D-SBS.mkv", items[0].Name);
            Assert.AreEqual("Avatar", items[0].DisplayTitle);
            Assert.AreEqual(2009, items[0].Year);
            Assert.AreEqual(12345678901L, items[0].Size);
            Assert.AreEqual(Base + "Videos/m1/stream?static=true&MediaSourceId=ms1&ApiKey=tok&api_key=tok", items[0].Location);
            Assert.AreEqual("S02E01 · Pilot", items[1].Title);
            Assert.AreEqual("Pilot.mkv", items[1].Name);
            Assert.IsNull(items[1].Poster);
            Assert.IsTrue(items[2].IsFolder);
            Assert.AreEqual("s1", items[2].Location);
        }

        [Test]
        public void ParsesDiscoveryAnswers()
        {
            var s = Jellyfin.ParseDiscoveryAnswer("{\"Address\":\"http://192.168.1.5:8096\",\"Id\":\"x\",\"Name\":\"Wohnzimmer\",\"EndpointAddress\":null}");
            Assert.AreEqual(NetworkSourceKind.Jellyfin, s.Kind);
            Assert.AreEqual("http://192.168.1.5:8096/", s.Address);
            Assert.AreEqual("Wohnzimmer", s.Name);
            Assert.IsNull(Jellyfin.ParseDiscoveryAnswer("nope"));
        }

        [TestCase("http://nas:8096", NetworkSourceKind.Jellyfin, "http://nas:8096/")]
        [TestCase("jellyfin://nas", NetworkSourceKind.Jellyfin, "http://nas:8096/")]
        [TestCase("jellyfin://nas:9000/jf", NetworkSourceKind.Jellyfin, "http://nas:9000/jf/")]
        [TestCase("https://nas:5006/filme", NetworkSourceKind.WebDav, "https://nas:5006/filme/")]
        [TestCase("Plex", NetworkSourceKind.Plex, PlexServers.AccountAddress)]
        public void RecognizesMediaServerAddresses(string input, NetworkSourceKind kind, string address)
        {
            Assert.IsTrue(NetworkSource.TryParse(input, out var s, out string error), error);
            Assert.AreEqual(kind, s.Kind);
            Assert.AreEqual(address, s.Address);
        }

        [Test]
        public void StoresMediaServerSources()
        {
            var list = new List<NetworkSource>
            {
                new NetworkSource { Kind = NetworkSourceKind.Jellyfin, Name = "JF", Address = "http://nas:8096/", User = "anna", Password = "x" },
                new NetworkSource { Kind = NetworkSourceKind.Plex, Name = "Plex", Address = PlexServers.AccountAddress, User = "plex", Password = "token" },
            };
            var back = NetworkSourceStore.Deserialize(NetworkSourceStore.Serialize(list));
            Assert.AreEqual(2, back.Count);
            Assert.AreEqual(NetworkSourceKind.Plex, back[1].Kind);
            Assert.AreEqual("token", back[1].Password);
            Assert.AreEqual(string.Empty, back[1].RootLocation);
        }

        [Test]
        public void DecidesWhatThePlayerOpensDirectly()
        {
            Assert.IsTrue(NetworkBrowser.PlayableDirectly("http://192.168.1.5:8096/Videos/1/stream"));
            Assert.IsTrue(NetworkBrowser.PlayableDirectly("https://192-168-1-5.abc.plex.direct:32400/library/parts/1/file.mkv"));
            Assert.IsFalse(NetworkBrowser.PlayableDirectly("https://192.168.1.5:8920/Videos/1/stream"));
            Assert.IsFalse(NetworkBrowser.PlayableDirectly("smb://nas/filme/a.mkv"));
        }
    }

    /// <summary>JellyfinBrowser against a small fake server (login, headers, libraries, items, search).</summary>
    public class JellyfinBrowserTests
    {
        [Test]
        public void LogsInAndBrowses()
        {
            int port = 18000 + new Random().Next(1000);
            using (var listener = new HttpListener())
            {
                listener.Prefixes.Add($"http://127.0.0.1:{port}/jf/");
                listener.Start();
                var seen = new List<string>();
                var server = new Thread(() =>
                {
                    while (listener.IsListening)
                    {
                        HttpListenerContext ctx;
                        try { ctx = listener.GetContext(); }
                        catch (Exception) { return; }
                        string path = ctx.Request.Url.PathAndQuery;
                        string auth = ctx.Request.Headers["Authorization"] ?? string.Empty;
                        lock (seen) seen.Add(ctx.Request.HttpMethod + " " + path + " " + auth);
                        string body;
                        int status = 200;
                        if (path.StartsWith("/jf/Users/AuthenticateByName"))
                        {
                            string json = new System.IO.StreamReader(ctx.Request.InputStream).ReadToEnd();
                            body = json.Contains("\"Pw\":\"geheim\"") ? "{\"AccessToken\":\"T0K\",\"User\":{\"Id\":\"u9\"}}" : "{}";
                            if (body == "{}") status = 401;
                        }
                        else if (!auth.Contains("Token=\"T0K\"")) { status = 401; body = "{}"; }
                        else if (path.StartsWith("/jf/Users/u9/Views")) body = "{\"Items\":[{\"Name\":\"Filme\",\"Id\":\"v1\",\"CollectionType\":\"movies\"}]}";
                        else if (path.StartsWith("/jf/Users/u9/Items?ParentId=v1")) body = "{\"Items\":[{\"Name\":\"Up\",\"Id\":\"m1\",\"MediaType\":\"Video\",\"MediaSources\":[{\"Path\":\"/f/Up.mp4\",\"Size\":7}]}]}";
                        else if (path.StartsWith("/jf/Users/u9/Items?searchTerm=Up")) body = "{\"Items\":[{\"Name\":\"Up\",\"Id\":\"m1\",\"MediaType\":\"Video\"}]}";
                        else { status = 404; body = "{}"; }
                        byte[] data = Encoding.UTF8.GetBytes(body);
                        ctx.Response.StatusCode = status;
                        ctx.Response.ContentType = "application/json";
                        ctx.Response.OutputStream.Write(data, 0, data.Length);
                        ctx.Response.Close();
                    }
                }) { IsBackground = true };
                server.Start();

                var source = new NetworkSource { Kind = NetworkSourceKind.Jellyfin, Address = $"http://127.0.0.1:{port}/jf/", User = "anna", Password = "falsch" };
                using (var wrong = NetworkBrowser.Create(source))
                    StringAssert.StartsWith("Anmeldung abgelehnt", Assert.Throws<System.IO.IOException>(() => wrong.List(source.RootLocation)).Message);

                source.Password = "geheim";
                using (var browser = NetworkBrowser.Create(source))
                {
                    var views = browser.List(source.RootLocation);
                    Assert.AreEqual("Filme", views[0].Name);
                    var movies = browser.List(views[0].Location);
                    Assert.AreEqual("Up.mp4", movies[0].Name);
                    StringAssert.Contains("ApiKey=T0K", movies[0].Location);
                    Assert.AreEqual(movies[0].Location, browser.DirectUrl(movies[0].Location));
                    Assert.AreEqual(1, ((ISearchableBrowser)browser).Search("Up").Count);
                }
                listener.Stop();
                lock (seen) StringAssert.Contains("DeviceId=", seen[0]);
            }
        }
    }

    public class PlexTests
    {
        [Test]
        public void ParsesResourcesWithConnectionOrder()
        {
            var servers = PlexServers.ParseResources(Json.Parse(
                "[{\"name\":\"Wohnzimmer\",\"provides\":\"server\",\"clientIdentifier\":\"srv1\",\"accessToken\":\"st\",\"connections\":[" +
                "{\"protocol\":\"https\",\"address\":\"84.1.2.3\",\"port\":32400,\"uri\":\"https://84-1-2-3.h.plex.direct:32400\",\"local\":false,\"relay\":false,\"IPv6\":false}," +
                "{\"protocol\":\"https\",\"address\":\"138.1.1.1\",\"port\":8443,\"uri\":\"https://138-1-1-1.h.plex.direct:8443\",\"local\":false,\"relay\":true,\"IPv6\":false}," +
                "{\"protocol\":\"https\",\"address\":\"192.168.1.5\",\"port\":32400,\"uri\":\"https://192-168-1-5.h.plex.direct:32400\",\"local\":true,\"relay\":false,\"IPv6\":false}]}," +
                "{\"name\":\"Handy\",\"provides\":\"client,player\",\"clientIdentifier\":\"c1\",\"connections\":[]}]"));
            Assert.AreEqual(1, servers.Count);
            Assert.AreEqual("st", servers[0].Token);
            CollectionAssert.AreEqual(new[]
            {
                "http://192.168.1.5:32400",
                "https://192-168-1-5.h.plex.direct:32400",
                "https://84-1-2-3.h.plex.direct:32400",
                "https://138-1-1-1.h.plex.direct:8443",
            }, servers[0].Connections);
        }

        [Test]
        public void ParsesSectionsAndMetadata()
        {
            var sections = PlexServers.ParseSections(Json.Parse(
                "{\"MediaContainer\":{\"Directory\":[{\"key\":\"1\",\"title\":\"Filme\",\"type\":\"movie\"},{\"key\":\"2\",\"title\":\"Musik\",\"type\":\"artist\"},{\"key\":\"3\",\"title\":\"Serien\",\"type\":\"show\"}]}}"), "srv1");
            Assert.AreEqual(2, sections.Count);
            Assert.AreEqual("srv1|/library/sections/3/all", sections[1].Location);

            const string b = "http://192.168.1.5:32400";
            var items = PlexServers.ParseMetadata(Json.Parse(
                "{\"MediaContainer\":{\"Metadata\":[" +
                "{\"ratingKey\":\"10\",\"key\":\"/library/metadata/10\",\"type\":\"movie\",\"title\":\"Inception\",\"year\":2010,\"thumb\":\"/library/metadata/10/thumb/1\"," +
                " \"Media\":[{\"container\":\"mkv\",\"Part\":[{\"key\":\"/library/parts/55/1/file.mkv\",\"size\":9000000000,\"file\":\"/data/Filme/Inception (2010).mkv\",\"container\":\"mkv\"}]}]}," +
                "{\"ratingKey\":\"20\",\"key\":\"/library/metadata/20/children\",\"type\":\"show\",\"title\":\"Dark\",\"year\":2017}," +
                "{\"ratingKey\":\"30\",\"key\":\"/library/metadata/30\",\"type\":\"episode\",\"title\":\"Geheimnisse\",\"index\":1,\"parentIndex\":1," +
                " \"Media\":[{\"container\":\"mp4\",\"Part\":[{\"key\":\"/library/parts/77/2/file.mp4\",\"size\":10,\"file\":\"D:\\\\Serien\\\\x.m2ts\"}]}]}]}}")
                .Path("MediaContainer").Array("Metadata"), "srv1", b, "st");
            Assert.AreEqual(3, items.Count);
            Assert.AreEqual("Inception (2010).mkv", items[0].Name);
            Assert.AreEqual("Inception", items[0].Title);
            Assert.AreEqual(b + "/library/parts/55/1/file.mkv?X-Plex-Token=st", items[0].Location);
            Assert.AreEqual(9000000000L, items[0].Size);
            Assert.AreEqual(b + "/photo/:/transcode?width=240&height=360&minSize=1&upscale=1&url=%2Flibrary%2Fmetadata%2F10%2Fthumb%2F1&X-Plex-Token=st", items[0].Poster);
            Assert.IsTrue(items[1].IsFolder);
            Assert.AreEqual("srv1|/library/metadata/20/children", items[1].Location);
            Assert.AreEqual("S01E01 · Geheimnisse", items[2].Title);
            Assert.AreEqual("Geheimnisse.mp4", items[2].Name);
        }

        [Test]
        public void ParsesSearchAndGdm()
        {
            var found = PlexServers.ParseSearch(Json.Parse(
                "{\"MediaContainer\":{\"Hub\":[{\"type\":\"movie\",\"Metadata\":[{\"key\":\"/library/metadata/10\",\"type\":\"movie\",\"title\":\"Inception\"," +
                "\"Media\":[{\"Part\":[{\"key\":\"/p/1\",\"file\":\"/x/Inception.mkv\"}]}]}]},{\"type\":\"artist\",\"Metadata\":[{\"type\":\"artist\",\"title\":\"X\",\"key\":\"/a\"}]}]}}"),
                "s", "http://h:32400", "t");
            Assert.AreEqual(1, found.Count);
            Assert.AreEqual("Inception.mkv", found[0].Name);
            Assert.AreEqual("Wohnzimmer", PlexServers.ParseGdmAnswer("HTTP/1.0 200 OK\r\nContent-Type: plex/media-server\r\nName: Wohnzimmer\r\nPort: 32400\r\n"));
            Assert.IsNull(PlexServers.ParseGdmAnswer("HTTP/1.0 200 OK\r\nContent-Type: plex/media-player\r\nName: TV\r\n"));
        }
    }
}

namespace TheMovies.Tests
{
    public class ArtworkTests
    {
        static NetworkItem F(string name) => new NetworkItem { Name = name, Location = "smb://nas/f/" + name };

        [Test]
        public void FindsPostersNextToMovies()
        {
            var items = new List<NetworkItem>
            {
                F("Avatar (2009).mkv"), F("avatar (2009)-poster.JPG"), F("Avatar (2009).nfo"),
                F("Up.mp4"), F("Up.tbn"), F("Cars.mkv"), F("folder.jpg"), new NetworkItem { Name = "Extras", IsFolder = true },
            };
            Artwork.AttachSidecars(items);
            Assert.AreEqual("smb://nas/f/avatar (2009)-poster.JPG", items[0].Poster);
            Assert.AreEqual("smb://nas/f/Avatar (2009).nfo", items[0].Nfo);
            Assert.AreEqual("smb://nas/f/Up.tbn", items[3].Poster);
            Assert.IsNull(items[5].Poster, "folder.jpg belongs to the folder when there are several movies");

            var single = new List<NetworkItem> { F("Film.mkv"), F("poster.jpg"), F("movie.nfo") };
            Artwork.AttachSidecars(single);
            Assert.AreEqual("smb://nas/f/poster.jpg", single[0].Poster);
            Assert.AreEqual("smb://nas/f/movie.nfo", single[0].Nfo);
            Assert.AreEqual("smb://nas/f/poster.jpg", Artwork.FolderPoster(single));
            Assert.AreEqual("smb://nas/f/X.png", Artwork.FolderPoster(new List<NetworkItem> { F("X.mkv"), F("X.png") }));
            Assert.IsNull(Artwork.FolderPoster(new List<NetworkItem> { F("X.mkv"), F("Y.mkv") }));
        }

        [Test]
        public void ReadsKodiNfo()
        {
            var info = Artwork.ParseNfo("﻿<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\" ?>\n<movie>\n  <title>Die fabelhafte Welt der Am&#233;lie</title>\n" +
                                        "  <originaltitle>Le Fabuleux Destin</originaltitle>\n  <set><title>Sammlung</title></set>\n" +
                                        "  <thumb aspect=\"landscape\">http://img/land.jpg</thumb>\n  <thumb aspect=\"poster\">http://img/poster.jpg</thumb>\n" +
                                        "  <premiered>2001-04-25</premiered>\n</movie>\nhttps://www.imdb.com/title/tt0211915/");
            Assert.AreEqual("Die fabelhafte Welt der Amélie", info.Title);
            Assert.AreEqual(2001, info.Year);
            Assert.AreEqual("http://img/poster.jpg", info.Thumb);
            Assert.IsNull(Artwork.ParseNfo("https://www.imdb.com/title/tt0211915/"));
            Assert.IsNull(Artwork.ParseNfo("<movie><title>kaputt</movie>"));
        }
    }
}

namespace TheMovies.Tests
{
    public class RecentStoreTests
    {
        [Test]
        public void KeepsNewestFirstWithoutDuplicates()
        {
            var store = new RecentStore();
            var t = new DateTime(2026, 1, 1);
            store.Add(new MediaEntry { Title = "A", Location = "/m/a.mkv", FormatHint = StereoFormat.SideBySideHalf }, t);
            store.Add(new MediaEntry { Title = "B\tx", Location = "http://h:8096/Videos/1/stream?a=1&b=2", Name = "B.mkv", SourceId = "jellyfin:http://h:8096/", Poster = "http://h/p.jpg", Size = 5 }, t);
            store.Add(new MediaEntry { Title = "A2", Location = "/m/a.mkv" }, t);
            Assert.AreEqual(2, store.Count);
            Assert.AreEqual("A2", store.Latest.Title);

            var back = RecentStore.Deserialize(store.Serialize());
            Assert.AreEqual(2, back.Count);
            var b = back.Entries[1];
            Assert.AreEqual("B\tx", b.Title);
            Assert.AreEqual("http://h:8096/Videos/1/stream?a=1&b=2", b.Location);
            Assert.AreEqual("B.mkv", b.FileName);
            Assert.IsTrue(b.IsNetwork);
            Assert.AreEqual("http://h/p.jpg", b.Poster);
            Assert.AreEqual(5, b.Size);
            Assert.IsNull(back.Entries[0].SourceId);
            Assert.IsNull(back.Entries[0].FormatHint, "the newer entry of the same movie replaced the old one");
            store.Add(new MediaEntry { Title = "3D", Location = "/m/3d.mkv", FormatHint = StereoFormat.SideBySideHalf }, t);
            Assert.AreEqual(StereoFormat.SideBySideHalf, RecentStore.Deserialize(store.Serialize()).Latest.FormatHint);

            for (int i = 0; i < 40; i++) store.Add(new MediaEntry { Title = "x", Location = "/m/" + i + ".mp4" }, t);
            Assert.AreEqual(RecentStore.MaxEntries, store.Count);
        }
    }
}

namespace TheMovies.Tests
{
    public class DurationFormatTests
    {
        [TestCase(0, "1 min")]
        [TestCase(61, "2 min")]
        [TestCase(3540, "59 min")]
        [TestCase(3900, "1:05 h")]
        public void FormatsRemainingTime(double seconds, string expected) => Assert.AreEqual(expected, TextUtil.FormatDuration(seconds));
    }
}

namespace TheMovies.Tests
{
    public class TextFitTests
    {
        // Monospace for the test: every character is 1 wide, "W" 2.
        static float W(char c) => c == 'W' ? 2f : 1f;

        [Test]
        public void WrapsLongFileNamesAtDots()
        {
            string wrapped = TextFit.Wrap("Avatar.2009.3D.HSBS.1080p", 12, 3, W);
            foreach (string line in wrapped.Split('\n'))
                Assert.LessOrEqual(TextFit.Width(line, W), 12f, line);
            Assert.AreEqual("Avatar.2009.\n3D.HSBS.\n1080p", wrapped);
        }

        [Test]
        public void CutsWhatDoesNotFit()
        {
            Assert.AreEqual("Der Herr der\nRinge – Die…", TextFit.Wrap("Der Herr der Ringe – Die Gefährten", 12, 2, W));
            Assert.AreEqual("Supercalifra\ngilistic", TextFit.Wrap("Supercalifragilistic", 12, 3, W));
            Assert.AreEqual("WWWWW…", TextFit.Ellipsize("WWWWWWWW", 11, W));
            Assert.AreEqual("kurz", TextFit.Ellipsize("kurz", 11, W));
            Assert.AreEqual("Avatar 2009 3D HSBS", TextUtil.PrettyTitle("Avatar.2009.3D.HSBS"));
            Assert.AreEqual("Dr. No", TextUtil.PrettyTitle("Dr. No"));
        }
    }
}

namespace TheMovies.Tests
{
    public class DepthNormalizerTests
    {
        // A "scene": the lower rows are nearer (bigger values), rows arrive top-down like from the model.
        static float[] Scene(int w, int h, float offset, bool topDown = true)
        {
            var raw = new float[w * h];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int fromBottom = topDown ? h - 1 - y : y;
                raw[y * w + x] = offset + 10f * (1f - fromBottom / (float)(h - 1));
            }
            return raw;
        }

        [Test]
        public void NormalizesAndOrientsBottomNear()
        {
            var n = new DepthNormalizer(8, 6);
            for (int i = 0; i < 10; i++) n.Add(Scene(8, 6, 100f));
            Assert.IsTrue(n.OrientationDecided);
            Assert.IsTrue(n.TopDown);
            Assert.Greater(n.Map[0], 0.9f, "bottom row (texture row 0) is near");
            Assert.Less(n.Map[5 * 8], 0.1f, "top row is far");

            var flipped = new DepthNormalizer(8, 6);
            for (int i = 0; i < 10; i++) flipped.Add(Scene(8, 6, 0f, topDown: false));
            Assert.IsFalse(flipped.TopDown);
            Assert.Greater(flipped.Map[0], 0.9f);
        }

        [Test]
        public void SmoothsButFollowsCuts()
        {
            var n = new DepthNormalizer(4, 4) { Smoothing = 0.5f };
            var a = new float[16];
            var b = new float[16];
            for (int i = 0; i < 16; i++)
            {
                a[i] = i < 8 ? 0 : 1;      // top half far, bottom half near
                b[i] = i < 8 ? 1 : 0;      // completely different picture
            }
            n.Add(a);
            float before = n.Map[0];
            n.Add(b);
            Assert.AreNotEqual(before, n.Map[0]);
            Assert.AreEqual(1f - before, n.Map[0], 1e-3f, "a cut replaces the map instead of blending");

            var bytes = new byte[16];
            n.ToBytes(bytes);
            Assert.AreEqual((byte)(n.Map[3] * 255 + 0.5f), bytes[3]);
        }

        [Test]
        public void KeepsFarPartsBelowEyeDistanceAndBringsNearPartsOut()
        {
            foreach (var (width, distance) in new[] { (4.9f, 4.2f), (14f, 12f), (19f, 12f) })
            {
                for (int level = 1; level < Conversion3D.Levels; level++)
                {
                    Conversion3D.Parameters(level, width, distance, out float shift, out float convergence);
                    float far = 2f * shift * convergence * width;            // uncrossed, depth 0
                    float near = 2f * shift * (1f - convergence) * width;    // crossed, depth 1
                    Assert.LessOrEqual(far, Conversion3D.MaxFarSeparation + 1e-4f);
                    double nearDegrees = 2 * Math.Atan(near / 2 / distance) * 180 / Math.PI;
                    Assert.AreEqual(Conversion3D.NearDisparityDegrees(level), nearDegrees, 0.01);
                }
            }
            Conversion3D.Parameters(0, 10f, 10f, out float off, out _);
            Assert.AreEqual(0f, off);
        }
    }
}

namespace TheMovies.Tests
{
    public class SecretStorageTests
    {
        [Test]
        public void LocalSecretRoundTripsAndRejectsOtherKeys()
        {
            string p = LocalSecret.Protect("geheim äöü 🔑", "device-1");
            Assert.AreNotEqual(LocalSecret.Protect("geheim äöü 🔑", "device-1"), p, "random IV");
            StringAssert.DoesNotContain("geheim", p);
            Assert.AreEqual("geheim äöü 🔑", LocalSecret.Unprotect(p, "device-1"));
            Assert.IsNull(LocalSecret.Unprotect(p, "device-2"));
            var bytes = Convert.FromBase64String(p);
            bytes[20] ^= 1;
            Assert.IsNull(LocalSecret.Unprotect(Convert.ToBase64String(bytes), "device-1"), "tampered");
            Assert.IsNull(LocalSecret.Unprotect("kein base64!", "device-1"));
        }

        [Test]
        public void StoresPasswordsProtectedAndMigratesOldEntries()
        {
            // Stand-in for the keystore: anything that changes the text reversibly.
            Func<string, string> protect = s => "X" + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("salt:" + s));
            Func<string, string> unprotect = s => s.StartsWith("X") ? System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(s.Substring(1))).Substring(5) : null;
            var sources = new List<NetworkSource>
            {
                new NetworkSource { Kind = NetworkSourceKind.Smb, Name = "NAS", Address = "smb://nas/filme", User = "anna", Password = "pa\tss" },
                new NetworkSource { Kind = NetworkSourceKind.Smb, Name = "Gast", Address = "smb://nas/gast" },
            };
            string text = NetworkSourceStore.Serialize(sources, protect);
            StringAssert.StartsWith("v2\t", text);
            StringAssert.DoesNotContain(Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("pa\tss")), text);
            var back = NetworkSourceStore.Deserialize(text, unprotect, out bool hadPlain);
            Assert.IsFalse(hadPlain);
            Assert.AreEqual("pa\tss", back[0].Password);
            Assert.AreEqual(string.Empty, back[1].Password);

            // Secret that can't be read any more (app reinstalled): empty password, user logs in again.
            Assert.AreEqual(string.Empty, NetworkSourceStore.Deserialize(text, s => null, out _)[0].Password);

            // Old format from earlier versions is read and reported for re-saving.
            string old = NetworkSourceStore.Serialize(sources);
            StringAssert.StartsWith("v1\t", old);
            var migrated = NetworkSourceStore.Deserialize(old, unprotect, out hadPlain);
            Assert.IsTrue(hadPlain);
            Assert.AreEqual("pa\tss", migrated[0].Password);

            // Protection failing (keystore broken): falls back to the old format instead of losing the password.
            StringAssert.StartsWith("v1\t", NetworkSourceStore.Serialize(sources, s => null));
        }
    }
}

namespace TheMovies.Tests
{
    public class GltfTests
    {
        static byte[] Glb(string json, byte[] bin)
        {
            byte[] j = System.Text.Encoding.UTF8.GetBytes(json);
            int jPad = (4 - j.Length % 4) % 4, bPad = (4 - bin.Length % 4) % 4;
            var ms = new System.IO.MemoryStream();
            var w = new System.IO.BinaryWriter(ms);
            w.Write(0x46546C67u); w.Write(2u); w.Write(0u);
            w.Write((uint)(j.Length + jPad)); w.Write(0x4E4F534Au); w.Write(j); for (int i = 0; i < jPad; i++) w.Write((byte)' ');
            w.Write((uint)(bin.Length + bPad)); w.Write(0x004E4942u); w.Write(bin); for (int i = 0; i < bPad; i++) w.Write((byte)0);
            var bytes = ms.ToArray();
            BitConverter.GetBytes((uint)bytes.Length).CopyTo(bytes, 8);
            return bytes;
        }

        [Test]
        public void ReadsTriangleWithNodeTransformAndMaterial()
        {
            // One triangle (0,0,0) (1,0,0) (0,1,0), uv, indices 0 1 2 (ushort), a parent node moved by +2 on Z, a red material.
            var bin = new System.IO.MemoryStream();
            var bw = new System.IO.BinaryWriter(bin);
            foreach (float f in new float[] { 0, 0, 0, 1, 0, 0, 0, 1, 0 }) bw.Write(f);       // positions: 36 bytes
            foreach (float f in new float[] { 0, 0, 1, 0, 0, 1 }) bw.Write(f);                 // uvs: 24 bytes
            foreach (ushort s in new ushort[] { 0, 1, 2, 0 }) bw.Write(s);                    // indices + pad: 8 bytes
            string json = "{\"asset\":{\"version\":\"2.0\"},\"scene\":0,\"scenes\":[{\"nodes\":[0]}]," +
                          "\"nodes\":[{\"translation\":[0,0,2],\"children\":[1]},{\"name\":\"Trigger\",\"mesh\":0}]," +
                          "\"meshes\":[{\"primitives\":[{\"attributes\":{\"POSITION\":0,\"TEXCOORD_0\":1},\"indices\":2,\"material\":0}]}]," +
                          "\"materials\":[{\"pbrMetallicRoughness\":{\"baseColorFactor\":[1,0,0,1]}}]," +
                          "\"accessors\":[{\"bufferView\":0,\"componentType\":5126,\"count\":3,\"type\":\"VEC3\"}," +
                          "{\"bufferView\":1,\"componentType\":5126,\"count\":3,\"type\":\"VEC2\"}," +
                          "{\"bufferView\":2,\"componentType\":5123,\"count\":3,\"type\":\"SCALAR\"}]," +
                          "\"bufferViews\":[{\"buffer\":0,\"byteOffset\":0,\"byteLength\":36},{\"buffer\":0,\"byteOffset\":36,\"byteLength\":24}," +
                          "{\"buffer\":0,\"byteOffset\":60,\"byteLength\":6}],\"buffers\":[{\"byteLength\":68}]}";
            var model = Gltf.ReadGlb(Glb(json, bin.ToArray()));
            Assert.AreEqual(1, model.Meshes.Count);
            var m = model.Meshes[0];
            Assert.AreEqual("Trigger", m.Name);
            CollectionAssert.AreEqual(new float[] { 0, 0, 2, -1, 0, 2, 0, 1, 2 }, m.Positions, "moved by the parent, X mirrored");
            CollectionAssert.AreEqual(new[] { 0, 2, 1 }, m.Indices, "winding flipped for Unity");
            Assert.AreEqual(1f, m.Uvs[1], "v flipped");
            Assert.AreEqual(0f, m.Uvs[5]);
            CollectionAssert.AreEqual(new float[] { 1, 0, 0, 1 }, m.Color);
            Assert.AreEqual(-1, m.Image);
            Assert.Throws<FormatException>(() => Gltf.ReadGlb(new byte[30]));
        }

        [Test]
        public void ReadsKtx2Header()
        {
            var data = new byte[80 + 24 + 16];
            new byte[] { 0xAB, 0x4B, 0x54, 0x58, 0x20, 0x32, 0x30, 0xBB, 0x0D, 0x0A, 0x1A, 0x0A }.CopyTo(data, 0);
            BitConverter.GetBytes(158u).CopyTo(data, 12);   // ASTC 4x4 sRGB
            BitConverter.GetBytes(4u).CopyTo(data, 20);
            BitConverter.GetBytes(4u).CopyTo(data, 24);
            BitConverter.GetBytes(104ul).CopyTo(data, 80);  // level 0 offset
            BitConverter.GetBytes(16ul).CopyTo(data, 88);   // level 0 length
            data[104] = 7;
            var image = Ktx2Image.Read(data);
            Assert.AreEqual(158, image.VkFormat);
            Assert.AreEqual(4, image.Width);
            Assert.AreEqual(16, image.Level0.Length);
            Assert.AreEqual(7, image.Level0[0]);
            Assert.IsFalse(image.Supercompressed);
        }
    }
}
