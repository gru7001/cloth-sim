using System.Collections.Generic;
using DelaunyFabric.Core;
using Godot;

namespace DelaunyFabric.View;

/// <summary>Build collision + SDF sampling from imported mesh scenes (GLB, etc.).</summary>
public static class GodotMeshCollider
{
	public static MeshTriangleCollider BuildFrom(Node3D root)
	{
		var a = new List<Vector3>();
		var b = new List<Vector3>();
		var c = new List<Vector3>();
		CollectTriangles(root, a, b, c);
		return new MeshTriangleCollider(a.ToArray(), b.ToArray(), c.ToArray());
	}

	public static void EnsureTrimeshCollision(StaticBody3D body, MeshInstance3D meshInstance)
	{
		var mesh = meshInstance.Mesh;
		if (mesh == null) return;

		var shapeNode = body.GetNodeOrNull<CollisionShape3D>("CollisionShape3D");
		if (shapeNode == null)
		{
			shapeNode = new CollisionShape3D { Name = "CollisionShape3D" };
			body.AddChild(shapeNode);
		}

		shapeNode.Shape = mesh.CreateTrimeshShape();
	}

	static void CollectTriangles(Node3D node, List<Vector3> a, List<Vector3> b, List<Vector3> c)
	{
		if (node is MeshInstance3D mi && mi.Mesh != null)
			AppendMesh(mi.GlobalTransform, mi.Mesh, a, b, c);

		foreach (var child in node.GetChildren())
		{
			if (child is Node3D n3) CollectTriangles(n3, a, b, c);
		}
	}

	static void AppendMesh(Transform3D xf, Mesh mesh, List<Vector3> a, List<Vector3> b, List<Vector3> c)
	{
		for (int s = 0; s < mesh.GetSurfaceCount(); s++)
		{
			var st = new SurfaceTool();
			st.CreateFrom(mesh, s);
			var arrays = st.CommitToArrays();
			var verts = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
			if (verts.Length == 0) continue;

			var indexVar = arrays[(int)Mesh.ArrayType.Index];
			if (indexVar.VariantType != Variant.Type.Nil)
			{
				var indices = indexVar.AsInt32Array();
				for (int t = 0; t + 2 < indices.Length; t += 3)
					AddTri(xf, verts, indices[t], indices[t + 1], indices[t + 2], a, b, c);
			}
			else
			{
				for (int t = 0; t + 2 < verts.Length; t += 3)
					AddTri(xf, verts, t, t + 1, t + 2, a, b, c);
			}
		}
	}

	static void AddTri(
		Transform3D xf,
		Vector3[] verts,
		int i0,
		int i1,
		int i2,
		List<Vector3> a,
		List<Vector3> b,
		List<Vector3> c)
	{
		a.Add(xf * verts[i0]);
		b.Add(xf * verts[i1]);
		c.Add(xf * verts[i2]);
	}
}
