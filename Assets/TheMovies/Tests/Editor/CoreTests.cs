using System.Collections.Generic;
using NUnit.Framework;
using TheMovies.Core;

namespace TheMovies.Tests
{
    public class ClockSyncTests
    {
        [Test]
        public void EstimatesOffsetFromSymmetricPing()
        {
            var clock = new ClockSync();
            // host clock is 100 s ahead, one-way latency 40 ms
            Assert.IsTrue(clock.AddSample(10.00, 110.04, 10.08));
            Assert.IsTrue(clock.HasEstimate);
            Assert.AreEqual(100.0, clock.Offset, 1e-9);
            Assert.AreEqual(0.08, clock.BestRtt, 1e-9);
            Assert.AreEqual(115.0, clock.ToHost(15.0), 1e-9);
            Assert.AreEqual(15.0, clock.ToLocal(115.0), 1e-9);
        }

        [Test]
        public void PrefersFastSamplesOverSlowOnes()
        {
            var clock = new ClockSync();
            clock.AddSample(0.0, 100.02, 0.04);   // rtt 40 ms, offset 100.00
            clock.AddSample(1.0, 101.40, 1.80);   // rtt 800 ms, asymmetric -> offset 100.0 but noisy
            clock.AddSample(2.0, 102.25, 2.30);   // rtt 300 ms, asymmetric -> offset 100.10
            Assert.AreEqual(100.0, clock.Offset, 1e-9);
            Assert.AreEqual(0.04, clock.BestRtt, 1e-9);
        }

        [Test]
        public void RejectsNegativeRoundTrip()
        {
            var clock = new ClockSync();
            Assert.IsFalse(clock.AddSample(5.0, 50.0, 4.0));
            Assert.IsFalse(clock.HasEstimate);
        }

        [Test]
        public void DropsOldSamples()
        {
            var clock = new ClockSync(maxSamples: 2);
            clock.AddSample(0, 10.01, 0.02);   // offset 10, very fast
            clock.AddSample(1, 21.05, 1.10);   // offset 20
            clock.AddSample(2, 22.05, 2.10);   // offset 20 - the fast sample is gone now
            Assert.AreEqual(2, clock.SampleCount);
            Assert.AreEqual(20.0, clock.Offset, 1e-9);
        }
    }

    public class PlaybackTimelineTests
    {
        [Test]
        public void PausedTimelineStaysPut()
        {
            var t = new PlaybackTimeline { AnchorMediaTime = 42, AnchorHostTime = 10 };
            Assert.AreEqual(42, t.MediaTimeAt(1000), 1e-9);
        }

        [Test]
        public void PlayStartsAfterDelay()
        {
            var t = new PlaybackTimeline { AnchorMediaTime = 5 }.WithPlay(hostNow: 100, startDelay: 0.5);
            Assert.IsTrue(t.Playing);
            Assert.IsTrue(t.IsPreRoll(100.2));
            Assert.AreEqual(5, t.MediaTimeAt(100.2), 1e-9);
            Assert.IsFalse(t.IsPreRoll(100.5));
            Assert.AreEqual(7, t.MediaTimeAt(102.5), 1e-9);
        }

        [Test]
        public void PauseFreezesCurrentPosition()
        {
            var t = new PlaybackTimeline().WithPlay(0, 0).WithPause(30);
            Assert.IsFalse(t.Playing);
            Assert.AreEqual(30, t.MediaTimeAt(99), 1e-9);
            Assert.AreEqual(2, t.Revision);
        }

        [Test]
        public void SeekWhilePlayingSchedulesStart()
        {
            var t = new PlaybackTimeline().WithPlay(0, 0).WithSeek(10, 600, 0.4);
            Assert.IsTrue(t.Playing);
            Assert.AreEqual(600, t.MediaTimeAt(10.2), 1e-9);
            Assert.AreEqual(601, t.MediaTimeAt(11.4), 1e-9);
        }

        [Test]
        public void SeekClampsNegative()
        {
            var t = new PlaybackTimeline().WithSeek(0, -5, 0);
            Assert.AreEqual(0, t.AnchorMediaTime, 1e-9);
        }
    }

    public class SyncControllerTests
    {
        static LocalPlayerState Local(double time, bool playing, bool canSpeed = true, double duration = 7200) =>
            new LocalPlayerState
            {
                MediaTime = time,
                IsPlaying = playing,
                Duration = duration,
                CanSetSpeed = canSpeed,
                SeekLatency = 0.3,
            };

