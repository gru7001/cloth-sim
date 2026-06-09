using System.Collections.Generic;

namespace DelaunyFabric.Core;

public sealed class CollapseFaceSets
{
	public List<IReadOnlyList<Corner>> Before { get; } = [];
	public List<IReadOnlyList<Corner>> After { get; } = [];
}

public static class TopologyCoarsening
{
	public static Topology Coarsen(Topology source, float maxIntegralError, int maxCollapses = int.MaxValue)
	{
		var pending = new HashSet<Corner>(source.Corners);
		int collapses = 0;

		while (pending.Count > 0 && collapses < maxCollapses)
		{
			var origin = TopologyWalk.TakeAny(pending);
			var strip = TopologyWalk.WalkStrip(origin);
			var faceSets = BuildCollapseFaceSets(strip);

			if (CanCollapse(strip, faceSets, maxIntegralError))
			{
				CollapseStrip(source, strip, faceSets);
				collapses++;
			}

			foreach (var corner in strip)
				pending.Remove(corner);
		}

		return source;
	}

	public static CollapseFaceSets BuildStripCollapseFaceSets(Corner origin) =>
		BuildCollapseFaceSets(TopologyWalk.WalkStrip(origin));

	public static CollapseFaceSets BuildCollapseFaceSets(IReadOnlyList<Corner> strip)
	{
		var faceSets = new CollapseFaceSets();

		foreach (var corner in strip)
			AddStripEntryFaces(faceSets, corner);

		return faceSets;
	}

	static void AddStripEntryFaces(CollapseFaceSets faceSets, Corner corner)
	{
		var across = corner.Prev.Across;
		if (across == null)
			return;

		faceSets.Before.Add(TopologyWalk.WalkFace(corner));
		faceSets.Before.Add(TopologyWalk.WalkFace(across));

		faceSets.After.Add(
		[
			corner.Next,
			corner.Next.Next,
			across.Next.Next,
			across.Prev,
		]);
	}

	static bool CanCollapse(
		IReadOnlyList<Corner> strip,
		CollapseFaceSets faceSets,
		float maxIntegralError)
	{
		if (strip.Count == 0 || faceSets.Before.Count == 0 || faceSets.After.Count == 0)
			return false;

		foreach (var corner in strip)
		{
			if (!corner.Next.Vertex.FromSubdivision || !corner.Next.Next.Vertex.FromSubdivision)
				return false;
		}

		return true;
	}

	static void CollapseStrip(Topology topology, IReadOnlyList<Corner> strip, CollapseFaceSets faceSets)
	{
		RewireAfterFaces(strip, faceSets);
		RemoveEdgeLoopVertices(topology, strip);
	}

	static void RewireAfterFaces(IReadOnlyList<Corner> strip, CollapseFaceSets faceSets)
	{
		int faceIndex = 0;
		foreach (var corner in strip)
		{
			if (corner.Prev.Across == null)
				continue;

			var face = faceSets.After[faceIndex++];
			TopologyWalk.WireNext(face[1], face[2]);
			TopologyWalk.WireNext(face[3], face[0]);
		}
	}

	static void RemoveEdgeLoopVertices(Topology topology, IReadOnlyList<Corner> strip)
	{
		var vertices = new HashSet<Vertex>();
		foreach (var corner in strip)
		{
			vertices.Add(corner.Vertex);
		}

		var cornersToRemove = new HashSet<Corner>();
		foreach (var vertex in vertices)
		{
			foreach (var corner in vertex.Corners)
				cornersToRemove.Add(corner);
		}

		foreach (var corner in cornersToRemove)
			topology.Corners.Remove(corner);

		foreach (var vertex in vertices)
			topology.Vertices.Remove(vertex);
	}
}
