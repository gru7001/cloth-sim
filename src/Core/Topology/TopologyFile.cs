using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Godot;

namespace DelaunyFabric.Core;

public static class TopologyFile
{
	const string Header = "DelaunyFabric.Topology 1";

	public static void Save(Topology topology, string path)
	{
		var vertexIndex = new Dictionary<Vertex, int>(topology.Vertices.Count);
		for (int i = 0; i < topology.Vertices.Count; i++)
			vertexIndex[topology.Vertices[i]] = i;

		var cornerIndex = new Dictionary<Corner, int>(topology.Corners.Count);
		for (int i = 0; i < topology.Corners.Count; i++)
			cornerIndex[topology.Corners[i]] = i;

		using var writer = new StreamWriter(path);
		writer.WriteLine(Header);
		writer.WriteLine($"vertices {topology.Vertices.Count}");
		writer.WriteLine($"corners {topology.Corners.Count}");

		foreach (var vertex in topology.Vertices)
		{
			writer.Write("v");
			writer.Write($" {vertexIndex[vertex]}");
			writer.Write($" {F(vertex.Xyz.X)} {F(vertex.Xyz.Y)} {F(vertex.Xyz.Z)}");
			writer.Write(vertex.FromSubdivision ? " 1" : " 0");

			if (vertex.ContactNormal is { } normal)
				writer.Write($" {F(normal.X)} {F(normal.Y)} {F(normal.Z)}");

			writer.WriteLine();
		}

		foreach (var corner in topology.Corners)
		{
			writer.Write("c");
			writer.Write($" {cornerIndex[corner]}");
			writer.Write($" {vertexIndex[corner.Vertex]}");
			writer.Write($" {F(corner.Uv.X)} {F(corner.Uv.Y)}");
			writer.Write($" {cornerIndex[corner.Next]}");
			writer.Write($" {cornerIndex[corner.Prev]}");
			writer.Write($" {IndexOrNull(corner.Across, cornerIndex)}");
			writer.WriteLine();
		}
	}

	public static Topology Load(string path)
	{
		var lines = File.ReadAllLines(path);
		if (lines.Length == 0 || lines[0].Trim() != Header)
			throw new InvalidDataException($"Expected header '{Header}'.");

		int line = 1;
		int vertexCount = ReadCount(lines, ref line, "vertices");
		int cornerCount = ReadCount(lines, ref line, "corners");

		var topology = new Topology();
		var vertices = new Vertex[vertexCount];
		var corners = new Corner[cornerCount];

		for (int i = 0; i < vertexCount; i++)
		{
			var parts = ReadParts(lines, ref line, "v");
			var vertex = new Vertex
			{
				Xyz = new Vector3(ParseF(parts[2]), ParseF(parts[3]), ParseF(parts[4])),
				FromSubdivision = parts[5] != "0",
			};

			if (parts.Length > 6)
			{
				vertex.ContactNormal = new Vector3(
					ParseF(parts[6]),
					ParseF(parts[7]),
					ParseF(parts[8]));
			}

			vertices[i] = vertex;
			topology.Vertices.Add(vertex);
		}

		var cornerParts = new string[cornerCount][];
		for (int i = 0; i < cornerCount; i++)
		{
			var parts = ReadParts(lines, ref line, "c");
			int index = ParseI(parts[1]);
			var corner = new Corner
			{
				Uv = new Vector2(ParseF(parts[3]), ParseF(parts[4])),
				Vertex = vertices[ParseI(parts[2])],
			};

			corners[index] = corner;
			cornerParts[index] = parts;
			topology.Corners.Add(corner);
			corner.Vertex.Corners.Add(corner);
		}

		for (int i = 0; i < cornerCount; i++)
		{
			var parts = cornerParts[i];
			corners[i].Next = corners[ParseI(parts[5])];
			corners[i].Prev = corners[ParseI(parts[6])];
			corners[i].Across = ParseNullableIndex(parts[7], corners);
		}

		return topology;
	}

	static int ReadCount(string[] lines, ref int line, string label)
	{
		var parts = ReadParts(lines, ref line, null);
		if (parts.Length != 2 || parts[0] != label)
			throw new InvalidDataException($"Expected '{label} <count>'.");

		return ParseI(parts[1]);
	}

	static string[] ReadParts(string[] lines, ref int line, string? prefix)
	{
		while (line < lines.Length)
		{
			var text = lines[line++].Trim();
			if (text.Length == 0 || text.StartsWith('#'))
				continue;

			var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
			if (prefix != null && parts[0] != prefix)
				throw new InvalidDataException($"Expected '{prefix}' record, got '{parts[0]}'.");

			return parts;
		}

		throw new InvalidDataException("Unexpected end of topology file.");
	}

	static string F(float value) =>
		value.ToString("R", CultureInfo.InvariantCulture);

	static float ParseF(string value) =>
		float.Parse(value, CultureInfo.InvariantCulture);

	static int ParseI(string value) =>
		int.Parse(value, CultureInfo.InvariantCulture);

	static int IndexOrNull(Corner? corner, IReadOnlyDictionary<Corner, int> cornerIndex) =>
		corner == null ? -1 : cornerIndex[corner];

	static Corner? ParseNullableIndex(string value, IReadOnlyList<Corner> corners)
	{
		int index = ParseI(value);
		return index < 0 ? null : corners[index];
	}
}
