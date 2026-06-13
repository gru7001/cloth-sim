using Godot;

namespace DelaunyFabric.Core;

public static class ImageUv
{
	public static Vector2 ToUv(int imageX, int imageY, int width, int height) =>
		new((float)imageX / width, 1f - (float)imageY / height);

	public static Vector2 BlockCenterToUv(int blockX, int blockY, int width, int height) =>
		new((blockX + 0.5f) / width, 1f - (blockY + 0.5f) / height);

	public static Vector2 ToImage(Vector2 uv, int width, int height) =>
		new(uv.X * width, (1f - uv.Y) * height);
}
