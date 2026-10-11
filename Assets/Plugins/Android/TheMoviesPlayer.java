package com.themovies.player;

import android.content.Context;
import android.graphics.SurfaceTexture;
import android.net.Uri;
import android.opengl.GLES11Ext;
import android.opengl.GLES20;
import android.opengl.GLES30;
import android.os.Handler;
import android.os.HandlerThread;
import android.os.SystemClock;
import android.util.Log;
import android.view.Surface;

import androidx.annotation.OptIn;
import androidx.media3.common.C;
import androidx.media3.common.Format;
import androidx.media3.common.MediaItem;
import androidx.media3.common.PlaybackException;
import androidx.media3.common.PlaybackParameters;
import androidx.media3.common.Player;
import androidx.media3.common.TrackSelectionParameters;
import androidx.media3.common.Tracks;
import androidx.media3.common.VideoSize;
import androidx.media3.common.text.Cue;
import androidx.media3.common.text.CueGroup;
import androidx.media3.common.util.UnstableApi;
import androidx.media3.datasource.DefaultDataSource;
import androidx.media3.datasource.DefaultHttpDataSource;
import androidx.media3.exoplayer.DefaultLoadControl;
import androidx.media3.exoplayer.DefaultRenderersFactory;
import androidx.media3.exoplayer.ExoPlayer;
import androidx.media3.exoplayer.SeekParameters;
import androidx.media3.exoplayer.source.DefaultMediaSourceFactory;

import java.io.File;
import java.nio.ByteBuffer;
import java.nio.ByteOrder;
import java.nio.FloatBuffer;
import java.util.ArrayList;
import java.util.HashMap;
import java.util.List;
import java.util.concurrent.atomic.AtomicBoolean;

/**
 * Video player for The Movies: ExoPlayer (Media3) decodes on its own thread into a SurfaceTexture; on Unity's
 * render thread (OpenGL ES 3) each new frame is copied into a regular sRGB texture with mipmaps, which Unity
 * shows via Texture2D.CreateExternalTexture.
 *
 * Threads:
 *  - Unity main thread: create/open/play/pause/seek/... and state() (called from C# via JNI)
 *  - player thread:     owns the ExoPlayer (all player calls are posted there)
 *  - Unity render thread: renderFrame() (via GL.IssuePluginEvent), owns all GL objects
 */
@OptIn(markerClass = UnstableApi.class)
public final class TheMoviesPlayer {
    private static final String TAG = "TheMoviesPlayer";
    private static final HashMap<Integer, TheMoviesPlayer> sPlayers = new HashMap<>();
    private static final List<TheMoviesPlayer> sReleasing = new ArrayList<>();
    private static int sNextId = 1;

    // ---------------- Static API for C# ----------------

    public static synchronized int create(Context context) {
        int id = sNextId++;
        sPlayers.put(id, new TheMoviesPlayer(context.getApplicationContext(), id));
        return id;
    }

    private static synchronized TheMoviesPlayer get(int id) {
        return sPlayers.get(id);
    }

    public static void open(int id, String location, String preferredLanguage) {
        TheMoviesPlayer p = get(id);
        if (p != null) p.openOnPlayerThread(location, preferredLanguage);
    }

    public static void setPlaying(int id, boolean playing) {
        TheMoviesPlayer p = get(id);
        if (p != null) p.post(() -> { if (p.mPlayer != null) p.mPlayer.setPlayWhenReady(playing); });
    }

    /** @param exact true: frame accurate (sync), false: nearest keyframe (fast scrubbing) */
    public static void seek(int id, double seconds, boolean exact) {
        TheMoviesPlayer p = get(id);
        if (p == null) return;
        p.mSeeking = true;
        p.mSeekIssuedAt = SystemClock.elapsedRealtime();
        p.mSeekTarget = seconds;
        p.mSampleMs = (long) (seconds * 1000);
        p.mSampleTime = SystemClock.elapsedRealtime();
        p.post(() -> {
            if (p.mPlayer == null) return;
            p.mPlayer.setSeekParameters(exact ? SeekParameters.EXACT : SeekParameters.CLOSEST_SYNC);
            p.mPlayer.seekTo(Math.max(0, (long) (seconds * 1000)));
        });
    }

