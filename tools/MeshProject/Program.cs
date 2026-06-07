using System.Numerics;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SharpGLTF.Schema2;

// Orthographic projection of body mesh vertex colors.
// Keeps vertices whose normal·direction > 0, projects onto the plane perpendicular to direction.
// Image vertical axis aligns with world +Y when possible (default view direction +Z).

var opts = ParseArgs(args);
if (!File.Exists(opts.MeshPath))
{
	Console.Error.WriteLine($"Mesh not found: {opts.MeshPath}");
	return 1;
}

var samples = LoadMesh(opts.MeshPath);
Console.WriteLine($"Mesh vertices: {samples.Count}");

var basis = ProjectionBasis.FromDirection(opts.Direction);
Console.WriteLine($"Direction: {opts.Direction}");
Console.WriteLine($"Image horizontal (U): {basis.U}, vertical (V): {basis.V}, depth axis: {basis.D}");

var kept = new List<Sample>();
	foreach (var s in samples)
{
	if (IsBlack(s.Rgba)) continue;
	if (Vector3.Dot(s.Normal, basis.D) <= 0f) continue;
	float u = Vector3.Dot(s.Position, basis.U);
	float v = Vector3.Dot(s.Position, basis.V);
	float depth = Vector3.Dot(s.Position, basis.D);
	kept.Add(s with { U = u, V = v, Depth = depth });
}

Console.WriteLine($"Facing direction: {kept.Count}");

if (kept.Count == 0)
{
	Console.Error.WriteLine("No vertices passed the normal test.");
	return 1;
}

float minU = kept.Min(s => s.U), maxU = kept.Max(s => s.U);
float minV = kept.Min(s => s.V), maxV = kept.Max(s => s.V);
float padU = (maxU - minU) * opts.Padding;
float padV = (maxV - minV) * opts.Padding;
minU -= padU;
maxU += padU;
minV -= padV;
maxV += padV;

using var image = new Image<Rgba32>(opts.Width, opts.Height);
image.ProcessPixelRows(accessor =>
{
	for (int y = 0; y < accessor.Height; y++)
		accessor.GetRowSpan(y).Fill(new Rgba32(255, 255, 255, 255));
});

// Farther depth first so nearer vertices paint on top.
kept.Sort((a, b) => a.Depth.CompareTo(b.Depth));

int painted = 0;
foreach (var s in kept)
{
	int px = WorldToPixel(s.U, minU, maxU, opts.Width);
	int py = WorldToPixel(s.V, minV, maxV, opts.Height);
	py = opts.Height - 1 - py; // V up on image
	if ((uint)px >= (uint)opts.Width || (uint)py >= (uint)opts.Height) continue;
	image[px, py] = s.Rgba;
	painted++;
}

Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(opts.OutputPath))!);
await image.SaveAsPngAsync(opts.OutputPath);
Console.WriteLine($"Wrote {opts.OutputPath} ({opts.Width}x{opts.Height}, {painted} splats)");
return 0;

static bool IsBlack(Rgba32 c) => c.R == 0 && c.G == 0 && c.B == 0;

static int WorldToPixel(float w, float min, float max, int size)
{
	float t = max > min ? (w - min) / (max - min) : 0.5f;
	return (int)MathF.Round(t * (size - 1));
}

static Options ParseArgs(string[] args)
{
	string mesh = Path.GetFullPath(Path.Combine("assets", "fembody.glb"));
	string output = Path.GetFullPath(Path.Combine("assets", "body_projected.png"));
	Vector3 dir = Vector3.UnitZ;
	int size = 256;
	float padding = 0.02f;

	for (int i = 0; i < args.Length; i++)
	{
		string a = args[i];
		if (a == "--mesh" && i + 1 < args.Length) mesh = Path.GetFullPath(args[++i]);
		else if (a == "--out" && i + 1 < args.Length) output = Path.GetFullPath(args[++i]);
		else if (a == "--size" && i + 1 < args.Length) size = int.Parse(args[++i]);
		else if (a == "--padding" && i + 1 < args.Length) padding = float.Parse(args[++i]);
		else if (a == "--dir" && i + 1 < args.Length)
		{
			var p = args[++i].Split(',', StringSplitOptions.TrimEntries);
			dir = new Vector3(float.Parse(p[0]), float.Parse(p[1]), float.Parse(p[2]));
		}
		else if (a is "--help" or "-h")
		{
			PrintHelp();
			Environment.Exit(0);
		}
	}

	if (dir.LengthSquared() < 1e-12f) dir = Vector3.UnitZ;
	else dir = Vector3.Normalize(dir);

	return new Options(mesh, output, dir, size, padding);
}

