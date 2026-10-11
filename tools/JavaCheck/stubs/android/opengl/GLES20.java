package android.opengl;
public class GLES20 {
  public static final int GL_TEXTURE_2D=0x0DE1, GL_TEXTURE_MIN_FILTER=0x2801, GL_TEXTURE_MAG_FILTER=0x2800, GL_LINEAR=0x2601,
   GL_LINEAR_MIPMAP_LINEAR=0x2703, GL_TEXTURE_WRAP_S=0x2802, GL_TEXTURE_WRAP_T=0x2803, GL_CLAMP_TO_EDGE=0x812F,
   GL_EXTENSIONS=0x1F03, GL_TEXTURE_BINDING_2D=0x8069, GL_VIEWPORT=0x0BA2, GL_CURRENT_PROGRAM=0x8B8D, GL_ACTIVE_TEXTURE=0x84E0,
   GL_TEXTURE0=0x84C0, GL_ARRAY_BUFFER_BINDING=0x8894, GL_COLOR_WRITEMASK=0x0C23, GL_BLEND=0x0BE2, GL_DEPTH_TEST=0x0B71,
   GL_CULL_FACE=0x0B44, GL_SCISSOR_TEST=0x0C11, GL_STENCIL_TEST=0x0B90, GL_ARRAY_BUFFER=0x8892, GL_FRAMEBUFFER=0x8D40,
   GL_COLOR_ATTACHMENT0=0x8CE0, GL_FLOAT=0x1406, GL_TRIANGLE_STRIP=5, GL_VERTEX_SHADER=0x8B31, GL_FRAGMENT_SHADER=0x8B30,
   GL_LINK_STATUS=0x8B82, GL_COMPILE_STATUS=0x8B81;
  public static void glGenTextures(int n, int[] t, int o) {} public static void glDeleteTextures(int n, int[] t, int o) {}
  public static void glGetIntegerv(int p, int[] v, int o) {} public static void glGetBooleanv(int p, boolean[] v, int o) {}
  public static void glBindTexture(int t, int id) {} public static void glTexParameteri(int t, int p, int v) {} public static void glTexParameterf(int t, int p, float v) {}
  public static String glGetString(int n) { return ""; } public static int glGetUniformLocation(int p, String n) { return 0; }
  public static void glGenFramebuffers(int n, int[] f, int o) {} public static void glDeleteFramebuffers(int n, int[] f, int o) {}
  public static void glActiveTexture(int t) {} public static boolean glIsEnabled(int c) { return false; } public static void glEnable(int c) {} public static void glDisable(int c) {}
  public static void glBindBuffer(int t, int b) {} public static void glBindFramebuffer(int t, int f) {}
  public static void glFramebufferTexture2D(int t, int a, int tt, int tex, int l) {} public static void glViewport(int x, int y, int w, int h) {}
  public static void glColorMask(boolean r, boolean g, boolean b, boolean a) {} public static void glUseProgram(int p) {}
  public static void glUniformMatrix4fv(int l, int c, boolean t, float[] v, int o) {}
  public static void glVertexAttribPointer(int i, int s, int t, boolean n, int st, java.nio.Buffer p) {}
  public static void glEnableVertexAttribArray(int i) {} public static void glDisableVertexAttribArray(int i) {}
  public static void glDrawArrays(int m, int f, int c) {} public static void glGenerateMipmap(int t) {}
  public static int glCreateShader(int t) { return 0; } public static void glShaderSource(int s, String src) {} public static void glCompileShader(int s) {}
  public static void glGetShaderiv(int s, int p, int[] v, int o) {} public static String glGetShaderInfoLog(int s) { return ""; }
  public static int glCreateProgram() { return 0; } public static void glAttachShader(int p, int s) {} public static void glBindAttribLocation(int p, int i, String n) {}
  public static void glLinkProgram(int p) {} public static void glGetProgramiv(int p, int n, int[] v, int o) {} public static String glGetProgramInfoLog(int p) { return ""; }
  public static void glDeleteShader(int s) {} public static void glDeleteProgram(int p) {}
}
