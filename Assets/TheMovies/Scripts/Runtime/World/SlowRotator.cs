using UnityEngine;

namespace TheMovies.World
{
    /// <summary>
    /// Slow motion in room models: an object whose name contains "_rotate&lt;seconds per turn&gt;" (e.g. "Earth_rotate5520",
    /// one turn in 92 minutes) turns around its local up axis (Blender's local Z). Added by CinemaEnvironment when a
    /// room model is loaded; children turn with it.
    /// </summary>
    public sealed class SlowRotator : MonoBehaviour
    {
        const string k_Tag = "_rotate";
        const float k_DefaultSeconds = 600f;

        public float SecondsPerTurn = k_DefaultSeconds;

        void Update()
        {
            if (SecondsPerTurn > 0f)
                transform.Rotate(Vector3.up, 360f / SecondsPerTurn * Time.deltaTime, Space.Self);
        }

        /// <summary>Adds a rotator to every object below <paramref name="root"/> tagged by its name; returns how many.</summary>
        public static int AttachAll(Transform root)
        {
            int count = 0;
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                int at = t.name.IndexOf(k_Tag, System.StringComparison.OrdinalIgnoreCase);
                if (at < 0) continue;
                int start = at + k_Tag.Length, end = start;
                while (end < t.name.Length && char.IsDigit(t.name[end])) end++;
                float seconds = end > start && float.TryParse(t.name.Substring(start, end - start), out float s) && s > 0f ? s : k_DefaultSeconds;
                t.gameObject.AddComponent<SlowRotator>().SecondsPerTurn = seconds;
                count++;
            }
            return count;
        }
    }
}
