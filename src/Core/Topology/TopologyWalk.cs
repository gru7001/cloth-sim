using System.Collections.Generic;

namespace DelaunyFabric.Core;

public static class TopologyWalk
{
	/// <summary>
	/// Walks a quad face via <see cref="Corner.Next"/>.
	/// Corner order is reversed relative to face index / bilinear parametric order.
	/// </summary>
	public static List<Corner> WalkFace(Corner origin)
	{
		var face = new List<Corner>();
		var current = origin;

		do
		{
			face.Add(current);
			current = current.Next;

			if (face.Count > 16)
				throw new System.InvalidOperationException("Expected a quad face.");
		}
		while (current != origin);

		return face;
	}

	/// <summary>
	/// Walks a quad face via <see cref="Corner.Prev"/>.
	/// Corner order matches face index and bilinear corners at (0,0), (1,0), (1,1), (0,1).
	/// </summary>
	public static List<Corner> WalkFaceCcw(Corner origin)
	{
		var face = new List<Corner>();
		var current = origin;

		do
		{
			face.Add(current);
			current = current.Prev;

			if (face.Count > 16)
				throw new System.InvalidOperationException("Expected a quad face.");
		}
		while (current != origin);

		return face;
	}

	public static List<Corner> WalkStrip(Corner origin)
	{
		var (forward, forwardEnd) = WalkForward(origin);
		if (forwardEnd == origin)
			return forward;

		var (backward, _) = WalkBackward(origin);
		var strip = new List<Corner>(backward.Count + forward.Count);
		for (int i = backward.Count - 1; i >= 1; i--)
			strip.Add(backward[i]);

		strip.AddRange(forward);
		return strip;
	}

	public static (List<Corner> Corners, Corner? End) WalkForward(Corner origin)
	{
		var corners = new List<Corner>();
		Corner? current = origin;
		bool first = true;

		while (current != null && (first || current != origin))
		{
			first = false;
			corners.Add(current);

			current = StripStepForward(current);
		}

		return (corners, current);
	}

	public static (List<Corner> Corners, Corner? End) WalkBackward(Corner origin)
	{
		var corners = new List<Corner>();
		Corner? current = origin;
		bool first = true;

		while (current != null && (first || current != origin))
		{
			first = false;
			corners.Add(current);

			current = StripStepBackward(current);
		}

		return (corners, current);
	}

	public static Corner? StripStepForward(Corner corner) => corner.Across?.Next;

	public static Corner? StripStepBackward(Corner corner) =>
		corner.Next.Next.Across?.Prev;

	public static Corner TakeAny(HashSet<Corner> corners)
	{
		foreach (var corner in corners)
			return corner;

		throw new System.InvalidOperationException("Expected at least one pending corner.");
	}

	public static void WireNext(Corner corner, Corner next)
	{
		corner.Next = next;
		next.Prev = corner;
	}
}
