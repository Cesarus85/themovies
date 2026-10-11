package androidx.media3.common;
public interface Player {
  int STATE_IDLE = 1, STATE_BUFFERING = 2, STATE_READY = 3, STATE_ENDED = 4;
  interface Listener {
    default void onPlaybackStateChanged(int s) {} default void onIsPlayingChanged(boolean p) {} default void onPlayerError(PlaybackException e) {}
    default void onVideoSizeChanged(VideoSize s) {} default void onCues(androidx.media3.common.text.CueGroup g) {} default void onTracksChanged(Tracks t) {}
  }
  void addListener(Listener l); void setPlayWhenReady(boolean b); boolean getPlayWhenReady(); void seekTo(long ms); void setPlaybackParameters(PlaybackParameters p);
  void setVolume(float v); void setVideoSurface(android.view.Surface s); void setMediaItem(MediaItem m); void prepare(); void release();
  long getCurrentPosition(); int getPlaybackState(); long getDuration(); long getBufferedPosition();
  TrackSelectionParameters getTrackSelectionParameters(); void setTrackSelectionParameters(TrackSelectionParameters p);
}