        [Test]
        public void InSyncDoesNothing()
        {
            var sync = new SyncController();
            var t = new PlaybackTimeline().WithPlay(0, 0);
            var cmd = sync.Update(t, 100, Local(100.01, true));
            Assert.IsTrue(cmd.ShouldPlay);
            Assert.IsFalse(cmd.Seek);
            Assert.AreEqual(1f, cmd.Speed);
        }

        [Test]
        public void SmallDriftIsCorrectedBySpeed()
        {
            var sync = new SyncController();
            var t = new PlaybackTimeline().WithPlay(0, 0);

            var behind = sync.Update(t, 100, Local(99.8, true));
            Assert.IsFalse(behind.Seek);
            Assert.Greater(behind.Speed, 1f);
            Assert.LessOrEqual(behind.Speed, 1.05f + 1e-6f);

            sync.Reset();
            var ahead = sync.Update(t, 100, Local(100.2, true));
            Assert.IsFalse(ahead.Seek);
            Assert.Less(ahead.Speed, 1f);
        }

        [Test]
        public void SpeedCorrectionHasHysteresis()
        {
            var sync = new SyncController();
            var t = new PlaybackTimeline().WithPlay(0, 0);
            Assert.AreNotEqual(1f, sync.Update(t, 100, Local(99.9, true)).Speed);   // starts correcting
            Assert.AreNotEqual(1f, sync.Update(t, 101, Local(100.97, true)).Speed); // 30 ms: still correcting
            Assert.AreEqual(1f, sync.Update(t, 102, Local(101.995, true)).Speed);   // 5 ms: done
            Assert.AreEqual(1f, sync.Update(t, 103, Local(102.97, true)).Speed);    // 30 ms: below start threshold
        }

        [Test]
        public void LargeDriftSeeksAheadByLatency()
        {
            var sync = new SyncController();
            var t = new PlaybackTimeline().WithPlay(0, 0);
            var cmd = sync.Update(t, 100, Local(50, true));
            Assert.IsTrue(cmd.Seek);
            Assert.AreEqual(100.3, cmd.SeekTarget, 1e-9);
        }

        [Test]
        public void SeekCooldownPreventsSeekStorm()
        {
            var sync = new SyncController();
            var t = new PlaybackTimeline().WithPlay(0, 0);
            Assert.IsTrue(sync.Update(t, 100, Local(98, true)).Seek);
            Assert.IsFalse(sync.Update(t, 100.5, Local(98.9, true)).Seek, "within cooldown and drift not extreme");
            Assert.IsTrue(sync.Update(t, 100.6, Local(90, true)).Seek, "extreme drift ignores cooldown");
        }

        [Test]
        public void RepeatedMissedSeeksBackOff()
        {
            var sync = new SyncController();
            var t = new PlaybackTimeline().WithPlay(0, 0);
            Assert.IsTrue(sync.Update(t, 100, Local(97, true)).Seek);       // first correction
            Assert.IsTrue(sync.Update(t, 102.5, Local(100, true)).Seek);    // landed 2.5 s off again
            Assert.AreEqual(4.0, sync.CurrentSeekCooldown, 1e-9);
            Assert.IsFalse(sync.Update(t, 105, Local(103, true)).Seek, "waits for the longer cooldown");
            Assert.IsTrue(sync.Update(t, 107, Local(105, true)).Seek);
            sync.Update(t, 110, Local(110.01, true));                       // in sync again
            Assert.AreEqual(2.0, sync.CurrentSeekCooldown, 1e-9);
        }

        [Test]
        public void WithoutSpeedControlSeeksEarlier()
        {
            var sync = new SyncController();
            var t = new PlaybackTimeline().WithPlay(0, 0);
            Assert.IsFalse(sync.Update(t, 100, Local(99.9, true, canSpeed: false)).Seek);
            Assert.IsTrue(sync.Update(t, 100, Local(99.5, true, canSpeed: false)).Seek);
        }

        [Test]
        public void PausedParksOnExactFrame()
        {
            var sync = new SyncController();
            var t = new PlaybackTimeline { AnchorMediaTime = 600 };
            var cmd = sync.Update(t, 5, Local(612, false));
            Assert.IsFalse(cmd.ShouldPlay);
            Assert.IsTrue(cmd.Seek);
            Assert.AreEqual(600, cmd.SeekTarget, 1e-9);

            var ok = sync.Update(t, 6, Local(600.03, false));
            Assert.IsFalse(ok.Seek);
        }

