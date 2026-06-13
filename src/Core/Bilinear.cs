using Godot;

namespace DelaunyFabric.Core;

/// <summary>
/// Bilinear patch over a quad with corners p0..p3 at parametric (0,0), (1,0), (1,1), (0,1).
/// </summary>
public static class Bilinear
{
	public static float Eval(float p0, float p1, float p2, float p3, float s, float t) =>
		(1f - s) * (1f - t) * p0
		+ s * (1f - t) * p1
		+ s * t * p2
		+ (1f - s) * t * p3;

	public static Vector2 Eval(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float s, float t) =>
		new(
			Eval(p0.X, p1.X, p2.X, p3.X, s, t),
			Eval(p0.Y, p1.Y, p2.Y, p3.Y, s, t));

	public static Vector3 Eval(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float s, float t) =>
		new(
			Eval(p0.X, p1.X, p2.X, p3.X, s, t),
			Eval(p0.Y, p1.Y, p2.Y, p3.Y, s, t),
			Eval(p0.Z, p1.Z, p2.Z, p3.Z, s, t));

	public static Vector3 Ds(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float s, float t) =>
		-(1f - t) * p0 + (1f - t) * p1 + t * p2 - t * p3;

	public static Vector3 Dt(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float s, float t) =>
		-(1f - s) * p0 - s * p1 + s * p2 + (1f - s) * p3;

	public static Vector2 Du(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float s, float t) =>
		-(1f - t) * p0 + (1f - t) * p1 + t * p2 - t * p3;

	public static Vector2 Dv(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float s, float t) =>
		-(1f - s) * p0 - s * p1 + s * p2 + (1f - s) * p3;

	public static bool TryInverse(
		Vector2 p0,
		Vector2 p1,
		Vector2 p2,
		Vector2 p3,
		Vector2 uv,
		out float s,
		out float t)
	{
		s = t = 0.5f;
		for (int i = 0; i < 10; i++)
		{
			var q = Eval(p0, p1, p2, p3, s, t);
			var du = Du(p0, p1, p2, p3, s, t);
			var dv = Dv(p0, p1, p2, p3, s, t);
			var diff = uv - q;
			float det = du.X * dv.Y - du.Y * dv.X;
			if (Mathf.Abs(det) < 1e-12f)
				break;

			s += (diff.X * dv.Y - diff.Y * dv.X) / det;
			t += (du.X * diff.Y - du.Y * diff.X) / det;
		}

		const float eps = -1e-4f;
		if (s < eps || s > 1f - eps || t < eps || t > 1f - eps)
			return false;

		var residual = Eval(p0, p1, p2, p3, s, t) - uv;
		return residual.LengthSquared() <= 1e-6f;
	}
}
