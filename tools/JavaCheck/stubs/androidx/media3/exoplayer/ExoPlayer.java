package androidx.media3.exoplayer;
public interface ExoPlayer extends androidx.media3.common.Player {
  void setSeekParameters(SeekParameters p);
  final class Builder { public Builder(android.content.Context c, RenderersFactory r) {} public Builder setLooper(android.os.Looper l) { return this; }
    public Builder setLoadControl(LoadControl l) { return this; } public Builder setMediaSourceFactory(androidx.media3.exoplayer.source.MediaSource.Factory f) { return this; }
    public Builder setSeekBackIncrementMs(long m) { return this; } public Builder setSeekForwardIncrementMs(long m) { return this; } public ExoPlayer build() { return null; } }
}
