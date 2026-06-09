using Godot;

namespace DelaunyFabric.Core;

public static class TopologyCollision
{
	public static void Move(
		Vertex vertex,
		Vector3 movement,
		ISdfCollider collider,
		float skinDistance,
		float frictionStrength = 0f,
		float staticFrictionStrength = 0f)
	{
		(vertex.Xyz, vertex.ContactNormal) = Move(
			vertex.Xyz,
			movement,
			vertex.ContactNormal,
			collider,
			skinDistance,
			frictionStrength,
			staticFrictionStrength);
	}

	static (Vector3 Xyz, Vector3? ContactNormal) Move(
		Vector3 position,
		Vector3 movement,
		Vector3? previousContactNormal,
		ISdfCollider collider,
		float skinDistance,
		float frictionStrength,
		float staticFrictionStrength)
	{
		const int maxSteps = 16;
		const float epsilon = 1e-5f;

		var current = position;
		Vector3? contactNormal = previousContactNormal;
		float time = 0f;

		for (int step = 0; step < maxSteps; step++)
		{
			var slideMovement = SlideAgainstContact(
				movement,
				contactNormal,
				frictionStrength,
				staticFrictionStrength);
			float slideLength = slideMovement.Length();
			if (slideLength <= epsilon)
			{
				(current, contactNormal) = UpdateContact(current, collider, skinDistance);
				break;
			}

			var sample = Sample(collider, current);
			float stepTime = Mathf.Clamp(sample.SignedDistance / slideLength, 0f, 1f - time);

			current += slideMovement * stepTime;
			time += stepTime;

			(current, contactNormal) = UpdateContact(current, collider, skinDistance);
			if (time >= 1f - epsilon)
				break;
		}

		return (current, contactNormal);
	}

	static (Vector3 Xyz, Vector3? ContactNormal) UpdateContact(
		Vector3 position,
		ISdfCollider collider,
		float skinDistance)
	{
		var sample = Sample(collider, position);
		if (sample.SignedDistance > skinDistance)
			return (position, null);

		return (ProjectToSkin(position, sample, skinDistance), sample.Normal);
	}

	static Vector3 SlideAgainstContact(
		Vector3 movement,
		Vector3? contactNormal,
		float frictionStrength,
		float staticFrictionStrength)
	{
		if (contactNormal is not { } normal)
			return movement;

		float intoContact = movement.Dot(normal);
		if (intoContact >= 0f)
			return movement;

		var tangent = movement - normal * intoContact;
		float inwardLength = -intoContact;
		float tangentLength = tangent.Length();
		if (tangentLength <= inwardLength * staticFrictionStrength)
			return Vector3.Zero;

		float friction = 1f / (1f + inwardLength * frictionStrength);
		return tangent * friction;
	}

	static Vector3 ProjectToSkin(Vector3 position, CollisionSample sample, float skinDistance) =>
		position + sample.Normal * (skinDistance - sample.SignedDistance);

	static CollisionSample Sample(ISdfCollider collider, Vector3 position)
	{
		collider.Sample(position, out float signedDistance, out var normal);
		return new CollisionSample(signedDistance, normal);
	}

	readonly record struct CollisionSample(float SignedDistance, Vector3 Normal);
}
