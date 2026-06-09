using System.Collections.Generic;
using DelaunyFabric.Core;
using Godot;
using TopologyCorner = DelaunyFabric.Core.Corner;

namespace DelaunyFabric.View;

public static class TopologyMeshBuilder
{
	public static ArrayMesh Build(Topology topology)
	{
		var vertices = new List<Vector3>();
		var uvs = new List<Vector2>();
		var indices = new List<int>();

		foreach (var face in Faces(topology))
			AddFace(face, vertices, uvs, indices);

		var arrays = new Godot.Collections.Array();
		arrays.Resize((int)Mesh.ArrayType.Max);
		arrays[(int)Mesh.ArrayType.Vertex] = vertices.ToArray();
		arrays[(int)Mesh.ArrayType.TexUV] = uvs.ToArray();
		arrays[(int)Mesh.ArrayType.Index] = indices.ToArray();

		var mesh = new ArrayMesh();
		mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
		return mesh;
	}

	public static ArrayMesh BuildDebugMarkers(
		Topology topology,
		float markerRadius = 0.015f,
		float uvScale = 1f)
	{
		var vertices = new List<Vector3>();
		var colors = new List<Color>();
		var indices = new List<int>();
		var vertexIndex = IndexVertices(topology);
		var edgeMarkers = new HashSet<(int A, int B)>();
		var edgeLines = new List<Vector3>();
		var edgeLineColors = new List<Color>();

		foreach (var vertex in topology.Vertices)
			AddMarker(vertices, colors, indices, vertex.Xyz, markerRadius, VertexDebugColor(vertex));

		foreach (var corner in topology.Corners)
		{
			int a = vertexIndex[corner.Vertex];
			int b = vertexIndex[corner.Next.Vertex];
			if (a == b) continue;
			if (a > b) (a, b) = (b, a);
			if (!edgeMarkers.Add((a, b))) continue;

			AddLine(
				edgeLines,
				edgeLineColors,
				corner.Vertex.Xyz,
				corner.Next.Vertex.Xyz,
				EdgeDebugColor(corner, uvScale));
		}

		var arrays = new Godot.Collections.Array();
		arrays.Resize((int)Mesh.ArrayType.Max);
		arrays[(int)Mesh.ArrayType.Vertex] = vertices.ToArray();
		arrays[(int)Mesh.ArrayType.Color] = colors.ToArray();
		arrays[(int)Mesh.ArrayType.Index] = indices.ToArray();

		var mesh = new ArrayMesh();
		mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
		if (edgeLines.Count > 0)
		{
			var lineArrays = new Godot.Collections.Array();
			lineArrays.Resize((int)Mesh.ArrayType.Max);
			lineArrays[(int)Mesh.ArrayType.Vertex] = edgeLines.ToArray();
			lineArrays[(int)Mesh.ArrayType.Color] = edgeLineColors.ToArray();
			mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Lines, lineArrays);
		}

		return mesh;
	}

	static Color VertexDebugColor(Vertex vertex) =>
		vertex.ContactNormal.HasValue
			? new Color(0.1f, 1f, 0.25f)
			: new Color(0.35f, 0.55f, 1f);

	static Color EdgeDebugColor(TopologyCorner corner, float uvScale)
	{
		float currentLength = (corner.Next.Vertex.Xyz - corner.Vertex.Xyz).Length();
		float restLength = (corner.Next.Uv - corner.Uv).Length() * uvScale;
		if (restLength < 1e-6f)
			return new Color(0.95f, 0.95f, 0.95f);

		float strain = (currentLength - restLength) / restLength;
		if (strain > 0f)
			return LerpColor(new Color(0.95f, 0.95f, 0.95f), new Color(1f, 0.15f, 0.05f), Mathf.Min(strain, 1f));

		return LerpColor(new Color(0.95f, 0.95f, 0.95f), new Color(0.1f, 0.35f, 1f), Mathf.Min(-strain * 4f, 1f));
	}

	static Color LerpColor(Color a, Color b, float t) =>
		new(
			Mathf.Lerp(a.R, b.R, t),
			Mathf.Lerp(a.G, b.G, t),
			Mathf.Lerp(a.B, b.B, t),
			Mathf.Lerp(a.A, b.A, t));

	static IEnumerable<List<TopologyCorner>> Faces(Topology topology)
	{
		var used = new HashSet<TopologyCorner>();

		foreach (var corner in topology.Corners)
		{
			if (used.Contains(corner)) continue;

			var face = Face(corner);
			foreach (var faceCorner in face)
				used.Add(faceCorner);

			yield return face;
		}
	}

	static List<TopologyCorner> Face(TopologyCorner origin)
	{
		var face = new List<TopologyCorner>();
		var current = origin;

		do
		{
			face.Add(current);
			current = current.Next;
		}
		while (current != origin);

		return face;
	}

	static void AddFace(
		IReadOnlyList<TopologyCorner> face,
		List<Vector3> vertices,
		List<Vector2> uvs,
		List<int> indices)
	{
		int start = vertices.Count;
		foreach (var corner in face)
		{
			vertices.Add(corner.Vertex.Xyz);
			uvs.Add(corner.Uv);
		}

		for (int i = 1; i < face.Count - 1; i++)
		{
			indices.Add(start);
			indices.Add(start + i);
			indices.Add(start + i + 1);
		}
	}

	static Dictionary<Vertex, int> IndexVertices(Topology topology)
	{
		var index = new Dictionary<Vertex, int>(topology.Vertices.Count);
		for (int i = 0; i < topology.Vertices.Count; i++)
			index[topology.Vertices[i]] = i;

		return index;
	}

	static void AddMarker(
		List<Vector3> vertices,
		List<Color> colors,
		List<int> indices,
		Vector3 center,
		float radius,
		Color color)
	{
		int start = vertices.Count;
		vertices.Add(center + Vector3.Up * radius);
		vertices.Add(center + Vector3.Down * radius);
		vertices.Add(center + Vector3.Right * radius);
		vertices.Add(center + Vector3.Left * radius);
		vertices.Add(center + Vector3.Forward * radius);
		vertices.Add(center + Vector3.Back * radius);

		for (int i = 0; i < 6; i++)
			colors.Add(color);

		AddTriangle(indices, start, 0, 2, 4);
		AddTriangle(indices, start, 0, 4, 3);
		AddTriangle(indices, start, 0, 3, 5);
		AddTriangle(indices, start, 0, 5, 2);
		AddTriangle(indices, start, 1, 4, 2);
		AddTriangle(indices, start, 1, 3, 4);
		AddTriangle(indices, start, 1, 5, 3);
		AddTriangle(indices, start, 1, 2, 5);
	}

	static void AddTriangle(List<int> indices, int start, int a, int b, int c)
	{
		indices.Add(start + a);
		indices.Add(start + b);
		indices.Add(start + c);
	}

	static void AddLine(
		List<Vector3> vertices,
		List<Color> colors,
		Vector3 a,
		Vector3 b,
		Color color)
	{
		vertices.Add(a);
		vertices.Add(b);
		colors.Add(color);
		colors.Add(color);
	}
}