static void PrintHelp()
{
	Console.WriteLine("""
		MeshProject — project facing mesh vertex colors to a 2D image.

		Usage:
		  dotnet run --project tools/MeshProject -- [options]

		Options:
		  --mesh <path>     GLB mesh (default: assets/fembody.glb)
		  --out <path>      Output PNG (default: assets/body_projected.png)
		  --dir <x,y,z>     Cull + projection axis (default: 0,0,1 = view +Z, Y up on image)
		  --size <n>        Image width/height (default: 256)
		  --padding <f>     Fractional bounds padding (default: 0.02)

		Vertices are kept when normal·direction > 0.
		2D position uses axes perpendicular to direction; world +Y is image up when possible.
		""");
}

static List<Sample> LoadMesh(string path)
{
	var list = new List<Sample>();
	var model = ModelRoot.Load(path);
	foreach (var mesh in model.LogicalMeshes)
	{
		foreach (var prim in mesh.Primitives)
		{
			var pos = prim.GetVertexAccessor("POSITION")?.AsVector3Array();
			var nrm = prim.GetVertexAccessor("NORMAL")?.AsVector3Array();
			var col = prim.GetVertexAccessor("COLOR_0")?.AsVector4Array();
			if (pos == null) continue;

			int n = pos.Count;
			for (int i = 0; i < n; i++)
			{
				var p = new Vector3(pos[i].X, pos[i].Y, pos[i].Z);
				var normal = nrm != null && i < nrm.Count
					? Vector3.Normalize(new Vector3(nrm[i].X, nrm[i].Y, nrm[i].Z))
					: Vector3.UnitZ;
				Rgba32 rgba = new(255, 255, 255, 255);
				if (col != null && i < col.Count)
				{
					var c = col[i];
					rgba = new Rgba32(
						(byte)Math.Clamp((int)MathF.Round(c.X * 255f), 0, 255),
						(byte)Math.Clamp((int)MathF.Round(c.Y * 255f), 0, 255),
						(byte)Math.Clamp((int)MathF.Round(c.Z * 255f), 0, 255),
						(byte)Math.Clamp((int)MathF.Round(c.W * 255f), 0, 255));
				}

				list.Add(new Sample(p, normal, rgba, 0, 0, 0));
			}
		}
	}

	return list;
}

readonly record struct Options(string MeshPath, string OutputPath, Vector3 Direction, int Width, int Height, float Padding)
{
	public Options(string mesh, string output, Vector3 direction, int size, float padding)
		: this(mesh, output, direction, size, size, padding) { }
}

readonly record struct Sample(Vector3 Position, Vector3 Normal, Rgba32 Rgba, float U, float V, float Depth);

readonly struct ProjectionBasis
{
	public Vector3 D { get; }
	public Vector3 U { get; }
	public Vector3 V { get; }

	ProjectionBasis(Vector3 d, Vector3 u, Vector3 v)
	{
		D = d;
		U = u;
		V = v;
	}

	public static ProjectionBasis FromDirection(Vector3 direction)
	{
		Vector3 d = Vector3.Normalize(direction);
		// Prefer world +Y as image vertical unless direction is parallel to Y.
		Vector3 worldUp = MathF.Abs(Vector3.Dot(d, Vector3.UnitY)) > 0.99f
			? Vector3.UnitZ
			: Vector3.UnitY;
		Vector3 u = Vector3.Normalize(Vector3.Cross(worldUp, d));
		Vector3 v = Vector3.Cross(d, u);
		return new ProjectionBasis(d, u, v);
	}
}
