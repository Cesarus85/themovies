using System;
using System.Collections.Generic;
using System.Text;
using TheMovies.Core;
using TheMovies.Core.Network;
using TheMovies.Net;
using TheMovies.Video;
using UnityEngine;

namespace TheMovies.UI
{
    /// <summary>The movie browser: recently watched, this device, network sources (list or poster tiles, search).</summary>
    public sealed partial class MenuUI
    {
        const int k_BrowserRows = 7;
        const int k_TileColumns = 6;
        const int k_Tiles = 12;
        const string k_TilesPrefsKey = "TheMovies.BrowserTiles";

        // Browser page
        UILabel m_BrowserTitle;
        UILabel m_BrowserInfo;
        UILabel m_PageLabel;
        readonly UIButton[] m_Rows = new UIButton[k_BrowserRows];
        readonly object[] m_RowItems = new object[k_BrowserRows];
        readonly PosterTile[] m_Tiles = new PosterTile[k_Tiles];
        readonly object[] m_TileItems = new object[k_Tiles];
        int m_BrowserPageIndex;
        bool m_BrowserAssignMode;
        bool m_BrowserNetwork;
        bool m_BrowserRecent;
        bool m_SearchedNetwork;
        bool m_TileView = true;
        bool m_ShowingTiles;
        string m_Filter = string.Empty;
        UIButton m_TabRecent, m_TabLocal, m_TabNetwork;
        UIButton m_SearchButton, m_ViewButton;
        UIButton[] m_LocalBar;
        UIButton[] m_NetworkBar;
        UIButton[] m_RecentBar;
        float m_ClearRecentUntil;
        float m_RemoveConfirmUntil;
        float m_DeleteCopiesUntil;

        GameObject BuildBrowserPage()
        {
            var p = Page("Browser Page");
            m_TileView = PlayerPrefs.GetInt(k_TilesPrefsKey, 1) == 1;
            m_BrowserTitle = Label(p, string.Empty, k_Left, 0.258f, 0.026f, 22, TextAnchor.MiddleLeft, UIStyle.Accent);
            m_TabRecent = Button(p, "Zuletzt", 0.035f, 0.258f, 0.12f, 0.045f, SetRecentTab, 0.017f);
            m_TabLocal = Button(p, "Dieses Gerät", 0.185f, 0.258f, 0.165f, 0.045f, () => SetBrowserTab(false), 0.017f);
            m_TabNetwork = Button(p, "Netzwerk", 0.345f, 0.258f, 0.14f, 0.045f, () => SetBrowserTab(true), 0.017f);
            m_BrowserInfo = Label(p, string.Empty, k_Left, 0.212f, 0.015f, 70, TextAnchor.MiddleLeft, UIStyle.TextDim);
            m_SearchButton = Button(p, "Suchen", 0.215f, 0.212f, 0.1f, 0.034f, SearchOrClear, 0.015f, style: ButtonStyle.Subtle);
            m_ViewButton = Button(p, "Liste", 0.32f, 0.212f, 0.1f, 0.034f, ToggleTileView, 0.015f, style: ButtonStyle.Subtle);
            m_PageLabel = Label(p, string.Empty, k_Right, 0.212f, 0.016f, 9, TextAnchor.MiddleRight, UIStyle.TextDim);
            for (int i = 0; i < k_BrowserRows; i++)
            {
                int index = i;
                m_Rows[i] = Button(p, string.Empty, 0, 0.165f - i * 0.058f, k_Width - 0.06f, 0.05f, () => PickItem(m_RowItems[index]), 0.019f);
            }
            float spacing = PosterTile.Width + 0.01f;
            for (int i = 0; i < k_Tiles; i++)
            {
                int index = i;
                int column = i % k_TileColumns;
                int row = i / k_TileColumns;
                var position = new Vector3((column - (k_TileColumns - 1) * 0.5f) * spacing, 0.09f - row * (PosterTile.Height + 0.01f), -UIStyle.Layer);
                m_Tiles[i] = new PosterTile(p.transform, position, () => PickItem(m_TileItems[index]));
            }
            m_LocalBar = IconRow(p, -0.255f, 0.05f, 0.012f,
                ("Zurück", 0.12f, BrowserBack, UIIcon.None),
                (null, 0.07f, () => TurnPage(-1), UIIcon.ChevronLeft),
                (null, 0.07f, () => TurnPage(1), UIIcon.ChevronRight),
                ("Aktualisieren", 0.155f, () => m_App.Media.Library.Refresh(), UIIcon.None),
                ("Adresse eingeben", 0.19f, EnterStreamUrl, UIIcon.None),
                ("NAS-Kopien", 0.15f, DeleteCopies, UIIcon.None));
            m_NetworkBar = IconRow(p, -0.255f, 0.05f, 0.012f,
                ("Zurück", 0.13f, BrowserBack, UIIcon.None),
                (null, 0.07f, () => TurnPage(-1), UIIcon.ChevronLeft),
                (null, 0.07f, () => TurnPage(1), UIIcon.ChevronRight),
                ("Im Netz suchen", 0.19f, NetworkPrimary, UIIcon.None),
                ("Hinzufügen", 0.19f, NetworkSecondary, UIIcon.None));
            m_RecentBar = IconRow(p, -0.255f, 0.05f, 0.012f,
                ("Zurück", 0.13f, BrowserBack, UIIcon.None),
                (null, 0.07f, () => TurnPage(-1), UIIcon.ChevronLeft),
                (null, 0.07f, () => TurnPage(1), UIIcon.ChevronRight),
                ("Liste leeren", 0.17f, ClearRecent, UIIcon.None));
            PosterCache.Instance.Updated += () =>
            {
                if (m_CurrentPage == m_BrowserPage) RefreshBrowser();
            };
            return p;
        }

