#!/bin/bash
# Bauen, installieren und Logs lesen ohne offenen Editor (macOS).
#
#   tools/quest.sh build       APK im Batchmode bauen (Unity-Editor muss geschlossen sein)
#   tools/quest.sh install     APK installieren (adb install -r, Daten bleiben)
#   tools/quest.sh reinstall   erst deinstallieren, dann installieren (nötig nach Manifest-Änderungen;
#                              löscht App-Daten: Einstellungen, Zugangsdaten, NAS-Kopien im App-Ordner)
#   tools/quest.sh start       App starten
#   tools/quest.sh log         Logcat (Unity, The Movies, OpenXR, Abstürze) mitlesen
#   tools/quest.sh applog      In-App-Protokoll (themovies-log.txt) von der Brille holen
#   tools/quest.sh all         build + reinstall + start + log
#
# Unity-Pfad: UNITY=/pfad/zu/Unity; Standard ist die Version aus ProjectSettings/ProjectVersion.txt im Unity Hub.
set -euo pipefail

cd "$(dirname "$0")/.."
PROJECT="$PWD"
APK="$PROJECT/Builds/TheMovies.apk"
PACKAGE="com.themovies.cinema"
BUILD_LOG="$PROJECT/Logs/batch-build.log"

VERSION=$(sed -n 's/^m_EditorVersion: //p' ProjectSettings/ProjectVersion.txt)
UNITY="${UNITY:-/Applications/Unity/Hub/Editor/$VERSION/Unity.app/Contents/MacOS/Unity}"
ANDROID_PLAYER="$(dirname "$(dirname "$UNITY")")/PlaybackEngines/AndroidPlayer"

if [[ -z "${ADB:-}" ]]; then
    if command -v adb >/dev/null; then ADB=adb
    elif [[ -x "$HOME/Library/Android/sdk/platform-tools/adb" ]]; then ADB="$HOME/Library/Android/sdk/platform-tools/adb"
    else ADB="$ANDROID_PLAYER/SDK/platform-tools/adb"; fi
fi

build() {
    [[ -x "$UNITY" ]] || { echo "Unity $VERSION nicht gefunden: $UNITY (UNITY=… setzen)"; exit 1; }
    if pgrep -f "Unity.app/Contents/MacOS/Unity -projectPath $PROJECT" >/dev/null ||
       pgrep -f "Unity.app/Contents/MacOS/Unity.*-projectpath $PROJECT" >/dev/null; then
        echo "Der Unity-Editor hat das Projekt offen – bitte schließen (Batchmode braucht das Projekt exklusiv)."
        exit 1
    fi
    mkdir -p "$(dirname "$BUILD_LOG")"
    echo "Baue mit Unity $VERSION … (Log: $BUILD_LOG)"
    local start=$SECONDS status=0
    "$UNITY" -batchmode -quit -nographics -projectPath "$PROJECT" -buildTarget Android \
        -executeMethod TheMovies.EditorTools.ProjectSetup.BuildApkBatch -logFile "$BUILD_LOG" || status=$?
    echo "--- Fehler/Warnungen aus dem Build-Log ---"
    grep -E "error CS|\): error|Exception|\[TheMovies\]|BUILD FAILED|FAILURE:|What went wrong" "$BUILD_LOG" | grep -vE "^UnityEngine\.| kb[[:space:]]" | sort -u | head -60 || true
    echo "--- $(( SECONDS - start )) s, Exit-Code $status ---"
    [[ $status -eq 0 && -f "$APK" ]] || { echo "Build fehlgeschlagen."; exit 1; }
    ls -lh "$APK"
}

device() {
    # The Quest drops off USB for a moment now and then (sleep, putting it on): wait up to 20 s.
    for _ in $(seq 1 20); do
        "$ADB" get-state >/dev/null 2>&1 && return 0
        sleep 1
    done
    echo "Keine Quest per adb erreichbar (USB, Entwicklermodus, 'USB-Debugging zulassen')."
    exit 1
}

install() {
    device
    "$ADB" install -r "$APK"
}

reinstall() {
    device
    "$ADB" uninstall "$PACKAGE" || true
    "$ADB" install "$APK"
}

start_app() {
    device
    "$ADB" shell monkey -p "$PACKAGE" -c android.intent.category.LAUNCHER 1 >/dev/null 2>&1
    echo "Gestartet: $PACKAGE"
}

log() {
    device
    "$ADB" logcat -c
    "$ADB" logcat -v time Unity:V TheMovies:V TheMoviesPlayer:V OpenXR:V OVRPlugin:V XrRuntime:I \
        AndroidRuntime:E DEBUG:V libc:F CRASH:V ActivityManager:W '*:S'
}

applog() {
    device
    local dir="/sdcard/Android/data/$PACKAGE/files"
    "$ADB" shell ls "$dir" | grep -i log || true
    "$ADB" pull "$dir/themovies-log.txt" "$PROJECT/Logs/" && echo "→ Logs/themovies-log.txt"
}

case "${1:-}" in
    build) build ;;
    install) install ;;
    reinstall) reinstall ;;
    start) start_app ;;
    log) log ;;
    applog) applog ;;
    all) build; reinstall; start_app; log ;;
    *) sed -n '2,12p' "$0"; exit 1 ;;
esac