    public static void setSpeed(int id, float speed) {
        TheMoviesPlayer p = get(id);
        if (p == null) return;
        p.mSpeed = speed;
        p.post(() -> { if (p.mPlayer != null) p.mPlayer.setPlaybackParameters(new PlaybackParameters(speed)); });
    }

    public static void setVolume(int id, float volume) {
        TheMoviesPlayer p = get(id);
        if (p != null) p.post(() -> { if (p.mPlayer != null) p.mPlayer.setVolume(volume); });
    }

    /** Stops the player; GL objects are freed on the next renderFrame call. */
    public static void release(int id) {
        TheMoviesPlayer p;
        synchronized (TheMoviesPlayer.class) {
            p = sPlayers.remove(id);
            if (p != null) sReleasing.add(p);
        }
        if (p == null) return;
        p.mReleased = true;
        p.post(() -> {
            if (p.mPlayer != null) {
                p.mPlayer.release();
                p.mPlayer = null;
            }
            p.mThread.quitSafely();
        });
    }

    /**
     * State for C#: [0] state (0 idle, 1 buffering, 2 ready, 3 ended, -1 error), [1] position s, [2] duration s
     * (-1 unknown), [3] buffered position s, [4] playing, [5] video width, [6] video height, [7] pixel aspect,
     * [8] texture id, [9] texture width, [10] texture height, [11] frames, [12] seeking, [13] cue version,
     * [14] audio unsupported, [15] texture generation, [16] play when ready.
     */
    public static double[] state(int id) {
        TheMoviesPlayer p = get(id);
        double[] s = new double[17];
        if (p == null) return s;
        s[0] = p.mError != null ? -1 : p.mState;
        double position = p.mSampleMs / 1000.0;
        if (p.mIsPlaying && !p.mSeeking)
            position += (SystemClock.elapsedRealtime() - p.mSampleTime) / 1000.0 * p.mSpeed;
        s[1] = p.mSeeking ? p.mSeekTarget : position;
        s[2] = p.mDurationMs > 0 ? p.mDurationMs / 1000.0 : -1;
        s[3] = p.mBufferedMs / 1000.0;
        s[4] = p.mIsPlaying ? 1 : 0;
        s[5] = p.mVideoWidth;
        s[6] = p.mVideoHeight;
        s[7] = p.mPixelRatio;
        s[8] = p.mOutTex;
        s[9] = p.mOutWidth;
        s[10] = p.mOutHeight;
        s[11] = p.mFrames;
        s[12] = p.mSeeking ? 1 : 0;
        s[13] = p.mCueVersion;
        s[14] = p.mAudioUnsupported ? 1 : 0;
        s[15] = p.mTextureGeneration;
        s[16] = p.mPlayWhenReady ? 1 : 0;
        return s;
    }

    public static String error(int id) {
        TheMoviesPlayer p = get(id);
        return p != null && p.mError != null ? p.mError : "";
    }

    /** Text of the subtitle cues shown right now (embedded subtitle tracks), lines separated by \n. */
    public static String cues(int id) {
        TheMoviesPlayer p = get(id);
        return p != null ? p.mCues : "";
    }

    /** Audio and subtitle tracks for the menu, one per line: "a|de|AC-3 5.1|1" (type|language|label|selected). */
    public static String tracks(int id) {
        TheMoviesPlayer p = get(id);
        return p != null ? p.mTrackInfo : "";
    }