        /// <summary>Like <see cref="Row"/>, with icon-only buttons where the label is null.</summary>
        static UIButton[] IconRow(GameObject page, float y, float h, float gap, params (string label, float width, Action action, UIIcon icon)[] items)
        {
            float total = -gap;
            foreach (var it in items) total += it.width + gap;
            float x = -total * 0.5f;
            var result = new UIButton[items.Length];
            for (int i = 0; i < items.Length; i++)
            {
                result[i] = Button(page, items[i].label, x + items[i].width * 0.5f, y, items[i].width, h, items[i].action, 0.021f, items[i].icon);
                x += items[i].width + gap;
            }
            return result;
        }

        // ---------------- Browser ----------------

        void OpenBrowser(bool assignMode)
        {
            m_BrowserAssignMode = assignMode;
            m_BrowserPageIndex = 0;
            m_App.Media.Library.Refresh();
            ShowPage(m_BrowserPage);
            SetBrowserTab(m_BrowserNetwork);
        }

        void SetBrowserTab(bool network)
        {
            m_BrowserNetwork = network;
            m_BrowserRecent = false;
            m_BrowserPageIndex = 0;
            m_Filter = string.Empty;
            var library = NetworkLibrary.Instance;
            if (network && !m_SearchedNetwork && library != null)
            {
                m_SearchedNetwork = true;
                library.Discover();
            }
            RefreshBrowser();
        }

        void SetRecentTab()
        {
            m_BrowserRecent = true;
            m_BrowserPageIndex = 0;
            m_Filter = string.Empty;
            RefreshBrowser();
        }

        void ToggleTileView()
        {
            m_TileView = !m_TileView;
            PlayerPrefs.SetInt(k_TilesPrefsKey, m_TileView ? 1 : 0);
            PlayerPrefs.Save();
            m_BrowserPageIndex = 0;
            RefreshBrowser();
        }

