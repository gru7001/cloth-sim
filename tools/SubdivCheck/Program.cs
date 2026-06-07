using System;
using System.Collections.Generic;
using System.Numerics;
using DelaunyFabric.Core;

var vertices = new List<PatternVertex>
{
	new() { Uv = new Vector2(0, 0), Color = new PatternColor(1, 0, 0), SimId = 0 },
	new() { Uv = new Vector2(1, 0), Color = new PatternColor(1, 0, 0), SimId = 1 },
	new() { Uv = new Vector2(1, 1), Color = new PatternColor(1, 0, 0), SimId = 2 },
	new() { Uv = new Vector2(0, 1), Color = new PatternColor(1, 0, 0), SimId = 3 },
};

var adj = new Dictionary<int, List<int>>
{
	[0] = new List<int> { 1, 3 },
	[1] = new List<int> { 0, 2 },
	[2] = new List<int> { 1, 3 },
	[3] = new List<int> { 2, 0 },
};

int nextSim = 4;
var components = new List<List<int>> { new List<int> { 0, 1, 2, 3 } };

Console.WriteLine($"MaxEdge={PatternGraphSubdivision.MaxEdgeLength:F4}");
Dump("before", vertices, adj);

PatternGraphSubdivision.RefineLongEdges(vertices, adj, components, ref nextSim);

Dump("after", vertices, adj);

if (adj.TryGetValue(1, out var n1))
	Console.WriteLine($"vert1 neighbors: {string.Join(",", n1)}");
if (adj.TryGetValue(2, out var n2))
	Console.WriteLine($"vert2 neighbors: {string.Join(",", n2)}");

static void Dump(string label, List<PatternVertex> verts, Dictionary<int, List<int>> adj)
{
	int edgeCount = 0;
	float maxLen = 0f;
	for (int a = 0; a < verts.Count; a++)
	{
		if (!adj.TryGetValue(a, out var nbs)) continue;
		foreach (int b in nbs)
		{
			if (a >= b) continue;
			edgeCount++;
			float len = (verts[a].Uv - verts[b].Uv).Length();
			maxLen = MathF.Max(maxLen, len);
			if (len > PatternGraphSubdivision.MaxEdgeLength + 1e-6f)
				Console.WriteLine($"  LONG {a}-{b} len={len:F4}");
		}
	}

	Console.WriteLine($"{label}: verts={verts.Count} edges={edgeCount} maxLen={maxLen:F4}");
}
