using System.Collections.Generic;
using DelaunyFabric.View;
using Godot;

namespace DelaunyFabric.Core;

public sealed class PositionedPatternMarker
{
	public Vector2 Uv;
	public Vector3 Xyz;
	public List<PositionedPatternMarker> Connected { get; } = [];
	public List<PositionedPatternMarker> WeldedTo { get; } = [];
}

public static class PatternMarkerPlacement
{
	public static List<PositionedPatternMarker> Place(
		IReadOnlyList<PatternMarker> patternMarkers,
		IReadOnlyList<InitMarker> initMarkers)
	{
		var positioned = new List<PositionedPatternMarker>(patternMarkers.Count);
		var byPattern = new Dictionary<PatternMarker, PositionedPatternMarker>(patternMarkers.Count);
		var islands = BuildIslands(patternMarkers);

		foreach (var marker in patternMarkers)
		{
			var placed = new PositionedPatternMarker
			{
				Uv = marker.Uv,
				Xyz = Sample(ControlsOnIsland(initMarkers, islands[marker]), marker.Uv),
			};

			positioned.Add(placed);
			byPattern[marker] = placed;
		}

		foreach (var marker in patternMarkers)
		{
			var placed = byPattern[marker];
			CopyLinks(marker.Connected, placed.Connected, byPattern);
			CopyLinks(marker.WeldedTo, placed.WeldedTo, byPattern);
		}

		return positioned;
	}

	static List<InitMarker> ControlsOnIsland(IReadOnlyList<InitMarker> initMarkers, IReadOnlyList<Vector2> outline)
	{
		var controls = new List<InitMarker>();
		if (outline.Count < 3) return controls;

		foreach (var marker in initMarkers)
		{
			if (PolygonContains(outline, marker.Uv, includeBoundary: true))
				controls.Add(marker);
		}

		return controls;
	}

	static void CopyLinks(
		IReadOnlyList<PatternMarker> source,
		List<PositionedPatternMarker> target,
		IReadOnlyDictionary<PatternMarker, PositionedPatternMarker> byPattern)
	{
		foreach (var marker in source)
		{
			if (byPattern.TryGetValue(marker, out var positioned) && !target.Contains(positioned))
				target.Add(positioned);
		}
	}

	static Vector3 Sample(IReadOnlyList<InitMarker> initMarkers, Vector2 uv)
	{
		if (initMarkers.Count == 0) return Vector3.Zero;
		if (initMarkers.Count == 1) return initMarkers[0].Xyz;

		var controls = new List<(Vector2 uv, Vector3 value)>(initMarkers.Count);
		foreach (var marker in initMarkers)
			controls.Add((marker.Uv, marker.Xyz));

		return BarycentricUvField.Sample3(controls, uv);
	}

	static Dictionary<PatternMarker, List<Vector2>> BuildIslands(IReadOnlyList<PatternMarker> markers)
	{
		var islands = new Dictionary<PatternMarker, List<Vector2>>(markers.Count);
		var seen = new HashSet<PatternMarker>();

		foreach (var marker in markers)
		{
			if (!seen.Add(marker)) continue;

			var component = CollectComponent(marker, seen);
			var outline = FindOutline(component);
			foreach (var member in component)
				islands[member] = outline;
		}

		return islands;
	}

	static List<PatternMarker> CollectComponent(PatternMarker start, HashSet<PatternMarker> seen)
	{
		var component = new List<PatternMarker>();
		var stack = new Stack<PatternMarker>();
		stack.Push(start);

		while (stack.Count > 0)
		{
			var marker = stack.Pop();
			component.Add(marker);

			foreach (var next in marker.Connected)
			{
				if (seen.Add(next))
					stack.Push(next);
			}
		}

		return component;
	}