        /// <summary>"Suchen": media servers search their whole library, everything else filters the list shown. Again: filter off.</summary>
        void SearchOrClear()
        {
            if (m_Filter.Length > 0)
            {
                m_Filter = string.Empty;
                m_BrowserPageIndex = 0;
                RefreshBrowser();
                return;
            }
            var library = NetworkLibrary.Instance;
            bool server = !m_BrowserRecent && m_BrowserNetwork && library != null && library.CanSearchServer;
            OpenKeyboard(new KeyboardRequest
            {
                Title = server ? $"In {TextUtil.Ellipsize(library.Current.Name, 26)} suchen" : "Liste filtern",
                Hint = server ? "Sucht Filme, Serien und Folgen in der ganzen Bibliothek." : "Zeigt nur Einträge, deren Name den Text enthält.",
                MaxLength = 40,
                AllowLowercase = true,
                AllowSpace = true,
                ReturnPage = m_BrowserPage,
                Accepts = c => c >= ' ' && c != '•',
                Submit = text =>
                {
                    text = text.Trim();
                    if (text.Length == 0) return;
                    m_BrowserPageIndex = 0;
                    if (server) library.SearchServer(text);
                    else m_Filter = text;
                    RefreshBrowser();
                },
            });
        }

        void ClearRecent()
        {
            if (Time.unscaledTime < m_ClearRecentUntil)
            {
                m_ClearRecentUntil = 0;
                m_App.Media.Recent.Clear();
                m_App.Media.SaveRecent();
            }
            else
            {
                m_ClearRecentUntil = Time.unscaledTime + 3f;
            }
            RefreshBrowser();
        }

        void BrowserBack()
        {
            var library = NetworkLibrary.Instance;
            if (m_Filter.Length > 0)
            {
                m_Filter = string.Empty;
                m_BrowserPageIndex = 0;
                RefreshBrowser();
                return;
            }
            if (!m_BrowserRecent && m_BrowserNetwork && library != null && library.Current != null)
            {
                library.Back();
                m_BrowserPageIndex = 0;
                RefreshBrowser();
                return;
            }
            ShowPage(m_SessionPage);
        }

        int BrowserPageSize => m_ShowingTiles ? k_Tiles : k_BrowserRows;

        void TurnPage(int delta)
        {
            int pages = Mathf.Max(1, Mathf.CeilToInt(BrowserItems().Count / (float)BrowserPageSize));
            m_BrowserPageIndex = Mathf.Clamp(m_BrowserPageIndex + delta, 0, pages - 1);
            RefreshBrowser();
        }

        void PickItem(object picked)
        {
            var library = NetworkLibrary.Instance;
            switch (picked)
            {
                case MediaEntry entry:
                    PickEntry(entry, resume: m_BrowserRecent);
                    break;
                case NetworkSource source when library != null:
                    if (library.IsSaved(source)) library.OpenSource(source);
                    else if (source.Kind == NetworkSourceKind.Plex) library.StartPlexLink();
                    else if (source.Kind == NetworkSourceKind.Smb || source.Kind == NetworkSourceKind.Jellyfin) AskLogin(source.Clone());
                    else library.AddAndOpen(source.Clone());
                    m_BrowserPageIndex = 0;
                    RefreshBrowser();
                    break;
                case NetworkItem item when library != null:
                    if (item.IsFolder)
                    {
                        library.OpenFolder(item);
                        m_Filter = string.Empty;
                        m_BrowserPageIndex = 0;
                        RefreshBrowser();
                    }
                    else if (library.CopyFirst)
                    {
                        CopyThenPlay(library, library.ToEntry(item));
                    }
                    else
                    {
                        PickEntry(library.ToEntry(item));
                    }
                    break;
            }
        }

        /// <summary>Copies the NAS movie to the headset, then selects (or assigns) the local copy.</summary>
        void CopyThenPlay(NetworkLibrary library, MediaEntry entry)
        {
            if (entry == null) return;
            bool assign = m_BrowserAssignMode;
            if (!library.StartCopy(entry, local =>
                {
                    m_App.Media.Library.Refresh();
                    if (assign) m_App.Media.AssignLocalFile(local);
                    else if (!m_App.Media.Select(local, out string error)) m_App.Media.ReportProblem(error);
                }, out string problem))
            {
                m_BrowserInfo.Text = problem;
                m_App.Media.ReportProblem(problem);
                return;
            }
            ShowPage(m_SessionPage);
        }