    /** Called on Unity's render thread (GL.IssuePluginEvent): pulls the newest video frame into the texture. */
    public static void renderFrame(int id) {
        try {
            List<TheMoviesPlayer> releasing;
            synchronized (TheMoviesPlayer.class) {
                releasing = new ArrayList<>(sReleasing);
                sReleasing.clear();
            }
            for (TheMoviesPlayer r : releasing) r.releaseGl();
            TheMoviesPlayer p = get(id);
            if (p != null && !p.mReleased) p.render();
        } catch (Throwable t) {
            Log.e(TAG, "renderFrame failed", t);
        }
    }

    // ---------------- Instance ----------------

    private final Context mContext;
    private final int mId;
    private final HandlerThread mThread;
    private final Handler mHandler;
    private ExoPlayer mPlayer;

    private volatile boolean mReleased;
    private volatile int mState;
    private volatile boolean mIsPlaying;
    private volatile boolean mPlayWhenReady;
    private volatile long mSampleMs;
    private volatile long mSampleTime;
    private volatile long mDurationMs = -1;
    private volatile long mBufferedMs;
    private volatile float mSpeed = 1f;
    private volatile boolean mSeeking;
    private volatile long mSeekIssuedAt;
    private volatile double mSeekTarget;
    private volatile int mVideoWidth;
    private volatile int mVideoHeight;
    private volatile float mPixelRatio = 1f;
    private volatile String mError;
    private volatile String mCues = "";
    private volatile int mCueVersion;
    private volatile String mTrackInfo = "";
    private volatile boolean mAudioUnsupported;

    // GL (render thread only, except the ids read by state())
    private final AtomicBoolean mFrameAvailable = new AtomicBoolean();
    private SurfaceTexture mSurfaceTexture;
    private Surface mSurface;
    private int mOesTex;
    private int mProgram;
    private int mFbo;
    private int mUTexMatrix;
    private volatile int mOutTex;
    private volatile int mOutWidth;
    private volatile int mOutHeight;
    private volatile int mFrames;
    private volatile int mTextureGeneration;
    private final float[] mTexMatrix = new float[16];
    private FloatBuffer mQuad;
    private boolean mAnisotropic;

    private TheMoviesPlayer(Context context, int id) {
        mContext = context;
        mId = id;
        mThread = new HandlerThread("TheMoviesPlayer-" + id);
        mThread.start();
        mHandler = new Handler(mThread.getLooper());
    }

    private void post(Runnable r) {
        if (mThread.isAlive()) mHandler.post(r);
    }

    private void openOnPlayerThread(String location, String preferredLanguage) {
        post(() -> {
            try {
                DefaultRenderersFactory renderers = new DefaultRenderersFactory(mContext)
                        .setExtensionRendererMode(DefaultRenderersFactory.EXTENSION_RENDERER_MODE_ON)
                        .setEnableDecoderFallback(true);

                // Generous buffer for NAS streams: up to 60 s / 192 MB ahead, keep 10 s behind for quick
                // small rewinds; start after 1.5 s, after a stall wait for 3 s.
                DefaultLoadControl loadControl = new DefaultLoadControl.Builder()
                        .setBufferDurationsMs(20000, 60000, 1500, 3000)
                        .setTargetBufferBytes(192 * 1024 * 1024)
                        .setPrioritizeTimeOverSizeThresholds(false)
                        .setBackBuffer(10000, true)
                        .build();

                DefaultHttpDataSource.Factory http = new DefaultHttpDataSource.Factory()
                        .setUserAgent("TheMovies/1.0 (ExoPlayer)")
                        .setConnectTimeoutMs(15000)
                        .setReadTimeoutMs(30000)
                        .setAllowCrossProtocolRedirects(true);
                DefaultDataSource.Factory data = new DefaultDataSource.Factory(mContext, http);

                mPlayer = new ExoPlayer.Builder(mContext, renderers)
                        .setLooper(mThread.getLooper())
                        .setLoadControl(loadControl)
                        .setMediaSourceFactory(new DefaultMediaSourceFactory(data))
                        .setSeekBackIncrementMs(10000)
                        .setSeekForwardIncrementMs(10000)
                        .build();

                String lang = preferredLanguage == null || preferredLanguage.isEmpty() ? "de" : preferredLanguage;
                TrackSelectionParameters params = mPlayer.getTrackSelectionParameters().buildUpon()
                        .setPreferredAudioLanguages(lang, "deu", "ger")
                        .setPreferredTextLanguages(lang, "deu", "ger")
                        .build();
                mPlayer.setTrackSelectionParameters(params);

                mPlayer.addListener(new Listener());
                if (mSurface != null) mPlayer.setVideoSurface(mSurface);

                Uri uri = location.startsWith("/") ? Uri.fromFile(new File(location)) : Uri.parse(location);
                mPlayer.setMediaItem(MediaItem.fromUri(uri));
                mPlayer.setPlayWhenReady(false);
                mPlayer.prepare();
                mHandler.post(mPoll);
            } catch (Throwable t) {
                Log.e(TAG, "open failed", t);
                mError = t.getClass().getSimpleName() + ": " + t.getMessage();
            }
        });
    }

