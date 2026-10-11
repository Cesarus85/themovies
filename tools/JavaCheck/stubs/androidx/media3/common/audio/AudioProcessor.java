package androidx.media3.common.audio;
public interface AudioProcessor {
  final class AudioFormat { public final int sampleRate = 0, channelCount = 0, encoding = 0, bytesPerFrame = 0; }
  final class UnhandledAudioFormatException extends Exception { public UnhandledAudioFormatException(AudioFormat f) {} }
}
