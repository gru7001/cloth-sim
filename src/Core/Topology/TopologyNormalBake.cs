using System;
using System.Collections.Generic;
using System.IO;
using Godot;

namespace DelaunyFabric.Core;

/// <summary>
/// Bakes a tangent-space normal map for <paramref name="target"/> by sampling
/// geometric normals from the higher-resolution <paramref name="source"/> at matching UVs.
/// The tangent frame is built from the target surface; detail comes from the source.
/// Per-vertex tangent normals are bilinearly interpolated over faces, then rasterized
/// via a shared corner grid (same scheme as <see cref="TopologyCurvatureBake"/>).
/// </summary>
public static class TopologyNormalBake
{
	public static IReadOnlyDictionary<Vertex, Vector3> ComputeVertexTangentNormals(
		Topology target,
		Topology source)
	{
		var sourceFaces = CollectFaces(source);
		var tangentNormals = new Dictionary<Vertex, Vector3>(target.Vertices.Count);

		foreach (var vertex in target.Vertices)
		{
			if (TryComputeVertexTangentNormal(vertex, sourceFaces, out var tangentNormal))
				tangentNormals[vertex] = tangentNormal;
		}

		return tangentNormals;
	}

	public static Image BakeNormalMap(Topology target, Topology source, int width, int height)
	{
		if (width <= 0 || height <= 0)
			throw new ArgumentOutOfRangeException(nameof(width), "Normal map size must be positive.");

		var vertexTangentNormals = ComputeVertexTangentNormals(target, source);
		var targetFaces = CollectFaces(target);

		var image = Image.CreateEmpty(width, height, false, Image.Format.Rgba8);
		image.Fill(new Color(0.5f, 0.5f, 1f, 1f));

		int cornerWidth = width + 1;
		var corners = new Vector3?[cornerWidth * (height + 1)];

		for (int y = 0; y <= height; y++)
		{
			for (int x = 0; x <= width; x++)
			{
				if (TrySampleTangentNormal(targetFaces, vertexTangentNormals, ImageUv.ToUv(x, y, width, height), out var tangentNormal))
					corners[x + y * cornerWidth] = tangentNormal;
			}
		}

		for (int y = 0; y < height; y++)
		{
			for (int x = 0; x < width; x++)
			{
				if (!TryAverageCorners(corners, cornerWidth, x, y, out var tangentNormal))
					continue;

				image.SetPixel(x, y, EncodeNormal(tangentNormal));
			}
		}

		return image;
	}

	public static void BakeAndSave(
		Topology target,
		Topology source,
		int width,
		int height,
		string path) =>
		Save(BakeNormalMap(target, source, width, height), path);

	public static void Save(Image image, string path)
	{
		var directory = Path.GetDirectoryName(path);
		if (!string.IsNullOrEmpty(directory))
			Directory.CreateDirectory(directory);

		var error = image.SavePng(path);
		if (error != Error.Ok)
			throw new InvalidOperationException($"Failed to save normal map to '{path}': {error}");
	}

	static bool TryComputeVertexTangentNormal(
		Vertex vertex,
		IReadOnlyList<IReadOnlyList<Corner>> sourceFaces,
		out Vector3 tangentNormal)
	{
		var sum = Vector3.Zero;
		int count = 0;

		foreach (var corner in vertex.Corners)
		{
			if (!TrySampleFaceNormal(sourceFaces, corner.Uv, out var detailNormal))
				continue;

			var face = TopologyWalk.WalkFaceCcw(corner);
			CornerParam(face, corner, out float s, out float t);
			ComputeTbn(face, s, t, out var tangent, out var bitangent, out var normal);
			sum += WorldToTangent(detailNormal, tangent, bitangent, normal);
			count++;
		}

		if (count == 0)
		{
			tangentNormal = default;
			return false;
		}

		tangentNormal = SafeNormalize(sum / count, new Vector3(0f, 0f, 1f));
		return true;
	}

	static bool TrySampleTangentNormal(
		IEnumerable<IReadOnlyList<Corner>> faces,
		IReadOnlyDictionary<Vertex, Vector3> vertexTangentNormals,
		Vector2 uv,
		out Vector3 tangentNormal)
	{
		foreach (var face in faces)
		{
			if (TrySampleFaceTangentNormal(face, vertexTangentNormals, uv, out tangentNormal))
				return true;
		}

		tangentNormal = default;
		return false;
	}

	static bool TrySampleFaceTangentNormal(
		IReadOnlyList<Corner> face,
		IReadOnlyDictionary<Vertex, Vector3> vertexTangentNormals,
		Vector2 uv,
		out Vector3 tangentNormal)
	{
		if (!Bilinear.TryInverse(
			face[0].Uv,
			face[1].Uv,
			face[2].Uv,
			face[3].Uv,
			uv,
			out float s,
			out float t))
		{
			tangentNormal = default;
			return false;
		}

		if (!vertexTangentNormals.TryGetValue(face[0].Vertex, out var n0)
			|| !vertexTangentNormals.TryGetValue(face[1].Vertex, out var n1)
			|| !vertexTangentNormals.TryGetValue(face[2].Vertex, out var n2)
			|| !vertexTangentNormals.TryGetValue(face[3].Vertex, out var n3))
		{
			tangentNormal = default;
			return false;
		}

		tangentNormal = SafeNormalize(Bilinear.Eval(n0, n1, n2, n3, s, t), new Vector3(0f, 0f, 1f));
		return true;
	}

