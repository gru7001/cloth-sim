using System.Collections.Generic;
using Godot;

namespace DelaunyFabric.Core;

public static class TopologyRelaxation
{
	public static void Relax(
		Topology topology,
		ISdfCollider collider,
		float skinDistance,
		float frictionStrength,
		float staticFrictionStrength,
		Vector3 initialMovement,
		float uvScale = 1f,
		int iterations = 1)
	{
		iterations = Mathf.Max(iterations, 0);
		var vertexIndex = IndexVertices(topology);
		var corrections = new Vector3[topology.Vertices.Count];
		var weights = new float[topology.Vertices.Count];
		for (int iteration = 0; iteration < iterations; iteration++)
		{
			System.Array.Clear(corrections);
			System.Array.Clear(weights);

			foreach (var corner in topology.Corners)
			{
				
				int vi = vertexIndex[corner.Vertex];
				for (Corner c = corner.Next; c != corner; c = c.Next)
				{
					int vj = vertexIndex[c.Vertex];
					var delta = c.Vertex.Xyz - corner.Vertex.Xyz;
					float length = delta.Length();

					float restLength = (c.Uv - corner.Uv).Length() * uvScale;
					float error = length - restLength;
					var correction = delta / length * error;
					float weight = 1.0f;
					corrections[vi] += correction * weight;
					weights[vi] += weight;
				}

				if (corner.Across?.Prev is Corner across)
				{
					Vertex u = across.Next.Next.Vertex;
					Vertex v = corner.Vertex;
					Vertex w = corner.Prev.Vertex;

					var vu = v.Xyz - u.Xyz;
					var vw = v.Xyz - w.Xyz;
					var uw = u.Xyz - w.Xyz;

					var x = vu.Cross(vw);
					var n = x.Cross(uw);
          if (n.Length() > 1e-6f)
					{
						var correction = -n * n.Dot(vu) / n.Dot(n);
						float weight = 0.1f;
						corrections[vi] += correction * weight;
						weights[vi] += weight;
					}
				}
			}

			for (int i = 0; i < topology.Vertices.Count; i++)
			{
				var vertex = topology.Vertices[i];
				var gravity = iteration == 0 && !vertex.ContactNormal.HasValue
					? initialMovement
					: Vector3.Zero;

				var correction = weights[i] > 0f ? corrections[i] / weights[i] : Vector3.Zero;
				TopologyCollision.Move(
					vertex,
					gravity + correction / weights[i],
					collider,
					skinDistance,
					frictionStrength,
					staticFrictionStrength);
			}
		}
	}

	static Dictionary<Vertex, int> IndexVertices(Topology topology)
	{
		var index = new Dictionary<Vertex, int>(topology.Vertices.Count);
		for (int i = 0; i < topology.Vertices.Count; i++)
			index[topology.Vertices[i]] = i;

		return index;
	}

}