        void PickEntry(MediaEntry entry, bool resume = false)
        {
            if (entry == null) return;
            if (m_BrowserAssignMode)
            {
                m_App.Media.AssignLocalFile(entry);
            }
            else if (!m_App.Media.Select(entry, out string error, resume))
            {
                m_BrowserInfo.Text = error;
                return;
            }
            ShowPage(m_SessionPage);
        }

        /// <summary>"Im Netz suchen" in the source list; inside a source the copy/stream switch, or "Anmelden"/"Erneut versuchen" after an error.</summary>
        void NetworkPrimary()
        {
            var library = NetworkLibrary.Instance;
            if (library == null) return;
            if (library.Current == null && library.IsLinkingPlex) library.CancelPlexLink();
            else if (library.Current == null) library.Discover();
            else if (library.Error.Length > 0 && library.Current.Kind == NetworkSourceKind.Plex) library.StartPlexLink();
            else if (library.Error.Length > 0 && library.Current.Kind != NetworkSourceKind.Dlna) AskLogin(library.Current.Clone());
            else if (library.Error.Length > 0) library.Reload();
            else library.CopyFirst = !library.CopyFirst;
            RefreshBrowser();
        }

        /// <summary>"NAS hinzufügen" in the source list, "Quelle entfernen" (with confirmation) inside a saved source.</summary>
        void NetworkSecondary()
        {
            var library = NetworkLibrary.Instance;
            if (library == null) return;
            if (library.Current == null)
            {
                EnterNasAddress();
                return;
            }
            if (library.IsCopying)
            {
                library.CancelCopy();
                RefreshBrowser();
                return;
            }
            if (!library.IsSaved(library.Current)) return;
            if (Time.unscaledTime < m_RemoveConfirmUntil)
            {
                m_RemoveConfirmUntil = 0;
                library.Remove(library.Current);
                m_BrowserPageIndex = 0;
            }
            else
            {
                m_RemoveConfirmUntil = Time.unscaledTime + 3f;
            }
            RefreshBrowser();
        }

        /// <summary>Deletes the movies copied from a NAS (asks once; the movie playing right now is kept).</summary>
        void DeleteCopies()
        {
            if (Time.unscaledTime < m_DeleteCopiesUntil)
            {
                m_DeleteCopiesUntil = 0;
                var current = m_App.Media.CurrentEntry;
                long before = NetworkLibrary.CopiesSize();
                int deleted = NetworkLibrary.Instance != null ? NetworkLibrary.Instance.DeleteCopies(current != null && !current.IsUrl ? current.Location : null) : 0;
                m_App.Media.Library.Refresh();
                m_BrowserInfo.Text = $"{deleted} Kopien gelöscht, {TextUtil.FormatSize(before - NetworkLibrary.CopiesSize())} frei geworden.";
            }
            else
            {
                m_DeleteCopiesUntil = Time.unscaledTime + 3f;
                m_BrowserInfo.Text = $"NAS-Kopien auf der Quest: {TextUtil.FormatSize(NetworkLibrary.CopiesSize())} – nochmal tippen zum Löschen.";
            }
            RefreshBrowser();
        }

