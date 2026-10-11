using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using TheMovies.Core.Network;

namespace TheMovies.Tests.Integration
{
    /// <summary>
    /// Runs against a real Samba server when THEMOVIES_SMB is set to "host:port"
    /// (share "filme" for tester/geheim with film.mp4, share "gast" for guests); see tools/CoreTests/README.md.
    /// </summary>
    [TestFixture, Category("Integration")]
    public class SmbIntegrationTests
    {
        static string Host => (Environment.GetEnvironmentVariable("THEMOVIES_SMB") ?? string.Empty).Split(':')[0];
        static int Port => int.Parse((Environment.GetEnvironmentVariable("THEMOVIES_SMB") ?? ":445").Split(':')[1]);

        [SetUp]
        public void RequireServer()
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("THEMOVIES_SMB")))
                Assert.Ignore("THEMOVIES_SMB not set");
        }

        Smb2Client Login(string user = "tester", string password = "geheim")
        {
            var c = new Smb2Client(Host, Port);
            c.Connect();
            c.Login(user, password);
            return c;
        }

        [Test]
        public void LogsInListsAndReads()
        {
            using (var c = Login())
            {
                TestContext.WriteLine($"Dialect 0x{c.Dialect:X4}, signing required {c.ServerRequiresSigning}, max read {c.MaxReadSize}");
                uint tree = c.TreeConnect("filme");
                var entries = c.ListDirectory(tree, "");
                Assert.That(entries.Select(e => e.Name), Is.SupersetOf(new[] { "film.mp4", "film.srt", "Ordner mit Ä" }));
                Assert.That(entries.First(e => e.Name == "Ordner mit Ä").IsDirectory, Is.True);
                Assert.That(entries.First(e => e.Name == "film.mp4").Size, Is.EqualTo(3000000));
                Assert.That(c.ListDirectory(tree, "Ordner mit Ä").Single().Name, Is.EqualTo("datei.txt"));

                byte[] id = c.Open(tree, "film.mp4", false, out long size);
                Assert.That(size, Is.EqualTo(3000000));
                var buffer = new byte[size];
                long pos = 0;
                while (pos < size)
                {
                    int n = c.Read(tree, id, pos, buffer, (int)pos, (int)Math.Min(1 << 20, size - pos));
                    Assert.That(n, Is.GreaterThan(0));
                    pos += n;
                }
                Assert.That(c.Read(tree, id, size, new byte[10], 0, 10), Is.EqualTo(0));
                c.Close(tree, id);
                string local = Environment.GetEnvironmentVariable("THEMOVIES_SMB_FILE");
                if (!string.IsNullOrEmpty(local))
                    Assert.That(buffer, Is.EqualTo(File.ReadAllBytes(local)));
            }
        }

        [Test]
        public void WrongPasswordFails()
        {
            var e = Assert.Throws<SmbException>(() => Login("tester", "falsch").TreeConnect("filme"));
            Assert.That(e.Status, Is.EqualTo(Smb2Client.StatusLogonFailure).Or.EqualTo(Smb2Client.StatusAccessDenied));
        }

        [Test]
        public void GuestShare()
        {
            using (var c = Login("", ""))
            {
                uint tree = c.TreeConnect("gast");
                Assert.That(c.ListDirectory(tree, "").Single().Name, Is.EqualTo("g.mp4"));
            }
        }

        [Test]
        public void MissingShareFails()
        {
            using (var c = Login())
            {
                var e = Assert.Throws<SmbException>(() => c.TreeConnect("gibtsnicht"));
                Assert.That(e.Status, Is.EqualTo(Smb2Client.StatusBadNetworkName));
            }
        }

        [Test]
        public void ListsShares()
        {
            using (var c = Login())
                Assert.That(c.ListShares(), Is.SupersetOf(new[] { "filme", "gast" }));
        }
    }
}
