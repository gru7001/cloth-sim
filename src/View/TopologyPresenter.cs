using System;
using System.Collections.Generic;
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
	[Export] public float SubdivisionMinUvEdgeLength { get; set; } = 0.01f;
	[Export] public float UvScale { get; set; } = 0.5f;
	[Export] public int RelaxationIterations { get; set; } = 10;
	[Export] public float SkinDistance { get; set; } = 0.006f;
	[Export] public float ContactFrictionStrength { get; set; } = 4f;
	[Export] public float StaticFrictionStrength { get; set; } = 1.0f;
	[Export] public float DebugMarkerRadius { get; set; } = 0.0006f;
	[Export] public Vector3 Gravity { get; set; } = new Vector3(0, -0.001f, 0);

	Topology? _topology;
	Topology? _workerTopology;
	MeshTriangleCollider? _collider;
	MeshInstance3D? _surface;
	MeshInstance3D? _graph;
	CancellationTokenSource? _solverCancel;
	Task? _solverTask;
	readonly object _positionsLock = new();
	SolverSnapshot? _latestSnapshot;

	public override void _Ready()
	{
		var body = GetNode<Node3D>(BodyPath);
		_surface = GetNode<MeshInstance3D>(SurfacePath);
		_graph = GetNode<MeshInstance3D>(GraphPath);
		var sourceMesh = FindFirstMesh(body)
			?? throw new InvalidOperationException($"No MeshInstance3D with a Mesh found under {BodyPath}.");

		var patternMarkers = PatternMarkerParser.Parse(LoadImageGrid(PatternPath));
		var initMarkers = MeshInitMarkerParser.Parse(
			sourceMesh.Mesh,
			sourceMesh.GlobalTransform,
			LoadImageGrid(InitPath),
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
		while (!token.IsCancellationRequested)
		{
			var wishes = TopologyRelaxation.Relax(
				topology,
				UvScale,
				RelaxationIterations);

			AddGravity(wishes);
			DampenWishes(topology, wishes);
			TopologyCollision.Apply(
				topology,
				collider,
				wishes,
				SkinDistance,
				ContactFrictionStrength,
				StaticFrictionStrength);
			PublishSnapshot(topology);
			Thread.Sleep(1);
		}
	}

	static void DampenWishes(Topology topology, List<Vector3> wishes)
	{
		for (int i = 0; i < wishes.Count; i++)
		{
			var current = topology.Vertices[i].Xyz;
			var diff = wishes[i] - current;
			wishes[i] = current + diff * 0.1f;
		}
	}

	void PublishSnapshot(Topology topology)
	{
		var positions = new Vector3[topology.Vertices.Count];
		var contactNormals = new Vector3?[topology.Vertices.Count];
		for (int i = 0; i < positions.Length; i++)
		{
			positions[i] = topology.Vertices[i].Xyz;
			contactNormals[i] = topology.Vertices[i].ContactNormal;
		}

		lock (_positionsLock)
			_latestSnapshot = new SolverSnapshot(positions, contactNormals);
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

		return true;
	}

	void UpdateMeshes()
	{
		if (_topology == null || _surface == null || _graph == null) return;

		_surface.Mesh = TopologyMeshBuilder.Build(_topology);
		_graph.Mesh = TopologyMeshBuilder.BuildDebugMarkers(_topology, DebugMarkerRadius, UvScale);
	}

	static ImageGrid LoadImageGrid(string path)
	{
		var image = Image.LoadFromFile(path);
		if (image == null)
			throw new InvalidOperationException($"Could not load image: {path}");

		if (image.GetFormat() != Image.Format.Rgba8)
			image.Convert(Image.Format.Rgba8);

		return new ImageGrid(image.GetData(), image.GetWidth(), image.GetHeight());
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

	void AddGravity(List<Vector3> wishes)
	{
		if (Gravity == Vector3.Zero) return;

		var offset = Gravity;
		for (int i = 0; i < wishes.Count; i++)
			wishes[i] += offset;
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

	readonly record struct SolverSnapshot(Vector3[] Positions, Vector3?[] ContactNormals);
}