        void EnterNasAddress()
        {
            OpenKeyboard(new KeyboardRequest
            {
                Title = "Adresse von NAS, Netzlaufwerk oder Medienserver",
                Hint = "smb://192.168.1.10/filme · https://nas:5006/filme (WebDAV) · http://nas:8096 (Jellyfin) · plex",
                Initial = "smb://",
                MaxLength = 200,
                AllowLowercase = true,
                AllowSpace = true,
                ReturnPage = m_BrowserPage,
                Accepts = c => c > ' ' && c != '•',
                Submit = text =>
                {
                    if (!NetworkSource.TryParse(text, out var source, out string error))
                    {
                        m_App.Media.ReportProblem(error);
                        m_BrowserInfo.Text = error;
                        return;
                    }
                    var library = NetworkLibrary.Instance;
                    if (library == null) return;
                    if (source.Kind == NetworkSourceKind.Plex)
                    {
                        library.StartPlexLink();
                        return;
                    }
                    m_BrowserInfo.Text = "Prüfe " + source.Address + " …";
                    library.Identify(source, found =>
                    {
                        if (found.Kind == NetworkSourceKind.Plex) library.StartPlexLink();
                        else AskLogin(found);
                    });
                },
            });
        }

        /// <summary>Asks for user name and password, then tries the source and saves it if it works.</summary>
        void AskLogin(NetworkSource source)
        {
            bool mediaServer = source.Kind == NetworkSourceKind.Jellyfin;
            OpenKeyboard(new KeyboardRequest
            {
                Title = $"Benutzername für {TextUtil.Ellipsize(source.Name, 28)}",
                Hint = mediaServer ? "Dein Benutzer auf dem Jellyfin/Emby-Server."
                    : "Leer lassen für Gastzugang. Mit Domäne: DOMÄNE\\name (\\ über Umschalt).",
                Initial = source.User,
                MaxLength = 64,
                AllowLowercase = true,
                AllowSpace = true,
                AllowEmpty = !mediaServer,
                ReturnPage = m_BrowserPage,
                Accepts = c => c > ' ' && c != '•',
                Submit = user =>
                {
                    source.User = user;
                    if (user.Length == 0)
                    {
                        source.Password = string.Empty;
                        StartAddingSource(source);
                        return;
                    }
                    OpenKeyboard(new KeyboardRequest
                    {
                        Title = $"Passwort für {TextUtil.Ellipsize(user, 30)}",
                        Hint = "Wird nur auf dieser Brille gespeichert. Sonderzeichen über Umschalt.",
                        MaxLength = 128,
                        AllowLowercase = true,
                        AllowSpace = true,
                        AllowEmpty = true,
                        Secret = true,
                        ReturnPage = m_BrowserPage,
                        Accepts = c => c >= ' ' && c != '•',
                        Submit = password =>
                        {
                            source.Password = password;
                            StartAddingSource(source);
                        },
                    });
                },
            });
        }

        void StartAddingSource(NetworkSource source)
        {
            m_BrowserNetwork = true;
            m_BrowserRecent = false;
            m_BrowserPageIndex = 0;
            NetworkLibrary.Instance?.AddAndOpen(source);
            RefreshBrowser();
        }

        /// <summary>What the browser shows right now (recent movies, local movies, sources or a folder), filtered by the search text.</summary>
        List<object> BrowserItems()
        {
            var library = NetworkLibrary.Instance;
            var items = new List<object>();
            if (m_BrowserRecent)
                foreach (var e in m_App.Media.Recent.Entries) items.Add(e);
            else if (!m_BrowserNetwork || library == null)
                foreach (var e in m_App.Media.Library.Entries) items.Add(e);
            else if (library.Current == null)
                foreach (var src in library.Sources) items.Add(src);
            else
                foreach (var item in library.Items) items.Add(item);
            if (m_Filter.Length == 0) return items;

            string wanted = MediaMatcher.Normalize(m_Filter);
            items.RemoveAll(o =>
            {
                string text;
                switch (o)
                {
                    case MediaEntry e: text = e.Title + " " + e.FileName; break;
                    case NetworkItem i: text = i.DisplayTitle + " " + i.Name; break;
                    case NetworkSource src: text = src.Name; break;
                    default: return true;
                }
                return MediaMatcher.Normalize(text).IndexOf(wanted, StringComparison.Ordinal) < 0;
            });
            return items;
        }

