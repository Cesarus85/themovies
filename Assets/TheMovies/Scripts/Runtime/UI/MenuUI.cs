using System;
using System.Collections.Generic;
using System.Text;
using TheMovies.App;
using TheMovies.Core;
using TheMovies.Net;
using TheMovies.Video;
using TheMovies.World;
using TheMovies.XR;
using UnityEngine;

namespace TheMovies.UI
{
    /// <summary>
    /// The floating 3D menu in front of the seat: main menu, on-screen keyboard, playback controls
    /// and the movie browser. Built entirely in code from <see cref="UIKit"/> widgets.
    /// </summary>
    public sealed class MenuUI : MonoBehaviour
    {
        const float k_Width = 0.92f;
        const float k_Height = 0.6f;
        const float k_Left = -k_Width * 0.5f + 0.03f;
        const float k_Right = k_Width * 0.5f - 0.03f;
        const int k_BrowserRows = 7;

        AppController m_App;
        Transform m_Root;
        GameObject m_MainPage;
        GameObject m_KeyboardPage;
        GameObject m_SessionPage;
        GameObject m_BrowserPage;
        GameObject m_CurrentPage;
        bool m_Solo;
        bool m_Visible = true;
        float m_AutoHideAt = -1;
        float m_NextRefresh;

        // Main page
        UILabel m_MainName;
        UILabel m_MainStatus;
        UIButton m_HostOnline, m_JoinOnline, m_HostLan, m_JoinLan, m_SoloButton;

        // Keyboard page
        UILabel m_KeyTitle;
        UILabel m_KeyInput;
        UILabel m_KeyHint;
        readonly List<(UIButton button, char key)> m_Keys = new List<(UIButton, char)>();
        UIButton m_ShiftButton;
        UIButton m_SpaceButton;
        KeyboardRequest m_KeyRequest;
        StringBuilder m_KeyText = new StringBuilder();
        bool m_Shift;

        // Session page
        UILabel m_RoomLabel;
        UILabel m_VoiceLabel;
        UILabel m_TitleLabel;
        UILabel m_InfoLabel;
        UILabel m_ProblemLabel;
        UIButton m_AssignButton;
        UISlider m_Seek;
        UILabel m_TimeLabel;
        UIButton m_PlayButton;
        UIButton m_FormatButton;
        UIButton m_SwapButton;
        UIButton m_SizeButton;
        UIButton m_LightButton;
        UIButton m_MicButton;
        UIButton m_LeaveButton;
        UILabel m_SessionStatus;
        readonly UILabel[] m_Participants = new UILabel[CinemaEnvironment.MaxParticipants];
        readonly List<UIButton> m_TransportButtons = new List<UIButton>();

        // Browser page
        UILabel m_BrowserTitle;
        UILabel m_BrowserInfo;
        UILabel m_PageLabel;
        readonly UIButton[] m_Rows = new UIButton[k_BrowserRows];
        readonly MediaEntry[] m_RowEntries = new MediaEntry[k_BrowserRows];
        int m_BrowserPageIndex;
        bool m_BrowserAssignMode;
        UIButton m_UrlButton;

        sealed class KeyboardRequest
        {
            public string Title;
            public string Hint;
            public string Initial;
            public int MaxLength;
            public bool AllowLowercase;
            public bool AllowSpace;
            public Func<char, bool> Accepts;
            public Action<string> Submit;
        }

        public bool Visible => m_Visible;

        public static MenuUI Create(AppController app)
        {
            var go = new GameObject("Menu");
            var menu = go.AddComponent<MenuUI>();
            menu.m_App = app;
            menu.Build();
            return menu;
        }

        void Build()
        {
            m_Root = transform;
            UIFactory.CreateQuad(m_Root, "Panel", new Vector3(0, 0, UIStyle.Layer), new Vector2(k_Width, k_Height), UIStyle.Panel, UIFactory.PanelMaterial);
            // A collider behind everything so pressing on the panel background does not count as "pressed on nothing".
            var blocker = UIFactory.CreateEmpty(m_Root, "Blocker", new Vector3(0, 0, 0.01f));
            blocker.AddComponent<BoxCollider>().size = new Vector3(k_Width, k_Height, 0.005f);

            m_MainPage = BuildMainPage();
            m_KeyboardPage = BuildKeyboardPage();
            m_SessionPage = BuildSessionPage();
            m_BrowserPage = BuildBrowserPage();

            m_MainPage.SetActive(false);
            m_KeyboardPage.SetActive(false);
            m_SessionPage.SetActive(false);
            m_BrowserPage.SetActive(false);
            ShowPage(m_MainPage);

            LaserPointer.PressedOnNothing += OnPressedOnNothing;
        }

