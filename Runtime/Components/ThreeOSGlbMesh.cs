using System;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace ThreeOS
{
    /// <summary>
    /// Minimal GLB (glTF 2.0 binary) mesh extractor for UI glyphs (.3glyph = GLB bytes).
    /// Loads the first mesh primitive with POSITION (+ optional INDICES). No materials.
    /// Uses lightweight regex parsing (Unity JsonUtility cannot reliably read glTF).
    /// </summary>
    public static class ThreeOSGlbMesh
    {
        private const uint Magic = 0x46546C67; // glTF
        private const uint ChunkJson = 0x4E4F534A;
        private const uint ChunkBin = 0x004E4942;

        public static bool TryLoadFirstMesh(byte[] glb, out Mesh mesh, out string error)
        {
            mesh = null;
            error = null;
            if (glb == null || glb.Length < 12)
            {
                error = "glb too small";
                return false;
            }

            if (BitConverter.ToUInt32(glb, 0) != Magic || BitConverter.ToUInt32(glb, 4) != 2)
            {
                error = "not glTF 2 GLB";
                return false;
            }

            string json = null;
            byte[] bin = null;
            var offset = 12;
            while (offset + 8 <= glb.Length)
            {
                var chunkLen = (int)BitConverter.ToUInt32(glb, offset);
                var chunkType = BitConverter.ToUInt32(glb, offset + 4);
                offset += 8;
                if (offset + chunkLen > glb.Length)
                {
                    break;
                }

                if (chunkType == ChunkJson)
                {
                    json = Encoding.UTF8.GetString(glb, offset, chunkLen).TrimEnd(' ', '\0');
                }
                else if (chunkType == ChunkBin)
                {
                    bin = new byte[chunkLen];
                    Buffer.BlockCopy(glb, offset, bin, 0, chunkLen);
                }

                offset += chunkLen;
            }

            if (string.IsNullOrEmpty(json) || bin == null)
            {
                error = "missing JSON or BIN chunk";
                return false;
            }

            try
            {
                // First primitive attributes.POSITION
                var posAcc = MatchInt(json, "\"POSITION\"\\s*:\\s*(\\d+)");
                if (posAcc < 0)
                {
                    error = "no POSITION accessor";
                    return false;
                }

                if (!TryReadVec3Accessor(json, bin, posAcc, out var positions))
                {
                    error = "failed POSITION read";
                    return false;
                }

                int[] indices = null;
                var primBlock = FirstPrimitiveBlock(json);
                if (!string.IsNullOrEmpty(primBlock))
                {
                    var idxAcc = MatchInt(primBlock, "\"indices\"\\s*:\\s*(\\d+)");
                    if (idxAcc >= 0)
                    {
                        indices = ReadIndices(json, bin, idxAcc);
                    }
                }

                mesh = new Mesh { name = "3OS_GlbGlyph" };
                mesh.SetVertices(positions);
                if (indices != null && indices.Length >= 3)
                {
                    // Flip winding after X negate (RH→LH).
                    for (var i = 0; i + 2 < indices.Length; i += 3)
                    {
                        var tmp = indices[i + 1];
                        indices[i + 1] = indices[i + 2];
                        indices[i + 2] = tmp;
                    }

                    mesh.SetTriangles(indices, 0);
                }
                else
                {
                    var tri = new int[positions.Length];
                    for (var i = 0; i < tri.Length; i++)
                    {
                        tri[i] = i;
                    }

                    mesh.SetTriangles(tri, 0);
                }

                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static string FirstPrimitiveBlock(string json)
        {
            var m = Regex.Match(json, "\"primitives\"\\s*:\\s*\\[(?<body>[\\s\\S]*?)\\]", RegexOptions.CultureInvariant);
            return m.Success ? m.Groups["body"].Value : null;
        }

        private static int MatchInt(string text, string pattern)
        {
            var m = Regex.Match(text, pattern, RegexOptions.CultureInvariant);
            if (!m.Success)
            {
                return -1;
            }

            return int.TryParse(m.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v)
                ? v
                : -1;
        }

        private static bool TryReadVec3Accessor(string json, byte[] bin, int accessorIndex,
            out Vector3[] positions)
        {
            positions = null;
            if (!TryParseAccessor(json, accessorIndex, out var acc))
            {
                return false;
            }

            if (acc.ComponentType != 5126 || acc.Type != "VEC3")
            {
                return false;
            }

            if (!TryParseBufferView(json, acc.BufferView, out var view))
            {
                return false;
            }

            var stride = view.ByteStride > 0 ? view.ByteStride : 12;
            var start = view.ByteOffset + Math.Max(0, acc.ByteOffset);
            positions = new Vector3[acc.Count];
            for (var i = 0; i < acc.Count; i++)
            {
                var o = start + i * stride;
                var x = BitConverter.ToSingle(bin, o);
                var y = BitConverter.ToSingle(bin, o + 4);
                var z = BitConverter.ToSingle(bin, o + 8);
                positions[i] = new Vector3(-x, y, z);
            }

            return true;
        }

        private static int[] ReadIndices(string json, byte[] bin, int accessorIndex)
        {
            if (!TryParseAccessor(json, accessorIndex, out var acc) ||
                !TryParseBufferView(json, acc.BufferView, out var view))
            {
                return null;
            }

            var start = view.ByteOffset + Math.Max(0, acc.ByteOffset);
            var indices = new int[acc.Count];
            for (var i = 0; i < acc.Count; i++)
            {
                if (acc.ComponentType == 5123)
                {
                    indices[i] = BitConverter.ToUInt16(bin, start + i * 2);
                }
                else if (acc.ComponentType == 5125)
                {
                    indices[i] = (int)BitConverter.ToUInt32(bin, start + i * 4);
                }
                else if (acc.ComponentType == 5121)
                {
                    indices[i] = bin[start + i];
                }
                else
                {
                    return null;
                }
            }

            return indices;
        }

        private struct Acc
        {
            public int BufferView;
            public int ByteOffset;
            public int ComponentType;
            public int Count;
            public string Type;
        }

        private struct View
        {
            public int ByteOffset;
            public int ByteStride;
        }

        private static bool TryParseAccessor(string json, int index, out Acc acc)
        {
            acc = default;
            var block = NthObjectInArray(json, "accessors", index);
            if (block == null)
            {
                return false;
            }

            acc.BufferView = MatchInt(block, "\"bufferView\"\\s*:\\s*(\\d+)");
            acc.ByteOffset = Math.Max(0, MatchInt(block, "\"byteOffset\"\\s*:\\s*(\\d+)"));
            acc.ComponentType = MatchInt(block, "\"componentType\"\\s*:\\s*(\\d+)");
            acc.Count = MatchInt(block, "\"count\"\\s*:\\s*(\\d+)");
            var tm = Regex.Match(block, "\"type\"\\s*:\\s*\"([^\"]+)\"", RegexOptions.CultureInvariant);
            acc.Type = tm.Success ? tm.Groups[1].Value : "";
            return acc.BufferView >= 0 && acc.Count > 0;
        }

        private static bool TryParseBufferView(string json, int index, out View view)
        {
            view = default;
            var block = NthObjectInArray(json, "bufferViews", index);
            if (block == null)
            {
                return false;
            }

            view.ByteOffset = Math.Max(0, MatchInt(block, "\"byteOffset\"\\s*:\\s*(\\d+)"));
            var stride = MatchInt(block, "\"byteStride\"\\s*:\\s*(\\d+)");
            view.ByteStride = stride > 0 ? stride : 0;
            return true;
        }

        private static string NthObjectInArray(string json, string arrayName, int index)
        {
            var m = Regex.Match(json, "\"" + arrayName + "\"\\s*:\\s*\\[", RegexOptions.CultureInvariant);
            if (!m.Success)
            {
                return null;
            }

            var i = m.Index + m.Length;
            var depth = 0;
            var obj = -1;
            var start = -1;
            for (; i < json.Length; i++)
            {
                var c = json[i];
                if (c == '{')
                {
                    if (depth == 0)
                    {
                        obj++;
                        start = i;
                    }

                    depth++;
                }
                else if (c == '}')
                {
                    depth--;
                    if (depth == 0 && obj == index && start >= 0)
                    {
                        return json.Substring(start, i - start + 1);
                    }
                }
                else if (c == ']' && depth == 0)
                {
                    break;
                }
            }

            return null;
        }
    }
}
