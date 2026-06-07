using System;
using System.Collections.Generic;
using Godot;

namespace DelaunyFabric.Core;

/// <summary>Closest-point collider. Signed distance: negative inside (against face normal), positive outside.</summary>
public sealed class MeshTriangleCollider : ISdfCollider
{
    readonly Vector3[] _a;
    readonly Vector3[] _b;
    readonly Vector3[] _c;
    readonly int[] _triangles;
    readonly List<Node> _nodes = [];
    readonly int _root;

    public MeshTriangleCollider(Vector3[] a, Vector3[] b, Vector3[] c)
    {
        if (a.Length != b.Length || a.Length != c.Length)
            throw new ArgumentException("Triangle arrays must match length.");
        _a = a;
        _b = b;
        _c = c;
        _triangles = new int[a.Length];
        for (int i = 0; i < _triangles.Length; i++)
            _triangles[i] = i;

        _root = _triangles.Length == 0 ? -1 : BuildNode(0, _triangles.Length);
    }

    public int TriangleCount => _a.Length;

    public void Sample(Vector3 position, out float signedDistance, out Vector3 normal)
    {
        float bestDistSq = float.MaxValue;
        Vector3 bestPoint = position;
        Vector3 bestNormal = Vector3.Up;

        if (_root >= 0)
            SampleNode(_root, position, ref bestDistSq, ref bestPoint, ref bestNormal);

        Vector3 delta = position - bestPoint;
        float len = MathF.Sqrt(bestDistSq);
        if (len < 1e-8f)
        {
            normal = bestNormal;
            signedDistance = 0f;
            return;
        }

        bool outside = delta.Dot(bestNormal) >= 0f;
        normal = outside ? delta / len : -delta / len;
        signedDistance = outside ? len : -len;
    }

    int BuildNode(int start, int count)
    {
        Bounds(start, count, out var min, out var max);
        int nodeIndex = _nodes.Count;
        _nodes.Add(new Node { Min = min, Max = max, Start = start, Count = count, Left = -1, Right = -1 });

        if (count <= 8)
            return nodeIndex;

        CentroidBounds(start, count, out var cmin, out var cmax);
        int axis = LongestAxis(cmax - cmin);
        Array.Sort(_triangles, start, count, Comparer<int>.Create((x, y) =>
            Centroid(x)[axis].CompareTo(Centroid(y)[axis])));

        int leftCount = count / 2;
        var node = _nodes[nodeIndex];
        node.Left = BuildNode(start, leftCount);
        node.Right = BuildNode(start + leftCount, count - leftCount);
        node.Count = 0;
        _nodes[nodeIndex] = node;
        return nodeIndex;
    }

    void SampleNode(
        int nodeIndex,
        Vector3 position,
        ref float bestDistSq,
        ref Vector3 bestPoint,
        ref Vector3 bestNormal)
    {
        var node = _nodes[nodeIndex];
        if (DistanceSquaredToBounds(position, node.Min, node.Max) >= bestDistSq)
            return;

        if (node.Left < 0)
        {
            for (int i = 0; i < node.Count; i++)
                SampleTriangle(_triangles[node.Start + i], position, ref bestDistSq, ref bestPoint, ref bestNormal);

            return;
        }

        var left = _nodes[node.Left];
        var right = _nodes[node.Right];
        float leftDist = DistanceSquaredToBounds(position, left.Min, left.Max);
        float rightDist = DistanceSquaredToBounds(position, right.Min, right.Max);

        if (leftDist <= rightDist)
        {
            if (leftDist < bestDistSq) SampleNode(node.Left, position, ref bestDistSq, ref bestPoint, ref bestNormal);
            if (rightDist < bestDistSq) SampleNode(node.Right, position, ref bestDistSq, ref bestPoint, ref bestNormal);
        }
        else
        {
            if (rightDist < bestDistSq) SampleNode(node.Right, position, ref bestDistSq, ref bestPoint, ref bestNormal);
            if (leftDist < bestDistSq) SampleNode(node.Left, position, ref bestDistSq, ref bestPoint, ref bestNormal);
        }
    }

