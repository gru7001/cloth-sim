using System;
using System.Collections.Generic;
using System.IO;
using DelaunyFabric.Core;
using CoreCorner = DelaunyFabric.Core.Corner;
using Vector2 = Godot.Vector2;
using Vector3 = Godot.Vector3;

var markers = BuildInsetSquare();
var topology = TopologyBuilder.Build(markers);

var onceSplit = TopologySubdivision.Subdivide(topology, minEdgeLength: 0.1f, maxSplits: 1);
Console.WriteLine($"before: v={topology.Vertices.Count} c={topology.Corners.Count}");
Console.WriteLine($"after 1 split: v={onceSplit.Vertices.Count} c={onceSplit.Corners.Count}");

var once = TopologySubdivision.Subdivide(topology, minEdgeLength: 0.1f, maxSplits: 1);
CheckStripAdjacency(once);
CheckFaceSets(once);

var coarsened = TopologySubdivision.Subdivide(topology, minEdgeLength: 0.1f, maxSplits: 1);
TopologyCoarsening.Coarsen(coarsened, maxIntegralError: 1e-6f, maxCollapses: 1);
Console.WriteLine($"after 1 coarsen: v={coarsened.Vertices.Count} c={coarsened.Corners.Count}");

CheckTopologyFileRoundTrip(once);

static void CheckTopologyFileRoundTrip(Topology topology)
{
	var path = Path.Combine(Path.GetTempPath(), "delauny-fabric-topology.txt");
	TopologyFile.Save(topology, path);
	var loaded = TopologyFile.Load(path);
	Console.WriteLine(
		$"topology file: saved v={topology.Vertices.Count} c={topology.Corners.Count} loaded v={loaded.Vertices.Count} c={loaded.Corners.Count}");
	File.Delete(path);
}

static void CheckFaceSets(Topology subdiv)
{
	var strip = TopologyWalk.WalkStrip(subdiv.Corners[0]);
	var faceSets = TopologyCoarsening.BuildCollapseFaceSets(strip);
	Console.WriteLine(
		$"face sets: before={faceSets.Before.Count} after={faceSets.After.Count}");
	for (int i = 0; i < strip.Count; i++)
	{
		var c = strip[i];
		Console.WriteLine(
			$"  strip{i}: Next.FromSubdiv={c.Next.Vertex.FromSubdivision} Next.Next.FromSubdiv={c.Next.Next.Vertex.FromSubdivision}");
	}

	int subdivVertices = 0;
	foreach (var vertex in subdiv.Vertices)
	{
		if (vertex.FromSubdivision)
			subdivVertices++;
	}

	Console.WriteLine(
		$"  unsubdivided: vertices={subdiv.Vertices.Count - subdivVertices} subdiv={subdivVertices}");
	for (int i = 0; i < faceSets.Before.Count; i++)
		Console.WriteLine($"  before{i}: {FormatFace(faceSets.Before[i])}");

	for (int i = 0; i < faceSets.After.Count; i++)
		Console.WriteLine($"  after{i}: {FormatFace(faceSets.After[i])}");
}

static string FormatFace(IReadOnlyList<CoreCorner> face)
{
	var uvs = new string[face.Count];
	for (int i = 0; i < face.Count; i++)
		uvs[i] = $"({face[i].Uv.X:F2},{face[i].Uv.Y:F2})";

	return $"corners={face.Count} uvs={string.Join(" ", uvs)}";
}

static void CheckStripAdjacency(Topology subdiv)
{
	var strip = TopologyWalk.WalkStrip(subdiv.Corners[0]);
	Console.WriteLine($"strip count={strip.Count}");
	for (int i = 0; i + 1 < strip.Count; i++)
	{
		var a = strip[i];
		var b = strip[i + 1];
		bool eq1 = ReferenceEquals(a.Vertex, b.Prev.Vertex);
		bool eq2 = ReferenceEquals(a.Next.Vertex, b.Vertex);
		var step = TopologyWalk.StripStepForward(a);
		Console.WriteLine(
			$"  {i}-{i + 1}: Vertex==Prev.Vertex {eq1}  Next.Vertex==Vertex {eq2}  b==Across.Next {ReferenceEquals(b, step)}");
	}
}

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
