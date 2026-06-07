using System;
using System.Collections.Generic;
using Godot;

namespace DelaunyFabric.Core;

/// <summary>Bowyer–Watson Delaunay triangulation in 2D. Returns triangle indices (3 per tri).</summary>
public static class Delaunay2D
{
	public static int[] Triangulate(IReadOnlyList<Vector2> points)
	{
		int n = points.Count;
		if (n < 3) return [];

		var pts = new List<Vector2>(points);
		float minX = pts[0].X, minY = pts[0].Y, maxX = minX, maxY = minY;
		for (int i = 1; i < n; i++)
		{
			minX = MathF.Min(minX, pts[i].X);
			minY = MathF.Min(minY, pts[i].Y);
			maxX = MathF.Max(maxX, pts[i].X);
			maxY = MathF.Max(maxY, pts[i].Y);
		}

		float dx = maxX - minX;
		float dy = maxY - minY;
		float dmax = MathF.Max(MathF.Max(dx, dy), 1e-6f);
		float midX = (minX + maxX) * 0.5f;
		float midY = (minY + maxY) * 0.5f;

		int i0 = n;
		pts.Add(new Vector2(midX - 20f * dmax, midY - dmax));
		int i1 = n + 1;
		pts.Add(new Vector2(midX, midY + 20f * dmax));
		int i2 = n + 2;
		pts.Add(new Vector2(midX + 20f * dmax, midY - dmax));

		// Super-triangle must be CCW for the circumcircle test below.
		var tris = new List<(int a, int b, int c)> { (i0, i2, i1) };

		for (int pi = 0; pi < n; pi++)
		{
			Vector2 p = pts[pi];
			var bad = new List<int>();
			for (int t = 0; t < tris.Count; t++)
			{
				var (a, b, c) = tris[t];
				if (InCircumcircle(p, pts[a], pts[b], pts[c]))
					bad.Add(t);
			}

			var edges = new List<(int, int)>();
			for (int bi = 0; bi < bad.Count; bi++)
			{
				var (a, b, c) = tris[bad[bi]];
				AddEdge(edges, a, b);
				AddEdge(edges, b, c);
				AddEdge(edges, c, a);
			}

			bad.Sort((a, b) => b.CompareTo(a));
			for (int t = 0; t < bad.Count; t++)
				tris.RemoveAt(bad[t]);

			foreach (var (a, b) in edges)
				tris.Add((a, b, pi));
		}

		tris.RemoveAll(t => t.a >= n || t.b >= n || t.c >= n);

		var flat = new int[tris.Count * 3];
		for (int i = 0; i < tris.Count; i++)
		{
			flat[i * 3] = tris[i].a;
			flat[i * 3 + 1] = tris[i].b;
			flat[i * 3 + 2] = tris[i].c;
		}

		return flat;
	}

	static void AddEdge(List<(int, int)> edges, int a, int b)
	{
		if (a > b) (a, b) = (b, a);
		for (int i = 0; i < edges.Count; i++)
		{
			var (ea, eb) = edges[i];
			if (ea == a && eb == b)
			{
				edges.RemoveAt(i);
				return;
			}
		}

		edges.Add((a, b));
	}

	static float Orient(Vector2 a, Vector2 b, Vector2 c) =>
		(b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);

	static bool InCircumcircle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
	{
		float orient = Orient(a, b, c);
		if (MathF.Abs(orient) < 1e-12f) return false;

		float ax = a.X - p.X, ay = a.Y - p.Y;
		float bx = b.X - p.X, by = b.Y - p.Y;
		float cx = c.X - p.X, cy = c.Y - p.Y;
		float det = (ax * ax + ay * ay) * (bx * cy - cx * by)
			- (bx * bx + by * by) * (ax * cy - cx * ay)
			+ (cx * cx + cy * cy) * (ax * by - bx * ay);

		return orient > 0f ? det > 0f : det < 0f;
	}
}
