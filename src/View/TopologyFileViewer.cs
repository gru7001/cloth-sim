using System.Collections.Generic;
using DelaunyFabric.Core;
using Godot;
using TopologyCorner = DelaunyFabric.Core.Corner;

namespace DelaunyFabric.View;

public partial class TopologyFileViewer : Node3D
{
	[Export] public string TopologyPath { get; set; } = "user://topology.topo";
	[Export] public NodePath BodyPath { get; set; } = "Body";
	[Export] public NodePath OriginalSurfacePath { get; set; } = "Original/Surface";
	[Export] public NodePath OriginalGraphPath { get; set; } = "Original/Graph";
	[Export] public NodePath CoarseSurfacePath { get; set; } = "Coarse/Surface";
	[Export] public NodePath CoarseGraphPath { get; set; } = "Coarse/Graph";
	[Export] public float MaxIntegralError { get; set; } = 0.08f;
	[Export] public float UvScale { get; set; } = 0.7f;
	[Export] public float DebugMarkerRadius { get; set; } = 0.0006f;
	[Export] public string NormalMapPath { get; set; } = "user://topology_normal.png";
	[Export] public int NormalMapSize { get; set; } = 1;
	[Export] public string CoarseMeshPath { get; set; } = "user://topology_coarse.obj";
	[Export] public string CurvatureMapPath { get; set; } = "user://topology_curvature.png";
	[Export] public float CurvatureScale { get; set; } = 0f;

	public override void _Ready()
	{
		var absolutePath = ProjectSettings.GlobalizePath(TopologyPath);
		var topology = TopologyFile.Load(absolutePath);

		var body = GetNode<Node3D>(BodyPath);
		var collider = GodotMeshCollider.BuildFrom(body);

		var coarse = TopologyCoarsening.Coarsen(CloneTopology(topology), MaxIntegralError);
		TopologyFaceSdfResolve.Resolve(coarse, collider, 20, 0.002f);

		var normalMapPath = ProjectSettings.GlobalizePath(NormalMapPath);
		TopologyNormalBake.BakeAndSave(coarse, topology, NormalMapSize, NormalMapSize, normalMapPath);
		GD.Print($"Saved normal map to {normalMapPath}");

		var coarseMeshPath = ProjectSettings.GlobalizePath(CoarseMeshPath);
		TopologyMeshBuilder.SaveObj(coarse, coarseMeshPath);
		GD.Print($"Saved coarse mesh to {coarseMeshPath}");

		var curvatureMapPath = ProjectSettings.GlobalizePath(CurvatureMapPath);
		TopologyCurvatureBake.BakeAndSave(coarse, NormalMapSize, NormalMapSize, curvatureMapPath, CurvatureScale);
		GD.Print($"Saved curvature map to {curvatureMapPath}");

		SetMeshes(OriginalSurfacePath, OriginalGraphPath, topology);
		SetMeshes(CoarseSurfacePath, CoarseGraphPath, coarse);
	}

	void SetMeshes(NodePath surfacePath, NodePath graphPath, Topology topology)
	{
		GetNode<MeshInstance3D>(surfacePath).Mesh = TopologyMeshBuilder.Build(topology);
		GetNode<MeshInstance3D>(graphPath).Mesh =
			TopologyMeshBuilder.BuildDebugMarkers(topology, DebugMarkerRadius, UvScale);
	}

	static Topology CloneTopology(Topology source)
	{
		var clone = new Topology();
		var vertexMap = new Dictionary<Vertex, Vertex>(source.Vertices.Count);
		var cornerMap = new Dictionary<TopologyCorner, TopologyCorner>(source.Corners.Count);

		foreach (var vertex in source.Vertices)
		{
			var copy = new Vertex
			{
				Xyz = vertex.Xyz,
				ContactNormal = vertex.ContactNormal,
				FromSubdivision = vertex.FromSubdivision,
			};
			clone.Vertices.Add(copy);
			vertexMap[vertex] = copy;
		}

		foreach (var corner in source.Corners)
		{
			var copy = new TopologyCorner
			{
				Uv = corner.Uv,
				Vertex = vertexMap[corner.Vertex],
			};
			clone.Corners.Add(copy);
			copy.Vertex.Corners.Add(copy);
			cornerMap[corner] = copy;
		}

		foreach (var corner in source.Corners)
		{
			var copy = cornerMap[corner];
			copy.Next = cornerMap[corner.Next];
			copy.Prev = cornerMap[corner.Prev];
			if (corner.Across != null)
				copy.Across = cornerMap[corner.Across];
		}

		return clone;
	}
}
