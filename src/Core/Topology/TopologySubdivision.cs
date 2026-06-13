using System.Collections.Generic;
using Godot;

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
		int splits = 0;

		while (pending.Count > 0 && splits < maxSplits)
		{
			var origin = TopologyWalk.TakeAny(pending);
			var strip = TopologyWalk.WalkStrip(origin);

			if (CanSplit(strip, minEdgeLength))
			{
				var newCorners = SplitStrip(topology, strip);	
				splits++;
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

	static List<Corner> SplitStrip(Topology topology, IReadOnlyList<Corner> strip)
	{
		var created = new List<Corner>();
		if (strip.Count == 0)
			return created;

		bool closed = IsClosedStrip(strip);
		Corner? prevAboveRight = null;
		Corner? prevBelowRight = null;
		Corner? firstAboveLeft = null;
		Corner? firstBelowLeft = null;
		Vertex? prevRightVertex = null;
		Vertex? firstLeftVertex = null;

		for (int i = 0; i < strip.Count; i++)
		{
			var topRight = strip[i];
			var bottomRight = topRight.Next;
			var bottomLeft = bottomRight.Next;
			var topLeft = bottomLeft.Next;

			Vertex leftVertex = prevRightVertex ?? CreateSplitVertexOnEdge(topology, bottomLeft);
			Vertex rightVertex = closed && i == strip.Count - 1
				? firstLeftVertex!
				: CreateSplitVertexOnEdge(topology, topRight);

			var rightMidUv = MidUv(topRight, bottomRight);
			var aboveRight = CreateCorner(topology, rightVertex, rightMidUv);
			var belowRight = CreateCorner(topology, rightVertex, rightMidUv);

			var leftMidUv = MidUv(topLeft, bottomLeft);
			var aboveLeft = CreateCorner(topology, leftVertex, leftMidUv);
			var belowLeft = CreateCorner(topology, leftVertex, leftMidUv);

			TopologyWalk.WireNext(topRight, aboveRight);
			TopologyWalk.WireNext(aboveRight, aboveLeft);
			TopologyWalk.WireNext(aboveLeft, topLeft);

			TopologyWalk.WireNext(bottomLeft, belowLeft);
			TopologyWalk.WireNext(belowLeft, belowRight);
			TopologyWalk.WireNext(belowRight, bottomRight);

			WireNewCornerAcross(
				aboveRight,
				belowRight,
				aboveLeft,
				belowLeft,
				prevAboveRight,
				prevBelowRight);

			if (i == 0)
			{
				firstAboveLeft = aboveLeft;
				firstBelowLeft = belowLeft;
				firstLeftVertex = leftVertex;
			}

			if (closed && i == strip.Count - 1)
			{
				firstAboveLeft!.Across = aboveRight;
				belowRight.Across = firstBelowLeft!;
			}

			prevAboveRight = aboveRight;
			prevBelowRight = belowRight;
			prevRightVertex = rightVertex;

			created.AddRange([aboveRight, belowRight, aboveLeft, belowLeft]);
		}

		return created;
	}

	static bool IsClosedStrip(IReadOnlyList<Corner> strip)
	{
		if (strip.Count < 2)
			return false;

		var (_, end) = TopologyWalk.WalkForward(strip[0]);
		return end == strip[0];
	}

	/// <summary>
	/// Directed Across on split corners only (one pointer per corner).
	/// Within cell: above-right → below-right, below-left → above-left.
	/// Along strip: above-left → previous-above-right, previous-below-right → below-left.
	/// Closed wrap: first above-left → last above-right, last below-right → first below-left.
	/// </summary>
	static void WireNewCornerAcross(
		Corner aboveRight,
		Corner belowRight,
		Corner aboveLeft,
		Corner belowLeft,
		Corner? prevAboveRight,
		Corner? prevBelowRight)
	{
		aboveRight.Across = belowRight;
		belowLeft.Across = aboveLeft;

		if (prevAboveRight != null)
			aboveLeft.Across = prevAboveRight;

		if (prevBelowRight != null)
			prevBelowRight.Across = belowLeft;
	}

	static Vector2 MidUv(Corner a, Corner b) => (a.Uv + b.Uv) * 0.5f;

	static Vertex CreateSplitVertexOnEdge(Topology topology, Corner edgeStart)
	{
		var edgeEnd = edgeStart.Next;
		var vertex = new Vertex
		{
			Xyz = (edgeStart.Vertex.Xyz + edgeEnd.Vertex.Xyz) * 0.5f,
			FromSubdivision = true,
		};

		topology.Vertices.Add(vertex);
		return vertex;
	}

	static Corner CreateCorner(Topology topology, Vertex vertex, Vector2 uv)
	{
		var corner = new Corner { Uv = uv, Vertex = vertex };
		topology.Corners.Add(corner);
		vertex.Corners.Add(corner);
		return corner;
	}

	static bool CanSplit(IReadOnlyList<Corner> strip, float minEdgeLength)
	{
		if (strip.Count == 0)
			return false;

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

}
