using System.Collections.Generic;
using DelaunyFabric.Core;
using Godot;

namespace DelaunyFabric.View;

public readonly record struct InitMarker(Vector2 Uv, Vector3 Xyz);

public static class MeshInitMarkerParser
{
	public static List<InitMarker> Parse(Mesh mesh, ImageGrid initImage, float normalOffset = 0f) =>
		Parse(mesh, Transform3D.Identity, initImage, normalOffset);

	public static List<InitMarker> Parse(
		Mesh mesh,
		Transform3D transform,
		ImageGrid initImage,
		float normalOffset = 0f)
	{
		var anchors = BuildAnchorMap(mesh, transform, normalOffset);
		var markers = new List<InitMarker>();

		for (int y = 0; y < initImage.Height - 1; y++)
		{
			for (int x = 0; x < initImage.Width - 1; x++)
			{
				if (!initImage.TryClaimMarkerBlock(x, y, out var color)) continue;
				initImage.RegisterMarkerBlock(x, y, markers.Count);

				if (!anchors.TryGetValue(color, out var xyz)) continue;
				markers.Add(new InitMarker(ImageUv.BlockCenterToUv(x, y, initImage.Width, initImage.Height), xyz));
			}
		}

		return markers;
	}

	static Dictionary<MarkerColor, Vector3> BuildAnchorMap(
		Mesh mesh,
		Transform3D transform,
		float normalOffset)
	{
		var sum = new Dictionary<MarkerColor, Vector3>();
		var count = new Dictionary<MarkerColor, int>();

		for (int s = 0; s < mesh.GetSurfaceCount(); s++)
			AccumulateSurface(mesh, s, transform, normalOffset, sum, count);

		var anchors = new Dictionary<MarkerColor, Vector3>(sum.Count);
		foreach (var kv in sum)
			anchors[kv.Key] = kv.Value / count[kv.Key];

		return anchors;
	}

	static void AccumulateSurface(
		Mesh mesh,
		int surface,
		Transform3D transform,
		float normalOffset,
		Dictionary<MarkerColor, Vector3> sum,
		Dictionary<MarkerColor, int> count)
	{
		var st = new SurfaceTool();
		st.CreateFrom(mesh, surface);
		var arrays = st.CommitToArrays();

		var verts = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
		var colorVar = arrays[(int)Mesh.ArrayType.Color];
		if (colorVar.VariantType == Variant.Type.Nil || verts.Length == 0) return;

		var colors = colorVar.AsColorArray();
		var normalVar = arrays[(int)Mesh.ArrayType.Normal];
		var normals = normalVar.VariantType != Variant.Type.Nil ? normalVar.AsVector3Array() : null;
		int n = Mathf.Min(verts.Length, colors.Length);

		for (int i = 0; i < n; i++)
		{
			var key = ToMarkerColor(colors[i]);
			var pos = transform * verts[i];

			if (normalOffset != 0f && normals != null && i < normals.Length)
				pos += (transform.Basis * normals[i]).Normalized() * normalOffset;

			Accumulate(key, pos, sum, count);
		}
	}

	static void Accumulate(
		MarkerColor key,
		Vector3 xyz,
		Dictionary<MarkerColor, Vector3> sum,
		Dictionary<MarkerColor, int> count)
	{
		if (!sum.TryGetValue(key, out var acc))
		{
			sum[key] = xyz;
			count[key] = 1;
			return;
		}

		sum[key] = acc + xyz;
		count[key] = count[key] + 1;
	}

	static MarkerColor ToMarkerColor(Color c) =>
		new(
			(byte)Mathf.Clamp(Mathf.RoundToInt(c.R * 255f), 0, 255),
			(byte)Mathf.Clamp(Mathf.RoundToInt(c.G * 255f), 0, 255),
			(byte)Mathf.Clamp(Mathf.RoundToInt(c.B * 255f), 0, 255));

}
