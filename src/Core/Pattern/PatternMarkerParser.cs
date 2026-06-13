using System.Collections.Generic;
using Godot;

namespace DelaunyFabric.Core;

public sealed class PatternMarker
{
	public Vector2 Uv;
	public List<PatternMarker> Connected { get; } = [];
	public List<PatternMarker> WeldedTo { get; } = [];
}

public static class PatternMarkerParser
{
	public static List<PatternMarker> Parse(ImageGrid grid)
	{
		var records = CollectMarkers(grid);

		ConnectMarkers(grid, records);
		WeldMarkers(records);

		var markers = new List<PatternMarker>(records.Count);
		foreach (var record in records)
			markers.Add(record.Marker);

		return markers;
	}

	static List<ParsedMarker> CollectMarkers(ImageGrid pattern)
	{
		var records = new List<ParsedMarker>();

		for (int y = 0; y < pattern.Height - 1; y++)
		{
			for (int x = 0; x < pattern.Width - 1; x++)
			{
				if (!pattern.TryClaimMarkerBlock(x, y, out var color)) continue;

				pattern.RegisterMarkerBlock(x, y, records.Count);
				records.Add(new ParsedMarker(
					x,
					y,
					color,
					new PatternMarker { Uv = ImageUv.BlockCenterToUv(x, y, pattern.Width, pattern.Height) }));
			}
		}

		return records;
	}

	static void ConnectMarkers(ImageGrid grid, List<ParsedMarker> records)
	{
		for (int i = 0; i < records.Count; i++)
		{
			for (int j = i + 1; j < records.Count; j++)
			{
				if (!grid.ConnectsMarkerBlocks(
						records[i].BlockX,
						records[i].BlockY,
						records[j].BlockX,
						records[j].BlockY))
					continue;

				Link(records[i].Marker.Connected, records[j].Marker);
				Link(records[j].Marker.Connected, records[i].Marker);
			}
		}
	}

	static void WeldMarkers(List<ParsedMarker> records)
	{
		var byColor = new Dictionary<MarkerColor, List<PatternMarker>>();

		foreach (var record in records)
		{
			if (record.Color.IsBlack) continue;

			if (!byColor.TryGetValue(record.Color, out var group))
				byColor[record.Color] = group = [];

			group.Add(record.Marker);
		}

		foreach (var group in byColor.Values)
		{
			for (int i = 0; i < group.Count; i++)
			for (int j = 0; j < group.Count; j++)
			{
				if (i == j) continue;
				Link(group[i].WeldedTo, group[j]);
			}
		}
	}

	static void Link(List<PatternMarker> markers, PatternMarker marker)
	{
		if (!markers.Contains(marker))
			markers.Add(marker);
	}

	readonly record struct ParsedMarker(int BlockX, int BlockY, MarkerColor Color, PatternMarker Marker);
}