        [Test]
        public void PreRollWaitsAtAnchor()
        {
            var sync = new SyncController();
            var t = new PlaybackTimeline { AnchorMediaTime = 10 }.WithPlay(hostNow: 50, startDelay: 0.5);
            var cmd = sync.Update(t, 50.2, Local(10, false));
            Assert.IsFalse(cmd.ShouldPlay);
            Assert.IsFalse(cmd.Seek);
            var go = sync.Update(t, 50.5, Local(10, false));
            Assert.IsTrue(go.ShouldPlay);
        }

        [Test]
        public void WaitsWhileSeeking()
        {
            var sync = new SyncController();
            var t = new PlaybackTimeline().WithPlay(0, 0);
            var local = Local(3, true);
            local.IsSeeking = true;
            var cmd = sync.Update(t, 100, local);
            Assert.IsFalse(cmd.Seek);
            Assert.IsTrue(cmd.ShouldPlay);
        }

        [Test]
        public void StopsAtEnd()
        {
            var sync = new SyncController();
            var t = new PlaybackTimeline().WithPlay(0, 0);
            var cmd = sync.Update(t, 130, Local(119.97, true, duration: 120));
            Assert.IsTrue(cmd.ReachedEnd);
            Assert.IsFalse(cmd.ShouldPlay);
            Assert.IsFalse(cmd.Seek);
        }
    }

    public class StereoFormatTests
    {
        [TestCase("Avatar.2009.3D.HSBS.1080p.mkv", 1920, 1080, StereoFormat.SideBySideHalf)]
        [TestCase("Movie.3D.Half-SBS.mp4", 0, 0, StereoFormat.SideBySideHalf)]
        [TestCase("Movie.3D.H-SBS.mp4", 0, 0, StereoFormat.SideBySideHalf)]
        [TestCase("Movie_Full-SBS.mp4", 0, 0, StereoFormat.SideBySideFull)]
        [TestCase("Movie.SBS.mp4", 3840, 1080, StereoFormat.SideBySideFull)]
        [TestCase("Movie.SBS.mp4", 1920, 1080, StereoFormat.SideBySideHalf)]
        [TestCase("Movie.3D.HOU.mp4", 1920, 1080, StereoFormat.TopBottomHalf)]
        [TestCase("Movie.3D.TAB.mp4", 1920, 2160, StereoFormat.TopBottomFull)]
        [TestCase("Movie over under.mp4", 0, 0, StereoFormat.TopBottomHalf)]
        [TestCase("Movie.3D.mp4", 3840, 1080, StereoFormat.SideBySideFull)]
        [TestCase("Movie.3D.mp4", 0, 0, StereoFormat.Mono)]
        [TestCase("Household.mp4", 1920, 1080, StereoFormat.Mono)]
        [TestCase("Holiday.1080p.mp4", 1920, 1080, StereoFormat.Mono)]
        [TestCase(null, 0, 0, StereoFormat.Mono)]
        public void DetectsFormat(string name, int w, int h, StereoFormat expected)
        {
            Assert.AreEqual(expected, StereoFormats.Detect(name, w, h));
        }

        [Test]
        public void DisplayAspect()
        {
            Assert.AreEqual(16.0 / 9.0, StereoFormat.SideBySideHalf.DisplayAspect(1920, 1080), 1e-9);
            Assert.AreEqual(16.0 / 9.0, StereoFormat.SideBySideFull.DisplayAspect(3840, 1080), 1e-9);
            Assert.AreEqual(16.0 / 9.0, StereoFormat.TopBottomFull.DisplayAspect(1920, 2160), 1e-9);
            Assert.AreEqual(16.0 / 9.0, StereoFormat.Mono.DisplayAspect(0, 0), 1e-9);
        }

        [Test]
        public void EyeUvs()
        {
            var l = StereoFormat.SideBySideHalf.EyeUv(0, false);
            var r = StereoFormat.SideBySideHalf.EyeUv(1, false);
            Assert.AreEqual(0f, l.OffsetX);
            Assert.AreEqual(0.5f, r.OffsetX);
            Assert.AreEqual(0.5f, StereoFormat.SideBySideHalf.EyeUv(0, true).OffsetX, "swapped");

            var top = StereoFormat.TopBottomHalf.EyeUv(0, false);
            Assert.AreEqual(0.5f, top.OffsetY, "left eye is the upper half");
            Assert.AreEqual(0.5f, top.ScaleY);

            var mono = StereoFormat.Mono.EyeUv(1, true);
            Assert.AreEqual(1f, mono.ScaleX);
            Assert.AreEqual(0f, mono.OffsetX);
        }