        void OnDestroy()
        {
            LaserPointer.PressedOnNothing -= OnPressedOnNothing;
        }

        // ---------------- Visibility & placement ----------------

        public void Show()
        {
            m_Visible = true;
            m_AutoHideAt = -1;
            gameObject.SetActive(true);
            Place();
        }

        public void Hide()
        {
            m_Visible = false;
            m_AutoHideAt = -1;
            gameObject.SetActive(false);
        }

        public void Toggle()
        {
            if (m_Visible) Hide();
            else Show();
        }

        void OnPressedOnNothing()
        {
            if (!m_Visible) Show();
        }

        /// <summary>Floats in front of the seat, low and tilted like a remote control.</summary>
        public void Place()
        {
            var rig = XRRig.Instance;
            if (rig == null) return;
            Pose seat = rig.Seat;
            bool xr = rig.XRActive;
            Vector3 local = xr ? new Vector3(0, 0.82f, 0.55f) : new Vector3(0, 1.0f, 0.85f);
            float tilt = xr ? 35f : 8f;
            transform.SetPositionAndRotation(seat.position + seat.rotation * local, seat.rotation * Quaternion.Euler(tilt, 0, 0));
        }

        // ---------------- Pages ----------------

        GameObject Page(string name)
        {
            var go = UIFactory.CreateEmpty(m_Root, name, Vector3.zero);
            return go;
        }

        void ShowPage(GameObject page)
        {
            if (m_CurrentPage == page) return;
            if (m_CurrentPage != null) m_CurrentPage.SetActive(false);
            m_CurrentPage = page;
            page.SetActive(true);
            Refresh();
        }

        static UIButton Button(GameObject page, string label, float x, float y, float w, float h, Action onClick, float textHeight = 0.024f) =>
            UIButton.Create(page.transform, label, new Vector3(x, y, -UIStyle.Layer), new Vector2(w, h), onClick, textHeight);

        static UILabel Label(GameObject page, string text, float x, float y, float height, int maxChars,
            TextAnchor anchor = TextAnchor.MiddleLeft, Color? color = null) =>
            new UILabel(page.transform, text, new Vector3(x, y, -2 * UIStyle.Layer), height, maxChars, anchor, color);

        /// <summary>Lays out buttons of the given widths centered in a row.</summary>
        static UIButton[] Row(GameObject page, float y, float h, float gap, params (string label, float width, Action action)[] items)
        {
            float total = -gap;
            foreach (var it in items) total += it.width + gap;
            float x = -total * 0.5f;
            var result = new UIButton[items.Length];
            for (int i = 0; i < items.Length; i++)
            {
                result[i] = Button(page, items[i].label, x + items[i].width * 0.5f, y, items[i].width, h, items[i].action, 0.021f);
                x += items[i].width + gap;
            }
            return result;
        }

        GameObject BuildMainPage()
        {
            var p = Page("Main Page");
            Label(p, "THE MOVIES", 0, 0.24f, 0.055f, 0, TextAnchor.MiddleCenter, UIStyle.Accent);
            Label(p, "Gemeinsam Kino schauen – mit bis zu 4 Personen", 0, 0.192f, 0.02f, 0, TextAnchor.MiddleCenter, UIStyle.TextDim);

            m_MainName = Label(p, string.Empty, k_Left, 0.125f, 0.026f, 40);
            Button(p, "Name ändern", 0.33f, 0.125f, 0.2f, 0.055f, EditName, 0.02f);

            Label(p, "Über das Internet (Raum-Code)", k_Left, 0.06f, 0.019f, 0, TextAnchor.MiddleLeft, UIStyle.TextDim);
            m_HostOnline = Button(p, "Kino eröffnen", -0.217f, 0.005f, 0.42f, 0.075f, () => m_App.Sessions.HostOnline());
            m_JoinOnline = Button(p, "Mit Code beitreten", 0.217f, 0.005f, 0.42f, 0.075f, EnterRoomCode);

            Label(p, "Im selben WLAN (ohne Unity-Dienste)", k_Left, -0.065f, 0.019f, 0, TextAnchor.MiddleLeft, UIStyle.TextDim);
            m_HostLan = Button(p, "LAN-Kino eröffnen", -0.217f, -0.115f, 0.42f, 0.06f, () => m_App.Sessions.HostLan(), 0.021f);
            m_JoinLan = Button(p, "Per IP beitreten", 0.217f, -0.115f, 0.42f, 0.06f, EnterLanHost, 0.021f);

            m_SoloButton = Button(p, "Allein schauen", -0.217f, -0.195f, 0.42f, 0.06f, StartSolo, 0.021f);
            Button(p, "Menü ausblenden", 0.217f, -0.195f, 0.42f, 0.06f, Hide, 0.021f);

            m_MainStatus = Label(p, string.Empty, 0, -0.258f, 0.018f, 70, TextAnchor.MiddleCenter, UIStyle.TextDim);
            return p;
        }

