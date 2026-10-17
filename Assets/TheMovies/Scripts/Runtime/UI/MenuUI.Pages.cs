using System.Collections.Generic;
using System.Text;
using TheMovies.App;
using TheMovies.Core;
using TheMovies.Net;
using TheMovies.Video;
using UnityEngine;

namespace TheMovies.UI
{
    /// <summary>Additional menu pages: "Ton & Untertitel" and "Protokoll".</summary>
    public sealed partial class MenuUI
    {
        const int k_TrackRows = 6;
        const int k_LogLines = 17;

        GameObject m_TracksPage;
        GameObject m_LogPage;
        GameObject m_PicturePage;

        // Picture page
        readonly UIButton[] m_Convert3DButtons = new UIButton[Conversion3D.Levels];
        UIButton m_ShowDepthButton;
        UIButton m_DepthAiButton;
        UIButton m_RealHandsButton, m_ModelHandsButton;
        UILabel m_RealHandsInfo;
        readonly UILabel[] m_PictureValues = new UILabel[3];
        UILabel m_DepthStepsLabel;
        readonly UIButton[] m_GlowButtons = new UIButton[3];
        UILabel m_Convert3DStatus;

        // Tracks page
        UILabel m_TracksInfo;
        UILabel m_TracksPageLabel;
        UILabel m_AudioDelayLabel;
        UILabel m_SubtitleSizeLabel;
        readonly UIButton[] m_SubtitlePlacementButtons = new UIButton[3];
        readonly UIButton[] m_TrackRows = new UIButton[k_TrackRows];
        readonly object[] m_TrackRowItems = new object[k_TrackRows];
        int m_TracksPageIndex;
        float m_NextTracksPoll;
        List<MediaTrack> m_Tracks = new List<MediaTrack>();

        // Log page
        readonly UILabel[] m_LogLabels = new UILabel[k_LogLines];
        UILabel m_LogInfo;
        UIButton m_LogFilterButton, m_LogSessionButton, m_LogMixedTest;
        int m_LogPageIndex;
        bool m_LogProblemsOnly;
        bool m_LogPrevious;
        int m_LogShownVersion = -1;

        sealed class SubtitleChoice
        {
            public string Label;
            public bool Off;
            public bool File;
            public MediaTrack Track;
        }

        void BuildExtraPages()
        {
            m_TracksPage = BuildTracksPage();
            m_LogPage = BuildLogPage();
            m_PicturePage = BuildPicturePage();
        }

        void HideExtraPages()
        {
            m_TracksPage.SetActive(false);
            m_LogPage.SetActive(false);
            m_PicturePage.SetActive(false);
        }

        void RefreshExtraPages()
        {
            if (m_CurrentPage == m_TracksPage) RefreshTracks();
            else if (m_CurrentPage == m_LogPage) RefreshLog();
            else if (m_CurrentPage == m_PicturePage) RefreshPicture();
        }

        // ---------------- Bild & 3D ----------------

