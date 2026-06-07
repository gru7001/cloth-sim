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

using var pi = SixLabors.ImageSharp.Image.Load<Rgba32>(Path.Combine(assets, "pattern.png"));
using var ii = SixLabors.ImageSharp.Image.Load<Rgba32>(Path.Combine(assets, "init.png"));
var pattern = PatternParser.Parse(Rgba(pi), pi.Width, pi.Height);
var init = InitParseResult.Parse(Rgba(ii), ii.Width, ii.Height);
var bodyColors = LoadBody(Path.Combine(assets, "fembody.glb"));
InitPlacement.Apply(pattern, init, bodyColors);
var collider = LoadCollider(Path.Combine(assets, "fembody.glb"));

const float skin = 0.01f;
int inside = 0, near = 0, outside = 0;
float min = float.MaxValue, max = float.MinValue;
for (int sim = 0; sim < pattern.SimCount; sim++)
{
	var p = pattern.SimXyz[sim];
	collider.Sample(p, out float d, out _);
	min = MathF.Min(min, d);
	max = MathF.Max(max, d);
	if (d < -1e-4f) inside++;
	else if (d < skin) near++;
	else outside++;
}

Console.WriteLine($"tris={collider.TriangleCount} sim={pattern.SimCount}");
Console.WriteLine($"signed dist range [{min:F4}, {max:F4}]");
Console.WriteLine($"inside(neg)={inside} dist<{skin}={near} far outside={outside}");
Console.WriteLine("If inside=0 and near=0, EnforceSkin never moves verts at TimeScale=0.");

static MeshTriangleCollider LoadCollider(string path)
{
	var a = new List<Vector3>(); var b = new List<Vector3>(); var c = new List<Vector3>();
	var model = ModelRoot.Load(path);
	foreach (var mesh in model.LogicalMeshes)
	{
		var prim = mesh.Primitives[0];
		var pos = prim.GetVertexAccessor("POSITION")!.AsVector3Array();
		var idx = prim.GetIndexAccessor()?.AsIndicesArray().ToArray();
		if (idx != null)
		{
			for (int t = 0; t + 2 < idx.Length; t += 3)
			{
				a.Add(pos[(int)idx[t]]); b.Add(pos[(int)idx[t + 1]]); c.Add(pos[(int)idx[t + 2]]);
			}
		}
		else
		{
			for (int t = 0; t + 2 < pos.Count; t += 3)
			{
				a.Add(pos[t]); b.Add(pos[t + 1]); c.Add(pos[t + 2]);
			}
		}
	}
	return new MeshTriangleCollider(a.ToArray(), b.ToArray(), c.ToArray());
}

static Dictionary<InitColor, Vector3> LoadBody(string path)
{
	var sum = new Dictionary<InitColor, Vector3>();
	var cnt = new Dictionary<InitColor, int>();
	var model = ModelRoot.Load(path);
	foreach (var mesh in model.LogicalMeshes)
	{
		var prim = mesh.Primitives[0];
		var pos = prim.GetVertexAccessor("POSITION")!.AsVector3Array();
		var col = prim.GetVertexAccessor("COLOR_0")!.AsVector4Array();
		for (int i = 0; i < Math.Min(pos.Count, col.Count); i++)
		{
			var k = new InitColor(
				(byte)Math.Clamp((int)MathF.Round(col[i].X * 255f), 0, 255),
				(byte)Math.Clamp((int)MathF.Round(col[i].Y * 255f), 0, 255),
				(byte)Math.Clamp((int)MathF.Round(col[i].Z * 255f), 0, 255));
			if (!sum.TryGetValue(k, out var acc)) { sum[k] = pos[i]; cnt[k] = 1; continue; }
			sum[k] = acc + pos[i]; cnt[k]++;
		}
	}
	return sum.ToDictionary(kv => kv.Key, kv => kv.Value / cnt[kv.Key]);
}