        GameObject BuildKeyboardPage()
        {
            var p = Page("Keyboard Page");
            m_KeyTitle = Label(p, string.Empty, 0, 0.255f, 0.03f, 50, TextAnchor.MiddleCenter, UIStyle.Accent);
            UIFactory.CreateQuad(p.transform, "Input", new Vector3(0, 0.185f, -UIStyle.Layer), new Vector2(0.8f, 0.07f), UIStyle.PanelInset, UIFactory.WidgetMaterial);
            m_KeyInput = Label(p, string.Empty, 0, 0.185f, 0.036f, 34, TextAnchor.MiddleCenter);

            string[] rows = { "1234567890", "QWERTZUIOP", "ASDFGHJKLÖ", "YXCVBNMÄÜ.", "-_:/@&?=%+" };
            float keyW = 0.074f, step = 0.081f, keyH = 0.055f;
            for (int r = 0; r < rows.Length; r++)
            {
                string row = rows[r];
                float y = 0.11f - r * 0.064f;
                float x0 = -(row.Length - 1) * step * 0.5f;
                for (int i = 0; i < row.Length; i++)
                {
                    char c = row[i];
                    var b = Button(p, c.ToString(), x0 + i * step, y, keyW, keyH, () => TypeKey(c), 0.026f);
                    m_Keys.Add((b, c));
                }
            }

            var bottom = Row(p, -0.215f, 0.06f, 0.012f,
                ("Umschalt", 0.15f, ToggleShift),
                ("Leerzeichen", 0.2f, () => TypeKey(' ')),
                ("Löschen", 0.14f, Backspace),
                ("Abbrechen", 0.15f, CancelKeyboard),
                ("OK", 0.15f, SubmitKeyboard));
            m_ShiftButton = bottom[0];
            m_SpaceButton = bottom[1];
            bottom[4].Highlighted = true;

            m_KeyHint = Label(p, string.Empty, 0, -0.268f, 0.017f, 80, TextAnchor.MiddleCenter, UIStyle.TextDim);
            return p;
        }