        GameObject BuildPicturePage()
        {
            var p = Page("Picture Page");
            Label(p, "Bild & 3D", k_Left, 0.305f, 0.026f, 0, TextAnchor.MiddleLeft, UIStyle.Accent);
            Button(p, "Zurücksetzen", 0.17f, 0.305f, 0.17f, 0.04f, () => m_App.ChangePicture(0, float.NaN), 0.016f, style: ButtonStyle.Subtle);
            Button(p, "Zurück", 0.35f, 0.305f, 0.16f, 0.045f, () => ShowPage(m_SettingsPage), 0.018f, UIIcon.ChevronLeft, ButtonStyle.Subtle);
            Divider(p, 0.272f);

            const float minusX = 0.0f, valueX = 0.18f, plusX = 0.36f, small = 0.045f, step = 0.055f;
            string[] names = { "Helligkeit", "Kontrast", "Farbsättigung" };
            float[] steps = { 0.05f, 0.05f, 0.1f };
            float y = 0.235f;
            for (int i = 0; i < 3; i++)
            {
                int which = i;
                SettingRow(p, names[i], y);
                Button(p, null, minusX, y, small, small, () => m_App.ChangePicture(which, -steps[which]), 0.02f, UIIcon.Minus);
                m_PictureValues[i] = Label(p, string.Empty, valueX, y, 0.019f, 12, TextAnchor.MiddleCenter);
                Button(p, null, plusX, y, small, small, () => m_App.ChangePicture(which, steps[which]), 0.02f, UIIcon.Plus);
                y -= step;
            }
            SettingRow(p, "3D-Tiefe (3D-Filme)", y);
            Button(p, null, minusX, y, small, small, () => m_App.ChangeDepthSteps(-1), 0.02f, UIIcon.Minus);
            m_DepthStepsLabel = Label(p, string.Empty, valueX, y, 0.017f, 18, TextAnchor.MiddleCenter);
            Button(p, null, plusX, y, small, small, () => m_App.ChangeDepthSteps(1), 0.02f, UIIcon.Plus);

            y -= step + 0.012f;
            string[] levels = { "Aus", "Leicht", "Mittel", "Stark" };
            SettingRow(p, "2D → 3D (experimentell)", y, divider: false);
            for (int i = 0; i < levels.Length; i++)
            {
                int level = i;
                m_Convert3DButtons[i] = Button(p, levels[i], -0.08f + i * 0.11f, y, 0.1f, small, () => m_App.SetConvert3D(level), 0.017f);
            }
            m_ShowDepthButton = Button(p, "Tiefenbild", 0.37f, y, 0.13f, small, () => m_App.Screen.ShowDepth = !m_App.Screen.ShowDepth, 0.016f,
                style: ButtonStyle.Subtle);
            y -= 0.045f;
            m_Convert3DStatus = Label(p, string.Empty, k_Left, y, 0.0145f, 74, TextAnchor.MiddleLeft, UIStyle.Accent);
            m_DepthAiButton = Button(p, "KI: An", 0.37f, y, 0.13f, 0.038f, () => m_App.SetDepthAi(!m_App.Screen.UseAiDepth), 0.016f,
                style: ButtonStyle.Subtle);
            Divider(p, y - 0.026f);

            y -= 0.062f;
            SettingRow(p, "Leuchten um die Leinwand", y, divider: false);
            levels = new[] { "Aus", "Schwach", "Stark" };
            for (int i = 0; i < 3; i++)
            {
                int level = i;
                m_GlowButtons[i] = Button(p, levels[i], 0.07f + i * 0.13f, y, 0.12f, small, () => m_App.SetScreenGlow(level), 0.018f);
            }
            Label(p, "Nur für dich: Bild, 3D-Tiefe und 2D→3D. Leuchten: in Mixed Reality strahlen die Randfarben ins Zimmer.",
                k_Left, y - 0.045f, 0.0135f, 0, TextAnchor.MiddleLeft, UIStyle.TextDim);
            Divider(p, y - 0.07f);

            y -= 0.105f;
            SettingRow(p, "Hände & Controller", y, divider: false);
            m_RealHandsButton = Button(p, "Echt (Kamera)", 0.12f, y, 0.19f, small, () => m_App.SetRealHands(true), 0.016f);
            m_ModelHandsButton = Button(p, "Modell", 0.3f, y, 0.13f, small, () => m_App.SetRealHands(false), 0.016f);
            m_RealHandsInfo = Label(p, string.Empty, k_Left, y - 0.045f, 0.0135f, 110, TextAnchor.MiddleLeft, UIStyle.TextDim);
            return p;
        }

        void RefreshPicture()
        {
            var screen = m_App.Screen;
            var settings = m_App.Settings;
            m_PictureValues[0].Text = Mathf.RoundToInt(settings.Brightness * 100) + " %";
            m_PictureValues[1].Text = Mathf.RoundToInt(settings.Contrast * 100) + " %";
            m_PictureValues[2].Text = Mathf.RoundToInt(settings.Saturation * 100) + " %";
            int d = screen.DepthSteps;
            m_DepthStepsLabel.Text = d == 0 ? "0 (Original)" : d > 0 ? $"+{d} (weiter weg)" : $"{d} (näher)";
            for (int i = 0; i < m_Convert3DButtons.Length; i++)
                m_Convert3DButtons[i].Highlighted = screen.Convert3D == i;
            for (int i = 0; i < m_GlowButtons.Length; i++)
                m_GlowButtons[i].Highlighted = screen.GlowLevel == i;
            m_ShowDepthButton.Highlighted = screen.ShowDepth;
            m_RealHandsButton.Highlighted = m_App.RealHands;
            m_ModelHandsButton.Highlighted = !m_App.RealHands;
            m_RealHandsInfo.Text = !m_App.RealHands ? "Hände und Controller als Modell."
                : m_App.ShowingRealHands ? "Deine echten Hände und Controller sind durch die Kamera der Brille zu sehen."
                : "Echt braucht das Passthrough-Feature (wie Mixed Reality) – bis dahin Modelle.";
            m_ShowDepthButton.Interactable = screen.Convert3D > 0;
            m_DepthAiButton.Label = screen.UseAiDepth ? "KI: An" : "KI: Aus";
            m_DepthAiButton.Highlighted = screen.UseAiDepth;
            m_Convert3DStatus.Text = screen.Convert3D == 0 ? "Macht aus 2D-Filmen 3D. „Tiefenbild“ zeigt die Tiefe (hell = nah)." : "Stand: " + screen.Convert3DStatus;
        }

