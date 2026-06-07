using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace DelaunyFabric.Core;

public static class ImageMarkerRules
{
	static readonly ConditionalWeakTable<ImageGrid, ClaimedMarkerPixels> Claimed = new();

	public static bool TryClaimMarkerBlock(this ImageGrid grid, int x, int y, out MarkerColor color)
	{
		color = default;
		if (x < 0 || y < 0 || x >= grid.Width - 1 || y >= grid.Height - 1) return false;

		color = grid.GetPixel(x, y);
		if (IsBackgroundLike(color)) return false;

		var claimed = Claimed.GetOrCreateValue(grid);
		for (int dy = 0; dy < 2; dy++)
		for (int dx = 0; dx < 2; dx++)
		{
			if (!grid.GetPixel(x + dx, y + dy).Equals(color)) return false;
			if (claimed.IsMarkerPixel(grid, x + dx, y + dy)) return false;
		}

		return true;
	}

	public static void RegisterMarkerBlock(this ImageGrid grid, int x, int y, int markerId)
	{
		var claimed = Claimed.GetOrCreateValue(grid);
		for (int dy = 0; dy < 2; dy++)
		for (int dx = 0; dx < 2; dx++)
			claimed.SetMarkerPixel(grid, x + dx, y + dy, markerId);
	}

	public static bool ConnectsMarkerBlocks(this ImageGrid grid, int bx0, int by0, int bx1, int by1)
	{
		if (bx0 == bx1 && by0 == by1) return false;
		if (!BlockRegistered(grid, bx0, by0) || !BlockRegistered(grid, bx1, by1)) return false;

		foreach (var (px, py) in NeighborsOutsideBlock(bx0, by0))
		{
			if (!grid.Contains(px, py)) continue;
			if (!IsLinePixel(grid, px, py)) continue;

			int key = ColorKey(grid.GetPixel(px, py));
			if (TouchesBlock(px, py, bx1, by1)) return true;
			if (BfsLineToBlock(grid, key, px, py, bx0, by0, bx1, by1)) return true;
		}

		return false;
	}

	static bool BlockRegistered(ImageGrid grid, int bx, int by) =>
		bx >= 0 && by >= 0 && bx < grid.Width - 1 && by < grid.Height - 1 &&
		Claimed.TryGetValue(grid, out var claimed) &&
		claimed.IsMarkerPixel(grid, bx, by);

	static bool IsLinePixel(ImageGrid grid, int x, int y) =>
		grid.Contains(x, y) && !IsBackgroundLike(grid.GetPixel(x, y));

	static bool IsMarkerPixel(ImageGrid grid, int x, int y) =>
		Claimed.TryGetValue(grid, out var claimed) && claimed.IsMarkerPixel(grid, x, y);

	static bool IsBackgroundLike(MarkerColor c) => c.R >= 250 && c.G >= 250 && c.B >= 250;

	static int ColorKey(MarkerColor c) => ((c.R >> 4) << 8) | ((c.G >> 4) << 4) | (c.B >> 4);

	static bool ContainsBlock(int px, int py, int blockX, int blockY) =>
		px >= blockX && px <= blockX + 1 && py >= blockY && py <= blockY + 1;

	static bool TouchesBlock(int px, int py, int bx, int by)
	{
		for (int oy = -1; oy <= 1; oy++)
		for (int ox = -1; ox <= 1; ox++)
		{
			if (ox == 0 && oy == 0) continue;
			if (ContainsBlock(px + ox, py + oy, bx, by)) return true;
		}

		return false;
	}

	static IEnumerable<(int x, int y)> NeighborsOutsideBlock(int bx, int by)
	{
		for (int py = by - 1; py <= by + 2; py++)
		for (int px = bx - 1; px <= bx + 2; px++)
		{
			if (ContainsBlock(px, py, bx, by)) continue;
			yield return (px, py);
		}
	}

	static bool BfsLineToBlock(
		ImageGrid grid,
		int lineColorKey,
		int sx,
		int sy,
		int sourceBx,
		int sourceBy,
		int targetBx,
		int targetBy)
	{
		var q = new Queue<(int x, int y)>();
		var seen = new bool[grid.Width * grid.Height];

		void Enqueue(int x, int y)
		{
			if (!grid.Contains(x, y)) return;
			int i = y * grid.Width + x;
			if (seen[i]) return;
			seen[i] = true;
			q.Enqueue((x, y));
		}

		Enqueue(sx, sy);

		while (q.Count > 0)
		{
			var (x, y) = q.Dequeue();
			if (TouchesBlock(x, y, targetBx, targetBy)) return true;

			for (int oy = -1; oy <= 1; oy++)
			for (int ox = -1; ox <= 1; ox++)
			{
				if (ox == 0 && oy == 0) continue;
				int nx = x + ox, ny = y + oy;
				if (!grid.Contains(nx, ny)) continue;

				if (ContainsBlock(nx, ny, sourceBx, sourceBy) || ContainsBlock(nx, ny, targetBx, targetBy))
				{
					Enqueue(nx, ny);
					continue;
				}

				if (IsMarkerPixel(grid, nx, ny)) continue;
				if (!IsLinePixel(grid, nx, ny)) continue;
				if (ColorKey(grid.GetPixel(nx, ny)) != lineColorKey) continue;

				Enqueue(nx, ny);
			}
		}

		return false;
	}

	sealed class ClaimedMarkerPixels
	{
		int[] _ids = [];

		public bool IsMarkerPixel(ImageGrid grid, int x, int y)
		{
			EnsureSize(grid);
			return grid.Contains(x, y) && _ids[y * grid.Width + x] >= 0;
		}

		public void SetMarkerPixel(ImageGrid grid, int x, int y, int markerId)
		{
			EnsureSize(grid);
			if (grid.Contains(x, y))
				_ids[y * grid.Width + x] = markerId;
		}

		void EnsureSize(ImageGrid grid)
		{
			int size = grid.Width * grid.Height;
			if (_ids.Length == size) return;

			_ids = new int[size];
			Array.Fill(_ids, -1);
		}
	}
}
