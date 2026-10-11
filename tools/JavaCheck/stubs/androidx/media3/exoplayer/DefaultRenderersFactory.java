package androidx.media3.exoplayer; public class DefaultRenderersFactory implements RenderersFactory { public static final int EXTENSION_RENDERER_MODE_ON = 1;
  public DefaultRenderersFactory(android.content.Context c) {} public DefaultRenderersFactory setExtensionRendererMode(int m) { return this; } public DefaultRenderersFactory setEnableDecoderFallback(boolean b) { return this; } }
