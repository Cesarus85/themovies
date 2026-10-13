// API stubs for KTX for Unity (com.unity.cloud.ktx 3.x), only what the controller textures use.
using System.Threading.Tasks;
using Unity.Collections;

namespace KtxUnity
{
    [System.Flags]
    public enum TextureOrientation { KtxDefault = 0, XLeft = 1, YUp = 2, ZIn = 4, UnityDefault = YUp }

    public static class TextureOrientationExtension
    {
        public static bool IsXFlipped(this TextureOrientation orientation) => false;
        public static bool IsYFlipped(this TextureOrientation orientation) => false;
    }

    public enum ErrorCode { Success, UnsupportedVersion, UnsupportedFormat, OpenUriFailed, LoadingFailed, TranscodeFailed }

    public class TextureResult
    {
        public UnityEngine.Texture2D texture;
        public TextureOrientation orientation;
        public ErrorCode errorCode;
    }

    public class KtxTexture
    {
        public Task<TextureResult> LoadFromBytes(NativeArray<byte>.ReadOnly data, bool linear = false, uint layer = 0,
            uint faceSlice = 0, uint mipLevel = 0, bool mipChain = true) => Task.FromResult(new TextureResult());
    }
}