        GameObject BuildSessionPage()
        {
            var p = Page("Session Page");
            m_RoomLabel = Label(p, string.Empty, k_Left, 0.255f, 0.03f, 30, TextAnchor.MiddleLeft, UIStyle.Accent);
            m_VoiceLabel = Label(p, string.Empty, k_Right, 0.255f, 0.017f, 40, TextAnchor.MiddleRight, UIStyle.TextDim);

            m_TitleLabel = Label(p, string.Empty, k_Left, 0.205f, 0.027f, 30);
            m_InfoLabel = Label(p, string.Empty, k_Left, 0.168f, 0.018f, 44, TextAnchor.MiddleLeft, UIStyle.TextDim);
            for (int i = 0; i < m_Participants.Length; i++)
                m_Participants[i] = Label(p, string.Empty, 0.06f, 0.215f - i * 0.026f, 0.017f, 34);

            m_ProblemLabel = Label(p, string.Empty, k_Left, 0.1f, 0.017f, 62, TextAnchor.MiddleLeft, UIStyle.Warning);
            m_AssignButton = Button(p, "Datei zuordnen", 0.345f, 0.1f, 0.19f, 0.042f, () => OpenBrowser(true), 0.017f);

            m_Seek = UISlider.Create(p.transform, new Vector3(-0.085f, 0.04f, -UIStyle.Layer), 0.64f, 0.016f);
            m_Seek.Committed += v => Playback.Current.RequestSeek(v * m_App.Media.DisplayDuration);
            m_TimeLabel = Label(p, string.Empty, k_Right, 0.04f, 0.02f, 24, TextAnchor.MiddleRight);

            var transport = Row(p, -0.04f, 0.075f, 0.014f,
                ("-30 s", 0.12f, () => Playback.Current.RequestSeekRelative(-30)),
                ("-10 s", 0.12f, () => Playback.Current.RequestSeekRelative(-10)),
                ("Abspielen", 0.24f, PlayPause),
                ("+10 s", 0.12f, () => Playback.Current.RequestSeekRelative(10)),
                ("+30 s", 0.12f, () => Playback.Current.RequestSeekRelative(30)));
            m_PlayButton = transport[2];
            m_PlayButton.Highlighted = true;
            m_TransportButtons.AddRange(transport);

            var row2 = Row(p, -0.125f, 0.06f, 0.012f,
                ("Film wählen", 0.2f, () => OpenBrowser(false)),
                ("Format", 0.24f, () => m_App.Media.CycleFormat()),
                ("Augen tauschen", 0.19f, () => m_App.Media.ToggleSwapEyes()),
                ("Leinwand", 0.21f, CycleScreenSize));
            m_FormatButton = row2[1];
            m_SwapButton = row2[2];
            m_SizeButton = row2[3];

            var row3 = Row(p, -0.198f, 0.055f, 0.01f,
                ("Licht", 0.14f, CycleLights),
                ("Leiser", 0.1f, () => m_App.ChangeVolume(-0.1f)),
                ("Lauter", 0.1f, () => m_App.ChangeVolume(0.1f)),
                ("Mikro", 0.14f, ToggleMic),
                ("Ausrichten", 0.13f, () => XRRig.Instance?.RequestAlign()),
                ("Ausblenden", 0.13f, Hide),
                ("Verlassen", 0.12f, Leave));
            m_LightButton = row3[0];
            m_MicButton = row3[3];
            m_LeaveButton = row3[6];
            m_LeaveButton.BaseColor = UIStyle.Danger;

            m_SessionStatus = Label(p, string.Empty, 0, -0.262f, 0.016f, 90, TextAnchor.MiddleCenter, UIStyle.TextDim);
            return p;
        }

        GameObject BuildBrowserPage()
        {
            var p = Page("Browser Page");
            m_BrowserTitle = Label(p, string.Empty, k_Left, 0.258f, 0.028f, 40, TextAnchor.MiddleLeft, UIStyle.Accent);
            m_BrowserInfo = Label(p, string.Empty, k_Left, 0.222f, 0.015f, 95, TextAnchor.MiddleLeft, UIStyle.TextDim);
            for (int i = 0; i < k_BrowserRows; i++)
            {
                int index = i;
                m_Rows[i] = Button(p, string.Empty, 0, 0.17f - i * 0.058f, k_Width - 0.06f, 0.05f, () => PickRow(index), 0.019f);
            }
            var bottom = Row(p, -0.255f, 0.05f, 0.012f,
                ("Zurück", 0.13f, () => ShowPage(m_SessionPage)),
                ("Vorherige", 0.13f, () => TurnPage(-1)),
                ("Nächste", 0.13f, () => TurnPage(1)),
                ("Aktualisieren", 0.15f, () => m_App.Media.Library.Refresh()),
                ("Adresse eingeben", 0.18f, EnterStreamUrl));
            m_UrlButton = bottom[4];
            m_PageLabel = Label(p, string.Empty, k_Right, 0.258f, 0.018f, 12, TextAnchor.MiddleRight, UIStyle.TextDim);
            return p;
        }

        // ---------------- Actions ----------------

        void StartSolo()
        {
            m_Solo = true;
            ShowPage(m_SessionPage);
        }

        void Leave()
        {
            if (m_App.Sessions.State != ConnectionState.Offline)
            {
                m_App.Sessions.Leave();
            }
            else
            {
                m_Solo = false;
                ShowPage(m_MainPage);
            }
        }

