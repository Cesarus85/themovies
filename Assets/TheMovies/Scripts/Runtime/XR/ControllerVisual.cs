using TheMovies.World;
using UnityEngine;

namespace TheMovies.XR
{
    /// <summary>Minimal stand-in model for a Touch controller (placed at the grip pose).</summary>
    public static class ControllerVisual
    {
        public static void Build(Transform grip)
        {
            var dark = new Color(0.12f, 0.12f, 0.14f);
            Shapes.Create(PrimitiveType.Cube, grip, "Body", new Vector3(0, 0, -0.01f),
                new Vector3(0.032f, 0.04f, 0.1f), dark);
            Shapes.Create(PrimitiveType.Cylinder, grip, "Ring", new Vector3(0, 0.03f, 0.045f),
                new Vector3(0.075f, 0.008f, 0.075f), new Color(0.2f, 0.2f, 0.24f));
            Shapes.Create(PrimitiveType.Sphere, grip, "Tip", new Vector3(0, 0.005f, 0.05f),
                Vector3.one * 0.02f, new Color(0.95f, 0.62f, 0.18f));
        }
    }
}
