using DelaunyFabric.Core;
using SixLabors.ImageSharp.PixelFormats;

var assets = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "assets"));
byte[] Rgba(SixLabors.ImageSharp.Image<Rgba32> i)
{
	var b = new byte[i.Width * i.Height * 4];
	i.CopyPixelDataTo(b);
	return b;
}

using var img = SixLabors.ImageSharp.Image.Load<Rgba32>(Path.Combine(assets, "pattern.png"));
var p = PatternParser.Parse(Rgba(img), img.Width, img.Height);
Console.WriteLine($"islands={p.Islands.Count}");
foreach (var isl in p.Islands)
	Console.WriteLine($"  corners={isl.Surface.Uv.Count} tris={isl.Surface.Triangles.Length / 3}");