    /** Samples position and buffer every 20 ms (C# extrapolates in between). */
    private final Runnable mPoll = new Runnable() {
        @Override
        public void run() {
            ExoPlayer player = mPlayer;
            if (player == null || mReleased) return;
            // A seek inside the buffer never passes through BUFFERING: done once the player is ready again.
            if (mSeeking && SystemClock.elapsedRealtime() - mSeekIssuedAt > 120
                    && player.getPlaybackState() != Player.STATE_BUFFERING) {
                finishSeek();
            }
            if (!mSeeking) {
                mSampleMs = player.getCurrentPosition();
                mSampleTime = SystemClock.elapsedRealtime();
            }
            long duration = player.getDuration();
            mDurationMs = duration == C.TIME_UNSET ? -1 : duration;
            mBufferedMs = player.getBufferedPosition();
            mPlayWhenReady = player.getPlayWhenReady();
            mHandler.postDelayed(this, 20);
        }
    };

    private final class Listener implements Player.Listener {
        @Override
        public void onPlaybackStateChanged(int state) {
            switch (state) {
                case Player.STATE_BUFFERING: mState = 1; break;
                case Player.STATE_READY: mState = 2; break;
                case Player.STATE_ENDED: mState = 3; break;
                default: mState = 0; break;
            }
            if (state == Player.STATE_READY || state == Player.STATE_ENDED) finishSeek();
        }

        @Override
        public void onIsPlayingChanged(boolean isPlaying) {
            ExoPlayer player = mPlayer;
            if (player != null) {
                mSampleMs = player.getCurrentPosition();
                mSampleTime = SystemClock.elapsedRealtime();
            }
            mIsPlaying = isPlaying;
        }

        @Override
        public void onPlayerError(PlaybackException error) {
            String message = error.getMessage();
            Throwable cause = error.getCause();
            if (cause != null && cause.getMessage() != null) message += " (" + cause.getMessage() + ")";
            mError = error.getErrorCodeName() + ": " + message;
            Log.e(TAG, "Playback error", error);
        }

        @Override
        public void onVideoSizeChanged(VideoSize size) {
            if (size.width <= 0 || size.height <= 0) return;
            mVideoWidth = size.width;
            mVideoHeight = size.height;
            mPixelRatio = size.pixelWidthHeightRatio > 0 ? size.pixelWidthHeightRatio : 1f;
        }

        @Override
        public void onCues(CueGroup cueGroup) {
            StringBuilder sb = new StringBuilder();
            for (Cue cue : cueGroup.cues) {
                if (cue.text == null) continue;
                if (sb.length() > 0) sb.append('\n');
                sb.append(cue.text);
            }
            mCues = sb.toString();
            mCueVersion++;
        }

