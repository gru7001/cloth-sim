using System;

namespace DelaunyFabric.Core;

public sealed class ImageGrid
{
	public int Width { get; }
	public int Height { get; }

	readonly MarkerColor[] _pixels;

	public ImageGrid(ReadOnlySpan<byte> rgba, int width, int height)
	{
		if (width < 1 || height < 1)
			throw new ArgumentException("Image must be at least 1x1 pixels.");

		Width = width;
		Height = height;
		_pixels = new MarkerColor[width * height];

		for (int i = 0; i < _pixels.Length; i++)
		{
			int o = i * 4;
			_pixels[i] = new MarkerColor(rgba[o], rgba[o + 1], rgba[o + 2]);
		}
	}

	public bool Contains(int x, int y) =>
		x >= 0 && y >= 0 && x < Width && y < Height;

	public MarkerColor GetPixel(int x, int y) => _pixels[y * Width + x];
}
