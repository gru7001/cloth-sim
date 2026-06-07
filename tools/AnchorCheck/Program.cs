using System.Numerics;
using DelaunyFabric.Core;
using SixLabors.ImageSharp.PixelFormats;
using SharpGLTF.Schema2;

var assets = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "assets"));

static byte[] Rgba(SixLabors.ImageSharp.Image<Rgba32> img)
{
	var buf = new byte[img.Width * img.Height * 4];
	img.CopyPixelDataTo(buf);
	return buf;
}

using var patternImg = SixLabors.ImageSharp.Image.Load<Rgba32>(Path.Combine(assets, "pattern.png"));
using var initImg = SixLabors.ImageSharp.Image.Load<Rgba32>(Path.Combine(assets, "init.png"));

var pattern = PatternParser.Parse(Rgba(patternImg), patternImg.Width, patternImg.Height);
var init = InitParseResult.Parse(Rgba(initImg), initImg.Width, initImg.Height);

var bodyColors = LoadBodyColorMap(Path.Combine(assets, "fembody.glb"));

Console.WriteLine($"Islands: {pattern.Islands.Count}");
Console.WriteLine($"Init marks: {init.Marks.Count}");
Console.WriteLine($"Body colors in mesh: {bodyColors.Count}");
Console.WriteLine();

// 1) Init marks missing body vertex color
int missingBody = 0;
foreach (var mark in init.Marks)
{
	if (bodyColors.ContainsKey(mark.Color)) continue;
	missingBody++;
	Console.WriteLine(
		$"  NO BODY: init mark rgb=({mark.Color.R},{mark.Color.G},{mark.Color.B}) uv=({mark.Uv.X:F4},{mark.Uv.Y:F4})");
}

if (missingBody == 0)
	Console.WriteLine("All init mark colors exist on mannequin mesh.");
else
	Console.WriteLine($"Missing body color: {missingBody} / {init.Marks.Count} init marks");
Console.WriteLine();

// 2) Init marks on neither island
int orphan = 0;
foreach (var mark in init.Marks)
{
	int onIsland = -1;
	for (int ii = 0; ii < pattern.Islands.Count; ii++)
	{
		if (!PatternGeometry.MarkOnIsland(mark.Uv, init.Width, init.Height, pattern.Islands[ii].OutlineUv)) continue;
		onIsland = ii;
		break;
	}

	if (onIsland >= 0) continue;
	orphan++;
	string bodyNote = bodyColors.ContainsKey(mark.Color) ? "has body" : "no body";
	Console.WriteLine(
		$"  ORPHAN: rgb=({mark.Color.R},{mark.Color.G},{mark.Color.B}) uv=({mark.Uv.X:F4},{mark.Uv.Y:F4}) {bodyNote}");
}

if (orphan == 0)
	Console.WriteLine("Every init mark lies inside at least one island outline.");
else
	Console.WriteLine($"Orphan init marks: {orphan} / {init.Marks.Count}");
Console.WriteLine();

// Per-island summary
for (int ii = 0; ii < pattern.Islands.Count; ii++)
{
	var island = pattern.Islands[ii];
	int inside = 0, withBody = 0;
	foreach (var mark in init.Marks)
	{
		if (!PatternGeometry.MarkOnIsland(mark.Uv, init.Width, init.Height, island.OutlineUv)) continue;
		inside++;
		if (bodyColors.ContainsKey(mark.Color)) withBody++;
	}

	Console.WriteLine($"Island {ii}: init marks inside outline={inside}, with body color={withBody}");
	var outline = island.OutlineUv;
	float minX = outline.Min(v => v.X), maxX = outline.Max(v => v.X);
	float minY = outline.Min(v => v.Y), maxY = outline.Max(v => v.Y);
	Console.WriteLine($"         outline UV bounds X=[{minX:F4},{maxX:F4}] Y=[{minY:F4},{maxY:F4}]");
}

Console.WriteLine();
Console.WriteLine("Mark UV = center of TOP-LEFT pixel of 2x2 (see BlockToUv), not geometric block center.");
Console.WriteLine("Block corners use ±0.5px and +1.5px from that point in UV.");
foreach (var mark in init.Marks)
{
	if (mark.Color.R != 0 || mark.Color.G != 255 || mark.Color.B != 0) continue;
	var c = mark.Uv;
	float du = 1f / init.Width, dv = 1f / init.Height;
	var corners = new[]
	{
		c + new Vector2(-0.5f * du, -0.5f * dv),
		c + new Vector2(1.5f * du, -0.5f * dv),
		c + new Vector2(1.5f * du, 1.5f * dv),
		c + new Vector2(-0.5f * du, 1.5f * dv),
	};
	bool center0 = PointInPolygon(c, pattern.Islands[0].OutlineUv);
	bool center1 = PointInPolygon(c, pattern.Islands[1].OutlineUv);
	int cornersIn0 = corners.Count(p => PointInPolygon(p, pattern.Islands[0].OutlineUv));
	int cornersIn1 = corners.Count(p => PointInPolygon(p, pattern.Islands[1].OutlineUv));
	Console.WriteLine(
		$"green center ({c.X:F4},{c.Y:F4}) centerIn0={center0} centerIn1={center1} cornersIn0={cornersIn0}/4 cornersIn1={cornersIn1}/4");
}

static Dictionary<InitColor, Vector3> LoadBodyColorMap(string glbPath)
{
	var sum = new Dictionary<InitColor, Vector3>();
	var count = new Dictionary<InitColor, int>();

	var model = ModelRoot.Load(glbPath);
	foreach (var mesh in model.LogicalMeshes)
	{
		var prim = mesh.Primitives[0];
		var pos = prim.GetVertexAccessor("POSITION")?.AsVector3Array();
		var col = prim.GetVertexAccessor("COLOR_0")?.AsVector4Array();
		if (pos == null || col == null) continue;

		int n = Math.Min(pos.Count, col.Count);
		for (int i = 0; i < n; i++)
		{
			var key = ToColor(col[i]);
			var p = new Vector3(pos[i].X, pos[i].Y, pos[i].Z);
			if (!sum.TryGetValue(key, out var acc))
			{
				sum[key] = p;
				count[key] = 1;
				continue;
			}

			sum[key] = acc + p;
			count[key]++;
		}
	}

	var map = new Dictionary<InitColor, Vector3>();
	foreach (var (key, total) in sum)
		map[key] = total / count[key];
	return map;
}

static InitColor ToColor(Vector4 c) =>
	new(
		(byte)Math.Clamp((int)MathF.Round(c.X * 255f), 0, 255),
		(byte)Math.Clamp((int)MathF.Round(c.Y * 255f), 0, 255),
		(byte)Math.Clamp((int)MathF.Round(c.Z * 255f), 0, 255));

static bool PointInPolygon(Vector2 p, IReadOnlyList<Vector2> poly)
{
	bool inside = false;
	int n = poly.Count;
	for (int i = 0, j = n - 1; i < n; j = i++)
	{
		var pi = poly[i];
		var pj = poly[j];
		if ((pi.Y > p.Y) == (pj.Y > p.Y)) continue;
		float x = (pj.X - pi.X) * (p.Y - pi.Y) / (pj.Y - pi.Y) + pi.X;
		if (p.X < x) inside = !inside;
	}

	return inside;
}