        @Override
        public void onTracksChanged(Tracks tracks) {
            StringBuilder sb = new StringBuilder();
            boolean anyAudio = false, anyAudioSupported = false;
            for (Tracks.Group group : tracks.getGroups()) {
                int type = group.getType();
                if (type != C.TRACK_TYPE_AUDIO && type != C.TRACK_TYPE_TEXT) continue;
                for (int i = 0; i < group.length; i++) {
                    Format f = group.getTrackFormat(i);
                    boolean supported = group.isTrackSupported(i);
                    if (type == C.TRACK_TYPE_AUDIO) {
                        anyAudio = true;
                        anyAudioSupported |= supported;
                    }
                    sb.append(type == C.TRACK_TYPE_AUDIO ? 'a' : 't').append('|')
                            .append(f.language != null ? f.language : "").append('|')
                            .append(describe(f)).append('|')
                            .append(group.isTrackSelected(i) ? '1' : '0').append('|')
                            .append(supported ? '1' : '0').append('\n');
                }
            }
            mTrackInfo = sb.toString();
            mAudioUnsupported = anyAudio && !anyAudioSupported;
        }
    }

    private void finishSeek() {
        ExoPlayer player = mPlayer;
        if (player != null) {
            mSampleMs = player.getCurrentPosition();
            mSampleTime = SystemClock.elapsedRealtime();
        }
        mSeeking = false;
    }

    private static String describe(Format f) {
        StringBuilder sb = new StringBuilder();
        if (f.label != null) sb.append(f.label);
        String mime = f.sampleMimeType != null ? f.sampleMimeType : "";
        int slash = mime.indexOf('/');
        if (sb.length() > 0) sb.append(' ');
        sb.append((slash >= 0 ? mime.substring(slash + 1) : mime).toUpperCase());
        if (f.channelCount > 0) sb.append(' ').append(f.channelCount == 6 ? "5.1" : f.channelCount == 8 ? "7.1" : f.channelCount + "ch");
        return sb.toString().replace('|', '/').replace('\n', ' ');
    }

    // ---------------- Render thread ----------------

    private static final String VERTEX =
            "attribute vec4 aPos;\n" +
            "attribute vec4 aUv;\n" +
            "uniform mat4 uTexMatrix;\n" +
            "varying vec2 vUv;\n" +
            "void main() { gl_Position = aPos; vUv = (uTexMatrix * aUv).xy; }\n";

    // The decoder delivers gamma-encoded RGB; the target is an sRGB texture (encodes on write), so decode first.
    private static final String FRAGMENT =
            "#extension GL_OES_EGL_image_external : require\n" +
            "precision highp float;\n" +
            "uniform samplerExternalOES sTex;\n" +
            "varying vec2 vUv;\n" +
            "vec3 toLinear(vec3 c) { return mix(c / 12.92, pow((c + 0.055) / 1.055, vec3(2.4)), step(0.04045, c)); }\n" +
            "void main() { gl_FragColor = vec4(toLinear(texture2D(sTex, vUv).rgb), 1.0); }\n";

    private void render() {
        if (mSurfaceTexture == null) createSurface();
        if (!mFrameAvailable.getAndSet(false)) return;

        mSurfaceTexture.updateTexImage();
        mSurfaceTexture.getTransformMatrix(mTexMatrix);
        int w = mVideoWidth, h = mVideoHeight;
        if (w <= 0 || h <= 0) return;
        if (mOutTex == 0 || w != mOutWidth || h != mOutHeight) createOutput(w, h);
        blit();
        mFrames++;
    }

