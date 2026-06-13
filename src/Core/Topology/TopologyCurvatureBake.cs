using System;
using System.Collections.Generic;
using System.IO;
using Godot;

namespace DelaunyFabric.Core;

/// <summary>
/// Cotangent weighted signed mean curvature per vertex, baked to a UV texture.
/// Encodes H as 0.5 + 0.5 * clamp(H / scale, -1, 1) in all RGB channels.
/// </summary>
public static class TopologyCurvatureBake
{
	public static IReadOnlyDictionary<Vertex, float> ComputeVertexCurvature(Topology topology)
	{
		var curvature = new Dictionary<Vertex, float>(topology.Vertices.Count);
		foreach (var vertex in topology.Vertices)
			curvature[vertex] = ComputeSignedMeanCurvature(vertex);

		return curvature;
	}

	public static Image BakeCurvatureMap(Topology topology, int width, int height, float curvatureScale = 0f)
	{
		if (width <= 0 || height <= 0)
			throw new ArgumentOutOfRangeException(nameof(width), "Curvature map size must be positive.");

		if (curvatureScale <= 0f)
			curvatureScale = EstimateCurvatureScale(topology);

		var vertexCurvature = ComputeVertexCurvature(topology);
		var faces = CollectFaces(topology);

		var image = Image.CreateEmpty(width, height, false, Image.Format.Rgba8);
		image.Fill(new Color(0.5f, 0.5f, 0.5f, 1f));

		int cornerWidth = width + 1;
		var corners = new float?[cornerWidth * (height + 1)];

		for (int y = 0; y <= height; y++)
		{
			for (int x = 0; x <= width; x++)
			{
				if (TrySampleCurvature(faces, vertexCurvature, ImageUv.ToUv(x, y, width, height), out float value))
					corners[x + y * cornerWidth] = value;
			}
		}

		for (int y = 0; y < height; y++)
		{
			for (int x = 0; x < width; x++)
			{
				if (!TryAverageCorners(corners, cornerWidth, x, y, out float value))
					continue;

				image.SetPixel(x, y, EncodeCurvature(value, curvatureScale));
			}
		}

		return image;
	}

	public static void BakeAndSave(
		Topology topology,
		int width,
		int height,
		string path,
		float curvatureScale = 0f) =>
		Save(BakeCurvatureMap(topology, width, height, curvatureScale), path);

	public static void Save(Image image, string path)
	{
		var directory = Path.GetDirectoryName(path);
		if (!string.IsNullOrEmpty(directory))
			Directory.CreateDirectory(directory);

		var error = image.SavePng(path);
		if (error != Error.Ok)
			throw new InvalidOperationException($"Failed to save curvature map to '{path}': {error}");
	}

	static float ComputeSignedMeanCurvature(Vertex vertex)
	{
		var position = vertex.Xyz;
		var normal = ComputeVertexNormal(vertex);
		if (normal.LengthSquared() < 1e-12f)
			return 0f;

		normal = normal.Normalized();

		var laplacian = Vector3.Zero;
		var seenNeighbors = new HashSet<Vertex>();
		foreach (var corner in vertex.Corners)
		{
			var neighbor = corner.Next.Vertex;
			if (neighbor == vertex || !seenNeighbors.Add(neighbor))
				continue;

			float cotAlpha = CotAngleAtCorner(corner);
			float cotBeta = corner.Across != null ? CotAngleAtCorner(corner.Across) : 0f;
			laplacian += (cotAlpha + cotBeta) * (neighbor.Xyz - position);
		}

		float mixedArea = ComputeMixedArea(vertex);
		if (mixedArea < 1e-12f)
			return 0f;

		var meanCurvatureNormal = laplacian / (2f * mixedArea);
		return meanCurvatureNormal.Dot(normal) * 0.5f;
	}

	static Vector3 ComputeVertexNormal(Vertex vertex)
	{
		var sum = Vector3.Zero;
		foreach (var corner in vertex.Corners)
		{
			var toNext = corner.Next.Vertex.Xyz - corner.Vertex.Xyz;
			var toPrev = corner.Prev.Vertex.Xyz - corner.Vertex.Xyz;
			sum += toNext.Cross(toPrev);
		}

		return sum.LengthSquared() < 1e-12f ? Vector3.Zero : sum.Normalized();
	}