    void SampleTriangle(
        int i,
        Vector3 position,
        ref float bestDistSq,
        ref Vector3 bestPoint,
        ref Vector3 bestNormal)
    {
        Vector3 cp = ClosestPointOnTriangle(position, _a[i], _b[i], _c[i]);
        float dSq = (position - cp).LengthSquared();
        if (dSq >= bestDistSq) return;

        bestDistSq = dSq;
        bestPoint = cp;
        bestNormal = -TriangleNormal(_a[i], _b[i], _c[i]);
    }

    void Bounds(int start, int count, out Vector3 min, out Vector3 max)
    {
        min = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
        max = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);

        for (int i = 0; i < count; i++)
        {
            int triangle = _triangles[start + i];
            Include(ref min, ref max, _a[triangle]);
            Include(ref min, ref max, _b[triangle]);
            Include(ref min, ref max, _c[triangle]);
        }
    }

    void CentroidBounds(int start, int count, out Vector3 min, out Vector3 max)
    {
        min = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
        max = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);

        for (int i = 0; i < count; i++)
        {
            var centroid = Centroid(_triangles[start + i]);
            Include(ref min, ref max, centroid);
        }
    }

    Vector3 Centroid(int triangle) => (_a[triangle] + _b[triangle] + _c[triangle]) / 3f;

    static void Include(ref Vector3 min, ref Vector3 max, Vector3 point)
    {
        min = new Vector3(
            MathF.Min(min.X, point.X),
            MathF.Min(min.Y, point.Y),
            MathF.Min(min.Z, point.Z));
        max = new Vector3(
            MathF.Max(max.X, point.X),
            MathF.Max(max.Y, point.Y),
            MathF.Max(max.Z, point.Z));
    }

    static int LongestAxis(Vector3 size)
    {
        if (size.X >= size.Y && size.X >= size.Z) return 0;
        return size.Y >= size.Z ? 1 : 2;
    }

    static float DistanceSquaredToBounds(Vector3 point, Vector3 min, Vector3 max)
    {
        float dx = AxisDistance(point.X, min.X, max.X);
        float dy = AxisDistance(point.Y, min.Y, max.Y);
        float dz = AxisDistance(point.Z, min.Z, max.Z);
        return dx * dx + dy * dy + dz * dz;
    }

    static float AxisDistance(float value, float min, float max)
    {
        if (value < min) return min - value;
        if (value > max) return value - max;
        return 0f;
    }

    static Vector3 TriangleNormal(Vector3 a, Vector3 b, Vector3 c)
    {
        Vector3 n = (b - a).Cross(c - a);
        float lenSq = n.LengthSquared();
        return lenSq < 1e-12f ? Vector3.Up : n / MathF.Sqrt(lenSq);
    }

    static Vector3 ClosestPointOnTriangle(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
    {
        Vector3 ab = b - a, ac = c - a, ap = p - a;
        float d1 = ab.Dot(ap);
        float d2 = ac.Dot(ap);
        if (d1 <= 0f && d2 <= 0f) return a;

        Vector3 bp = p - b;
        float d3 = ab.Dot(bp);
        float d4 = ac.Dot(bp);
        if (d3 >= 0f && d4 <= d3) return b;

        float vc = d1 * d4 - d3 * d2;
        if (vc <= 0f && d1 >= 0f && d3 <= 0f)
            return a + ab * (d1 / (d1 - d3));

        Vector3 cp = p - c;
        float d5 = ab.Dot(cp);
        float d6 = ac.Dot(cp);
        if (d6 >= 0f && d5 <= d6) return c;

        float vb = d5 * d2 - d1 * d6;
        if (vb <= 0f && d2 >= 0f && d6 <= 0f)
            return a + ac * (d2 / (d2 - d6));

        float va = d3 * d6 - d5 * d4;
        if (va <= 0f && d4 - d3 >= 0f && d5 - d6 >= 0f)
            return b + (c - b) * ((d4 - d3) / ((d4 - d3) + (d5 - d6)));

        float denom = 1f / (va + vb + vc);
        return a + ab * (vb * denom) + ac * (vc * denom);
    }

    struct Node
    {
        public Vector3 Min;
        public Vector3 Max;
        public int Left;
        public int Right;
        public int Start;
        public int Count;
    }
}
