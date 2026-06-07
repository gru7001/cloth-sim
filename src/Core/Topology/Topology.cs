using System.Collections.Generic;
using Godot;

namespace DelaunyFabric.Core;

public sealed class Topology
{
	public List<Vertex> Vertices { get; } = [];
	public List<Corner> Corners { get; } = [];
}

public sealed class Vertex
{
	public Vector3 Xyz;
	public Vector3? ContactNormal;
	public List<Corner> Corners { get; } = [];
}

public sealed class Corner
{
	public Vector2 Uv;
	public Vertex Vertex = null!;
	public Corner Next = null!;
	public Corner Prev = null!;
	public Corner? Across;
}

public static class TopologyBuilder
{
	public static Topology Build(IReadOnlyList<PositionedPatternMarker> markers)
	{
		var topology = new Topology();
		if (markers.Count == 0) return topology;

		var markerToVertex = BuildVertices(markers, topology);
		var faces = FindQuadFaces(markers);
		var cornerAtEdge = new Dictionary<EdgeKey, Corner>();

		foreach (var face in faces)
		{
			var corners = new Corner[4];
			for (int i = 0; i < 4; i++)
			{
				var marker = markers[face[i]];
				var corner = new Corner
				{
					Uv = marker.Uv,
					Vertex = markerToVertex[marker],
				};

				corners[i] = corner;
				topology.Corners.Add(corner);
				corner.Vertex.Corners.Add(corner);
			}

			for (int i = 0; i < 4; i++)
			{
				corners[i].Next = corners[(i + 1) % 4];
				corners[i].Prev = corners[(i + 3) % 4];
			}

			for (int i = 0; i < 4; i++)
			{
				int a = face[i];
				int b = face[(i + 1) % 4];
				var forward = new EdgeKey(a, b);
				var reverse = new EdgeKey(b, a);

				if (cornerAtEdge.TryGetValue(reverse, out var across))
				{
					corners[i].Across = across;
					across.Across = corners[i];
					continue;
				}

				cornerAtEdge[forward] = corners[i];
			}
		}

		return topology;
	}

	static Dictionary<PositionedPatternMarker, Vertex> BuildVertices(
		IReadOnlyList<PositionedPatternMarker> markers,
		Topology topology)
	{
		var markerToVertex = new Dictionary<PositionedPatternMarker, Vertex>(markers.Count);
		var seen = new HashSet<PositionedPatternMarker>();

		foreach (var marker in markers)
		{
			if (!seen.Add(marker)) continue;

			var weldGroup = CollectWeldGroup(marker, seen);
			var vertex = new Vertex { Xyz = AverageXyz(weldGroup) };
			topology.Vertices.Add(vertex);

			foreach (var welded in weldGroup)
				markerToVertex[welded] = vertex;
		}

		return markerToVertex;
	}

	static List<PositionedPatternMarker> CollectWeldGroup(
		PositionedPatternMarker start,
		HashSet<PositionedPatternMarker> seen)
	{
		var group = new List<PositionedPatternMarker>();
		var stack = new Stack<PositionedPatternMarker>();
		stack.Push(start);

		while (stack.Count > 0)
		{
			var marker = stack.Pop();
			group.Add(marker);

			foreach (var welded in marker.WeldedTo)
			{
				if (seen.Add(welded))
					stack.Push(welded);
			}
		}

		return group;
	}

	static Vector3 AverageXyz(IReadOnlyList<PositionedPatternMarker> markers)
	{
		var sum = Vector3.Zero;
		foreach (var marker in markers)
			sum += marker.Xyz;

		return sum / markers.Count;
	}

	static List<int[]> FindQuadFaces(IReadOnlyList<PositionedPatternMarker> markers)
	{
		var local = new Dictionary<PositionedPatternMarker, int>(markers.Count);
		for (int i = 0; i < markers.Count; i++)
			local[markers[i]] = i;

		var uv = new Vector2[markers.Count];
		var adj = new List<int>[markers.Count];
		for (int i = 0; i < markers.Count; i++)
		{
			uv[i] = markers[i].Uv;
			adj[i] = [];
			foreach (var connected in markers[i].Connected)
			{
				if (local.TryGetValue(connected, out int j))
					adj[i].Add(j);
			}
		}

		var quads = new List<int[]>();
		foreach (var face in EnumeratePlanarFaces(uv, adj))
		{
			if (face.Count != 4) continue;
			if (SignedArea(face, uv) <= 0f) continue;
			quads.Add([face[0], face[1], face[2], face[3]]);
		}

		return quads;
	}

	static List<List<int>> EnumeratePlanarFaces(Vector2[] uv, List<int>[] adj)
	{
		var sorted = new List<int>[uv.Length];
		for (int i = 0; i < uv.Length; i++)
		{
			sorted[i] = new List<int>(adj[i]);
			sorted[i].Sort((a, b) =>
			{
				float aa = Mathf.Atan2(uv[a].Y - uv[i].Y, uv[a].X - uv[i].X);
				float ab = Mathf.Atan2(uv[b].Y - uv[i].Y, uv[b].X - uv[i].X);
				return aa.CompareTo(ab);
			});
		}

		var used = new HashSet<(int, int)>();
		var faces = new List<List<int>>();

		for (int u = 0; u < uv.Length; u++)
		{
			foreach (int v in sorted[u])
			{
				if (used.Contains((u, v))) continue;
				var face = WalkFace(u, v, sorted, used);
				if (face.Count >= 3)
					faces.Add(face);
			}
		}

		return faces;
	}

	static List<int> WalkFace(int from, int to, List<int>[] sorted, HashSet<(int, int)> used)
	{
		var face = new List<int>();
		int u = from, v = to;
		int guard = 0;

		do
		{
			if (++guard > 4096) return [];

			used.Add((u, v));
			face.Add(u);

			var neighbors = sorted[v];
			if (neighbors.Count < 2) return [];

			int i = neighbors.IndexOf(u);
			if (i < 0) return [];

			int j = (i - 1 + neighbors.Count) % neighbors.Count;
			u = v;
			v = neighbors[j];
		}
		while (u != from || v != to);

		return face;
	}

	static float SignedArea(IReadOnlyList<int> face, Vector2[] uv)
	{
		float area = 0f;
		for (int i = 0; i < face.Count; i++)
		{
			var p = uv[face[i]];
			var q = uv[face[(i + 1) % face.Count]];
			area += p.X * q.Y - q.X * p.Y;
		}

		return area * 0.5f;
	}

	readonly record struct EdgeKey(int From, int To);
}
