using System.Collections.Generic;

namespace DelaunyFabric.Core;

public static class TopologyWalk
{
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

	public static List<Corner> WalkStrip(Corner origin)
	{
		var (forward, end) = WalkForward(origin);
		if (end == origin)
			return forward;

		if (origin.Across == null)
			return forward;

		var (backward, _) = WalkForward(origin.Across);
		var strip = new List<Corner>(backward.Count + forward.Count);
		for (int i = backward.Count - 1; i >= 0; i--)
			strip.Add(backward[i].Next.Next);

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

			current = current.Next.Next;
			current = current.Across;
		}

		return (corners, current);
	}

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
