package androidx.media3.datasource;
public final class DefaultHttpDataSource { public static final class Factory implements DataSource.Factory {
  public Factory setUserAgent(String u) { return this; } public Factory setConnectTimeoutMs(int m) { return this; } public Factory setReadTimeoutMs(int m) { return this; } public Factory setAllowCrossProtocolRedirects(boolean b) { return this; } } }
