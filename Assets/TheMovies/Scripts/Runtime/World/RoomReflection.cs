using UnityEngine;

namespace TheMovies.World
{
    /// <summary>
    /// Reflections of a photorealistic room (pipeline v2): the panorama rendered in Blender from the marker
    /// "ReflectionProbe" (imported as a convolved cubemap), projected onto the box given by the marker's scale (half
    /// size in meters). Added to the room prefab by RoomModelSetup; CinemaEnvironment sets the shader globals.
    /// </summary>
    public sealed class RoomReflection : MonoBehaviour
    {
        public Cubemap Map;

        /// <summary>
        /// Turns the model's axes into the cubemap's. Blender's FBX export maps (x, y, z) to Unity's (−x, z, −y); the
        /// panorama camera turned (90°, 0, 0) puts Blender +Y in the image center, which Unity's latitude-longitude
        /// import makes +Z – together a half turn about the vertical (checked on both sides, October 2026).
        /// </summary>
        public Vector3 CubeRotation = new Vector3(0f, 180f, 0f);

        /// <summary>Overall strength of the reflections (1 = physically as rendered).</summary>
        public float Intensity = 1f;

        static readonly int k_Cube = Shader.PropertyToID("_TM_ReflCube");
        static readonly int k_WorldToBox = Shader.PropertyToID("_TM_ReflWorldToBox");
        static readonly int k_RoomToCube = Shader.PropertyToID("_TM_ReflRoomToCube");
        static readonly int k_HalfExtents = Shader.PropertyToID("_TM_ReflHalfExtents");

        /// <summary>Sets the reflection globals for this room; false if its probe marker is missing or unusable.</summary>
        public bool Apply()
        {
            var probe = Find(transform, "ReflectionProbe");
            if (Map == null || probe == null) return false;
            var scale = probe.lossyScale;
            var half = new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
            if (half.x < 0.01f || half.y < 0.01f || half.z < 0.01f)
            {
                Debug.LogWarning($"[TheMovies] '{name}': ReflectionProbe needs its scale as the half size of the room box.");
                return false;
            }
            // Box space: the probe's local space scaled to [-1, 1]; a hit point × half size is the direction from the
            // probe in its own (unscaled) axes, which are the room's axes in Blender.
            var probeToWorld = Matrix4x4.TRS(probe.position, probe.rotation, half);
            var toModel = Quaternion.Inverse(transform.rotation) * probe.rotation;
            Shader.SetGlobalTexture(k_Cube, Map);
            Shader.SetGlobalMatrix(k_WorldToBox, probeToWorld.inverse);
            Shader.SetGlobalMatrix(k_RoomToCube, Matrix4x4.Rotate(Quaternion.Euler(CubeRotation) * toModel));
            Shader.SetGlobalVector(k_HalfExtents, new Vector4(half.x, half.y, half.z, Mathf.Max(0f, Intensity)));
            return true;
        }

        /// <summary>No reflections (rooms of the old pipeline).</summary>
        public static void Clear() => Shader.SetGlobalVector(k_HalfExtents, Vector4.zero);

        static Transform Find(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (string.Equals(t.name, name, System.StringComparison.OrdinalIgnoreCase)) return t;
            return null;
        }
    }
}