        void RefreshBrowser()
        {
            var library = NetworkLibrary.Instance;
            bool recent = m_BrowserRecent;
            bool network = !recent && m_BrowserNetwork && library != null;
            m_TabRecent.Highlighted = recent;
            m_TabLocal.Highlighted = !recent && !network;
            m_TabNetwork.Highlighted = network;
            foreach (var b in m_LocalBar) b.gameObject.SetActive(!recent && !network);
            foreach (var b in m_NetworkBar) b.gameObject.SetActive(network);
            foreach (var b in m_RecentBar) b.gameObject.SetActive(recent);
            if (!recent && !network)
            {
                m_LocalBar[4].gameObject.SetActive(!m_BrowserAssignMode);
                long copies = NetworkLibrary.CopiesSize();
                m_LocalBar[5].gameObject.SetActive(copies > 0);
                m_LocalBar[5].Label = Time.unscaledTime < m_DeleteCopiesUntil ? "Alle löschen?" : "NAS-Kopien";
                m_LocalBar[5].Style = Time.unscaledTime < m_DeleteCopiesUntil ? ButtonStyle.Danger : ButtonStyle.Normal;
            }
            if (recent)
            {
                bool confirm = Time.unscaledTime < m_ClearRecentUntil;
                m_RecentBar[3].Label = confirm ? "Wirklich leeren?" : "Liste leeren";
                m_RecentBar[3].Style = confirm ? ButtonStyle.Danger : ButtonStyle.Normal;
                m_RecentBar[3].Interactable = m_App.Media.Recent.Count > 0;
            }

            if (m_BrowserAssignMode)
                m_BrowserTitle.Text = "Datei für: " + Playback.Current.Media.Title;
            else if (network && library.Current != null)
                m_BrowserTitle.Text = library.Current.Name;
            else
                m_BrowserTitle.Text = recent ? "Weiterschauen" : "Film wählen";

            m_BrowserInfo.Color = network && library.Error.Length > 0 ? UIStyle.Warning : UIStyle.TextDim;
            bool sourceList = false;
            if (recent)
            {
                m_BrowserInfo.Text = m_App.Media.Recent.Count == 0
                    ? "Noch nichts geschaut. Hier erscheinen die zuletzt gesehenen Filme."
                    : "Zuletzt gesehen – antippen setzt an der gespeicherten Stelle fort.";
            }
            else if (!network)
            {
                var local = m_App.Media.Library;
                m_BrowserInfo.Text = local.IsScanning
                    ? "Suche Filme …"
                    : local.Entries.Count == 0
                        ? "Keine Filme gefunden. Lege Videos in Movies/ oder Download/ ab, Streams in urls.txt."
                        : "Gesucht in Movies, Download und " + MediaLibrary.AppMoviesFolder;
            }
            else if (library.Current == null)
            {
                sourceList = true;
                var sources = library.Sources;
                if (library.IsLinkingPlex)
                    m_BrowserTitle.Text = library.PlexCode.Length > 0 ? "Plex-Code:  " + library.PlexCode : "Mit Plex verbinden …";
                m_BrowserInfo.Text = library.IsLinkingPlex
                        ? library.PlexCode.Length > 0 ? $"Am Handy/PC {PlexServers.LinkPage} öffnen und den Code eingeben – danach geht es hier von selbst weiter." : "Hole einen Code von plex.tv …"
                    : library.Error.Length > 0 ? library.Error
                    : library.IsDiscovering ? "Suche NAS und Medienserver im WLAN …"
                    : sources.Count == 0 ? "Nichts gefunden. Über „Hinzufügen“ die Adresse eingeben (SMB, WebDAV, Jellyfin oder „plex“)."
                    : "Freigaben (SMB), WebDAV, DLNA, Jellyfin/Emby und Plex";
                m_NetworkBar[3].Label = library.IsLinkingPlex ? "Abbrechen" : "Im Netz suchen";
                m_NetworkBar[3].Highlighted = false;
                m_NetworkBar[3].Interactable = !library.IsDiscovering || library.IsLinkingPlex;
                m_NetworkBar[4].Label = "Hinzufügen";
                m_NetworkBar[4].Interactable = true;
            }
            else
            {
                string copy = library.CopyStatus;
                m_BrowserInfo.Text = library.IsBusy ? $"Verbinde mit {library.Current.Name} …"
                    : copy.Length > 0 ? copy
                    : library.Error.Length > 0
                        ? library.Error + (library.LoginRejected || (library.CurrentFailed && library.Current.Kind != NetworkSourceKind.Dlna && library.Current.Kind != NetworkSourceKind.WebDav) ? "  → „Anmelden“" : string.Empty)
                    : library.Items.Count == 0 ? "Keine Filme oder Ordner hier."
                    : library.PathLabel;
                bool failed = library.Error.Length > 0;
                m_NetworkBar[3].Label = !failed ? (library.CopyFirst ? "Erst kopieren" : "Streamen")
                    : library.Current.Kind == NetworkSourceKind.Dlna ? "Erneut versuchen" : "Anmelden";
                m_NetworkBar[3].Highlighted = failed;
                m_NetworkBar[3].Interactable = !library.IsBusy;
                bool saved = library.IsSaved(library.Current);
                m_NetworkBar[4].Label = library.IsCopying ? "Kopieren abbrechen"
                    : Time.unscaledTime < m_RemoveConfirmUntil ? "Wirklich entfernen?" : "Quelle entfernen";
                m_NetworkBar[4].Interactable = saved || library.IsCopying;
            }

            var items = BrowserItems();
            if (m_Filter.Length > 0)
            {
                m_BrowserInfo.Color = UIStyle.Accent;
                m_BrowserInfo.Text = $"Filter „{m_Filter}“: {items.Count} Treffer – „Filter aus“ zeigt wieder alles";
            }
            m_SearchButton.Label = m_Filter.Length > 0 ? "Filter aus" : "Suchen";
            m_SearchButton.Highlighted = m_Filter.Length > 0;
            m_ViewButton.Label = m_TileView ? "Liste" : "Kacheln";
            m_ViewButton.gameObject.SetActive(!sourceList);

            m_ShowingTiles = m_TileView && !sourceList;
            int pageSize = BrowserPageSize;
            int pages = Mathf.Max(1, Mathf.CeilToInt(items.Count / (float)pageSize));
            m_BrowserPageIndex = Mathf.Clamp(m_BrowserPageIndex, 0, pages - 1);
            m_PageLabel.Text = $"{m_BrowserPageIndex + 1}/{pages}";

            for (int i = 0; i < k_BrowserRows; i++)
            {
                int idx = m_BrowserPageIndex * pageSize + i;
                object item = !m_ShowingTiles && idx < items.Count ? items[idx] : null;
                m_RowItems[i] = item;
                m_Rows[i].gameObject.SetActive(item != null);
                if (item != null)
                    m_Rows[i].Label = RowLabel(item, library);
            }
            for (int i = 0; i < k_Tiles; i++)
            {
                int idx = m_BrowserPageIndex * pageSize + i;
                object item = m_ShowingTiles && idx < items.Count ? items[idx] : null;
                m_TileItems[i] = item;
                m_Tiles[i].Visible = item != null;
                if (item != null) UpdateTile(m_Tiles[i], item, library);
            }
        }