	static float ComputeMixedArea(Vertex vertex)
	{
		float area = 0f;
		var position = vertex.Xyz;
		foreach (var corner in vertex.Corners)
		{
			var toNext = corner.Next.Vertex.Xyz - position;
			var toPrev = corner.Prev.Vertex.Xyz - position;
			area += toNext.Cross(toPrev).Length();
		}

		return area * 0.125f;
	}

	static float CotAngleAtCorner(Corner corner)
	{
		var position = corner.Vertex.Xyz;
		var toPrev = corner.Prev.Vertex.Xyz - position;
		var toNext = corner.Next.Vertex.Xyz - position;
		float dot = toPrev.Dot(toNext);
		float crossLength = toPrev.Cross(toNext).Length();
		if (crossLength < 1e-12f)
			return 0f;

		return dot / crossLength;
	}

	static float EstimateCurvatureScale(Topology topology)
	{
		float edgeLengthSum = 0f;
		int edgeCount = 0;
		var seen = new HashSet<(Corner, Corner)>();

		foreach (var corner in topology.Corners)
		{
			if (!seen.Add(OrderedCornerPair(corner, corner.Next)))
				continue;

			edgeLengthSum += (corner.Vertex.Xyz - corner.Next.Vertex.Xyz).Length();
			edgeCount++;
		}

		if (edgeCount == 0)
			return 1f;

		float meanEdgeLength = edgeLengthSum / edgeCount;
		return 2f / meanEdgeLength;
	}

	static (Corner, Corner) OrderedCornerPair(Corner a, Corner b) =>
		ReferenceEquals(a, b) ? (a, b) : (a.GetHashCode(), b.GetHashCode()) switch
		{
			var (ha, hb) when ha <= hb => (a, b),
			_ => (b, a),
		};

	static bool TrySampleCurvature(
		IEnumerable<IReadOnlyList<Corner>> faces,
		IReadOnlyDictionary<Vertex, float> vertexCurvature,
		Vector2 uv,
		out float curvature)
	{
		foreach (var face in faces)
		{
			if (TrySampleFaceCurvature(face, vertexCurvature, uv, out curvature))
				return true;
		}

		curvature = default;
		return false;
	}

	static bool TrySampleFaceCurvature(
		IReadOnlyList<Corner> face,
		IReadOnlyDictionary<Vertex, float> vertexCurvature,
		Vector2 uv,
		out float curvature)
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
			curvature = default;
			return false;
		}

		float h0 = vertexCurvature[face[0].Vertex];
		float h1 = vertexCurvature[face[1].Vertex];
		float h2 = vertexCurvature[face[2].Vertex];
		float h3 = vertexCurvature[face[3].Vertex];
		curvature = Bilinear.Eval(h0, h1, h2, h3, s, t);
		return true;
	}

	static bool TryAverageCorners(
		float?[] corners,
		int cornerWidth,
		int x,
		int y,
		out float value)
	{
		float sum = 0f;
		int count = 0;
		AccumulateCorner(corners, cornerWidth, x, y, ref sum, ref count);
		AccumulateCorner(corners, cornerWidth, x + 1, y, ref sum, ref count);
		AccumulateCorner(corners, cornerWidth, x, y + 1, ref sum, ref count);
		AccumulateCorner(corners, cornerWidth, x + 1, y + 1, ref sum, ref count);

		if (count == 0)
		{
			value = default;
			return false;
		}

		value = sum / count;
		return true;
	}

	static void AccumulateCorner(
		float?[] corners,
		int cornerWidth,
		int x,
		int y,
		ref float sum,
		ref int count)
	{
		var corner = corners[x + y * cornerWidth];
		if (corner == null)
			return;

		sum += corner.Value;
		count++;
	}

	static Color EncodeCurvature(float curvature, float scale)
	{
		float encoded = 0.5f + 0.5f * Mathf.Clamp(curvature / scale, -1f, 1f);
		return new Color(encoded, encoded, encoded, 1f);
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
}
