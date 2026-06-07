using System;
using System.Collections.Generic;
using Godot;

namespace DelaunyFabric.Core;

/// <summary>Piecewise-linear field over UV: Delaunay on control UVs, barycentric sample of values.</summary>
public static class BarycentricUvField
{
	public static Vector3 Sample3(IReadOnlyList<(Vector2 uv, Vector3 value)> controls, Vector2 u)
	{
		int n = controls.Count;
		if (n == 0) return Vector3.Zero;
		if (n == 1) return controls[0].value;

		var uv = new Vector2[n];
		var values = new Vector3[n];
		for (int i = 0; i < n; i++)
		{
			uv[i] = controls[i].uv;
			values[i] = controls[i].value;
		}

		if (n == 2)
		{
			Vector2 ab = uv[1] - uv[0];
			float lenSq = ab.LengthSquared();
			float t = lenSq < 1e-12f ? 0.5f : (u - uv[0]).Dot(ab) / lenSq;
			return values[0] + (values[1] - values[0]) * t;
		}

		int[] tris = Delaunay2D.Triangulate(uv);
		if (tris.Length == 0) return values[0];

		if (TryLocate(uv, tris, u, out Vector3 w, out int i0, out int i1, out int i2))
			return w.X * values[i0] + w.Y * values[i1] + w.Z * values[i2];

		return values[0];
	}

	static bool TryLocate(
		Vector2[] uv,
		int[] tris,
		Vector2 u,
		out Vector3 w,
		out int i0,
		out int i1,
		out int i2)
	{
		w = Vector3.Zero;
		i0 = i1 = i2 = 0;
		int bestTri = 0;
		float bestDistSq = float.MaxValue;
		Vector3 bestW = new(1f, 0f, 0f);

		for (int t = 0; t + 2 < tris.Length; t += 3)
		{
			int a = tris[t], b = tris[t + 1], c = tris[t + 2];
			var ta = uv[a];
			var tb = uv[b];
			var tc = uv[c];

			if (TryBarycentric(u, ta, tb, tc, out Vector3 bw)
				&& bw.X >= -1e-4f && bw.Y >= -1e-4f && bw.Z >= -1e-4f)
			{
				w = bw;
				i0 = a;
				i1 = b;
				i2 = c;
				return true;
			}

			Vector2 cp = ClosestPointOnTriangle(u, ta, tb, tc);
			float dSq = (u - cp).LengthSquared();
			if (dSq >= bestDistSq) continue;
			if (!TryBarycentric(cp, ta, tb, tc, out bw)) continue;
			bestDistSq = dSq;
			bestTri = t;
			bestW = bw;
		}

		i0 = tris[bestTri];
		i1 = tris[bestTri + 1];
		i2 = tris[bestTri + 2];
		w = bestW;
		return true;
	}

	static bool TryBarycentric(Vector2 p, Vector2 a, Vector2 b, Vector2 c, out Vector3 w)
	{
		float denom = (b.Y - c.Y) * (a.X - c.X) + (c.X - b.X) * (a.Y - c.Y);
		if (MathF.Abs(denom) < 1e-8f)
		{
			w = Vector3.Zero;
			return false;
		}

		float inv = 1f / denom;
		w.X = ((b.Y - c.Y) * (p.X - c.X) + (c.X - b.X) * (p.Y - c.Y)) * inv;
		w.Y = ((c.Y - a.Y) * (p.X - c.X) + (a.X - c.X) * (p.Y - c.Y)) * inv;
		w.Z = 1f - w.X - w.Y;
		return true;
	}

	static Vector2 ClosestPointOnTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
	{
		Vector2 ab = b - a, ac = c - a, ap = p - a;
		float d1 = ab.Dot(ap);
		float d2 = ac.Dot(ap);
		if (d1 <= 0f && d2 <= 0f) return a;

		Vector2 bp = p - b;
		float d3 = ab.Dot(bp);
		float d4 = ac.Dot(bp);
		if (d3 >= 0f && d4 <= d3) return b;

		float vc = d1 * d4 - d3 * d2;
		if (vc <= 0f && d1 >= 0f && d3 <= 0f)
			return a + ab * (d1 / (d1 - d3));

		Vector2 cp = p - c;
		float d5 = ab.Dot(cp);
		float d6 = ac.Dot(cp);
		if (d6 >= 0f && d5 <= d6) return c;

		float vb = d5 * d2 - d1 * d6;
		if (vb <= 0f && d2 >= 0f && d6 <= 0f)
			return a + ac * (d2 / (d2 - d6));

		float va = d3 * d6 - d5 * d4;
		if (va <= 0f && d4 - d3 >= 0f && d5 - d6 >= 0f)
			return b + (c - b) * ((d4 - d3) / ((d4 - d3) + (d5 - d6)));

		float denom = 1f / (va + vb + vc);
		return a + ab * (vb * denom) + ac * (vc * denom);
	}
}
