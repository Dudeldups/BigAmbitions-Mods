#nullable enable
using System;
using System.Collections;
using UnityEngine;

// Added to the loaded player prefab at runtime. Start runs on each created
// instance after the native spawn call has bound its saved VehicleInstance.
public sealed class KiaCeedSportswagonCreationNotifier : MonoBehaviour
{
    internal static event Action<VehicleController>? Created;
    internal static event Action<VehicleController>? AppearanceReady;
    [NonSerialized] private bool notified;

    private void Start()
    {
        var vehicle = GetComponent<VehicleController>();
        if (vehicle != null && !notified)
        {
            notified = true;
            Created?.Invoke(vehicle);
            if (isActiveAndEnabled)
                StartCoroutine(NotifyAppearanceReady(vehicle));
        }
    }

    // One spawn handoff after native CarFeatures has initialized, not a poll.
    private IEnumerator NotifyAppearanceReady(VehicleController vehicle)
    {
        yield return null;
        if (vehicle != null && vehicle.vehicleInstance != null)
            AppearanceReady?.Invoke(vehicle);
    }
}