    private void createSurface() {
        int[] tex = new int[1];
        GLES20.glGenTextures(1, tex, 0);
        mOesTex = tex[0];
        int[] prev = new int[1];
        GLES20.glGetIntegerv(GLES11Ext.GL_TEXTURE_BINDING_EXTERNAL_OES, prev, 0);
        GLES20.glBindTexture(GLES11Ext.GL_TEXTURE_EXTERNAL_OES, mOesTex);
        GLES20.glTexParameteri(GLES11Ext.GL_TEXTURE_EXTERNAL_OES, GLES20.GL_TEXTURE_MIN_FILTER, GLES20.GL_LINEAR);
        GLES20.glTexParameteri(GLES11Ext.GL_TEXTURE_EXTERNAL_OES, GLES20.GL_TEXTURE_MAG_FILTER, GLES20.GL_LINEAR);
        GLES20.glTexParameteri(GLES11Ext.GL_TEXTURE_EXTERNAL_OES, GLES20.GL_TEXTURE_WRAP_S, GLES20.GL_CLAMP_TO_EDGE);
        GLES20.glTexParameteri(GLES11Ext.GL_TEXTURE_EXTERNAL_OES, GLES20.GL_TEXTURE_WRAP_T, GLES20.GL_CLAMP_TO_EDGE);
        GLES20.glBindTexture(GLES11Ext.GL_TEXTURE_EXTERNAL_OES, prev[0]);

        mSurfaceTexture = new SurfaceTexture(mOesTex);
        mSurfaceTexture.setOnFrameAvailableListener(st -> mFrameAvailable.set(true), mHandler);
        mSurface = new Surface(mSurfaceTexture);
        final Surface surface = mSurface;
        post(() -> { if (mPlayer != null) mPlayer.setVideoSurface(surface); });

        mProgram = buildProgram();
        mUTexMatrix = GLES20.glGetUniformLocation(mProgram, "uTexMatrix");
        int[] fbo = new int[1];
        GLES20.glGenFramebuffers(1, fbo, 0);
        mFbo = fbo[0];

        float[] quad = {
                // x, y, u, v
                -1f, -1f, 0f, 0f,
                1f, -1f, 1f, 0f,
                -1f, 1f, 0f, 1f,
                1f, 1f, 1f, 1f,
        };
        mQuad = ByteBuffer.allocateDirect(quad.length * 4).order(ByteOrder.nativeOrder()).asFloatBuffer();
        mQuad.put(quad).position(0);

        String extensions = GLES20.glGetString(GLES20.GL_EXTENSIONS);
        mAnisotropic = extensions != null && extensions.contains("GL_EXT_texture_filter_anisotropic");
    }

    private void createOutput(int w, int h) {
        int[] prev = new int[1];
        GLES20.glGetIntegerv(GLES20.GL_TEXTURE_BINDING_2D, prev, 0);
        int[] tex = new int[1];
        if (mOutTex != 0) {
            tex[0] = mOutTex;
            GLES20.glDeleteTextures(1, tex, 0);
        }
        GLES20.glGenTextures(1, tex, 0);
        GLES20.glBindTexture(GLES20.GL_TEXTURE_2D, tex[0]);
        int levels = 1 + (int) Math.floor(Math.log(Math.max(w, h)) / Math.log(2));
        GLES30.glTexStorage2D(GLES20.GL_TEXTURE_2D, levels, GLES30.GL_SRGB8_ALPHA8, w, h);
        GLES20.glTexParameteri(GLES20.GL_TEXTURE_2D, GLES20.GL_TEXTURE_MIN_FILTER, GLES20.GL_LINEAR_MIPMAP_LINEAR);
        GLES20.glTexParameteri(GLES20.GL_TEXTURE_2D, GLES20.GL_TEXTURE_MAG_FILTER, GLES20.GL_LINEAR);
        GLES20.glTexParameteri(GLES20.GL_TEXTURE_2D, GLES20.GL_TEXTURE_WRAP_S, GLES20.GL_CLAMP_TO_EDGE);
        GLES20.glTexParameteri(GLES20.GL_TEXTURE_2D, GLES20.GL_TEXTURE_WRAP_T, GLES20.GL_CLAMP_TO_EDGE);
        if (mAnisotropic) GLES20.glTexParameterf(GLES20.GL_TEXTURE_2D, 0x84FE /* MAX_ANISOTROPY_EXT */, 4f);
        GLES20.glBindTexture(GLES20.GL_TEXTURE_2D, prev[0]);
        mOutWidth = w;
        mOutHeight = h;
        mOutTex = tex[0];
        mTextureGeneration++;
    }

