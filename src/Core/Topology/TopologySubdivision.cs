using System.Collections.Generic;

namespace DelaunyFabric.Core;

public static class TopologySubdivision
{
	public static Topology Subdivide(Topology source, float minEdgeLength, int maxSplits = int.MaxValue)
	{
		var result = new Topology();
		var vertexMap = CopyVertices(source, result);
		var cornerMap = CopyCorners(source, result, vertexMap);
		CopyCornerLinks(source, cornerMap);
		SubdivideInPlace(result, minEdgeLength, maxSplits);

		return result;
	}

	static void SubdivideInPlace(Topology topology, float minEdgeLength, int maxSplits)
	{
		var pending = new HashSet<Corner>(topology.Corners);
		var splitVertices = new Dictionary<EdgeKey, Vertex>();
		int splits = 0;

		while (pending.Count > 0 && splits < maxSplits)
		{
			var origin = TopologyWalk.TakeAny(pending);
			var strip = TopologyWalk.WalkStrip(origin);

			if (CanSplit(strip, minEdgeLength))
			{
				var newCorners = SplitStrip(topology, splitVertices, strip);
				splits++;
				foreach (var corner in strip)
					pending.Remove(corner);
				foreach (var corner in newCorners)
					pending.Add(corner);
			}
			else
			{
				foreach (var corner in strip)
					pending.Remove(corner);
			}
		}
	}

	static List<Corner> SplitStrip(
		Topology topology,
		Dictionary<EdgeKey, Vertex> splitVertices,
		IReadOnlyList<Corner> strip)
	{
		var created = new List<Corner>();
		if (strip.Count == 0) return created;

		var origin = strip[0];
		Corner? firstEntryUpper = null;
		Corner? previousExit = null;
		Corner? previousExitLower = null;

		foreach (var entry in strip)
		{
			var exit = entry.Next.Next;
			var entrySplitVertex = GetOrCreateSplitVertex(topology, splitVertices, entry);
			var exitSplitVertex = GetOrCreateSplitVertex(topology, splitVertices, exit);

			SplitSegment(
				topology,
				entry,
				exit,
				entrySplitVertex,
				exitSplitVertex,
				out var entryUpper,
				out var exitLower,
				out var newCorners);

			created.AddRange(newCorners);
			firstEntryUpper ??= entryUpper;

			if (previousExit != null && previousExitLower != null)
				ConnectAcross(previousExit, previousExitLower, entry, entryUpper);

			previousExit = exit;
			previousExitLower = exitLower;
		}

		if (previousExit != null && previousExitLower != null)
		{
			if (previousExit.Across == origin)
			{
				ConnectAcross(previousExit, previousExitLower, origin, firstEntryUpper);
			}
			else if (previousExit.Across == null)
			{
				previousExitLower.Across = null;
			}
		}

		return created;
	}

	static void SplitSegment(
		Topology topology,
		Corner entry,
		Corner exit,
		Vertex entrySplitVertex,
		Vertex exitSplitVertex,
		out Corner entryUpper,
		out Corner exitLower,
		out List<Corner> created)
	{
		var entryNext = entry.Next;
		var exitNext = exit.Next;
		var entryMidUv = (entry.Uv + entryNext.Uv) * 0.5f;
		var exitMidUv = (exit.Uv + exitNext.Uv) * 0.5f;

		var entryLower = CreateCorner(topology, entrySplitVertex, entryMidUv);
		entryUpper = CreateCorner(topology, entrySplitVertex, entryMidUv);
		exitLower = CreateCorner(topology, exitSplitVertex, exitMidUv);
		var exitUpper = CreateCorner(topology, exitSplitVertex, exitMidUv);

		TopologyWalk.WireNext(entry, entryLower);
		TopologyWalk.WireNext(entryLower, exitLower);
		TopologyWalk.WireNext(exitLower, exitNext);

		TopologyWalk.WireNext(entryUpper, entryNext);
		TopologyWalk.WireNext(exit, exitUpper);
		TopologyWalk.WireNext(exitUpper, entryUpper);

		entryLower.Across = exitUpper;
		exitUpper.Across = entryLower;

		created = [entryLower, entryUpper, exitLower, exitUpper];
	}

