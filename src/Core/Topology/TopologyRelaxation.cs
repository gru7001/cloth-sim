using System.Collections.Generic;
using Godot;

namespace DelaunyFabric.Core;

public static class TopologyRelaxation
{
	public static List<Vector3> Relax(
		Topology topology,
		float uvScale = 1f,
		int iterations = 1)
	{
		var positions = CurrentPositions(topology);
		var vertexIndex = IndexVertices(topology);

		for (int iteration = 0; iteration < iterations; iteration++)
		{
			//positions = ProjectToNeighborPlanes(topology, vertexIndex, positions);
			positions = RelaxVertices(topology, uvScale, vertexIndex, positions);
		}

		return positions;
	}

	static List<Vector3> CurrentPositions(Topology topology)
	{
		var positions = new List<Vector3>(topology.Vertices.Count);
		foreach (var vertex in topology.Vertices)
			positions.Add(vertex.Xyz);

		return positions;
	}

	static Dictionary<Vertex, int> IndexVertices(Topology topology)
	{
		var index = new Dictionary<Vertex, int>(topology.Vertices.Count);
		for (int i = 0; i < topology.Vertices.Count; i++)
			index[topology.Vertices[i]] = i;

		return index;
	}

	static List<Vector3> RelaxVertices(
		Topology topology,
		float uvScale,
		Dictionary<Vertex, int> vertexIndex,
		List<Vector3> positions)
	{
		var wishes = new List<Vector3>(positions);

		for (int i = 0; i < topology.Vertices.Count; i++)
		{
			var vertex = topology.Vertices[i];
			var correction = Vector3.Zero;
			int count = 0;

			foreach (var corner in vertex.Corners)
			{
				foreach (var other in FaceCorners(corner))
				{
					var neighbor = other.Vertex;
					if (neighbor == vertex) continue;

					int j = vertexIndex[neighbor];
					var delta = positions[j] - positions[i];
					float length = delta.Length();
					if (length < 1e-6f) continue;

					float restLength = (other.Uv - corner.Uv).Length() * uvScale;
					float error = length - restLength;
					correction += delta / length * (error * 0.5f);
					count++;
				}
			}

			if (count > 0)
				wishes[i] += correction / count;
		}

		return wishes;
	}

	static List<Vector3> ProjectToNeighborPlanes(
		Topology topology,
		Dictionary<Vertex, int> vertexIndex,
		List<Vector3> positions)
	{
		var wishes = new List<Vector3>(positions);

		for (int i = 0; i < topology.Vertices.Count; i++)
		{
			var neighbors = NeighborPositions(topology.Vertices[i], vertexIndex, positions);
			if (neighbors.Count < 3) continue;

			var center = Average(neighbors);
			var normal = FitPlaneNormal(neighbors, center);
			if (normal.LengthSquared() < 1e-8f) continue;

			var fromPlane = positions[i] - center;
			wishes[i] = positions[i] - normal * fromPlane.Dot(normal);
		}

		return wishes;
	}

	static List<Vector3> NeighborPositions(
		Vertex vertex,
		Dictionary<Vertex, int> vertexIndex,
		List<Vector3> positions)
	{
		var neighbors = new List<Vector3>();

		foreach (var corner in vertex.Corners)
		{
			AddNeighbor(corner.Next.Vertex, vertex, vertexIndex, positions, neighbors);
			AddNeighbor(corner.Prev.Vertex, vertex, vertexIndex, positions, neighbors);
		}

		return neighbors;
	}

	static void AddNeighbor(
		Vertex neighbor,
		Vertex vertex,
		Dictionary<Vertex, int> vertexIndex,
		List<Vector3> positions,
		List<Vector3> neighbors)
	{
		if (neighbor == vertex) return;
		neighbors.Add(positions[vertexIndex[neighbor]]);
	}

	static Vector3 Average(IReadOnlyList<Vector3> positions)
	{
		var sum = Vector3.Zero;
		foreach (var position in positions)
			sum += position;

		return sum / positions.Count;
	}

	static Vector3 FitPlaneNormal(IReadOnlyList<Vector3> points, Vector3 center)
	{
		float xx = 0f, xy = 0f, xz = 0f, yy = 0f, yz = 0f, zz = 0f;
		foreach (var point in points)
		{
			var d = point - center;
			xx += d.X * d.X;
			xy += d.X * d.Y;
			xz += d.X * d.Z;
			yy += d.Y * d.Y;
			yz += d.Y * d.Z;
			zz += d.Z * d.Z;
		}

		return SmallestEigenvector(xx, xy, xz, yy, yz, zz);
	}

	static Vector3 SmallestEigenvector(float xx, float xy, float xz, float yy, float yz, float zz)
	{
		var a = new[,]
		{
			{ xx, xy, xz },
			{ xy, yy, yz },
			{ xz, yz, zz },
		};
		var v = new[,]
		{
			{ 1f, 0f, 0f },
			{ 0f, 1f, 0f },
			{ 0f, 0f, 1f },
		};

		for (int step = 0; step < 8; step++)
		{
			int p = 0, q = 1;
			float largest = Mathf.Abs(a[0, 1]);
			if (Mathf.Abs(a[0, 2]) > largest)
			{
				p = 0;
				q = 2;
				largest = Mathf.Abs(a[0, 2]);
			}
			if (Mathf.Abs(a[1, 2]) > largest)
			{
				p = 1;
				q = 2;
				largest = Mathf.Abs(a[1, 2]);
			}

			if (largest < 1e-8f) break;
			Rotate(a, v, p, q);
		}

		int k = 0;
		if (a[1, 1] < a[k, k]) k = 1;
		if (a[2, 2] < a[k, k]) k = 2;

		return new Vector3(v[0, k], v[1, k], v[2, k]).Normalized();
	}

	static void Rotate(float[,] a, float[,] v, int p, int q)
	{
		float app = a[p, p];
		float aqq = a[q, q];
		float apq = a[p, q];
		float angle = 0.5f * Mathf.Atan2(2f * apq, aqq - app);
		float c = Mathf.Cos(angle);
		float s = Mathf.Sin(angle);

		for (int i = 0; i < 3; i++)
		{
			float aip = a[i, p];
			float aiq = a[i, q];
			a[i, p] = c * aip - s * aiq;
			a[i, q] = s * aip + c * aiq;
		}

		for (int i = 0; i < 3; i++)
		{
			float api = a[p, i];
			float aqi = a[q, i];
			a[p, i] = c * api - s * aqi;
			a[q, i] = s * api + c * aqi;
		}

		for (int i = 0; i < 3; i++)
		{
			float vip = v[i, p];
			float viq = v[i, q];
			v[i, p] = c * vip - s * viq;
			v[i, q] = s * vip + c * viq;
		}
	}

	static IEnumerable<Corner> FaceCorners(Corner origin)
	{
		var current = origin;
		do
		{
			yield return current;
			current = current.Next;
		}
		while (current != origin);
	}
}