    private void blit() {
        // Save the GL state Unity relies on.
        int[] i1 = new int[1];
        int[] drawFbo = new int[1], readFbo = new int[1], viewport = new int[4], program = new int[1], activeTex = new int[1];
        int[] tex2d = new int[1], texOes = new int[1], arrayBuffer = new int[1], vao = new int[1];
        boolean[] colorMask = new boolean[4];
        GLES20.glGetIntegerv(GLES30.GL_DRAW_FRAMEBUFFER_BINDING, drawFbo, 0);
        GLES20.glGetIntegerv(GLES30.GL_READ_FRAMEBUFFER_BINDING, readFbo, 0);
        GLES20.glGetIntegerv(GLES20.GL_VIEWPORT, viewport, 0);
        GLES20.glGetIntegerv(GLES20.GL_CURRENT_PROGRAM, program, 0);
        GLES20.glGetIntegerv(GLES20.GL_ACTIVE_TEXTURE, activeTex, 0);
        GLES20.glActiveTexture(GLES20.GL_TEXTURE0);
        GLES20.glGetIntegerv(GLES20.GL_TEXTURE_BINDING_2D, tex2d, 0);
        GLES20.glGetIntegerv(GLES11Ext.GL_TEXTURE_BINDING_EXTERNAL_OES, texOes, 0);
        GLES20.glGetIntegerv(GLES20.GL_ARRAY_BUFFER_BINDING, arrayBuffer, 0);
        GLES20.glGetIntegerv(GLES30.GL_VERTEX_ARRAY_BINDING, vao, 0);
        GLES20.glGetBooleanv(GLES20.GL_COLOR_WRITEMASK, colorMask, 0);
        boolean blend = GLES20.glIsEnabled(GLES20.GL_BLEND);
        boolean depth = GLES20.glIsEnabled(GLES20.GL_DEPTH_TEST);
        boolean cull = GLES20.glIsEnabled(GLES20.GL_CULL_FACE);
        boolean scissor = GLES20.glIsEnabled(GLES20.GL_SCISSOR_TEST);
        boolean stencil = GLES20.glIsEnabled(GLES20.GL_STENCIL_TEST);

        GLES30.glBindVertexArray(0);
        GLES20.glBindBuffer(GLES20.GL_ARRAY_BUFFER, 0);
        GLES20.glBindFramebuffer(GLES20.GL_FRAMEBUFFER, mFbo);
        GLES20.glFramebufferTexture2D(GLES20.GL_FRAMEBUFFER, GLES20.GL_COLOR_ATTACHMENT0, GLES20.GL_TEXTURE_2D, mOutTex, 0);
        GLES20.glViewport(0, 0, mOutWidth, mOutHeight);
        GLES20.glDisable(GLES20.GL_BLEND);
        GLES20.glDisable(GLES20.GL_DEPTH_TEST);
        GLES20.glDisable(GLES20.GL_CULL_FACE);
        GLES20.glDisable(GLES20.GL_SCISSOR_TEST);
        GLES20.glDisable(GLES20.GL_STENCIL_TEST);
        GLES20.glColorMask(true, true, true, true);

        GLES20.glUseProgram(mProgram);
        GLES20.glBindTexture(GLES11Ext.GL_TEXTURE_EXTERNAL_OES, mOesTex);
        GLES20.glUniformMatrix4fv(mUTexMatrix, 1, false, mTexMatrix, 0);
        mQuad.position(0);
        GLES20.glVertexAttribPointer(0, 2, GLES20.GL_FLOAT, false, 16, mQuad);
        GLES20.glEnableVertexAttribArray(0);
        mQuad.position(2);
        GLES20.glVertexAttribPointer(1, 2, GLES20.GL_FLOAT, false, 16, mQuad);
        GLES20.glEnableVertexAttribArray(1);
        GLES20.glDrawArrays(GLES20.GL_TRIANGLE_STRIP, 0, 4);
        GLES20.glDisableVertexAttribArray(0);
        GLES20.glDisableVertexAttribArray(1);

        // Mipmaps: no shimmering when the screen is small or seen at an angle.
        GLES20.glBindTexture(GLES20.GL_TEXTURE_2D, mOutTex);
        GLES20.glGenerateMipmap(GLES20.GL_TEXTURE_2D);

        // Restore
        GLES20.glBindTexture(GLES20.GL_TEXTURE_2D, tex2d[0]);
        GLES20.glBindTexture(GLES11Ext.GL_TEXTURE_EXTERNAL_OES, texOes[0]);
        GLES20.glActiveTexture(activeTex[0]);
        GLES20.glUseProgram(program[0]);
        GLES20.glBindBuffer(GLES20.GL_ARRAY_BUFFER, arrayBuffer[0]);
        GLES30.glBindVertexArray(vao[0]);
        GLES20.glBindFramebuffer(GLES30.GL_DRAW_FRAMEBUFFER, drawFbo[0]);
        GLES20.glBindFramebuffer(GLES30.GL_READ_FRAMEBUFFER, readFbo[0]);
        GLES20.glViewport(viewport[0], viewport[1], viewport[2], viewport[3]);
        GLES20.glColorMask(colorMask[0], colorMask[1], colorMask[2], colorMask[3]);
        setEnabled(GLES20.GL_BLEND, blend);
        setEnabled(GLES20.GL_DEPTH_TEST, depth);
        setEnabled(GLES20.GL_CULL_FACE, cull);
        setEnabled(GLES20.GL_SCISSOR_TEST, scissor);
        setEnabled(GLES20.GL_STENCIL_TEST, stencil);
    }

