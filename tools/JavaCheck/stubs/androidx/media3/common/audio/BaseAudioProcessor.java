package androidx.media3.common.audio;
public abstract class BaseAudioProcessor implements AudioProcessor {
  protected AudioFormat inputAudioFormat;
  protected AudioFormat onConfigure(AudioFormat f) throws UnhandledAudioFormatException { return f; }
  protected void onFlush() {} public abstract void queueInput(java.nio.ByteBuffer input);
  protected final java.nio.ByteBuffer replaceOutputBuffer(int size) { return null; }
}
