# Eigener Video-Player – Konzept

Unitys `VideoPlayer` ist unter Android für gestreamte Quellen ungeeignet: kleine Puffer, langsames Lesen,
träges Spulen, Ton verzerrt bei Geschwindigkeitsänderung, kein AC3/DTS, keine eingebetteten Untertitel.
The Movies bringt deshalb einen eigenen Player mit, gebaut auf **ExoPlayer (AndroidX Media3)** – der
Engine, die auch YouTube, Kodi-Forks, Jellyfin und die meisten Android-Video-Apps nutzen.

## Aufbau

```
NAS (SMB/WebDAV) ──► SourceStreamer (3 Verbindungen) ──► LocalStreamServer http://127.0.0.1 ─┐
DLNA / http(s) ─────────────────────────────────────────────────────────────────────────────┤
lokale Datei ───────────────────────────────────────────────────────────────────────────────┤
                                                                                           ▼
                                  ExoPlayer (eigener Thread)  ── Puffer bis 60 s / 192 MB, 10 s zurück
                                  ├─ Video: MediaCodec (H.264/HEVC/VP9/AV1, bis 8K) ──► SurfaceTexture (OES)
                                  ├─ Ton: MediaCodec / FFmpeg (AC3, E-AC3, DTS, TrueHD) ──► AudioTrack
                                  └─ Untertitel: eingebettete SRT/SSA/VobSub-Text ──► Cue-Text
                                                                                           │
Unity Render-Thread (GL.IssuePluginEvent) ◄────────────────────────────────────────────────┘
  updateTexImage → Kopie in sRGB-Textur mit Mipmaps + 4× anisotrop ──► Texture2D.CreateExternalTexture
  ──► StereoVideo-Shader (SBS/OU, gewölbte Leinwand, Saal-Licht)
```

| Datei | Aufgabe |
| --- | --- |
| `Assets/Plugins/Android/TheMoviesPlayer.java` | ExoPlayer, Zustand, Spuren, Untertitel, GL-Kopie |
| `Scripts/Runtime/Video/NativeVideoPlayer.cs` | JNI-Brücke, Render-Thread-Callback, externe Textur |
| `Scripts/Runtime/Video/MoviePlayer.cs` | Fassade: eigener Player, sonst Unitys VideoPlayer |
| `Editor/Setup/AndroidManifestFixes.cs` | Gradle: Media3 + FFmpeg-Decoder, AndroidX, ProGuard, Cleartext |
| `tools/JavaCheck` | Kompilierprüfung des Java-Teils gegen API-Stubs |

## Eigenschaften

- **Puffer & Spulen:** ExoPlayer lädt bis 60 s voraus und hält 10 s zurück; Sprünge innerhalb des Puffers
  sind sofort da, sonst lädt er gezielt per HTTP-Range nach. Synchronisations-Sprünge sind bildgenau (`EXACT`).
- **Geschwindigkeit ohne Tonverzerrung:** Drift-Korrektur (±5 %) per Time-Stretching (Sonic), Tonhöhe bleibt.
- **Ton:** alles, was der Quest-Decoder kann, plus AC3/E-AC3/DTS/TrueHD über den FFmpeg-Decoder (Jellyfin-Build).
  Deutsche Tonspur wird bevorzugt.
- **Untertitel:** eingebettete Textuntertitel (deutsch bevorzugt) erscheinen, wenn keine `.srt` daneben liegt.
- **Bildqualität:** Mipmaps + anisotrope Filterung gegen Flimmern auf der gewölbten Leinwand; sRGB-korrekt.
- **Zustand für die Sync-Logik:** Position (zwischen den 20-ms-Abtastungen hochgerechnet), Puffer-Füllstand,
  Puffern/Bereit, Spulen fertig.

## Voraussetzungen / Grenzen

- **OpenGL ES 3** als Grafik-API (Menü *The Movies → Grafik auf OpenGL ES 3 umstellen*). Mit Vulkan fällt die
  App auf Unitys VideoPlayer zurück (Hinweis im Menü). Ein Vulkan-Weg (AHardwareBuffer-Import) wäre möglich,
  braucht aber ein natives C++-Plugin.
- Gradle lädt beim Bauen `androidx.media3:media3-exoplayer:1.4.1` und
  `org.jellyfin.media3:media3-ffmpeg-decoder:1.4.1+1` (abschaltbar: *The Movies → Player: AC3-, DTS- und
  TrueHD-Ton (FFmpeg)*).
- HDR10/Dolby Vision werden als SDR gezeigt (Tonemapping macht der Decoder).

## Nächste Ausbaustufen

1. Ton-/Untertitelspur im Menü wählbar (Daten liegen schon vor: `TheMoviesPlayer.tracks`).
2. SMB direkt als ExoPlayer-`DataSource` (spart den lokalen HTTP-Umweg).
3. Wiedergabe als OpenXR-Composition-Layer (Android-Surface-Swapchain): schärfstes Bild, ganz ohne Kopie.