        void PlayPause()
        {
            bool willPlay = !Playback.Current.Timeline.Playing;
            m_App.Media.TogglePlay();
            if (willPlay && !Playback.Current.Media.IsEmpty)
                m_AutoHideAt = Time.unscaledTime + 1.2f;
        }

        void CycleScreenSize()
        {
            int next = (m_App.Screen.SizeIndex + 1) % ScreenController.Widths.Length;
            m_App.SetScreenSize(next);
        }

        void CycleLights()
        {
            var media = m_App.Media;
            media.Lights = (LightMode)(((int)media.Lights + 1) % 3);
        }

        void ToggleMic() => m_App.SetMicMuted(!VoiceChat.Muted);

        void EditName()
        {
            OpenKeyboard(new KeyboardRequest
            {
                Title = "Dein Name",
                Hint = "So sehen dich die anderen im Kino.",
                Initial = m_App.Settings.Name,
                MaxLength = 20,
                AllowLowercase = true,
                AllowSpace = true,
                Accepts = c => true,
                Submit = name => m_App.SetName(name),
            });
        }

        void EnterRoomCode()
        {
            OpenKeyboard(new KeyboardRequest
            {
                Title = "Raum-Code eingeben",
                Hint = "Den Code zeigt der Gastgeber oben in seinem Menü an.",
                Initial = m_App.Settings.LastRoomCode,
                MaxLength = 12,
                AllowLowercase = false,
                AllowSpace = false,
                Accepts = c => (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9'),
                Submit = code =>
                {
                    m_App.Settings.LastRoomCode = TextUtil.NormalizeRoomCode(code);
                    m_App.Settings.Save();
                    m_App.Sessions.JoinOnline(code);
                },
            });
        }

        void EnterLanHost()
        {
            OpenKeyboard(new KeyboardRequest
            {
                Title = "IP-Adresse des Gastgebers",
                Hint = "Steht beim Gastgeber oben im Menü, z. B. 192.168.0.23",
                Initial = m_App.Settings.LastLanHost,
                MaxLength = 40,
                AllowLowercase = true,
                AllowSpace = false,
                Accepts = c => char.IsLetterOrDigit(c) && c < 128 || c == '.' || c == '-',
                Submit = host =>
                {
                    m_App.Settings.LastLanHost = host;
                    m_App.Settings.Save();
                    m_App.Sessions.JoinLan(host);
                },
            });
        }

        void EnterStreamUrl()
        {
            OpenKeyboard(new KeyboardRequest
            {
                Title = "Stream-Adresse (http/https)",
                Hint = "Alle im Kino brauchen Zugriff auf diese Adresse. Mehrere Adressen: urls.txt verwenden.",
                Initial = "https://",
                MaxLength = 300,
                AllowLowercase = true,
                AllowSpace = false,
                Accepts = c => c > ' ' && c < 127,
                Submit = url =>
                {
                    if (!MediaMatcher.LooksLikeUrl(url))
                    {
                        m_App.Media.ReportProblem("Ungültige Adresse: " + url);
                        return;
                    }
                    var parsed = PlaylistParser.Parse(url);
                    var entry = new MediaEntry { Title = parsed.Count > 0 ? parsed[0].Title : url, Location = url, IsUrl = true };
                    if (!m_App.Media.Select(entry, out string error))
                        m_App.Media.ReportProblem(error);
                },
            });
        }

        // ---------------- Keyboard ----------------

        void OpenKeyboard(KeyboardRequest request)
        {
            m_KeyRequest = request;
            m_KeyText.Clear();
            m_KeyText.Append(request.Initial ?? string.Empty);
            m_Shift = false;
            m_KeyTitle.Text = request.Title;
            m_KeyHint.Text = request.Hint;
            foreach (var (button, key) in m_Keys)
                button.Interactable = request.Accepts(key) || (request.AllowLowercase && request.Accepts(char.ToLowerInvariant(key)));
            m_ShiftButton.Interactable = request.AllowLowercase;
            m_SpaceButton.Interactable = request.AllowSpace;
            UpdateKeyLabels();
            ShowPage(m_KeyboardPage);
        }

        void TypeKey(char c)
        {
            if (m_KeyRequest == null || m_KeyText.Length >= m_KeyRequest.MaxLength) return;
            if (c != ' ' && m_KeyRequest.AllowLowercase && !m_Shift)
                c = char.ToLowerInvariant(c);
            if (c == ' ' && !m_KeyRequest.AllowSpace) return;
            if (c != ' ' && !m_KeyRequest.Accepts(c)) return;
            m_KeyText.Append(c);
            if (m_Shift)
            {
                m_Shift = false;
                UpdateKeyLabels();
            }
            m_KeyInput.Text = m_KeyText + "_";
        }

        void Backspace()
        {
            if (m_KeyText.Length > 0) m_KeyText.Length--;
            m_KeyInput.Text = m_KeyText + "_";
        }

        void ToggleShift()
        {
            m_Shift = !m_Shift;
            UpdateKeyLabels();
        }

        void UpdateKeyLabels()
        {
            bool lower = m_KeyRequest != null && m_KeyRequest.AllowLowercase && !m_Shift;
            foreach (var (button, key) in m_Keys)
                button.Label = (lower ? char.ToLowerInvariant(key) : key).ToString();
            m_ShiftButton.Highlighted = m_Shift;
            m_KeyInput.Text = m_KeyText + "_";
        }

        void CancelKeyboard()
        {
            m_KeyRequest = null;
            ShowPage(m_Solo || m_App.Sessions.State != ConnectionState.Offline ? m_SessionPage : m_MainPage);
        }

        void SubmitKeyboard()
        {
            var request = m_KeyRequest;
            m_KeyRequest = null;
            string text = m_KeyText.ToString().Trim();
            ShowPage(m_Solo || m_App.Sessions.State != ConnectionState.Offline ? m_SessionPage : m_MainPage);
            if (request != null && text.Length > 0)
                request.Submit(text);
        }

        // ---------------- Browser ----------------

        void OpenBrowser(bool assignMode)
        {
            m_BrowserAssignMode = assignMode;
            m_BrowserPageIndex = 0;
            m_App.Media.Library.Refresh();
            ShowPage(m_BrowserPage);
            RefreshBrowser();
        }

        void TurnPage(int delta)
        {
            int pages = Mathf.Max(1, Mathf.CeilToInt(m_App.Media.Library.Entries.Count / (float)k_BrowserRows));
            m_BrowserPageIndex = Mathf.Clamp(m_BrowserPageIndex + delta, 0, pages - 1);
            RefreshBrowser();
        }

        void PickRow(int index)
        {
            var entry = m_RowEntries[index];
            if (entry == null) return;
            if (m_BrowserAssignMode)
            {
                m_App.Media.AssignLocalFile(entry);
            }
            else if (!m_App.Media.Select(entry, out string error))
            {
                m_BrowserInfo.Text = error;
                return;
            }
            ShowPage(m_SessionPage);
        }

        void RefreshBrowser()
        {
            var library = m_App.Media.Library;
            var entries = library.Entries;
            if (m_BrowserAssignMode)
            {
                string wanted = Playback.Current.Media.Title.ToString();
                m_BrowserTitle.Text = "Eigene Datei für: " + wanted;
            }
            else
            {
                m_BrowserTitle.Text = "Film wählen";
            }

            m_BrowserInfo.Text = library.IsScanning
                ? "Suche Filme …"
                : entries.Count == 0
                    ? "Keine Filme gefunden. Lege Videos in Movies/ oder Download/ ab, Streams in urls.txt."
                    : "Gesucht in Movies, Download und " + MediaLibrary.AppMoviesFolder;

            m_UrlButton.gameObject.SetActive(!m_BrowserAssignMode);

            int pages = Mathf.Max(1, Mathf.CeilToInt(entries.Count / (float)k_BrowserRows));
            m_BrowserPageIndex = Mathf.Clamp(m_BrowserPageIndex, 0, pages - 1);
            m_PageLabel.Text = $"{m_BrowserPageIndex + 1} / {pages}";

            for (int i = 0; i < k_BrowserRows; i++)
            {
                int idx = m_BrowserPageIndex * k_BrowserRows + i;
                var entry = idx < entries.Count ? entries[idx] : null;
                m_RowEntries[i] = entry;
                m_Rows[i].gameObject.SetActive(entry != null);
                if (entry == null) continue;

                var format = entry.FormatHint ?? StereoFormats.Detect(entry.FileName);
                var label = new StringBuilder(TextUtil.Ellipsize(entry.Title, 48));
                if (entry.IsUrl) label.Append("   ·  Stream");
                else if (entry.Size > 0) label.Append("   ·  ").Append(TextUtil.FormatSize(entry.Size));
                if (format.IsStereo()) label.Append("   ·  ").Append(format.DisplayName());
                m_Rows[i].Label = label.ToString();
            }
        }

        // ---------------- Per-frame refresh ----------------

        void Update()
        {
            if (m_AutoHideAt > 0 && Time.unscaledTime >= m_AutoHideAt)
            {
                Hide();
                return;
            }
            // 15 updates per second are plenty and keep string garbage low; a dragged slider updates every frame.
            if (Time.unscaledTime >= m_NextRefresh || m_Seek.IsDragging)
            {
                m_NextRefresh = Time.unscaledTime + 1f / 15f;
                Refresh();
            }
        }

        public void OnSessionChanged()
        {
            var state = m_App.Sessions.State;
            if (state == ConnectionState.Connected)
            {
                m_Solo = false;
                if (m_CurrentPage == m_MainPage) ShowPage(m_SessionPage);
            }
            else if (state == ConnectionState.Offline && !m_Solo && (m_CurrentPage == m_SessionPage || m_CurrentPage == m_BrowserPage))
            {
                ShowPage(m_MainPage);
            }
            if (!m_Visible && state == ConnectionState.Offline) Show();
        }

        void Refresh()
        {
            if (m_CurrentPage == m_MainPage) RefreshMain();
            else if (m_CurrentPage == m_SessionPage) RefreshSession();
            else if (m_CurrentPage == m_BrowserPage) RefreshBrowser();
        }

        void RefreshMain()
        {
            var sessions = m_App.Sessions;
            bool idle = sessions.State == ConnectionState.Offline;
            m_MainName.Text = "Dein Name: " + m_App.Settings.Name;
            m_HostOnline.Interactable = idle && SessionManager.OnlineAvailable;
            m_JoinOnline.Interactable = idle && SessionManager.OnlineAvailable;
            m_HostLan.Interactable = idle;
            m_JoinLan.Interactable = idle;
            m_SoloButton.Interactable = idle;
            m_SoloButton.Label = LocalAuthority.Instance.Media.IsEmpty ? "Allein schauen" : "Weiter allein schauen";
            m_MainStatus.Text = sessions.Status.Length > 0
                ? sessions.Status
                : SessionManager.OnlineAvailable ? "Bereit." : "Online-Modus: Paket 'Multiplayer Services' fehlt – LAN geht trotzdem.";
            m_MainStatus.Color = sessions.Status.Length > 0 && idle && sessions.Status.Contains("fehl") ? UIStyle.Error : UIStyle.TextDim;
        }

        void RefreshSession()
        {
            var sessions = m_App.Sessions;
            var auth = Playback.Current;
            var media = auth.Media;
            var coordinator = m_App.Media;
            bool networked = auth.IsNetworked;

            if (!networked)
                m_RoomLabel.Text = sessions.State == ConnectionState.Connecting ? "Verbinde …" : "Allein im Kino";
            else if (sessions.Kind == SessionKind.Online)
                m_RoomLabel.Text = "Raum-Code: " + sessions.RoomCode;
            else
                m_RoomLabel.Text = (sessions.IsHost ? "LAN-IP: " : "LAN: ") + sessions.RoomCode;

            m_VoiceLabel.Text = networked ? VoiceChat.Status : string.Empty;

            // Movie info
            if (media.IsEmpty)
            {
                m_TitleLabel.Text = "Noch kein Film gewählt";
                m_InfoLabel.Text = "Tippe auf „Film wählen“.";
            }
            else
            {
                m_TitleLabel.Text = media.Title.ToString();
                string res = coordinator.Player.State == MovieState.Ready ? $"{coordinator.Player.Width}×{coordinator.Player.Height}" : string.Empty;
                m_InfoLabel.Text = media.StereoFormat.DisplayName() + (media.SwapEyes ? " (getauscht)" : string.Empty) +
                                   (res.Length > 0 ? "  ·  " + res : string.Empty);
            }

            bool missing = !media.IsEmpty && (coordinator.LocalState == ParticipantMediaState.Missing || coordinator.LocalState == ParticipantMediaState.Error);
            m_ProblemLabel.Text = coordinator.Problem;
            m_AssignButton.gameObject.SetActive(missing);

            // Seek bar and time
            double duration = coordinator.DisplayDuration;
            double time = coordinator.DisplayTime;
            if (!m_Seek.IsDragging)
                m_Seek.SetValueWithoutNotify(duration > 0 ? (float)(time / duration) : 0f);
            m_Seek.Interactable = !media.IsEmpty && duration > 0;
            double shownTime = m_Seek.IsDragging ? m_Seek.Value * duration : time;
            m_TimeLabel.Text = media.IsEmpty ? string.Empty : $"{TextUtil.FormatTime(shownTime)} / {(duration > 0 ? TextUtil.FormatTime(duration) : "–")}";

            bool playing = auth.Timeline.Playing;
            m_PlayButton.Label = playing ? "Pause" : "Abspielen";
            foreach (var b in m_TransportButtons) b.Interactable = !media.IsEmpty;
            m_FormatButton.Label = media.IsEmpty ? "Format" : media.StereoFormat.DisplayName();
            m_FormatButton.Interactable = !media.IsEmpty;
            m_SwapButton.Interactable = !media.IsEmpty && media.StereoFormat.IsStereo();
            m_SwapButton.Highlighted = media.SwapEyes;
            m_SizeButton.Label = "Leinwand: " + ScreenController.SizeNames[m_App.Screen.SizeIndex];
            m_LightButton.Label = coordinator.Lights == LightMode.Auto ? "Licht: Auto" : coordinator.Lights == LightMode.On ? "Licht: an" : "Licht: aus";
            m_MicButton.Label = VoiceChat.Muted ? "Mikro: aus" : "Mikro: an";
            m_MicButton.Interactable = networked && VoiceChat.Compiled;
            m_LeaveButton.Label = networked || sessions.State != ConnectionState.Offline ? "Verlassen" : "Hauptmenü";

            RefreshParticipants(networked);

            // Status line
            var cmd = coordinator.LastCommand;
            string sync = coordinator.LocalState == ParticipantMediaState.Ready && networked
                ? $"Sync {cmd.Drift * 1000:+0;-0;0} ms · Ping {auth.RoundTrip * 1000:0} ms  ·  "
                : string.Empty;
            m_SessionStatus.Text = sync + $"Lautstärke {Mathf.RoundToInt(m_App.Settings.Volume * 100)} %  ·  Menü: Trigger ins Leere / B / Y";
        }

        void RefreshParticipants(bool networked)
        {
            var players = NetworkPlayer.All;
            int line = 0;
            if (networked)
            {
                // Sorted by seat for a stable order.
                for (int seat = 0; seat < CinemaEnvironment.MaxParticipants && line < m_Participants.Length; seat++)
                {
                    foreach (var p in players)
                    {
                        if (p.Seat != seat || line >= m_Participants.Length) continue;
                        var label = m_Participants[line++];
                        label.Text = "● " + p.DisplayName + (p.IsOwner ? " (du)" : string.Empty) + (p.IsHostPlayer ? " ★" : string.Empty) +
                                     " – " + Describe(p.Status, p.IsOwner);
                        label.Color = Color.Lerp(p.Color, Color.white, 0.25f);
                    }
                }
            }
            else
            {
                var label = m_Participants[line++];
                label.Text = "● " + m_App.Settings.Name + " (du)";
                label.Color = UIStyle.Text;
            }

            for (; line < m_Participants.Length; line++)
                m_Participants[line].Text = string.Empty;
        }

        static string Describe(ParticipantStatus s, bool self)
        {
            string text;
            switch (s.Media)
            {
                case ParticipantMediaState.NoMovie: text = "wartet"; break;
                case ParticipantMediaState.Missing: text = "Datei fehlt!"; break;
                case ParticipantMediaState.Loading: text = "lädt …"; break;
                case ParticipantMediaState.Buffering: text = "puffert …"; break;
                case ParticipantMediaState.Error: text = "Fehler!"; break;
                default: text = "bereit"; break;
            }
            if (s.Media == ParticipantMediaState.Ready && !self)
                text += $" ({s.DriftMs:+0;-0;0} ms)";
            if (s.Muted) text += " · stumm";
            return text;
        }
    }
}
