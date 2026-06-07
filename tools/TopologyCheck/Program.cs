using System;
using System.Collections.Generic;
using DelaunyFabric.Core;
using CoreCorner = DelaunyFabric.Core.Corner;
using Vector2 = Godot.Vector2;
using Vector3 = Godot.Vector3;

var markers = BuildInsetSquare();
var topology = TopologyBuilder.Build(markers);

Dump("before", topology);

Dump("after 1 split", TopologySubdivision.Subdivide(topology, minEdgeLength: 0.1f, maxSplits: 1));
Dump("after 2 splits", TopologySubdivision.Subdivide(topology, minEdgeLength: 0.1f, maxSplits: 2));
Dump("after 3 splits", TopologySubdivision.Subdivide(topology, minEdgeLength: 0.1f, maxSplits: 3));

static List<PositionedPatternMarker> BuildInsetSquare()
{
	var outer0 = Marker(0.0f, 0.0f);
	var outer1 = Marker(1.0f, 0.0f);
	var outer2 = Marker(1.0f, 1.0f);
	var outer3 = Marker(0.0f, 1.0f);

	var inner0 = Marker(0.3f, 0.3f);
	var inner1 = Marker(0.7f, 0.3f);
	var inner2 = Marker(0.7f, 0.7f);
	var inner3 = Marker(0.3f, 0.7f);

	Link(outer0, outer1);
	Link(outer1, outer2);
	Link(outer2, outer3);
	Link(outer3, outer0);

	Link(inner0, inner1);
	Link(inner1, inner2);
	Link(inner2, inner3);
	Link(inner3, inner0);

	Link(outer0, inner0);
	Link(outer1, inner1);
	Link(outer2, inner2);
	Link(outer3, inner3);

	return [outer0, outer1, outer2, outer3, inner0, inner1, inner2, inner3];
}

static PositionedPatternMarker Marker(float x, float y) =>
	new()
	{
		Uv = new Vector2(x, y),
		Xyz = new Vector3(x, 0f, y),
	};

static void Link(PositionedPatternMarker a, PositionedPatternMarker b)
{
	a.Connected.Add(b);
	b.Connected.Add(a);
}

static void Dump(string label, Topology topology)
{
	Console.WriteLine($"{label}: vertices={topology.Vertices.Count} corners={topology.Corners.Count}");
	Check(topology);

	for (int i = 0; i < topology.Corners.Count; i++)
	{
		var c = topology.Corners[i];
		Console.WriteLine(
			$"  c{i}: uv={Uv(c)} next={IndexOf(topology, c.Next)} prev={IndexOf(topology, c.Prev)} across={IndexOf(topology, c.Across)} edgeLen={(c.Uv - c.Next.Uv).Length():F3}");
	}
}

static void Check(Topology topology)
{
	var used = new HashSet<CoreCorner>();
	int faces = 0;
	foreach (var corner in topology.Corners)
	{
		if (corner.Across != null && corner.Across.Across != corner)
			Console.WriteLine($"  broken across at c{topology.Corners.IndexOf(corner)}");

		if (!used.Add(corner)) continue;

		int length = 0;
		var current = corner;
		do
		{
			length++;
			current = current.Next;
			if (length > 16)
			{
				Console.WriteLine($"  oversized face from c{topology.Corners.IndexOf(corner)}");
				break;
			}
		}
		while (current != corner);

		current = corner;
		for (int i = 0; i < length; i++)
		{
			used.Add(current);
			current = current.Next;
		}

		faces++;
		if (length != 4)
			Console.WriteLine($"  non-quad face from c{topology.Corners.IndexOf(corner)} length={length}");
	}

	Console.WriteLine($"  faces={faces}");
}

static string Uv(CoreCorner c) => $"({c.Uv.X:F2},{c.Uv.Y:F2})";

static string IndexOf(Topology topology, CoreCorner? corner)
{
	if (corner == null) return "null";
	int i = topology.Corners.IndexOf(corner);
	return i >= 0 ? i.ToString() : "?";
}