	static List<Vector2> FindOutline(IReadOnlyList<PatternMarker> component)
	{
		if (component.Count < 3) return [];

		var local = new Dictionary<PatternMarker, int>(component.Count);
		for (int i = 0; i < component.Count; i++)
			local[component[i]] = i;

		var uv = new Vector2[component.Count];
		var adj = new List<int>[component.Count];
		for (int i = 0; i < component.Count; i++)
		{
			uv[i] = component[i].Uv;
			adj[i] = [];
			foreach (var next in component[i].Connected)
			{
				if (local.TryGetValue(next, out int j))
					adj[i].Add(j);
			}
		}

		var faces = EnumeratePlanarFaces(uv, adj);
		List<int> best = [];
		float bestArea = 0f;
		foreach (var face in faces)
		{
			float area = Mathf.Abs(SignedArea(face, uv));
			if (area <= bestArea) continue;
			bestArea = area;
			best = face;
		}

		var outline = new List<Vector2>(best.Count);
		foreach (int i in best)
			outline.Add(uv[i]);

		return outline;
	}

	static List<List<int>> EnumeratePlanarFaces(Vector2[] uv, List<int>[] adj)
	{
		var sorted = new List<int>[uv.Length];
		for (int i = 0; i < uv.Length; i++)
		{
			sorted[i] = new List<int>(adj[i]);
			sorted[i].Sort((a, b) =>
			{
				float aa = Mathf.Atan2(uv[a].Y - uv[i].Y, uv[a].X - uv[i].X);
				float ab = Mathf.Atan2(uv[b].Y - uv[i].Y, uv[b].X - uv[i].X);
				return aa.CompareTo(ab);
			});
		}

		var used = new HashSet<(int, int)>();
		var faces = new List<List<int>>();
		for (int u = 0; u < uv.Length; u++)
		{
			foreach (int v in sorted[u])
			{
				if (used.Contains((u, v))) continue;
				var face = WalkFace(u, v, sorted, used);
				if (face.Count >= 3)
					faces.Add(face);
			}
		}

		return faces;
	}

	static List<int> WalkFace(int from, int to, List<int>[] sorted, HashSet<(int, int)> used)
	{
		var face = new List<int>();
		int u = from, v = to;
		int guard = 0;

		do
		{
			if (++guard > 4096) return [];

			used.Add((u, v));
			face.Add(u);

			var neighbors = sorted[v];
			if (neighbors.Count < 2) return [];

			int i = neighbors.IndexOf(u);
			if (i < 0) return [];

			int j = (i - 1 + neighbors.Count) % neighbors.Count;
			u = v;
			v = neighbors[j];
		}
		while (u != from || v != to);

		return face;
	}

	static bool PolygonContains(IReadOnlyList<Vector2> poly, Vector2 p, bool includeBoundary)
	{
		if (poly.Count < 3) return false;

		if (includeBoundary)
		{
			for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
			{
				if (PointOnSegment(p, poly[i], poly[j])) return true;
			}
		}

		int wn = 0;
		for (int i = 0; i < poly.Count; i++)
		{
			int j = (i + 1) % poly.Count;
			var pi = poly[i];
			var pj = poly[j];
			if (pi.Y <= p.Y)
			{
				if (pj.Y > p.Y && IsLeft(pi, pj, p) > 0f) wn++;
			}
			else
			{
				if (pj.Y <= p.Y && IsLeft(pi, pj, p) < 0f) wn--;
			}
		}

		return wn != 0;
	}

	static float SignedArea(IReadOnlyList<int> face, Vector2[] uv)
	{
		float area = 0f;
		for (int i = 0; i < face.Count; i++)
		{
			var p = uv[face[i]];
			var q = uv[face[(i + 1) % face.Count]];
			area += p.X * q.Y - q.X * p.Y;
		}

		return area * 0.5f;
	}

	static float IsLeft(Vector2 a, Vector2 b, Vector2 c) =>
		(b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);

	static bool PointOnSegment(Vector2 p, Vector2 a, Vector2 b)
	{
		if (Mathf.Abs(IsLeft(a, b, p)) > 1e-5f) return false;
		float dot = (p.X - a.X) * (b.X - a.X) + (p.Y - a.Y) * (b.Y - a.Y);
		if (dot < -1e-5f) return false;
		float lenSq = (b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y);
		return dot <= lenSq + 1e-5f;
	}
}
