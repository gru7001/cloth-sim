using System;
using DelaunyFabric.Core;
using Godot;

namespace DelaunyFabric.View;

static class ImageGridLoader
{
	public static ImageGrid FromResource(string resPath)
	{
		var texture = GD.Load<Texture2D>(resPath)
			?? throw new InvalidOperationException($"Could not load texture: {resPath}");

		var image = texture.GetImage();
		if (image.IsEmpty())
			throw new InvalidOperationException($"Texture has no image data: {resPath}");

		if (image.GetFormat() != Image.Format.Rgba8)
			image.Convert(Image.Format.Rgba8);

		return new ImageGrid(image.GetData(), image.GetWidth(), image.GetHeight());
	}
}
