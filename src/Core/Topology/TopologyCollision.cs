using System.Collections.Generic;
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

	public static void Apply(
		Topology topology,
		ISdfCollider collider,
		IReadOnlyList<Vector3> wishPositions,
		float skinDistance,
		float frictionStrength = 0f,
		float staticFrictionStrength = 0f)
	{
		if (topology.Vertices.Count != wishPositions.Count)
			throw new System.ArgumentException("Wish count must match topology vertex count.");

		for (int i = 0; i < topology.Vertices.Count; i++)
		{
			var vertex = topology.Vertices[i];
			var wishPosition = wishPositions[i];

			(vertex.Xyz, vertex.ContactNormal) = Move(
				vertex.Xyz,
				wishPosition - vertex.Xyz,
				vertex.ContactNormal,
				collider,
				skinDistance,
				frictionStrength,
				staticFrictionStrength);
		}
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
		float distanceLeft = movement.Length();
		var contactNormal = previousContactNormal;
		if (distanceLeft <= epsilon)
			return Finish(current, contactNormal, collider, skinDistance);

		var direction = movement / distanceLeft;

		for (int step = 0; step < maxSteps; step++)
		{
			if (distanceLeft <= epsilon)
				break;

			var sample = Sample(collider, current);
			if (sample.SignedDistance < skinDistance)
			{
				contactNormal = sample.Normal;
				current = ProjectToSkin(current, sample, skinDistance);
				sample = new CollisionSample(skinDistance, contactNormal.Value);
			}

			direction = SlideAgainstContact(
				direction,
				contactNormal,
				distanceLeft,
				frictionStrength,
				staticFrictionStrength);
			float directionLength = direction.Length();
			if (directionLength <= epsilon)
				break;

			direction /= directionLength;

			float stepLength = Mathf.Min(distanceLeft, sample.SignedDistance);
			if (stepLength <= epsilon)
				break;

			current += direction * stepLength;
			distanceLeft -= stepLength;
		}

		return Finish(current, contactNormal, collider, skinDistance);
	}

	static Vector3 SlideAgainstContact(
		Vector3 movement,
		Vector3? contactNormal,
		float movementLength,
		float frictionStrength,
		float staticFrictionStrength)
	{
		if (contactNormal is not { } normal)
			return movement;

		float intoContact = movement.Dot(normal);
		if (intoContact >= 0f)
			return movement;

		var tangent = movement - normal * intoContact;
		float inward = -intoContact * movementLength;
		float tangentLength = tangent.Length() * movementLength;
		if (tangentLength <= inward * staticFrictionStrength)
			return Vector3.Zero;

		float friction = 1f / (1f + inward * frictionStrength);
		return tangent * friction;
	}

	static (Vector3 Xyz, Vector3? ContactNormal) Finish(
		Vector3 position,
		Vector3? contactNormal,
		ISdfCollider collider,
		float skinDistance)
	{
		var sample = Sample(collider, position);
		if (sample.SignedDistance <= skinDistance)
			return (ProjectToSkin(position, sample, skinDistance), sample.Normal);

		return (position, null);
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