	static Vertex GetOrCreateSplitVertex(
		Topology topology,
		Dictionary<EdgeKey, Vertex> splitVertices,
		Corner corner)
	{
		var key = new EdgeKey(corner.Vertex, corner.Next.Vertex);
		if (splitVertices.TryGetValue(key, out var vertex))
			return vertex;

		vertex = CreateSplitVertex(topology, corner);
		splitVertices[key] = vertex;
		return vertex;
	}

	static Vertex CreateSplitVertex(Topology topology, Corner corner)
	{
		var vertex = new Vertex
		{
			Xyz = (corner.Vertex.Xyz + corner.Next.Vertex.Xyz) * 0.5f,
			FromSubdivision = true,
		};

		topology.Vertices.Add(vertex);
		return vertex;
	}

	static Corner CreateCorner(Topology topology, Vertex vertex, Godot.Vector2 uv)
	{
		var corner = new Corner { Uv = uv, Vertex = vertex };
		topology.Corners.Add(corner);
		vertex.Corners.Add(corner);
		return corner;
	}

	static void ConnectAcross(Corner exit, Corner exitLower, Corner entry, Corner entryUpper)
	{
		if (exit.Across != entry || entry.Across != exit)
			throw new System.InvalidOperationException("Can only split-connect corners that were already across.");

		exit.Across = entryUpper;
		entryUpper.Across = exit;
		exitLower.Across = entry;
		entry.Across = exitLower;
	}

	static bool CanSplit(IReadOnlyList<Corner> strip, float minEdgeLength)
	{
		float requiredLength = minEdgeLength * 2f;
		foreach (var corner in strip)
		{
			if ((corner.Uv - corner.Next.Uv).Length() < requiredLength)
				return false;
		}

		return true;
	}

	static Dictionary<Vertex, Vertex> CopyVertices(Topology source, Topology result)
	{
		var vertexMap = new Dictionary<Vertex, Vertex>(source.Vertices.Count);

		foreach (var vertex in source.Vertices)
		{
			var copy = new Vertex { Xyz = vertex.Xyz, FromSubdivision = vertex.FromSubdivision };
			result.Vertices.Add(copy);
			vertexMap[vertex] = copy;
		}

		return vertexMap;
	}

	static Dictionary<Corner, Corner> CopyCorners(
		Topology source,
		Topology result,
		IReadOnlyDictionary<Vertex, Vertex> vertexMap)
	{
		var cornerMap = new Dictionary<Corner, Corner>(source.Corners.Count);

		foreach (var corner in source.Corners)
		{
			var copy = new Corner
			{
				Uv = corner.Uv,
				Vertex = vertexMap[corner.Vertex],
			};

			result.Corners.Add(copy);
			copy.Vertex.Corners.Add(copy);
			cornerMap[corner] = copy;
		}

		return cornerMap;
	}

	static void CopyCornerLinks(
		Topology source,
		IReadOnlyDictionary<Corner, Corner> cornerMap)
	{
		foreach (var corner in source.Corners)
		{
			var copy = cornerMap[corner];
			copy.Next = cornerMap[corner.Next];
			copy.Prev = cornerMap[corner.Prev];
			if (corner.Across != null)
				copy.Across = cornerMap[corner.Across];
		}
	}

	readonly struct EdgeKey(Vertex a, Vertex b) : System.IEquatable<EdgeKey>
	{
		readonly Vertex _a = a;
		readonly Vertex _b = b;

		public bool Equals(EdgeKey other) =>
			(ReferenceEquals(_a, other._a) && ReferenceEquals(_b, other._b))
			|| (ReferenceEquals(_a, other._b) && ReferenceEquals(_b, other._a));

		public override bool Equals(object? obj) =>
			obj is EdgeKey other && Equals(other);

		public override int GetHashCode() =>
			System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(_a)
			^ System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(_b);
	}
}
