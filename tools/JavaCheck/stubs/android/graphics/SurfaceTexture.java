package android.graphics;
public class SurfaceTexture {
  public interface OnFrameAvailableListener { void onFrameAvailable(SurfaceTexture surfaceTexture); }
  public SurfaceTexture(int texName) {}
  public void setOnFrameAvailableListener(OnFrameAvailableListener l, android.os.Handler h) {}
  public void updateTexImage() {} public void getTransformMatrix(float[] mtx) {} public void release() {}
}