        [Test]
        public void NextCyclesThroughAll()
        {
            var f = StereoFormat.Mono;
            for (int i = 0; i < StereoFormats.All.Length; i++)
                f = f.Next();
            Assert.AreEqual(StereoFormat.Mono, f);
        }
    }

    public class MediaMatcherTests
    {
        static List<MediaEntry> Library() => new List<MediaEntry>
        {
            new MediaEntry { Title = "a", Location = "/sdcard/Movies/Avatar.3D.HSBS.mkv", Size = 100 },
            new MediaEntry { Title = "b", Location = "/sdcard/Download/avatar 3d hsbs.mkv", Size = 999 },
            new MediaEntry { Title = "c", Location = "/sdcard/Movies/Other.mp4", Size = 555 },
            new MediaEntry { Title = "d", Location = "https://x/y.mp4", IsUrl = true },
        };

        [Test]
        public void ExactNameAndSize() =>
            Assert.AreEqual("a", MediaMatcher.FindBest("Avatar.3D.HSBS.mkv", 100, Library()).Title);

        [Test]
        public void NameIgnoringCase() =>
            Assert.AreEqual("a", MediaMatcher.FindBest("avatar.3d.hsbs.MKV", 1, Library()).Title);

        [Test]
        public void SizeWhenRenamed() =>
            Assert.AreEqual("c", MediaMatcher.FindBest("Renamed.mp4", 555, Library()).Title);

        [Test]
        public void FuzzyName() =>
            Assert.AreEqual("a", MediaMatcher.FindBest("Avatar_3D_HSBS.mp4", 0, Library()).Title);

        [Test]
        public void NoMatch() =>
            Assert.IsNull(MediaMatcher.FindBest("Nope.mp4", 1, Library()));

        [Test]
        public void VideoExtensions()
        {
            Assert.IsTrue(MediaMatcher.IsVideoFile("/a/b.MKV"));
            Assert.IsFalse(MediaMatcher.IsVideoFile("/a/b.srt"));
        }
    }

    public class PlaylistParserTests
    {
        [Test]
        public void ParsesAllVariants()
        {
            const string text = "﻿# my list\n" +
                                "https://example.com/movies/Big%20Buck.mp4\r\n" +
                                "\n" +
                                "Avatar | https://example.com/avatar.mp4 | fsbs\n" +
                                "Broken | ftp://nope\n" +
                                " Spaces |  https://example.com/s.mp4  \n";
            var list = PlaylistParser.Parse(text);
            Assert.AreEqual(3, list.Count);
            Assert.AreEqual("Big Buck.mp4", list[0].Title);
            Assert.IsTrue(list[0].IsUrl);
            Assert.IsNull(list[0].FormatHint);
            Assert.AreEqual("Avatar", list[1].Title);
            Assert.AreEqual(StereoFormat.SideBySideFull, list[1].FormatHint);
            Assert.AreEqual("https://example.com/s.mp4", list[2].Location);
        }

        [Test]
        public void EmptyText() => Assert.AreEqual(0, PlaylistParser.Parse(null).Count);
    }

    public class TextUtilTests
    {
        [TestCase(0, "0:00")]
        [TestCase(59.9, "0:59")]
        [TestCase(61, "1:01")]
        [TestCase(3725, "1:02:05")]
        [TestCase(-3, "0:00")]
        [TestCase(double.NaN, "0:00")]
        public void FormatTime(double s, string expected) => Assert.AreEqual(expected, TextUtil.FormatTime(s));

        [Test]
        public void RoomCode() => Assert.AreEqual("AB12CD", TextUtil.NormalizeRoomCode(" ab-12 cd "));

        [Test]
        public void Ellipsize()
        {
            Assert.AreEqual("Hello", TextUtil.Ellipsize("Hello", 5));
            Assert.AreEqual("Hel…", TextUtil.Ellipsize("Hello", 4));
        }

        [Test]
        public void Hosts()
        {
            Assert.IsTrue(TextUtil.IsPlausibleHost("192.168.0.12"));
            Assert.IsTrue(TextUtil.IsPlausibleHost("my-pc.local"));
            Assert.IsFalse(TextUtil.IsPlausibleHost("http://x"));
            Assert.IsFalse(TextUtil.IsPlausibleHost(""));
        }

        [Test]
        public void ChannelName() => Assert.AreEqual("TM_AB12", TextUtil.SafeChannelName("TM_AB 12!"));
    }
}