        void UpdateTile(PosterTile tile, object item, NetworkLibrary library)
        {
            switch (item)
            {
                case MediaEntry entry:
                {
                    var details = new StringBuilder();
                    var format = entry.FormatHint ?? StereoFormats.Detect(entry.FileName);
                    var progress = m_App.Media.ProgressOf(entry);
                    float fraction = -1;
                    if (progress.HasValue && progress.Value.duration > 0)
                    {
                        fraction = (float)(progress.Value.position / progress.Value.duration);
                        details.Append("noch ").Append(TextUtil.FormatDuration(progress.Value.duration - progress.Value.position));
                    }
                    else if (entry.IsUrl) details.Append("Stream");
                    else if (entry.Size > 0) details.Append(TextUtil.FormatSize(entry.Size));
                    if (format != StereoFormat.Mono) details.Append(details.Length > 0 ? " · " : string.Empty).Append(format.ShortLabel());
                    tile.Set(entry.Title, details.ToString(), PosterCache.Instance.Get(entry), false, fraction);
                    break;
                }
                case NetworkItem networkItem:
                {
                    var details = new StringBuilder();
                    if (networkItem.Year > 0) details.Append(networkItem.Year);
                    float fraction = -1;
                    if (!networkItem.IsFolder)
                    {
                        var format = StereoFormats.Detect(networkItem.Name);
                        if (format != StereoFormat.Mono) details.Append(details.Length > 0 ? " · " : string.Empty).Append(format.ShortLabel());
                        var progress = m_App.Media.ProgressOf(new MediaEntry { Name = networkItem.Name, Location = networkItem.Location, SourceId = string.Empty });
                        if (progress.HasValue && progress.Value.duration > 0) fraction = (float)(progress.Value.position / progress.Value.duration);
                        if (networkItem.Size > 0 && details.Length < 12) details.Append(details.Length > 0 ? " · " : string.Empty).Append(TextUtil.FormatSize(networkItem.Size));
                    }
                    else if (details.Length == 0)
                    {
                        details.Append("Ordner");
                    }
                    tile.Set(networkItem.DisplayTitle, details.ToString(), PosterCache.Instance.Get(networkItem, library), networkItem.IsFolder, fraction);
                    break;
                }
            }
        }

