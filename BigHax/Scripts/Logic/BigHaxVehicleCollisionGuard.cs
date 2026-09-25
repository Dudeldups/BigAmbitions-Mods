#nullable enable
using System;
using UnityEngine;

namespace BigHax
{
    /// <summary>
    /// Receives collisions on the vehicle rigidbody and asks the runtime for one
    /// deferred repair pass. Deferring until the end of the frame lets the game's
    /// own deformation component finish processing the collision first.
    /// </summary>
    internal sealed class BigHaxVehicleCollisionGuard : MonoBehaviour
    {
        public static event Action? CollisionDetected;

        private void OnCollisionEnter(Collision collision)
        {
            CollisionDetected?.Invoke();
        }
    }
}
