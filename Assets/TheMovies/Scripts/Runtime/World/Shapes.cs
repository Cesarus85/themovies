using TheMovies.UI;
using UnityEngine;

namespace TheMovies.World
{
    /// <summary>Simple colored primitives without colliders, sharing one material.</summary>
    public static class Shapes
    {
        static Material s_Lit;
        static Material s_Flat;
        static Material s_Room;
        static Material s_Lamp;
        static readonly int k_Color = Shader.PropertyToID("_Color");

        /// <summary>Slightly shaded, for 3D objects like avatars and seats.</summary>
        public static Material Lit => s_Lit != null ? s_Lit : s_Lit = Create(shading: 1f, glow: 0f, roomLight: 0f);

        /// <summary>Completely flat.</summary>
        public static Material Flat => s_Flat != null ? s_Flat : s_Flat = Create(shading: 0f, glow: 0f, roomLight: 0f);

        /// <summary>Room surfaces: dim with the house lights and pick up the screen glow.</summary>
        public static Material Room => s_Room != null ? s_Room : s_Room = Create(shading: 0.6f, glow: 1.1f, roomLight: 1f);

        /// <summary>Wall lamps: unshaded, dim with the house lights.</summary>
        public static Material Lamp => s_Lamp != null ? s_Lamp : s_Lamp = Create(shading: 0f, glow: 0f, roomLight: 1f);

        static Material Create(float shading, float glow, float roomLight)
        {
            var m = new Material(UIFactory.FindShader("TheMovies/Unlit"));
            m.SetFloat("_Shading", shading);
            m.SetFloat("_ScreenGlow", glow);
            m.SetFloat("_RoomLightAffect", roomLight);
            return m;
        }

        public static MeshRenderer Create(PrimitiveType type, Transform parent, string name, Vector3 localPosition,
            Vector3 localScale, Color color, Material material = null)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            var collider = go.GetComponent<Collider>();
            if (collider != null) Object.Destroy(collider);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localScale = localScale;
            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = material != null ? material : Lit;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            SetColor(mr, color);
            return mr;
        }

        public static void SetColor(Renderer r, Color color)
        {
            var block = new MaterialPropertyBlock();
            r.GetPropertyBlock(block);
            block.SetColor(k_Color, color);
            r.SetPropertyBlock(block);
        }
    }
}