        // ---------------- Ton & Untertitel ----------------

        GameObject BuildTracksPage()
        {
            var p = Page("Tracks Page");
            Label(p, "Ton & Untertitel", k_Left, 0.305f, 0.026f, 0, TextAnchor.MiddleLeft, UIStyle.Accent);
            Button(p, "Zurück", 0.35f, 0.305f, 0.16f, 0.045f, () => ShowPage(m_SettingsPage), 0.018f, UIIcon.ChevronLeft, ButtonStyle.Subtle);
            Divider(p, 0.272f);
            m_TracksInfo = Label(p, string.Empty, k_Left, 0.245f, 0.015f, 80, TextAnchor.MiddleLeft, UIStyle.TextDim);
            m_TracksPageLabel = Label(p, string.Empty, k_Right, 0.245f, 0.016f, 12, TextAnchor.MiddleRight, UIStyle.TextDim);
            for (int i = 0; i < k_TrackRows; i++)
            {
                int index = i;
                m_TrackRows[i] = Button(p, string.Empty, 0, 0.19f - i * 0.058f, k_Width - 0.06f, 0.05f, () => PickTrack(index), 0.019f);
            }
            float sy = -0.172f;
            Label(p, "Untertitel", k_Left, sy, 0.018f, 12, TextAnchor.MiddleLeft, UIStyle.TextDim);
            Button(p, null, -0.27f, sy, 0.045f, 0.045f, () => m_App.ChangeSubtitleSize(-0.1f), 0.02f, UIIcon.Minus);
            m_SubtitleSizeLabel = Label(p, string.Empty, -0.195f, sy, 0.016f, 8, TextAnchor.MiddleCenter);
            Button(p, null, -0.12f, sy, 0.045f, 0.045f, () => m_App.ChangeSubtitleSize(0.1f), 0.02f, UIIcon.Plus);
            for (int i = 0; i < 3; i++)
            {
                int placement = i;
                m_SubtitlePlacementButtons[i] = Button(p, SubtitleService.PlacementNames[i], 0.0f + i * 0.15f, sy, 0.14f, 0.045f,
                    () => m_App.SetSubtitlePlacement(placement), 0.014f);
            }
            Divider(p, sy - 0.04f);

            float y = -0.255f;
            Button(p, "Vorherige", -0.33f, y, 0.15f, 0.05f, () => { m_TracksPageIndex--; RefreshTracks(); }, 0.018f);
            Button(p, "Nächste", -0.17f, y, 0.15f, 0.05f, () => { m_TracksPageIndex++; RefreshTracks(); }, 0.018f);
            Button(p, null, 0.04f, y, 0.05f, 0.05f, () => m_App.ChangeAudioDelay(-25), 0.02f, UIIcon.Minus);
            m_AudioDelayLabel = Label(p, string.Empty, 0.2f, y, 0.017f, 24, TextAnchor.MiddleCenter);
            Button(p, null, 0.36f, y, 0.05f, 0.05f, () => m_App.ChangeAudioDelay(25), 0.02f, UIIcon.Plus);
            Label(p, "Ton-Versatz: + = Ton später, − = Ton früher (25-ms-Schritte, gilt für alle Filme)", 0, -0.31f, 0.0135f, 0,
                TextAnchor.MiddleCenter, UIStyle.TextDim);
            return p;
        }

