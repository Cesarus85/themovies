using System;
using System.Collections.Generic;
using System.Text;
using TheMovies.App;
using TheMovies.Core;
using TheMovies.Core.Network;
using TheMovies.Net;
using TheMovies.Video;
using TheMovies.World;
using TheMovies.XR;
using UnityEngine;

namespace TheMovies.UI
{
    /// <summary>
    /// The floating 3D menu: main menu, on-screen keyboard, playback controls, settings and the movie
    /// browser. Appears at eye level in front of the user and can be moved with the grab bar below it.
    /// Built entirely in code from the widgets in UIKit.cs.
    /// </summary>
    public sealed partial class MenuUI : MonoBehaviour
    {
        const float k_Width = 0.92f;
        const float k_Height = 0.72f;
        const float k_Left = -k_Width * 0.5f + 0.03f;
        const float k_Right = k_Width * 0.5f - 0.03f;

        AppController m_App;
        Transform m_Root;
        GameObject m_MainPage;
        GameObject m_KeyboardPage;
        GameObject m_SessionPage;
        GameObject m_BrowserPage;
        GameObject m_SettingsPage;
        GameObject m_CurrentPage;
        UIGrabHandle m_Handle;
        bool m_PlacementPending = true;
        float m_ShowAnimation = 1f;
        float m_GrabDistance;
        Vector3 m_GrabOffset;
        bool m_Solo;
        bool m_Visible = true;
        float m_AutoHideAt = -1;
        float m_NextRefresh;

        // Main page
        UILabel m_MainName;
        UILabel m_MainStatus;
        UIButton m_HostOnline, m_JoinOnline, m_HostLan, m_JoinLan, m_SoloButton, m_ContinueButton;

        // Keyboard page
        UILabel m_KeyTitle;
        UILabel m_KeyInput;
        UILabel m_KeyHint;
        readonly List<(UIButton button, char key, char shifted)> m_Keys = new List<(UIButton, char, char)>();
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
        UIButton m_StopButton;
        float m_StopConfirmUntil;
        UIButton m_LeaveButton;

        // Settings page
        UILabel m_SetFormat;
        UIButton m_SetFormatPrev, m_SetFormatNext;
        UIButton m_SetSwap;
        UILabel m_SetSize;
        UIButton m_SetLightAuto, m_SetLightOn, m_SetLightOff;
        UILabel m_SetVolume;
        UIButton m_SetMic;
        UILabel m_SetHeight;
        UIButton m_SetSubtitles;
        UIButton m_SetCinema, m_SetMixed;
        UILabel m_SetMixedStatus;
        UIButton m_SetMixedTest;
        GameObject m_SettingsReturn;
        UIButton m_ResumeButton, m_RestartButton;
        UILabel m_SessionStatus;
        readonly UILabel[] m_Participants = new UILabel[CinemaEnvironment.MaxParticipants];
        readonly List<UIButton> m_TransportButtons = new List<UIButton>();

        sealed class KeyboardRequest
        {
            public string Title;
            public string Hint;
            public string Initial;
            public int MaxLength;
            public bool AllowLowercase;
            public bool AllowSpace;
            public bool AllowEmpty;

            /// <summary>Shows dots instead of the text (passwords).</summary>
            public bool Secret;

            /// <summary>Page to return to after OK/cancel; null = main or session page.</summary>
            public GameObject ReturnPage;
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
            UIFactory.CreateRounded(m_Root, "Rim", new Vector3(0, 0, 2 * UIStyle.Layer), new Vector2(k_Width + 0.008f, k_Height + 0.008f),
                0.034f, UIStyle.PanelRim, UIFactory.PanelMaterial, 0f);
            UIFactory.CreateRounded(m_Root, "Panel", new Vector3(0, 0, UIStyle.Layer), new Vector2(k_Width, k_Height),
                0.03f, UIStyle.PanelTop, UIFactory.PanelMaterial, 0.55f);
            // A collider behind everything so pressing on the panel background does not count as "pressed on nothing".
            var blocker = UIFactory.CreateEmpty(m_Root, "Blocker", new Vector3(0, 0, 0.01f));
            blocker.AddComponent<BoxCollider>().size = new Vector3(k_Width, k_Height, 0.005f);

            m_MainPage = BuildMainPage();
            m_KeyboardPage = BuildKeyboardPage();
            m_SessionPage = BuildSessionPage();
            m_BrowserPage = BuildBrowserPage();
            m_SettingsPage = BuildSettingsPage();
            BuildExtraPages();

