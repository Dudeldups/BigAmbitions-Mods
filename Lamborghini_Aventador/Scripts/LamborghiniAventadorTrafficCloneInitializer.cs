#nullable enable
using BAModAPI;
using GleyTrafficSystem;
using UnityEngine;

/// <summary>Initializes components that must bind to each spawned traffic clone, never the inactive template.</summary>
[AddComponentMenu("")]
internal sealed class LamborghiniAventadorTrafficCloneInitializer : MonoBehaviour
{
    private static readonly string[] CaliperNames = { "Caliper_FL", "Caliper_FR", "Caliper_BL", "Caliper_BR" };

    private void Start()
    {
        var context = LamborghiniAventadorTrafficSupport.CurrentContext;
        if (context == null)
            return;

        // Native Gley traffic prefabs do not carry the game's player/vehicle
        // VehicleController. They remain valid NPC traffic when their own Gley
        // VehicleComponent and four wheel definitions are present.
        var hasGameVehicleController = GetComponent<VehicleController>() != null;

        var glass = GetComponent<LamborghiniAventadorGlassController>() ??
                    gameObject.AddComponent<LamborghiniAventadorGlassController>();
        glass.Initialize(context);

        var lighting = GetComponent<LamborghiniAventadorLightingController>() ??
                       gameObject.AddComponent<LamborghiniAventadorLightingController>();
        lighting.InitializeForAmbientTraffic(context);

        var driver = GetComponent<LamborghiniAventadorDriverController>() ??
                     gameObject.AddComponent<LamborghiniAventadorDriverController>();
        driver.InitializeForAmbientTraffic(context);

        var trafficVehicle = GetComponent<VehicleComponent>();
        if (trafficVehicle == null || trafficVehicle.allWheels == null || trafficVehicle.allWheels.Length != 4)
        {
            context.Logger.Warn("LamborghiniAventador traffic clone initialization failed: four native Gley wheels unavailable.");
            return;
        }

        var calipers = new Transform[CaliperNames.Length];
        for (var index = 0; index < CaliperNames.Length; index++)
        {
            var found = Find(transform, CaliperNames[index]);
            if (found == null)
            {
                context.Logger.Warn($"LamborghiniAventador traffic clone initialization failed: {CaliperNames[index]} missing.");
                return;
            }
            calipers[index] = found;
        }

        var binder = GetComponent<LamborghiniAventadorTrafficCalipers>() ??
                     gameObject.AddComponent<LamborghiniAventadorTrafficCalipers>();
        if (!binder.Bind(transform, trafficVehicle, calipers))
            context.Logger.Warn("LamborghiniAventador traffic clone initialization failed: native wheel hub/caliper mapping incomplete.");
        else if (LamborghiniAventadorDiagnostics.DebugEnabled &&
                 LamborghiniAventadorTrafficSupport.TrafficDebugEnabled)
            context.Logger.Info(
                $"LamborghiniAventador traffic clone initialized vehicle={GetInstanceID()} " +
                $"gleyWheels={trafficVehicle.allWheels.Length} gameVehicleController={hasGameVehicleController}.");
    }

    private static Transform? Find(Transform root, string name)
    {
        foreach (var candidate in root.GetComponentsInChildren<Transform>(true))
            if (candidate.name == name) return candidate;
        return null;
    }
}
