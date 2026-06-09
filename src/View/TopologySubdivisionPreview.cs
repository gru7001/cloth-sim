using System;
using System.Collections.Generic;
using DelaunyFabric.Core;
using Godot;

namespace DelaunyFabric.View;

public partial class TopologySubdivisionPreview : Node3D
{
	[Export] public bool UseInsetSquareTestTopology { get; set; } = true;
	[Export] public string PatternPath { get; set; } = "res://assets/pattern.png";
	[Export] public string InitPath { get; set; } = "res://assets/init.png";
	[Export] public NodePath BodyPath { get; set; } = "Body";
	[Export] public NodePath BeforeSurfacePath { get; set; } = "Before/Surface";
	[Export] public NodePath BeforeGraphPath { get; set; } = "Before/Graph";
	[Export] public NodePath AfterSurfacePath { get; set; } = "After/Surface";
	[Export] public NodePath AfterGraphPath { get; set; } = "After/Graph";
	[Export] public float InitNormalOffset { get; set; } = 0.2f;
	[Export] public float SubdivisionMinUvEdgeLength { get; set; } = 0.05f;
	[Export] public int SubdivisionSteps { get; set; } = 1;
	[Export] public float VertexPerturbRadius { get; set; } = 0.04f;
	[Export] public float DebugMarkerRadius { get; set; } = 0.015f;

	public override void _Ready()
	{
		var before = UseInsetSquareTestTopology
			? TopologyBuilder.Build(BuildInsetSquare())
			: BuildFromAssets();
		var after = TopologySubdivision.Subdivide(before, SubdivisionMinUvEdgeLength, SubdivisionSteps);

		PerturbVertices(before);
		PerturbVertices(after);

		SetMeshes(BeforeSurfacePath, BeforeGraphPath, before);
		SetMeshes(AfterSurfacePath, AfterGraphPath, after);
	}

	Topology BuildFromAssets()
	{
		var body = GetNode<Node3D>(BodyPath);
		var sourceMesh = FindFirstMesh(body)
			?? throw new InvalidOperationException($"No MeshInstance3D with a Mesh found under {BodyPath}.");

		var patternMarkers = PatternMarkerParser.Parse(ImageGridLoader.FromResource(PatternPath));
		var initMarkers = MeshInitMarkerParser.Parse(
			sourceMesh.Mesh,
			sourceMesh.GlobalTransform,
			ImageGridLoader.FromResource(InitPath),
			InitNormalOffset);

		return TopologyBuilder.Build(PatternMarkerPlacement.Place(patternMarkers, initMarkers));
	}

	void SetMeshes(NodePath surfacePath, NodePath graphPath, Topology topology)
	{
		GetNode<MeshInstance3D>(surfacePath).Mesh = TopologyMeshBuilder.Build(topology);
		GetNode<MeshInstance3D>(graphPath).Mesh = TopologyMeshBuilder.BuildDebugMarkers(topology, DebugMarkerRadius);
	}

	void PerturbVertices(Topology topology)
	{
		if (VertexPerturbRadius <= 0f) return;

		for (int i = 0; i < topology.Vertices.Count; i++)
			topology.Vertices[i].Xyz += PerturbOffset(i, VertexPerturbRadius);
	}

	static Vector3 PerturbOffset(int index, float radius)
	{
		const float goldenAngle = 2.3999631f;
		float angle = index * goldenAngle;
		float layer = (index % 7 - 3) / 3f;
		return new Vector3(
			Mathf.Cos(angle) * radius,
			layer * radius * 0.35f,
			Mathf.Sin(angle) * radius);
	}

	static MeshInstance3D? FindFirstMesh(Node node)
	{
		if (node is MeshInstance3D { Mesh: not null } mesh)
			return mesh;

		foreach (var child in node.GetChildren())
		{
			var found = FindFirstMesh(child);
			if (found != null) return found;
		}

		return null;
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
}
