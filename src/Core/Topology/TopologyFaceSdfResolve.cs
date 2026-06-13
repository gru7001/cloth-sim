using System.Collections.Generic;
using Godot;

namespace DelaunyFabric.Core;

/// <summary>
/// Post-processes topology faces against a collider SDF.
/// Visits each face once, finds the minimum signed distance over its bilinear patch,
/// and if penetrating, translates all face vertices outward by <c>-minSdf</c> along the face normal.
/// </summary>
public static class TopologyFaceSdfResolve
{
	public static void Resolve(Topology topology, ISdfCollider collider, int samplesPerAxis = 5, float foo = 0f)
	{
		var pending = new HashSet<Corner>(topology.Corners);
		var stack = new Stack<Corner>(topology.Corners);

		while (stack.Count > 0)
		{
			var start = stack.Pop();
			if (!pending.Remove(start))
				continue;

			var face = TopologyWalk.WalkFaceCcw(start);
			foreach (var corner in face)
				pending.Remove(corner);

			float minSdf = ComputeMinSdf(face, collider, samplesPerAxis) - foo;
			if (minSdf >= 0f)
				continue;

			var normal = ComputeFaceNormal(face);
			if (normal.LengthSquared() < 1e-12f)
				continue;

			var offset = normal * (-minSdf);
			foreach (var corner in face)
				corner.Vertex.Xyz += offset;
		}
	}

	static float ComputeMinSdf(IReadOnlyList<Corner> face, ISdfCollider collider, int samplesPerAxis)
	{
		samplesPerAxis = Mathf.Max(samplesPerAxis, 2);

		var q0 = face[0].Vertex.Xyz;
		var q1 = face[1].Vertex.Xyz;
		var q2 = face[2].Vertex.Xyz;
		var q3 = face[3].Vertex.Xyz;

		float minSdf = float.MaxValue;
		float step = 1f / (samplesPerAxis - 1);

		for (int i = 0; i < samplesPerAxis; i++)
		{
			float s = i * step;
			for (int j = 0; j < samplesPerAxis; j++)
			{
				float t = j * step;
				var position = Bilinear.Eval(q0, q1, q2, q3, s, t);
				collider.Sample(position, out float sdf, out _);
				if (sdf < minSdf)
					minSdf = sdf;
			}
		}

		return minSdf;
	}

	static Vector3 ComputeFaceNormal(IReadOnlyList<Corner> face)
	{
		var q0 = face[0].Vertex.Xyz;
		var q1 = face[1].Vertex.Xyz;
		var q2 = face[2].Vertex.Xyz;
		var q3 = face[3].Vertex.Xyz;

		var ds = Bilinear.Ds(q0, q1, q2, q3, 0.5f, 0.5f);
		var dt = Bilinear.Dt(q0, q1, q2, q3, 0.5f, 0.5f);
		return SafeNormalize(ds.Cross(dt), FaceFallbackNormal(q0, q1, q3));
	}

	static Vector3 FaceFallbackNormal(Vector3 q0, Vector3 q1, Vector3 q3) =>
		(q1 - q0).Cross(q3 - q0);

	static Vector3 SafeNormalize(Vector3 primary, Vector3 fallback)
	{
		if (primary.LengthSquared() >= 1e-12f)
			return primary.Normalized();

		if (fallback.LengthSquared() >= 1e-12f)
			return fallback.Normalized();

		return Vector3.Up;
	}
}
