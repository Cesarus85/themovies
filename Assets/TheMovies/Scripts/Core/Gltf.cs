using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace TheMovies.Core
{
    /// <summary>One drawable part of a glTF model, already in Unity's coordinate system (left-handed, X mirrored).</summary>
    public sealed class GltfMesh
    {
        public string Name = string.Empty;

        /// <summary>x, y, z per vertex, in model space (node transforms applied).</summary>
        public float[] Positions;

        /// <summary>x, y, z per vertex (may be null).</summary>
        public float[] Normals;

        /// <summary>u, v per vertex with v = 0 at the bottom like Unity (may be null).</summary>
        public float[] Uvs;

        /// <summary>Triangles, wound for Unity.</summary>
        public int[] Indices;

        /// <summary>Base color factor (r, g, b, a).</summary>
        public float[] Color = { 1, 1, 1, 1 };

        /// <summary>Index into <see cref="GltfModel.Images"/> of the base color texture, -1 if none.</summary>
        public int Image = -1;
    }

    public sealed class GltfImage
    {
        public string MimeType = string.Empty;
        public byte[] Bytes;
    }

    public sealed class GltfModel
    {
        public readonly List<GltfMesh> Meshes = new List<GltfMesh>();
        public readonly List<GltfImage> Images = new List<GltfImage>();
    }

    /// <summary>
    /// Minimal reader for binary glTF 2.0 (GLB) as delivered by the headset for its controller models: triangle
    /// meshes with positions, normals, texture coordinates and indices, the node hierarchy (matrix or TRS), base
    /// color factor and texture (embedded PNG/JPEG/KTX2). Converted to Unity's left-handed system by mirroring X.
    /// </summary>
    public static class Gltf
    {
        const uint Magic = 0x46546C67;     // "glTF"
        const uint ChunkJson = 0x4E4F534A; // "JSON"
        const uint ChunkBin = 0x004E4942;  // "BIN\0"

        public static GltfModel ReadGlb(byte[] glb)
        {
            if (glb == null || glb.Length < 20 || BitConverter.ToUInt32(glb, 0) != Magic)
                throw new FormatException("Keine GLB-Datei.");
            if (BitConverter.ToUInt32(glb, 4) != 2) throw new FormatException("Nur glTF 2.0 wird unterstützt.");
            int length = (int)Math.Min((uint)glb.Length, BitConverter.ToUInt32(glb, 8));
            string json = null;
            byte[] bin = null;
            int pos = 12;
            while (pos + 8 <= length)
            {
                int chunkLength = (int)BitConverter.ToUInt32(glb, pos);
                uint chunkType = BitConverter.ToUInt32(glb, pos + 4);
                if (chunkLength < 0 || pos + 8 + chunkLength > length) throw new FormatException("GLB-Abschnitt beschädigt.");
                if (chunkType == ChunkJson) json = Encoding.UTF8.GetString(glb, pos + 8, chunkLength);
                else if (chunkType == ChunkBin && bin == null)
                {
                    bin = new byte[chunkLength];
                    Buffer.BlockCopy(glb, pos + 8, bin, 0, chunkLength);
                }
                pos += 8 + chunkLength;
            }
            if (json == null) throw new FormatException("GLB ohne JSON.");
            return Read(Json.Parse(json), bin ?? new byte[0]);
        }

        static GltfModel Read(object doc, byte[] bin)
        {
            var model = new GltfModel();
            var bufferViews = doc.Array("bufferViews");
            var accessors = doc.Array("accessors");

            foreach (var image in doc.Array("images"))
            {
                var img = new GltfImage { MimeType = image.Str("mimeType") };
                if (image.Get("bufferView") != null)
                {
                    var view = bufferViews[(int)image.Long("bufferView")];
                    img.Bytes = Slice(bin, (int)view.Long("byteOffset"), (int)view.Long("byteLength"));
                }
                model.Images.Add(img);
            }

            var textures = doc.Array("textures");
            var materials = doc.Array("materials");
            var meshes = doc.Array("meshes");
            var nodes = doc.Array("nodes");

            int sceneIndex = (int)doc.Long("scene", 0);
            var scenes = doc.Array("scenes");
            var roots = new List<int>();
            if (scenes.Count > sceneIndex)
                foreach (var n in scenes[sceneIndex].Array("nodes")) roots.Add((int)(double)n);
            else
                for (int i = 0; i < nodes.Count; i++) roots.Add(i);

            var stack = new Stack<(int node, double[] parent, int depth)>();
            foreach (int root in roots) stack.Push((root, Identity(), 0));
            while (stack.Count > 0)
            {
                var (index, parent, depth) = stack.Pop();
                if (index < 0 || index >= nodes.Count || depth > 64) continue;
                var node = nodes[index];
                double[] world = Multiply(parent, LocalMatrix(node));
                if (node.Get("mesh") is double meshIndex && (int)meshIndex < meshes.Count)
                {
                    var mesh = meshes[(int)meshIndex];
                    foreach (var primitive in mesh.Array("primitives"))
                    {
                        if (primitive.Long("mode", 4) != 4) continue; // triangles only
                        var part = ReadPrimitive(primitive, accessors, bufferViews, bin, world);
                        if (part == null) continue;
                        part.Name = node.Str("name", mesh.Str("name"));
                        if (primitive.Get("material") is double materialIndex && (int)materialIndex < materials.Count)
                            ApplyMaterial(part, materials[(int)materialIndex], textures);
                        model.Meshes.Add(part);
                    }
                }
                foreach (var child in node.Array("children"))
                    stack.Push(((int)(double)child, world, depth + 1));
            }
            return model;
        }

        static GltfMesh ReadPrimitive(object primitive, List<object> accessors, List<object> views, byte[] bin, double[] world)
        {
            var attributes = primitive.Get("attributes");
            if (!(attributes.Get("POSITION") is double positionAccessor)) return null;
            float[] positions = ReadFloats(accessors[(int)positionAccessor], views, bin, 3);
            int vertexCount = positions.Length / 3;
            float[] normals = attributes.Get("NORMAL") is double n ? ReadFloats(accessors[(int)n], views, bin, 3) : null;
            float[] uvs = attributes.Get("TEXCOORD_0") is double t ? ReadFloats(accessors[(int)t], views, bin, 2) : null;
            int[] indices;
            if (primitive.Get("indices") is double i) indices = ReadIndices(accessors[(int)i], views, bin);
            else
            {
                indices = new int[vertexCount];
                for (int k = 0; k < vertexCount; k++) indices[k] = k;
            }

            // Node transform, then right-handed (glTF) to left-handed (Unity): mirror X.
            for (int v = 0; v < vertexCount; v++)
            {
                Transform(world, positions, v * 3, true);
                positions[v * 3] = -positions[v * 3];
                if (normals != null && normals.Length >= (v + 1) * 3)
                {
                    Transform(world, normals, v * 3, false);
                    normals[v * 3] = -normals[v * 3];
                    Normalize(normals, v * 3);
                }
                if (uvs != null && uvs.Length >= (v + 1) * 2) uvs[v * 2 + 1] = 1f - uvs[v * 2 + 1];
            }
            // Mirroring flips the winding; also a mirroring node transform would flip it once more.
            bool flip = Determinant3(world) >= 0;
            if (flip)
                for (int k = 0; k + 2 < indices.Length; k += 3)
                    (indices[k + 1], indices[k + 2]) = (indices[k + 2], indices[k + 1]);
            foreach (int index in indices)
                if (index < 0 || index >= vertexCount) throw new FormatException("Index außerhalb des Modells.");
            return new GltfMesh { Positions = positions, Normals = normals, Uvs = uvs, Indices = indices };
        }

        static void ApplyMaterial(GltfMesh part, object material, List<object> textures)
        {
            var pbr = material.Get("pbrMetallicRoughness");
            var factor = pbr.Array("baseColorFactor");
            if (factor.Count == 4)
                for (int c = 0; c < 4; c++) part.Color[c] = (float)(double)factor[c];
            if (pbr.Get("baseColorTexture").Get("index") is double textureIndex && (int)textureIndex < textures.Count)
            {
                var texture = textures[(int)textureIndex];
                object source = texture.Get("source") ?? texture.Get("extensions").Get("KHR_texture_basisu").Get("source");
                if (source is double s) part.Image = (int)s;
            }
        }

        // ---------------- Accessors ----------------

        static float[] ReadFloats(object accessor, List<object> views, byte[] bin, int expectedComponents)
        {
            int count = (int)accessor.Long("count");
            int components = Components(accessor.Str("type"));
            if (components < expectedComponents) throw new FormatException("Unerwarteter Accessor-Typ " + accessor.Str("type"));
            int componentType = (int)accessor.Long("componentType");
            bool normalized = accessor.Bool("normalized");
            var result = new float[count * expectedComponents];
            if (!(accessor.Get("bufferView") is double viewIndex)) return result; // all zero (sparse not supported)
            var view = views[(int)viewIndex];
            int size = ComponentSize(componentType);
            int stride = (int)view.Long("byteStride", components * size);
            if (stride == 0) stride = components * size;
            int start = (int)view.Long("byteOffset") + (int)accessor.Long("byteOffset");
            for (int e = 0; e < count; e++)
            {
                int at = start + e * stride;
                for (int c = 0; c < expectedComponents; c++)
                    result[e * expectedComponents + c] = ReadComponent(bin, at + c * size, componentType, normalized);
            }
            return result;
        }

        static int[] ReadIndices(object accessor, List<object> views, byte[] bin)
        {
            int count = (int)accessor.Long("count");
            int componentType = (int)accessor.Long("componentType");
            var view = views[(int)accessor.Long("bufferView")];
            int size = ComponentSize(componentType);
            int stride = (int)view.Long("byteStride", size);
            if (stride == 0) stride = size;
            int start = (int)view.Long("byteOffset") + (int)accessor.Long("byteOffset");
            var result = new int[count];
            for (int e = 0; e < count; e++)
            {
                int at = start + e * stride;
                Check(bin, at, size);
                switch (componentType)
                {
                    case 5121: result[e] = bin[at]; break;
                    case 5123: result[e] = BitConverter.ToUInt16(bin, at); break;
                    case 5125: result[e] = (int)BitConverter.ToUInt32(bin, at); break;
                    default: throw new FormatException("Ungültiger Index-Typ " + componentType);
                }
            }
            return result;
        }

        static float ReadComponent(byte[] bin, int at, int componentType, bool normalized)
        {
            Check(bin, at, ComponentSize(componentType));
            switch (componentType)
            {
                case 5126: return BitConverter.ToSingle(bin, at);
                case 5121: return normalized ? bin[at] / 255f : bin[at];
                case 5120: return normalized ? Math.Max((sbyte)bin[at] / 127f, -1f) : (sbyte)bin[at];
                case 5123: return normalized ? BitConverter.ToUInt16(bin, at) / 65535f : BitConverter.ToUInt16(bin, at);
                case 5122: return normalized ? Math.Max(BitConverter.ToInt16(bin, at) / 32767f, -1f) : BitConverter.ToInt16(bin, at);
                case 5125: return BitConverter.ToUInt32(bin, at);
                default: throw new FormatException("Ungültiger Komponenten-Typ " + componentType);
            }
        }

        static void Check(byte[] bin, int at, int size)
        {
            if (at < 0 || at + size > bin.Length) throw new FormatException("Accessor zeigt aus dem Puffer.");
        }

        static int Components(string type)
        {
            switch (type)
            {
                case "SCALAR": return 1;
                case "VEC2": return 2;
                case "VEC3": return 3;
                case "VEC4": return 4;
                case "MAT4": return 16;
                default: return 0;
            }
        }

        static int ComponentSize(int componentType) => componentType == 5126 || componentType == 5125 ? 4 : componentType == 5123 || componentType == 5122 ? 2 : 1;

        static byte[] Slice(byte[] bin, int offset, int length)
        {
            if (offset < 0 || length < 0 || offset + length > bin.Length) throw new FormatException("Bild zeigt aus dem Puffer.");
            var result = new byte[length];
            Buffer.BlockCopy(bin, offset, result, 0, length);
            return result;
        }

        // ---------------- Matrices (column-major like glTF) ----------------

        static double[] Identity() => new double[] { 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1 };

        static double[] LocalMatrix(object node)
        {
            var matrix = node.Array("matrix");
            if (matrix.Count == 16)
            {
                var m = new double[16];
                for (int i = 0; i < 16; i++) m[i] = (double)matrix[i];
                return m;
            }
            var t = node.Array("translation");
            var r = node.Array("rotation");
            var s = node.Array("scale");
            double tx = t.Count == 3 ? (double)t[0] : 0, ty = t.Count == 3 ? (double)t[1] : 0, tz = t.Count == 3 ? (double)t[2] : 0;
            double qx = r.Count == 4 ? (double)r[0] : 0, qy = r.Count == 4 ? (double)r[1] : 0, qz = r.Count == 4 ? (double)r[2] : 0, qw = r.Count == 4 ? (double)r[3] : 1;
            double sx = s.Count == 3 ? (double)s[0] : 1, sy = s.Count == 3 ? (double)s[1] : 1, sz = s.Count == 3 ? (double)s[2] : 1;
            // Rotation matrix from the quaternion, times scale; column-major.
            return new[]
            {
                (1 - 2 * (qy * qy + qz * qz)) * sx, (2 * (qx * qy + qz * qw)) * sx, (2 * (qx * qz - qy * qw)) * sx, 0,
                (2 * (qx * qy - qz * qw)) * sy, (1 - 2 * (qx * qx + qz * qz)) * sy, (2 * (qy * qz + qx * qw)) * sy, 0,
                (2 * (qx * qz + qy * qw)) * sz, (2 * (qy * qz - qx * qw)) * sz, (1 - 2 * (qx * qx + qy * qy)) * sz, 0,
                tx, ty, tz, 1,
            };
        }

        static double[] Multiply(double[] a, double[] b)
        {
            var m = new double[16];
            for (int col = 0; col < 4; col++)
            for (int row = 0; row < 4; row++)
            {
                double sum = 0;
                for (int k = 0; k < 4; k++) sum += a[k * 4 + row] * b[col * 4 + k];
                m[col * 4 + row] = sum;
            }
            return m;
        }

        static void Transform(double[] m, float[] v, int at, bool point)
        {
            double x = v[at], y = v[at + 1], z = v[at + 2];
            double w = point ? 1 : 0;
            v[at] = (float)(m[0] * x + m[4] * y + m[8] * z + m[12] * w);
            v[at + 1] = (float)(m[1] * x + m[5] * y + m[9] * z + m[13] * w);
            v[at + 2] = (float)(m[2] * x + m[6] * y + m[10] * z + m[14] * w);
        }

        static void Normalize(float[] v, int at)
        {
            double l = Math.Sqrt(v[at] * v[at] + v[at + 1] * v[at + 1] + v[at + 2] * v[at + 2]);
            if (l < 1e-9) return;
            v[at] = (float)(v[at] / l);
            v[at + 1] = (float)(v[at + 1] / l);
            v[at + 2] = (float)(v[at + 2] / l);
        }

        static double Determinant3(double[] m) =>
            m[0] * (m[5] * m[10] - m[9] * m[6]) - m[4] * (m[1] * m[10] - m[9] * m[2]) + m[8] * (m[1] * m[6] - m[5] * m[2]);
    }

    /// <summary>A KTX2 texture (as used in glTF models): format and the bytes of the largest mip level.</summary>
    public sealed class Ktx2Image
    {
        /// <summary>Vulkan format number (e.g. 37 = RGBA8, 152 = ETC2 RGBA sRGB, 158 = ASTC 4×4 sRGB).</summary>
        public int VkFormat;
        public int Width, Height;
        public byte[] Level0;

        /// <summary>Basis/Zstandard compressed – not readable here.</summary>
        public bool Supercompressed;

        public static Ktx2Image Read(byte[] data)
        {
            byte[] identifier = { 0xAB, 0x4B, 0x54, 0x58, 0x20, 0x32, 0x30, 0xBB, 0x0D, 0x0A, 0x1A, 0x0A };
            if (data == null || data.Length < 80) throw new FormatException("Keine KTX2-Datei.");
            for (int i = 0; i < identifier.Length; i++)
                if (data[i] != identifier[i]) throw new FormatException("Keine KTX2-Datei.");
            var image = new Ktx2Image
            {
                VkFormat = (int)BitConverter.ToUInt32(data, 12),
                Width = (int)BitConverter.ToUInt32(data, 20),
                Height = (int)BitConverter.ToUInt32(data, 24),
                Supercompressed = BitConverter.ToUInt32(data, 44) != 0,
            };
            long offset = (long)BitConverter.ToUInt64(data, 80);
            long length = (long)BitConverter.ToUInt64(data, 88);
            if (offset < 0 || length <= 0 || offset + length > data.Length) throw new FormatException("KTX2-Ebene zeigt aus der Datei.");
            image.Level0 = new byte[length];
            Buffer.BlockCopy(data, (int)offset, image.Level0, 0, (int)length);
            return image;
        }
    }
}