        void RefreshTracks()
        {
            var player = m_App.Media.Player;
            bool native = player.IsNative && player.State == MovieState.Ready;
            if (Time.unscaledTime >= m_NextTracksPoll)
            {
                m_NextTracksPoll = Time.unscaledTime + 0.5f;
                m_Tracks = native ? player.Tracks() : new List<MediaTrack>();
            }

            var subtitles = m_App.Subtitles;
            var items = new List<object>();
            foreach (var t in m_Tracks)
                if (t.Type == MediaTrackType.Audio) items.Add(t);
            items.Add(new SubtitleChoice { Label = "Untertitel aus", Off = true });
            if (subtitles.HasTrack) items.Add(new SubtitleChoice { Label = "Untertitel: Datei " + subtitles.TrackName, File = true });
            foreach (var t in m_Tracks)
                if (t.Type == MediaTrackType.Subtitle) items.Add(new SubtitleChoice { Label = "Untertitel: " + t.DisplayName, Track = t });

            m_TracksInfo.Text = Playback.Current.Media.IsEmpty ? "Kein Film geladen."
                : !player.IsNative ? "Spurwahl gibt es nur mit dem eigenen Player (Grafik OpenGL ES 3)."
                : m_Tracks.Count == 0 ? "Lädt …" : "Tonspur und Untertitel wählen";

            int pages = Mathf.Max(1, Mathf.CeilToInt(items.Count / (float)k_TrackRows));
            m_TracksPageIndex = Mathf.Clamp(m_TracksPageIndex, 0, pages - 1);
            m_TracksPageLabel.Text = $"{m_TracksPageIndex + 1} / {pages}";
            for (int i = 0; i < k_TrackRows; i++)
            {
                int idx = m_TracksPageIndex * k_TrackRows + i;
                object item = idx < items.Count ? items[idx] : null;
                m_TrackRowItems[i] = item;
                m_TrackRows[i].gameObject.SetActive(item != null);
                switch (item)
                {
                    case MediaTrack audio:
                        m_TrackRows[i].Label = "Ton: " + TextUtil.Ellipsize(audio.DisplayName, 60);
                        m_TrackRows[i].Highlighted = audio.Selected;
                        m_TrackRows[i].Interactable = audio.Supported;
                        break;
                    case SubtitleChoice choice:
                        m_TrackRows[i].Label = TextUtil.Ellipsize(choice.Label, 64);
                        m_TrackRows[i].Highlighted = choice.Off ? !subtitles.Enabled
                            : choice.File ? subtitles.Enabled && !subtitles.PreferEmbedded
                            : subtitles.Enabled && (subtitles.PreferEmbedded || !subtitles.HasTrack) && choice.Track.Selected;
                        m_TrackRows[i].Interactable = choice.Track == null || choice.Track.Supported;
                        break;
                }
            }
            int delay = m_App.Settings.AudioDelayMs;
            m_AudioDelayLabel.Text = "Ton-Versatz " + (delay > 0 ? "+" : string.Empty) + delay + " ms";
            m_SubtitleSizeLabel.Text = Mathf.RoundToInt(m_App.Subtitles.SizeScale * 100) + " %";
            for (int i = 0; i < m_SubtitlePlacementButtons.Length; i++)
                m_SubtitlePlacementButtons[i].Highlighted = m_App.Subtitles.Placement == i;
        }

        void PickTrack(int index)
        {
            var player = m_App.Media.Player;
            var subtitles = m_App.Subtitles;
            switch (m_TrackRowItems[index])
            {
                case MediaTrack audio:
                    player.SelectTrack(audio);
                    break;
                case SubtitleChoice choice when choice.Off:
                    m_App.SetSubtitles(false);
                    player.DisableEmbeddedSubtitles();
                    break;
                case SubtitleChoice choice when choice.File:
                    m_App.SetSubtitles(true);
                    subtitles.PreferEmbedded = false;
                    player.DisableEmbeddedSubtitles();
                    break;
                case SubtitleChoice choice:
                    m_App.SetSubtitles(true);
                    subtitles.PreferEmbedded = true;
                    player.SelectTrack(choice.Track);
                    break;
            }
            m_NextTracksPoll = Time.unscaledTime + 0.3f; // the player needs a moment to switch
            RefreshTracks();
        }