        /// <summary>Wraps long messages (errors) onto at most two lines.</summary>
        static string TwoLines(string text)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= 60) return text;
            string[] lines = SrtParser.Wrap(text, 60).Split('\n');
            if (lines.Length <= 2) return string.Join("\n", lines);
            return lines[0] + "\n" + TextUtil.Ellipsize(string.Join(" ", lines, 1, lines.Length - 1), 60);
        }

        static string RowLabel(object item, NetworkLibrary library)
        {
            var label = new StringBuilder();
            switch (item)
            {
                case MediaEntry entry:
                {
                    var format = entry.FormatHint ?? StereoFormats.Detect(entry.FileName);
                    label.Append(TextUtil.Ellipsize(entry.Title, 48));
                    if (entry.IsUrl) label.Append("   ·  Stream");
                    else if (entry.Size > 0) label.Append("   ·  ").Append(TextUtil.FormatSize(entry.Size));
                    if (format != StereoFormat.Mono) label.Append("   ·  ").Append(format.DisplayName());
                    break;
                }
                case NetworkSource source:
                    label.Append(TextUtil.Ellipsize(source.Name, 40)).Append("   ·  ").Append(source.KindLabel);
                    if (library != null && !library.IsSaved(source)) label.Append("  ·  gefunden");
                    else if (source.HasLogin && source.Kind != NetworkSourceKind.Plex) label.Append("  ·  ").Append(TextUtil.Ellipsize(source.User, 16));
                    break;
                case NetworkItem networkItem when networkItem.IsFolder:
                    label.Append(TextUtil.Ellipsize(networkItem.DisplayTitle, 56)).Append("  ›");
                    break;
                case NetworkItem file:
                {
                    var format = StereoFormats.Detect(file.Name);
                    label.Append(TextUtil.Ellipsize(file.DisplayTitle, 48));
                    if (file.Year > 0) label.Append(" (").Append(file.Year).Append(')');
                    if (file.Size > 0) label.Append("   ·  ").Append(TextUtil.FormatSize(file.Size));
                    if (format != StereoFormat.Mono) label.Append("   ·  ").Append(format.DisplayName());
                    break;
                }
            }
            return label.ToString();
        }

    }
}
