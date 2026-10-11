// API stubs for Unity Inference Engine (com.unity.ai.inference 2.2+), only what the 2D→3D depth model uses.
// Signatures follow the package documentation; ReadbackAndCloneAsync returns Awaitable<Tensor<T>> there.
using System.Collections;
using System.Threading.Tasks;

namespace Unity.InferenceEngine
{
    public class ModelAsset : UnityEngine.ScriptableObject { }

    public class Model { }

    public static class ModelLoader
    {
        public static Model Load(ModelAsset modelAsset) => null;
    }

    public enum BackendType { CPU, GPUCompute, GPUPixel }

    public struct TensorShape
    {
        public TensorShape(params int[] shape) { length = 0; }
        public int length { get; }
    }

    public abstract class Tensor : System.IDisposable
    {
        public TensorShape shape => default;
        public void Dispose() { }
    }

    public class Tensor<T> : Tensor where T : unmanaged
    {
        public Tensor(TensorShape shape) { }
        public Task<Tensor<T>> ReadbackAndCloneAsync() => null;
        public T[] DownloadToArray() => null;
    }

    public class Worker : System.IDisposable
    {
        public Worker(Model model, BackendType backendType) { }
        public void Schedule(params Tensor[] inputs) { }
        public IEnumerator ScheduleIterable(params Tensor[] inputs) => null;
        public Tensor PeekOutput() => null;
        public void Dispose() { }
    }

    public struct TextureTransform { }

    public static class TextureConverter
    {
        public static void ToTensor(UnityEngine.Texture texture, Tensor<float> tensor, TextureTransform transform) { }
    }
}