        // ---------------- Protokoll ----------------

        GameObject BuildLogPage()
        {
            var p = Page("Log Page");
            Label(p, "Protokoll", k_Left, 0.305f, 0.026f, 0, TextAnchor.MiddleLeft, UIStyle.Accent);
            Button(p, "Zurück", 0.35f, 0.305f, 0.16f, 0.045f, () => ShowPage(m_SettingsPage), 0.018f, UIIcon.ChevronLeft, ButtonStyle.Subtle);
            Divider(p, 0.272f);
            for (int i = 0; i < k_LogLines; i++)
                m_LogLabels[i] = Label(p, string.Empty, k_Left, 0.248f - i * 0.0285f, 0.0125f, 118, TextAnchor.MiddleLeft, UIStyle.Text);
            m_LogInfo = Label(p, string.Empty, k_Left, -0.25f, 0.013f, 110, TextAnchor.MiddleLeft, UIStyle.TextDim);
            var row = Row(p, -0.3f, 0.045f, 0.01f,
                ("Älter", 0.11f, () => { m_LogPageIndex++; m_LogShownVersion = -1; }),
                ("Neuer", 0.11f, () => { m_LogPageIndex = Mathf.Max(0, m_LogPageIndex - 1); m_LogShownVersion = -1; }),
                ("Neueste", 0.12f, () => { m_LogPageIndex = 0; m_LogShownVersion = -1; }),
                ("Nur Probleme", 0.16f, () => { m_LogProblemsOnly = !m_LogProblemsOnly; m_LogPageIndex = 0; m_LogShownVersion = -1; }),
                ("Vorige Sitzung", 0.18f, () => { m_LogPrevious = !m_LogPrevious; m_LogPageIndex = 0; m_LogShownVersion = -1; }),
                // Passthrough test modes (diagnosis), only while mixed reality is on.
                ("MR-Test", 0.12f, MixedReality.CycleTestMode));
            m_LogFilterButton = row[3];
            m_LogSessionButton = row[4];
            m_LogMixedTest = row[5];
            return p;
        }

        void OpenLog()
        {
            m_LogPageIndex = 0;
            m_LogShownVersion = -1;
            ShowPage(m_LogPage);
        }

        void RefreshLog()
        {
            var buffer = m_LogPrevious ? AppLog.Previous : AppLog.Buffer;
            int version = buffer.Version * 4 + (m_LogProblemsOnly ? 1 : 0) + (m_LogPrevious ? 2 : 0);
            m_LogFilterButton.Highlighted = m_LogProblemsOnly;
            m_LogSessionButton.Highlighted = m_LogPrevious;
            m_LogMixedTest.Interactable = MixedReality.Enabled;
            m_LogMixedTest.Label = MixedReality.Enabled ? $"MR-Test {MixedReality.TestMode + 1}/{MixedReality.TestModes}" : "MR-Test";
            if (version == m_LogShownVersion) return;
            m_LogShownVersion = version;

            var entries = buffer.Snapshot(m_LogProblemsOnly);
            int pages = Mathf.Max(1, Mathf.CeilToInt(entries.Count / (float)k_LogLines));
            m_LogPageIndex = Mathf.Clamp(m_LogPageIndex, 0, pages - 1);
            var lines = LogBuffer.Page(entries, m_LogPageIndex, k_LogLines, 118);
            for (int i = 0; i < k_LogLines; i++)
            {
                // Bottom-aligned: the newest line sits at the bottom of the page.
                int li = i - (k_LogLines - lines.Count);
                string line = li >= 0 ? lines[li] : string.Empty;
                m_LogLabels[i].Text = line;
                m_LogLabels[i].Color = line.Length > 9 && line[9] == 'F' ? UIStyle.Warning
                    : line.Length > 9 && line[9] == 'W' ? UIStyle.Accent : UIStyle.Text;
            }
            var info = new StringBuilder();
            info.Append(m_LogPrevious ? "Vorige Sitzung" : "Diese Sitzung").Append("  ·  Seite ").Append(m_LogPageIndex + 1).Append(" / ").Append(pages);
            if (AppLog.FilePath != null) info.Append("  ·  ").Append(AppLog.FilePath);
            m_LogInfo.Text = info.ToString();
        }
    }
}
