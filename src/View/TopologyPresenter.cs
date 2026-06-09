using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DelaunyFabric.Core;
using Godot;
using TopologyCorner = DelaunyFabric.Core.Corner;

namespace DelaunyFabric.View;

public partial class TopologyPresenter : Node3D
{
	[Export] public string PatternPath { get; set; } = "res://assets/pattern.png";
	[Export] public string InitPath { get; set; } = "res://assets/init.png";
	[Export] public NodePath BodyPath { get; set; } = "Body";
	[Export] public NodePath SurfacePath { get; set; } = "Surface";
	[Export] public NodePath GraphPath { get; set; } = "Graph";
	[Export] public float InitNormalOffset { get; set; } = 0.05f;
	[Export] public float SubdivisionMinUvEdgeLength { get; set; } = 0.02f;
	[Export] public float UvScale { get; set; } = 0.6f;	
	[Export] public int RelaxationIterations { get; set; } = 200;
	[Export] public float SkinDistance { get; set; } = 0.015f;
	[Export] public float ContactFrictionStrength { get; set; } = 2.0f;
	[Export] public float StaticFrictionStrength { get; set; } = 0.0f;
	[Export] public float DebugMarkerRadius { get; set; } = 0.0006f;
	[Export] public Vector3 Gravity { get; set; } = new Vector3(0, -0.005f, 0);
	[Export] public float GravityDecay { get; set; } = 0.99f;
	[Export] public int TopologySaveInterval { get; set; } = 10;
	[Export] public string TopologySavePath { get; set; } = "user://topology.topo";

	Topology? _topology;
	Topology? _workerTopology;
	MeshTriangleCollider? _collider;
	MeshInstance3D? _surface;
	MeshInstance3D? _graph;
	CancellationTokenSource? _solverCancel;
	Task? _solverTask;
	readonly object _positionsLock = new();
	SolverSnapshot? _latestSnapshot;
	int _snapshotPrintCounter;

	public override void _Ready()
	{
		var body = GetNode<Node3D>(BodyPath);
		_surface = GetNode<MeshInstance3D>(SurfacePath);
		_graph = GetNode<MeshInstance3D>(GraphPath);
		var sourceMesh = FindFirstMesh(body)
			?? throw new InvalidOperationException($"No MeshInstance3D with a Mesh found under {BodyPath}.");

		var patternMarkers = PatternMarkerParser.Parse(ImageGridLoader.FromResource(PatternPath));
		var initMarkers = MeshInitMarkerParser.Parse(
			sourceMesh.Mesh,
			sourceMesh.GlobalTransform,
			ImageGridLoader.FromResource(InitPath),
			InitNormalOffset);

		var positioned = PatternMarkerPlacement.Place(patternMarkers, initMarkers);
		_topology = TopologyBuilder.Build(positioned);
		if (SubdivisionMinUvEdgeLength > 0f)
			_topology = TopologySubdivision.Subdivide(_topology, SubdivisionMinUvEdgeLength);

		_collider = GodotMeshCollider.BuildFrom(body);
		_workerTopology = CloneTopology(_topology);
		StartSolver();
		UpdateMeshes();
	}

	public override void _PhysicsProcess(double delta)
	{
		if (_topology == null || _surface == null || _graph == null) return;
		if (ApplyLatestPositions())
			UpdateMeshes();
	}

	public override void _ExitTree()
	{
		_solverCancel?.Cancel();
	}

	void StartSolver()
	{
		if (_workerTopology == null || _collider == null) return;

		_solverCancel = new CancellationTokenSource();
		var token = _solverCancel.Token;
		var topology = _workerTopology;
		var collider = _collider;

		_solverTask = Task.Run(() => RunSolver(topology, collider, token), token);
	}

	void RunSolver(Topology topology, MeshTriangleCollider collider, CancellationToken token)
	{
		var currentGravity = Gravity;
		int iteration = 0;

		while (!token.IsCancellationRequested)
		{
			TopologyRelaxation.Relax(
				topology,
				collider,
				SkinDistance,
				ContactFrictionStrength,
				StaticFrictionStrength,
				currentGravity,
				UvScale,
				RelaxationIterations);
			PublishSnapshot(topology, currentGravity);
			currentGravity *= Mathf.Clamp(GravityDecay, 0f, 1f);
			iteration++;

			if (TopologySaveInterval > 0 && iteration % TopologySaveInterval == 0)
				SaveTopology(topology);

			Thread.Sleep(1);
		}
	}

	void SaveTopology(Topology topology)
	{
		try
		{
			var absolutePath = ProjectSettings.GlobalizePath(TopologySavePath);
			var directory = Path.GetDirectoryName(absolutePath);
			if (!string.IsNullOrEmpty(directory))
				Directory.CreateDirectory(directory);

			TopologyFile.Save(topology, absolutePath);
		}
		catch (Exception ex)
		{
			GD.PrintErr($"Failed to save topology: {ex.Message}");
		}
	}

	void PublishSnapshot(Topology topology, Vector3 currentGravity)
	{
		var positions = new Vector3[topology.Vertices.Count];
		var contactNormals = new Vector3?[topology.Vertices.Count];
		for (int i = 0; i < positions.Length; i++)
		{
			positions[i] = topology.Vertices[i].Xyz;
			contactNormals[i] = topology.Vertices[i].ContactNormal;
		}

		lock (_positionsLock)
			_latestSnapshot = new SolverSnapshot(positions, contactNormals, currentGravity);
	}

	bool ApplyLatestPositions()
	{
		if (_topology == null) return false;

		SolverSnapshot? snapshot;
		lock (_positionsLock)
		{
			snapshot = _latestSnapshot;
			_latestSnapshot = null;
		}

		if (snapshot == null) return false;
		var value = snapshot.Value;
		if (value.Positions.Length != _topology.Vertices.Count) return false;

		for (int i = 0; i < value.Positions.Length; i++)
		{
			_topology.Vertices[i].Xyz = value.Positions[i];
			_topology.Vertices[i].ContactNormal = value.ContactNormals[i];
		}

		GD.Print($"Current gravity slice: {value.CurrentGravity}");

		return true;
	}

	void UpdateMeshes()
	{
		if (_topology == null || _surface == null || _graph == null) return;

		_surface.Mesh = TopologyMeshBuilder.Build(_topology);
		_graph.Mesh = TopologyMeshBuilder.BuildDebugMarkers(_topology, DebugMarkerRadius, UvScale);
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

	readonly record struct SolverSnapshot(Vector3[] Positions, Vector3?[] ContactNormals, Vector3 CurrentGravity);
}