	static void CornerParam(IReadOnlyList<Corner> face, Corner corner, out float s, out float t)
	{
		for (int i = 0; i < face.Count; i++)
		{
			if (face[i] != corner)
				continue;

			(s, t) = i switch
			{
				0 => (0f, 0f),
				1 => (1f, 0f),
				2 => (1f, 1f),
				3 => (0f, 1f),
				_ => (0.5f, 0.5f),
			};
			return;
		}

		s = t = 0.5f;
	}

	static bool TryAverageCorners(
		Vector3?[] corners,
		int cornerWidth,
		int x,
		int y,
		out Vector3 tangentNormal)
	{
		var sum = Vector3.Zero;
		int count = 0;
		AccumulateCorner(corners, cornerWidth, x, y, ref sum, ref count);
		AccumulateCorner(corners, cornerWidth, x + 1, y, ref sum, ref count);
		AccumulateCorner(corners, cornerWidth, x, y + 1, ref sum, ref count);
		AccumulateCorner(corners, cornerWidth, x + 1, y + 1, ref sum, ref count);

		if (count == 0)
		{
			tangentNormal = default;
			return false;
		}

		tangentNormal = SafeNormalize(sum / count, new Vector3(0f, 0f, 1f));
		return true;
	}

	static void AccumulateCorner(
		Vector3?[] corners,
		int cornerWidth,
		int x,
		int y,
		ref Vector3 sum,
		ref int count)
	{
		var corner = corners[x + y * cornerWidth];
		if (corner == null)
			return;

		sum += corner.Value;
		count++;
	}

	static Color EncodeNormal(Vector3 tangentNormal)
	{
		var encoded = tangentNormal * 0.5f + Vector3.One * 0.5f;
		return new Color(encoded.X, encoded.Y, encoded.Z, 1f);
	}

	static Vector3 WorldToTangent(Vector3 world, Vector3 tangent, Vector3 bitangent, Vector3 normal) =>
		new(
			world.Dot(tangent),
			world.Dot(bitangent),
			world.Dot(normal));

	static void ComputeTbn(
		IReadOnlyList<Corner> face,
		float s,
		float t,
		out Vector3 tangent,
		out Vector3 bitangent,
		out Vector3 normal)
	{
		var q0 = face[0].Vertex.Xyz;
		var q1 = face[1].Vertex.Xyz;
		var q2 = face[2].Vertex.Xyz;
		var q3 = face[3].Vertex.Xyz;

		var ds = Bilinear.Ds(q0, q1, q2, q3, s, t);
		var dt = Bilinear.Dt(q0, q1, q2, q3, s, t);
		normal = SafeNormalize(ds.Cross(dt), FaceFallbackNormal(face));

		tangent = SafeNormalize(ds - normal * normal.Dot(ds), q1 - q0);
		bitangent = normal.Cross(tangent);
		if (bitangent.LengthSquared() < 1e-12f)
			bitangent = SafeNormalize(dt - normal * normal.Dot(dt), q3 - q0);
	}

	static Vector3 FaceFallbackNormal(IReadOnlyList<Corner> face)
	{
		var q0 = face[0].Vertex.Xyz;
		var q1 = face[1].Vertex.Xyz;
		var q3 = face[3].Vertex.Xyz;
		return (q1 - q0).Cross(q3 - q0);
	}

	static Vector3 SafeNormalize(Vector3 primary, Vector3 fallback)
	{
		if (primary.LengthSquared() >= 1e-12f)
			return primary.Normalized();

		if (fallback.LengthSquared() >= 1e-12f)
			return fallback.Normalized();

		return Vector3.Up;
	}

	static List<IReadOnlyList<Corner>> CollectFaces(Topology topology)
	{
		var used = new HashSet<Corner>();
		var faces = new List<IReadOnlyList<Corner>>();

		foreach (var corner in topology.Corners)
		{
			if (used.Contains(corner))
				continue;

			var face = TopologyWalk.WalkFaceCcw(corner);
			foreach (var faceCorner in face)
				used.Add(faceCorner);

			faces.Add(face);
		}

		return faces;
	}

	static bool TrySampleFaceNormal(IEnumerable<IReadOnlyList<Corner>> faces, Vector2 uv, out Vector3 normal)
	{
		foreach (var face in faces)
		{
			if (TrySampleFaceNormal(face, uv, out normal))
				return true;
		}

		normal = default;
		return false;
	}

	static bool TrySampleFaceNormal(IReadOnlyList<Corner> face, Vector2 uv, out Vector3 normal)
	{
		if (!Bilinear.TryInverse(
			face[0].Uv,
			face[1].Uv,
			face[2].Uv,
			face[3].Uv,
			uv,
			out float s,
			out float t))
		{
			normal = default;
			return false;
		}

		var q0 = face[0].Vertex.Xyz;
		var q1 = face[1].Vertex.Xyz;
		var q2 = face[2].Vertex.Xyz;
		var q3 = face[3].Vertex.Xyz;

		var ds = Bilinear.Ds(q0, q1, q2, q3, s, t);
		var dt = Bilinear.Dt(q0, q1, q2, q3, s, t);
		normal = SafeNormalize(ds.Cross(dt), FaceFallbackNormal(face));
		return true;
	}
}