            m_Handle = UIGrabHandle.Create(m_Root, new Vector3(0, -k_Height * 0.5f - 0.035f, 0), 0.2f);
            m_Handle.Began += OnGrabBegan;
            m_Handle.Moved += OnGrabMoved;
            m_Handle.Ended += OnGrabEnded;

            m_SettingsPage.SetActive(false);
            m_MainPage.SetActive(false);
            m_KeyboardPage.SetActive(false);
            m_SessionPage.SetActive(false);
            m_BrowserPage.SetActive(false);
            HideExtraPages();
            ShowPage(m_MainPage);

            LaserPointer.PressedOnNothing += OnPressedOnNothing;
            if (XRRig.Instance != null)
                XRRig.Instance.Aligned += OnRigAligned;
        }

        void OnDestroy()
        {
            LaserPointer.PressedOnNothing -= OnPressedOnNothing;
            if (XRRig.Instance != null)
                XRRig.Instance.Aligned -= OnRigAligned;
        }

        // ---------------- Visibility & placement ----------------

        public void Show()
        {
            bool wasVisible = m_Visible && gameObject.activeSelf;
            m_Visible = true;
            m_AutoHideAt = -1;
            gameObject.SetActive(true);
            if (!wasVisible)
            {
                PlaceInFrontOfUser();
                m_ShowAnimation = 0f;
            }
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

        // After a recenter (or the initial alignment) bring the menu back in front of the user.
        void OnRigAligned()
        {
            if (m_Visible && !m_Handle.IsDragging)
                PlaceInFrontOfUser();
        }

        void OnPressedOnNothing()
        {
            if (!m_Visible) Show();
        }

        /// <summary>Puts the menu at eye level in front of where the user looks, facing them.</summary>
        public void PlaceInFrontOfUser()
        {
            var rig = XRRig.Instance;
            if (rig == null || !rig.Ready)
            {
                m_PlacementPending = true;
                return;
            }
            m_PlacementPending = false;

            // Ride along with the rig, so it follows when the seat changes or the view is re-centered.
            if (transform.parent != rig.transform)
                transform.SetParent(rig.transform, true);

            var head = rig.Head;
            Vector3 forward = Vector3.ProjectOnPlane(head.forward, Vector3.up);
            if (forward.sqrMagnitude < 0.01f) forward = rig.transform.forward;
            forward.Normalize();
            float distance = rig.XRActive ? 0.8f : 1.15f;
            transform.position = head.position + forward * distance + Vector3.down * 0.08f;
            FaceUser();
        }

        void FaceUser()
        {
            var rig = XRRig.Instance;
            if (rig == null) return;
            Vector3 away = transform.position - rig.Head.position;
            if (away.sqrMagnitude > 1e-4f)
                transform.rotation = Quaternion.LookRotation(away, Vector3.up);
        }

        void OnGrabBegan(Ray ray, Vector3 hitPoint)
        {
            m_GrabDistance = Vector3.Distance(ray.origin, hitPoint);
            m_GrabOffset = transform.position - hitPoint;
            if (XRRig.Instance != null) XRRig.Instance.SuppressStickSeek = true;
        }

        void OnGrabMoved(Ray ray)
        {
            // Push/pull with the thumbstick while holding the bar.
            var rig = XRRig.Instance;
            if (rig != null)
                m_GrabDistance = Mathf.Clamp(m_GrabDistance + rig.StickY * Time.unscaledDeltaTime * 0.8f, 0.3f, 2.5f);
            Vector3 target = ray.origin + ray.direction * m_GrabDistance + m_GrabOffset;
            transform.position = Vector3.Lerp(transform.position, target, 1f - Mathf.Exp(-25f * Time.unscaledDeltaTime));
            FaceUser();
        }

        void OnGrabEnded()
        {
            if (XRRig.Instance != null) XRRig.Instance.SuppressStickSeek = false;
        }

        // ---------------- Pages ----------------

        GameObject Page(string name, float offsetY = 0f)
        {
            var go = UIFactory.CreateEmpty(m_Root, name, new Vector3(0, offsetY, 0));
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

        static UIButton Button(GameObject page, string label, float x, float y, float w, float h, Action onClick, float textHeight = 0.024f,
            UIIcon icon = UIIcon.None, ButtonStyle style = ButtonStyle.Normal, bool round = false) =>
            UIButton.Create(page.transform, label, new Vector3(x, y, -UIStyle.Layer), new Vector2(w, h), onClick, textHeight, icon, style, round);

        static void Divider(GameObject page, float y, float width = k_Width - 0.06f) =>
            UIFactory.CreateQuad(page.transform, "Divider", new Vector3(0, y, -UIStyle.Layer * 0.5f), new Vector2(width, 0.0015f),
                UIStyle.Divider, UIFactory.WidgetMaterial);

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
            Divider(p, 0.163f);

            m_MainName = Label(p, string.Empty, k_Left, 0.125f, 0.026f, 40);
            Button(p, "Name ändern", 0.33f, 0.125f, 0.2f, 0.055f, EditName, 0.02f);

            Label(p, "Über das Internet (Raum-Code)", k_Left, 0.06f, 0.019f, 0, TextAnchor.MiddleLeft, UIStyle.TextDim);
            m_HostOnline = Button(p, "Kino eröffnen", -0.217f, 0.005f, 0.42f, 0.075f, () => m_App.Sessions.HostOnline(), style: ButtonStyle.Primary);
            m_JoinOnline = Button(p, "Mit Code beitreten", 0.217f, 0.005f, 0.42f, 0.075f, EnterRoomCode);

            Label(p, "Im selben WLAN (ohne Unity-Dienste)", k_Left, -0.065f, 0.019f, 0, TextAnchor.MiddleLeft, UIStyle.TextDim);
            m_HostLan = Button(p, "LAN-Kino eröffnen", -0.217f, -0.115f, 0.42f, 0.06f, () => m_App.Sessions.HostLan(), 0.021f);
            m_JoinLan = Button(p, "Per IP beitreten", 0.217f, -0.115f, 0.42f, 0.06f, EnterLanHost, 0.021f);

            m_SoloButton = Button(p, "Allein schauen", -0.29f, -0.195f, 0.275f, 0.06f, StartSolo, 0.021f);
            Button(p, "Einstellungen", 0f, -0.195f, 0.275f, 0.06f, () => OpenSettings(m_MainPage), 0.021f, UIIcon.Settings);
            Button(p, "Menü ausblenden", 0.29f, -0.195f, 0.275f, 0.06f, Hide, 0.021f, style: ButtonStyle.Subtle);

            m_ContinueButton = Button(p, "Weiterschauen", 0f, -0.265f, 0.855f, 0.055f, ContinueLast, 0.019f, style: ButtonStyle.Primary);
            m_MainStatus = Label(p, string.Empty, 0, -0.322f, 0.018f, 70, TextAnchor.MiddleCenter, UIStyle.TextDim);
            return p;
        }

        GameObject BuildKeyboardPage()
        {
            var p = Page("Keyboard Page");
            m_KeyTitle = Label(p, string.Empty, 0, 0.255f, 0.03f, 50, TextAnchor.MiddleCenter, UIStyle.Accent);
            UIFactory.CreateQuad(p.transform, "Input", new Vector3(0, 0.185f, -UIStyle.Layer), new Vector2(0.8f, 0.07f), UIStyle.PanelInset, UIFactory.WidgetMaterial);
            m_KeyInput = Label(p, string.Empty, 0, 0.185f, 0.036f, 34, TextAnchor.MiddleCenter);

            // Second line of each row: what the key types with "Umschalt" (letters: upper case).
            string[] rows = { "1234567890", "QWERTZUIOP", "ASDFGHJKLÖ", "YXCVBNMÄÜ.", "-_:/@&?=%+" };
            string[] shifted = { "!\"§$%&/()=", "QWERTZUIOP", "ASDFGHJKLÖ", "YXCVBNMÄÜ,", "#*;\\<>~'ß$" };
            float keyW = 0.074f, step = 0.081f, keyH = 0.055f;
            for (int r = 0; r < rows.Length; r++)
            {
                string row = rows[r];
                float y = 0.11f - r * 0.064f;
                float x0 = -(row.Length - 1) * step * 0.5f;
                for (int i = 0; i < row.Length; i++)
                {
                    char c = row[i];
                    char alt = i < shifted[r].Length ? shifted[r][i] : c;
                    var b = Button(p, c.ToString(), x0 + i * step, y, keyW, keyH, () => TypeKey(c), 0.026f);
                    m_Keys.Add((b, c, alt));
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
            var p = Page("Session Page", 0.045f);
            m_RoomLabel = Label(p, string.Empty, k_Left, 0.268f, 0.024f, 30, TextAnchor.MiddleLeft, UIStyle.Accent);
            m_VoiceLabel = Label(p, string.Empty, k_Right, 0.268f, 0.015f, 40, TextAnchor.MiddleRight, UIStyle.TextDim);
            Divider(p, 0.24f);

            m_TitleLabel = Label(p, string.Empty, k_Left, 0.2f, 0.03f, 26);
            m_InfoLabel = Label(p, string.Empty, k_Left, 0.162f, 0.015f, 58, TextAnchor.MiddleLeft, UIStyle.TextDim);
            for (int i = 0; i < m_Participants.Length; i++)
                m_Participants[i] = Label(p, string.Empty, 0.1f, 0.205f - i * 0.026f, 0.016f, 34);

            m_ProblemLabel = Label(p, string.Empty, k_Left, 0.09f, 0.0145f, 64, TextAnchor.MiddleLeft, UIStyle.Warning);
            m_AssignButton = Button(p, "Datei zuordnen", 0.34f, 0.09f, 0.18f, 0.04f, () => OpenBrowser(true), 0.016f);
            m_ResumeButton = Button(p, "Fortsetzen", 0.2f, 0.09f, 0.17f, 0.042f, () => m_App.Media.AcceptResume(), 0.017f, UIIcon.Play, ButtonStyle.Primary);
            m_RestartButton = Button(p, "Von vorne", 0.36f, 0.09f, 0.13f, 0.042f, () => m_App.Media.DeclineResume(), 0.016f, style: ButtonStyle.Subtle);

            m_Seek = UISlider.Create(p.transform, new Vector3(-0.115f, 0.035f, -UIStyle.Layer), 0.62f, 0.014f);
            m_Seek.Committed += v => Playback.Current.RequestSeek(v * m_App.Media.DisplayDuration);
            m_TimeLabel = Label(p, string.Empty, k_Right, 0.035f, 0.019f, 24, TextAnchor.MiddleRight);

            float y = -0.07f;
            m_TransportButtons.Add(Button(p, "30", -0.34f, y, 0.12f, 0.075f, () => Playback.Current.RequestSeekRelative(-30), 0.02f, UIIcon.Back));
            m_TransportButtons.Add(Button(p, "10", -0.205f, y, 0.12f, 0.075f, () => Playback.Current.RequestSeekRelative(-10), 0.02f, UIIcon.Back));
            m_PlayButton = Button(p, null, -0.07f, y, 0.115f, 0.115f, PlayPause, 0.02f, UIIcon.Play, ButtonStyle.Primary, round: true);
            m_TransportButtons.Add(m_PlayButton);
            m_TransportButtons.Add(Button(p, "10", 0.065f, y, 0.12f, 0.075f, () => Playback.Current.RequestSeekRelative(10), 0.02f, UIIcon.Forward));
            m_TransportButtons.Add(Button(p, "30", 0.2f, y, 0.12f, 0.075f, () => Playback.Current.RequestSeekRelative(30), 0.02f, UIIcon.Forward));
            m_StopButton = Button(p, "Beenden", 0.35f, y, 0.15f, 0.075f, StopMovie, 0.018f, UIIcon.Close, ButtonStyle.Subtle);
            m_TransportButtons.Add(m_StopButton);

            Divider(p, -0.15f);
            float rowY = -0.2f;
            Button(p, "Film wählen", -0.3f, rowY, 0.22f, 0.06f, () => OpenBrowser(false), 0.02f, UIIcon.Film);
            Button(p, "Einstellungen", -0.06f, rowY, 0.24f, 0.06f, () => OpenSettings(m_SessionPage), 0.02f, UIIcon.Settings);
            Button(p, "Ausblenden", 0.16f, rowY, 0.18f, 0.06f, Hide, 0.02f, style: ButtonStyle.Subtle);
            m_LeaveButton = Button(p, "Verlassen", 0.345f, rowY, 0.16f, 0.06f, Leave, 0.02f, style: ButtonStyle.Danger);

            // Reactions: emojis above your avatar for everyone, popcorn flies towards the screen.
            Divider(p, -0.245f);
            Label(p, "Reaktion", k_Left, -0.292f, 0.016f, 0, TextAnchor.MiddleLeft, UIStyle.TextDim);
            float rx = -0.2f;
            foreach (var reaction in Reactions.All)
            {
                var type = reaction;
                var button = Button(p, null, rx, -0.292f, 0.062f, 0.062f, () => NetworkPlayer.SendLocalReaction(type), 0.02f,
                    style: ButtonStyle.Subtle, round: true);
                var emoji = UIFactory.CreateEmpty(button.transform, "Emoji", new Vector3(0, 0, -UIStyle.Layer));
                Reactions.BuildEmoji(emoji.transform, type, 0.042f, 0.0005f);
                rx += 0.08f;
            }

            m_SessionStatus = Label(p, string.Empty, 0, -0.345f, 0.014f, 100, TextAnchor.MiddleCenter, UIStyle.TextDim);
            return p;
        }

        GameObject BuildSettingsPage()
        {
            var p = Page("Settings Page");
            Label(p, "Einstellungen", k_Left, 0.305f, 0.026f, 0, TextAnchor.MiddleLeft, UIStyle.Accent);
            Button(p, "Zurück", 0.35f, 0.305f, 0.16f, 0.045f, () => ShowPage(m_SettingsReturn ?? m_SessionPage), 0.018f, UIIcon.ChevronLeft, ButtonStyle.Subtle);
            Divider(p, 0.272f);
            m_SetMixedStatus = Label(p, string.Empty, 0, 0.247f, 0.0135f, 110, TextAnchor.MiddleCenter, UIStyle.Warning);

            const float minusX = 0.0f, valueX = 0.18f, plusX = 0.36f, small = 0.05f, step = 0.062f;
            float y = 0.2f;

            SettingRow(p, "3D-Format", y);
            m_SetFormatPrev = Button(p, null, minusX, y, small, small, () => m_App.Media.CycleFormat(-1), 0.02f, UIIcon.ChevronLeft);
            m_SetFormat = Label(p, string.Empty, valueX, y, 0.02f, 16, TextAnchor.MiddleCenter);
            m_SetFormatNext = Button(p, null, plusX, y, small, small, () => m_App.Media.CycleFormat(1), 0.02f, UIIcon.ChevronRight);

            y -= step;
            SettingRow(p, "Augen tauschen", y);
            m_SetSwap = Button(p, "Aus", valueX, y, 0.2f, small, () => m_App.Media.ToggleSwapEyes(), 0.019f);

            y -= step;
            SettingRow(p, "Untertitel (.srt)", y);
            m_SetSubtitles = Button(p, "An", valueX, y, 0.2f, small, () => m_App.SetSubtitles(!m_App.Subtitles.Enabled), 0.019f);

            y -= step;
            SettingRow(p, "Leinwand", y);
            Button(p, null, minusX, y, small, small, () => ChangeScreenSize(-1), 0.02f, UIIcon.Minus);
            m_SetSize = Label(p, string.Empty, valueX, y, 0.02f, 16, TextAnchor.MiddleCenter);
            Button(p, null, plusX, y, small, small, () => ChangeScreenSize(1), 0.02f, UIIcon.Plus);

            y -= step;
            SettingRow(p, "Umgebung", y);
            m_SetCinema = Button(p, "Kino", 0.04f, y, 0.14f, small, () => m_App.SetMixedReality(false), 0.018f);
            m_SetMixed = Button(p, "Mixed Reality", 0.21f, y, 0.18f, small, () => m_App.SetMixedReality(true), 0.018f);
            m_SetMixedTest = Button(p, "Test", 0.37f, y, 0.12f, small, MixedReality.CycleTestMode, 0.016f, style: ButtonStyle.Subtle);

            y -= step;
            SettingRow(p, "Saallicht", y);
            m_SetLightAuto = Button(p, "Auto", 0.05f, y, 0.11f, small, () => m_App.Media.Lights = LightMode.Auto, 0.018f);
            m_SetLightOn = Button(p, "An", 0.18f, y, 0.11f, small, () => m_App.Media.Lights = LightMode.On, 0.018f);
            m_SetLightOff = Button(p, "Aus", 0.31f, y, 0.11f, small, () => m_App.Media.Lights = LightMode.Off, 0.018f);

            y -= step;
            SettingRow(p, "Lautstärke Film", y);
            Button(p, null, minusX, y, small, small, () => m_App.ChangeVolume(-0.1f), 0.02f, UIIcon.Minus);
            m_SetVolume = Label(p, string.Empty, valueX, y, 0.02f, 8, TextAnchor.MiddleCenter);
            Button(p, null, plusX, y, small, small, () => m_App.ChangeVolume(0.1f), 0.02f, UIIcon.Plus);

            y -= step;
            SettingRow(p, "Mikrofon", y);
            m_SetMic = Button(p, "An", valueX, y, 0.2f, small, ToggleMic, 0.019f);

            y -= step;
            SettingRow(p, "Sitzhöhe", y, divider: false);
            Button(p, null, minusX, y, small, small, () => m_App.ChangeHeight(-0.05f), 0.02f, UIIcon.Minus);
            m_SetHeight = Label(p, string.Empty, valueX, y, 0.02f, 10, TextAnchor.MiddleCenter);
            Button(p, null, plusX, y, small, small, () => m_App.ChangeHeight(0.05f), 0.02f, UIIcon.Plus);

            Button(p, "Bild & 3D", -0.352f, -0.322f, 0.166f, 0.042f, () => ShowPage(m_PicturePage), 0.015f, style: ButtonStyle.Subtle);
            Button(p, "Ton & Untertitel", -0.176f, -0.322f, 0.166f, 0.042f, () => ShowPage(m_TracksPage), 0.015f, style: ButtonStyle.Subtle);
            Button(p, "Protokoll", 0f, -0.322f, 0.166f, 0.042f, OpenLog, 0.015f, style: ButtonStyle.Subtle);
            Button(p, "Platz ausrichten", 0.176f, -0.322f, 0.166f, 0.042f, () => XRRig.Instance?.RequestAlign(), 0.015f, style: ButtonStyle.Subtle);
            Button(p, "Menü herholen", 0.352f, -0.322f, 0.166f, 0.042f, PlaceInFrontOfUser, 0.015f, style: ButtonStyle.Subtle);
            return p;
        }

        void OpenSettings(GameObject returnPage)
        {
            m_SettingsReturn = returnPage;
            ShowPage(m_SettingsPage);
        }

        void SettingRow(GameObject page, string title, float y, bool divider = true)
        {
            Label(page, title, k_Left, y, 0.02f, 24, TextAnchor.MiddleLeft, UIStyle.TextDim);
            if (divider) Divider(page, y - 0.031f);
        }

        void ChangeScreenSize(int delta)
        {
            int next = Mathf.Clamp(m_App.Screen.SizeIndex + delta, 0, ScreenController.Widths.Length - 1);
            m_App.SetScreenSize(next);
        }

        // ---------------- Actions ----------------

        void StartSolo()
        {
            m_Solo = true;
            ShowPage(m_SessionPage);
        }

        /// <summary>"Weiterschauen" on the start page: the last movie, alone, from where it was left off.</summary>
        void ContinueLast()
        {
            var last = m_App.Media.Recent.Latest;
            if (last == null) return;
            StartSolo();
            if (!m_App.Media.Select(last, out string error, resume: true))
                m_App.Media.ReportProblem(error);
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

        /// <summary>First press asks, second press (within 3 s) ends the movie for everyone and frees its memory.</summary>
        void StopMovie()
        {
            if (Playback.Current.Media.IsEmpty) return;
            if (Time.unscaledTime < m_StopConfirmUntil)
            {
                m_StopConfirmUntil = 0;
                m_App.Media.Stop();
            }
            else
            {
                m_StopConfirmUntil = Time.unscaledTime + 3f;
            }
            Refresh();
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
            foreach (var (button, key, alt) in m_Keys)
                button.Interactable = request.Accepts(key) || request.Accepts(alt) ||
                                      (request.AllowLowercase && request.Accepts(char.ToLowerInvariant(key)));
            m_ShiftButton.Interactable = true;
            m_SpaceButton.Interactable = request.AllowSpace;
            UpdateKeyLabels();
            ShowPage(m_KeyboardPage);
        }

        void TypeKey(char c)
        {
            if (m_KeyRequest == null || m_KeyText.Length >= m_KeyRequest.MaxLength) return;
            if (m_Shift && !char.IsLetter(c))
            {
                foreach (var (_, key, alt) in m_Keys)
                    if (key == c && m_KeyRequest.Accepts(alt))
                    {
                        c = alt;
                        break;
                    }
            }
            else if (c != ' ' && m_KeyRequest.AllowLowercase && !m_Shift)
            {
                c = char.ToLowerInvariant(c);
            }
            if (c == ' ' && !m_KeyRequest.AllowSpace) return;
            if (c != ' ' && !m_KeyRequest.Accepts(c)) return;
            m_KeyText.Append(c);
            if (m_Shift)
            {
                m_Shift = false;
                UpdateKeyLabels();
            }
            ShowKeyInput();
        }

        void Backspace()
        {
            if (m_KeyText.Length > 0) m_KeyText.Length--;
            ShowKeyInput();
        }

        void ShowKeyInput()
        {
            string text = m_KeyRequest != null && m_KeyRequest.Secret ? new string('•', m_KeyText.Length) : m_KeyText.ToString();
            m_KeyInput.Text = text + "_";
        }

        void ToggleShift()
        {
            m_Shift = !m_Shift;
            UpdateKeyLabels();
        }

        void UpdateKeyLabels()
        {
            bool lower = m_KeyRequest != null && m_KeyRequest.AllowLowercase && !m_Shift;
            foreach (var (button, key, alt) in m_Keys)
            {
                char shown = char.IsLetter(key) ? (lower ? char.ToLowerInvariant(key) : key) : (m_Shift ? alt : key);
                button.Label = shown.ToString();
            }
            m_ShiftButton.Highlighted = m_Shift;
            ShowKeyInput();
        }

        GameObject KeyboardReturnPage(KeyboardRequest request) =>
            request?.ReturnPage ?? (m_Solo || m_App.Sessions.State != ConnectionState.Offline ? m_SessionPage : m_MainPage);

        void CancelKeyboard()
        {
            var request = m_KeyRequest;
            m_KeyRequest = null;
            ShowPage(KeyboardReturnPage(request));
        }

        void SubmitKeyboard()
        {
            var request = m_KeyRequest;
            m_KeyRequest = null;
            string text = request != null && request.Secret ? m_KeyText.ToString() : m_KeyText.ToString().Trim();
            ShowPage(KeyboardReturnPage(request));
            if (request != null && (text.Length > 0 || request.AllowEmpty))
                request.Submit(text);
        }

        // ---------------- Per-frame refresh ----------------

        void Update()
        {
            if (m_AutoHideAt > 0 && Time.unscaledTime >= m_AutoHideAt && !m_Handle.IsDragging)
            {
                Hide();
                return;
            }
            if (m_PlacementPending)
                PlaceInFrontOfUser();
            if (m_ShowAnimation < 1f)
            {
                m_ShowAnimation = Mathf.Min(1f, m_ShowAnimation + Time.unscaledDeltaTime * 6f);
                float eased = 1f - (1f - m_ShowAnimation) * (1f - m_ShowAnimation);
                float scale = Mathf.Lerp(0.9f, 1f, eased);
                transform.localScale = new Vector3(scale, scale, scale);
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
            else if (state == ConnectionState.Offline && !m_Solo &&
                     (m_CurrentPage == m_SessionPage || m_CurrentPage == m_BrowserPage || m_CurrentPage == m_SettingsPage))
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
            else if (m_CurrentPage == m_SettingsPage) RefreshSettings();
            else RefreshExtraPages();
        }

        void RefreshSettings()
        {
            var media = Playback.Current.Media;
            bool hasMovie = !media.IsEmpty;
            m_SetFormat.Text = hasMovie ? media.StereoFormat.DisplayName() : "–";
            m_SetFormatPrev.Interactable = hasMovie;
            m_SetFormatNext.Interactable = hasMovie;
            m_SetSwap.Interactable = hasMovie && media.StereoFormat.IsStereo();
            m_SetSwap.Highlighted = media.SwapEyes;
            m_SetSwap.Label = media.SwapEyes ? "An" : "Aus";
            m_SetSize.Text = ScreenController.SizeNames[m_App.Screen.SizeIndex];
            var lights = m_App.Media.Lights;
            m_SetLightAuto.Highlighted = lights == LightMode.Auto;
            m_SetLightOn.Highlighted = lights == LightMode.On;
            m_SetLightOff.Highlighted = lights == LightMode.Off;
            m_SetVolume.Text = $"{Mathf.RoundToInt(m_App.Settings.Volume * 100)} %";
            bool micAvailable = Playback.Current.IsNetworked && VoiceChat.Compiled;
            m_SetMic.Interactable = micAvailable;
            m_SetMic.Highlighted = micAvailable && !VoiceChat.Muted;
            m_SetMic.Label = !VoiceChat.Compiled ? "nicht installiert" : VoiceChat.Muted ? "Aus" : "An";
            float height = XRRig.Instance != null ? XRRig.Instance.HeightOffset : 0f;
            m_SetHeight.Text = $"{height * 100:+0;-0;0} cm";

            var subtitles = m_App.Subtitles;
            m_SetSubtitles.Highlighted = subtitles.Enabled && subtitles.HasTrack;
            m_SetSubtitles.Label = !subtitles.Enabled ? "Aus" : subtitles.HasTrack ? "An" : "An (keine Datei)";

            m_SetCinema.Highlighted = !MixedReality.Enabled;
            m_SetMixed.Highlighted = MixedReality.Enabled;
            m_SetMixed.Interactable = MixedReality.Available;
            m_SetMixed.Label = MixedReality.Available ? "Mixed Reality" : "MR: Paket fehlt";
            m_SetMixedStatus.Text = MixedReality.Enabled ? TwoLines(MixedReality.Status) : string.Empty;
            m_SetMixedTest.gameObject.SetActive(MixedReality.Enabled);
            m_SetMixedTest.Label = $"Test {MixedReality.TestMode + 1}/{MixedReality.TestModes}";
            m_SetMixedStatus.Color = MixedReality.Problem.Length > 0 ? UIStyle.Warning : UIStyle.TextDim;
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
            var last = m_App.Media.Recent.Latest;
            m_ContinueButton.gameObject.SetActive(idle && last != null);
            if (last != null)
            {
                var progress = m_App.Media.ProgressOf(last);
                string rest = progress.HasValue && progress.Value.duration > 0
                    ? "  ·  noch " + TextUtil.FormatDuration(progress.Value.duration - progress.Value.position)
                    : string.Empty;
                m_ContinueButton.Label = "Weiterschauen: " + TextUtil.Ellipsize(last.Title, 40) + rest;
            }
            bool flatOnQuest = Application.platform == RuntimePlatform.Android && XRRig.Instance != null && !XRRig.Instance.XRActive;
            if (flatOnQuest)
            {
                m_MainStatus.Text = "VR ist nicht aktiv: Im Unity-Projekt OpenXR + 'Meta Quest Support' für Android aktivieren (README).";
                m_MainStatus.Color = UIStyle.Error;
                return;
            }
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
                string res = coordinator.Player.State == MovieState.Ready
                    ? $"{coordinator.Player.Width}×{coordinator.Player.Height}" + (coordinator.Player.IsNative ? "  ·  ExoPlayer" : "  ·  Unity-Player")
                    : string.Empty;
                string source = string.Empty;
                if (coordinator.IsStreaming && HostStreamService.Instance != null)
                    source = $"  ·  Stream vom Gastgeber {HostStreamService.Instance.ReceiveRate / 1e6f:0.0} MB/s";
                else if (coordinator.CurrentEntry != null && coordinator.CurrentEntry.IsNetwork && NetworkLibrary.Instance != null && NetworkLibrary.Instance.IsProxyStreaming)
                    source = $"  ·  NAS {NetworkLibrary.Instance.StreamRate / 1e6f:0.0} MB/s";
                else if (media.HostStream && sessions.IsHost)
                    source = "  ·  wird bei Bedarf gestreamt";
                m_InfoLabel.Text = media.StereoFormat.DisplayName() + (media.SwapEyes ? " (getauscht)" : string.Empty) +
                                   (res.Length > 0 ? "  ·  " + res : string.Empty) + source;
            }

            bool missing = !media.IsEmpty && (coordinator.LocalState == ParticipantMediaState.Missing || coordinator.LocalState == ParticipantMediaState.Error);
            bool waiting = CinemaSync.Instance != null && CinemaSync.Instance.IsSpawned && CinemaSync.Instance.WaitingForOthers;
            var resume = coordinator.ResumeOffer;
            m_ResumeButton.gameObject.SetActive(resume.HasValue && !missing);
            m_RestartButton.gameObject.SetActive(resume.HasValue && !missing);
            m_ProblemLabel.Color = resume.HasValue && !waiting ? UIStyle.Text : UIStyle.Warning;
            string mrProblem = MixedReality.Problem;
            bool buffering = Playback.Current is LocalAuthority solo && solo.WaitingForBuffer;
            m_ProblemLabel.Text = TwoLines(waiting ? "Pausiert – warte, bis alle bereit sind …"
                : buffering ? "Lade nach … geht gleich weiter."
                : NetworkLibrary.Instance != null && NetworkLibrary.Instance.CopyStatus.Length > 0 ? NetworkLibrary.Instance.CopyStatus
                : resume.HasValue && !missing ? $"Zuletzt bei {TextUtil.FormatTime(resume.Value)} aufgehört."
                : !string.IsNullOrEmpty(coordinator.Problem) ? coordinator.Problem
                : mrProblem);
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
            m_PlayButton.SetIcon(playing ? UIIcon.Pause : UIIcon.Play);
            foreach (var b in m_TransportButtons) b.Interactable = !media.IsEmpty;
            bool confirmStop = Time.unscaledTime < m_StopConfirmUntil;
            m_StopButton.Label = confirmStop ? "Sicher?" : "Beenden";
            m_StopButton.Style = confirmStop ? ButtonStyle.Danger : ButtonStyle.Subtle;
            m_LeaveButton.Label = networked || sessions.State != ConnectionState.Offline ? "Verlassen" : "Hauptmenü";

            RefreshParticipants(networked);

            // Status line
            var cmd = coordinator.LastCommand;
            string sync = coordinator.LocalState == ParticipantMediaState.Ready && networked
                ? $"Sync {cmd.Drift * 1000:+0;-0;0} ms · Ping {auth.RoundTrip * 1000:0} ms  ·  "
                : string.Empty;
            m_SessionStatus.Text = sync + "Menü: B / Y  ·  Verschieben: Leiste unten greifen, Stick vor/zurück für Abstand";
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