    private static void setEnabled(int cap, boolean on) {
        if (on) GLES20.glEnable(cap);
        else GLES20.glDisable(cap);
    }

    private static int buildProgram() {
        int vs = compile(GLES20.GL_VERTEX_SHADER, VERTEX);
        int fs = compile(GLES20.GL_FRAGMENT_SHADER, FRAGMENT);
        int program = GLES20.glCreateProgram();
        GLES20.glAttachShader(program, vs);
        GLES20.glAttachShader(program, fs);
        GLES20.glBindAttribLocation(program, 0, "aPos");
        GLES20.glBindAttribLocation(program, 1, "aUv");
        GLES20.glLinkProgram(program);
        int[] ok = new int[1];
        GLES20.glGetProgramiv(program, GLES20.GL_LINK_STATUS, ok, 0);
        if (ok[0] == 0) Log.e(TAG, "Link failed: " + GLES20.glGetProgramInfoLog(program));
        GLES20.glDeleteShader(vs);
        GLES20.glDeleteShader(fs);
        return program;
    }

    private static int compile(int type, String source) {
        int shader = GLES20.glCreateShader(type);
        GLES20.glShaderSource(shader, source);
        GLES20.glCompileShader(shader);
        int[] ok = new int[1];
        GLES20.glGetShaderiv(shader, GLES20.GL_COMPILE_STATUS, ok, 0);
        if (ok[0] == 0) Log.e(TAG, "Shader compile failed: " + GLES20.glGetShaderInfoLog(shader));
        return shader;
    }

    private void releaseGl() {
        int[] ids = new int[1];
        if (mOutTex != 0) { ids[0] = mOutTex; GLES20.glDeleteTextures(1, ids, 0); mOutTex = 0; }
        if (mOesTex != 0) { ids[0] = mOesTex; GLES20.glDeleteTextures(1, ids, 0); mOesTex = 0; }
        if (mFbo != 0) { ids[0] = mFbo; GLES20.glDeleteFramebuffers(1, ids, 0); mFbo = 0; }
        if (mProgram != 0) { GLES20.glDeleteProgram(mProgram); mProgram = 0; }
        if (mSurface != null) { mSurface.release(); mSurface = null; }
        if (mSurfaceTexture != null) { mSurfaceTexture.release(); mSurfaceTexture = null; }
    }
}
