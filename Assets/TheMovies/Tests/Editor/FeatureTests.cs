using System;
using System.Text;
using NUnit.Framework;
using TheMovies.Core;

namespace TheMovies.Tests
{
    public class SubtitleTests
    {
        const string k_Srt =
            "1\r\n00:00:01,000 --> 00:00:03,500\r\nHallo <i>Welt</i>!\r\n\r\n" +
            "2\n00:00:04,000 --> 00:00:06,000\n{\\an8}Zweite Zeile\nmit Umbruch\n\n" +
            "3\n00:00:05.500 --> 00:00:07,25\nÜberlappung\n" +
            "4\n00:01:00,000 --> 00:01:02,000\nOhne Leerzeile davor\n";

        [Test]
        public void ParsesCues()
        {
            var track = SrtParser.Parse(k_Srt);
            Assert.AreEqual(4, track.Count);
            Assert.AreEqual(1.0, track.Cues[0].Start, 1e-9);
            Assert.AreEqual(3.5, track.Cues[0].End, 1e-9);
            Assert.AreEqual("Hallo Welt!", track.Cues[0].Text);
            Assert.AreEqual("Zweite Zeile\nmit Umbruch", track.Cues[1].Text);
            Assert.AreEqual(7.25, track.Cues[2].End, 1e-9);
            Assert.AreEqual("Ohne Leerzeile davor", track.Cues[3].Text);
        }

        [Test]
        public void LooksUpByTime()
        {
            var track = SrtParser.Parse(k_Srt);
            Assert.IsNull(track.TextAt(0.5));
            Assert.AreEqual("Hallo Welt!", track.TextAt(2));
            Assert.IsNull(track.TextAt(3.7));
            Assert.AreEqual("Zweite Zeile\nmit Umbruch\nÜberlappung", track.TextAt(5.7));
            Assert.AreEqual("Ohne Leerzeile davor", track.TextAt(61));
            Assert.AreEqual("Hallo Welt!", track.TextAt(1.5), "seeking backwards");
        }

        [Test]
        public void DecodesUtf8AndLatin1()
        {
            Assert.AreEqual("Grüße", SrtParser.Decode(Encoding.UTF8.GetBytes("Grüße")));
            var withBom = new byte[] { 0xEF, 0xBB, 0xBF, (byte)'a' };
            Assert.AreEqual("a", SrtParser.Decode(withBom));
            var latin1 = new byte[] { (byte)'G', (byte)'r', 0xFC, 0xDF, 0x65, 0x84 };
            Assert.AreEqual("Grüße„", SrtParser.Decode(latin1));
        }

        [Test]
        public void WrapsLongLines()
        {
            Assert.AreEqual("eins zwei\ndrei", SrtParser.Wrap("eins zwei drei", 10));
            Assert.AreEqual("kurz\nzweite", SrtParser.Wrap("kurz\nzweite", 10));
        }

        [Test]
        public void EmptyInput() => Assert.AreEqual(0, SrtParser.Parse(null).Count);
    }

    public class ResumeStoreTests
    {
        static readonly DateTime k_Now = new DateTime(2026, 10, 1, 20, 0, 0, DateTimeKind.Utc);

        [Test]
        public void RecordsAndRestores()
        {
            var store = new ResumeStore();
            store.Record("Movie.mkv", 3725.5, 7200, k_Now);
            Assert.AreEqual(3725.5, store.Get("Movie.mkv"));
            var copy = ResumeStore.Deserialize(store.Serialize());
            Assert.AreEqual(3725.5, copy.Get("Movie.mkv"));
            Assert.IsFalse(copy.IsDirty);
        }

        [Test]
        public void IgnoresStartAndClearsWhenFinished()
        {
            var store = new ResumeStore();
            store.Record("a", 30, 7200, k_Now);
            Assert.IsNull(store.Get("a"));
            store.Record("a", 600, 7200, k_Now);
            Assert.AreEqual(600, store.Get("a"));
            store.Record("a", 7150, 7200, k_Now);
            Assert.IsNull(store.Get("a"), "near the end counts as finished");
        }

        [Test]
        public void KeepsOnlyNewestEntries()
        {
            var store = new ResumeStore();
            for (int i = 0; i < ResumeStore.MaxEntries + 5; i++)
                store.Record("m" + i, 100, 0, k_Now.AddMinutes(i));
            Assert.AreEqual(ResumeStore.MaxEntries, store.Count);
            Assert.IsNull(store.Get("m0"));
            Assert.IsNotNull(store.Get("m" + (ResumeStore.MaxEntries + 4)));
        }

        [Test]
        public void SkipsBrokenLines()
        {
            var store = ResumeStore.Deserialize("garbage\n1\t2\n5\t120.5\t0\thttps://x/y.mp4\n");
            Assert.AreEqual(1, store.Count);
            Assert.AreEqual(120.5, store.Get("https://x/y.mp4"));
        }
    }
}
