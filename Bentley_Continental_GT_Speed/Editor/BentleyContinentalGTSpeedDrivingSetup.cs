#nullable enable
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Vehicles.VehicleTypes;

/// <summary>
/// Applies Bentley-specific catalogue and serialized NWH targets to the
/// generated native vehicle rig. Keep this separate from the GLB generator so
/// the model import and the vehicle-dynamics decisions remain independently
/// reviewable.
/// </summary>
public static class BentleyContinentalGTSpeedDrivingSetup
{
    // Bentley publishes 782 PS (575 kW) combined system output, 1,000 Nm
    // combined torque, 335 km/h, and a 2,459 kg EU unladen mass. NWH has no
    // hybrid motor/battery model in this prefab; its single engine is therefore
    // a gameplay-equivalent system-power target, not the V8's 600 PS alone.
    private const float SystemPowerKilowatts = 575f;
    private const float TargetTopSpeedKph = 335f;
    private const float UnladenMassKg = 2459f;
    // Game economy and damage-response tuning targets, not Bentley specs.
    private const float DealerPriceTarget = 350000f;
    private const float DamageIntensityTarget = 0.80f;
    // VehicleType.brakeForce is the game catalogue value. NWH's physical brake
    // torque is configured separately below because the two APIs use different
    // scales.
    private const float VehicleTypeBrakeForceTarget = 3000f;
    // 4700 Nm alone is tire limited at 36.38 m from 70 mph. The native brake
    // events apply .56 grip only during braking, giving 32.48 m while preserving
    // the authored .50 launch grip, powertrain and accepted shifting.
    private const float NwhBrakeTorqueTarget = 4700f;
    // Bentley describes four-wheel steering on this GT Speed but does not
    // publish its rear angle in the MY25 material reviewed. The NWH prefab has
    // no rear steering model, so raise front lock modestly as a low-speed proxy
    // and validate its body-centre radius in the fixture.
    private const float VehicleSteeringAngleTarget = 35.80f;
    // Native NWH TCS sharply limits engine output above threshold. Use its 0.10
    // default: it keeps near-redline time below 0.30 s per gear, while a 0.05
    // comparison gave up more acceleration for only a small first-gear gain.
    private const float TcsSlipThreshold = 0.10f;
    private const float EngineRevLimiterTargetRpm = 6500f;
    private const float SystemPowerPeakTargetRpm = 6000f;
    private const float WheelbaseMeters = 2.851f;
    private const float PublishedFrontWeightFraction = 0.49f;

    private const float FrontTireWidthMeters = 0.275f;
    private const float RearTireWidthMeters = 0.315f;
    private const float FrontTireAspectRatio = 0.35f;
    private const float RearTireAspectRatio = 0.30f;
    private const float RimDiameterInches = 22f;
    private const float FrontLoadRatingKg = 900f; // 104 load index
    private const float RearLoadRatingKg = 975f; // 107 load index
    private const float GravityMetersPerSecondSquared = 9.81f;

    // NWH starting points for a heavy, wide-tired GT. These are game tuning
    // values, not published Bentley spring, damper, or tire-friction data.
    private const float FrontSuspensionTravelMeters = 0.18f;
    private const float RearSuspensionTravelMeters = 0.18f;
    private const float FrontSpringForceLimitNewtons = 22000f;
    private const float RearSpringForceLimitNewtons = 23000f;
    private const float SuspensionBumpRate = 16000f;
    private const float SuspensionReboundRate = 18500f;
    private const float FrontLateralGrip = 1.30f;
    private const float RearLateralGrip = 1.35f;
    // Native arcade preset D=2 and NWH's longitudinal load factor=1.35;
    // Authored .50 caps contact force near 1.35 times load during acceleration.
    // RoadDynamics switches to .56 on native brake activation and restores this
    // baseline on release, preserving full power and the native TCS behavior.
    private const float ForwardGrip = 0.50f;
    private const float TireFrictionCircleStrength = 0.88f;
    private const float AntiRollBarForce = 8000f;
    private const float NwhEngineInertia = 0.28f;
    private const float NwhEngineLossPercent = 0.22f;
    private const float NwhClutchSlipTorque = 1600f;
    private const float NwhFinalDriveRatio = 3.2f;

    /// <summary>
    /// Configure the VehicleType and the generated Bentley prefab before it is
    /// saved. The caller owns asset loading/saving and prefab persistence.
    /// </summary>
    public static void Configure(GameObject vehicleRoot, VehicleType vehicleType)
    {
        if (vehicleRoot == null)
            throw new ArgumentNullException(nameof(vehicleRoot));
        if (vehicleType == null)
            throw new ArgumentNullException(nameof(vehicleType));

        ConfigureVehicleType(vehicleType);
        ConfigureConditionDamage(vehicleRoot);
        ConfigureMass(vehicleRoot);
        ConfigureWheels(vehicleRoot);
        ConfigurePowertrain(vehicleRoot);
        ConfigureTractionControl(vehicleRoot);
        ConfigureAerodynamics(vehicleRoot);
        ConfigureFuelCapacity(vehicleRoot);
    }

    private static void ConfigureConditionDamage(GameObject root)
    {
        var handler = FindComponent(root, "NWH.VehiclePhysics2.Damage.DamageHandler") ??
                      throw new InvalidOperationException("Bentley prefab has no native DamageHandler.");
        var serialized = new SerializedObject(handler);
        SetNumber(serialized, "damageIntensity", DamageIntensityTarget);
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(handler);
    }
    private static void ConfigureVehicleType(VehicleType vehicleType)
    {
        var serialized = new SerializedObject(vehicleType);
        SetString(serialized, "vehicleTypeName",
            "bentleycontinentalgtspeed-vehicle:vehicletype_bentleycontinentalgtspeed");
        SetNumber(serialized, "price", DealerPriceTarget);
        SetNumber(serialized, "maxFuel", 80f); // published fuel capacity, litres
        SetNumber(serialized, "maxSpeed", TargetTopSpeedKph);
        SetNumber(serialized, "enginePower", SystemPowerKilowatts);
        SetNumber(serialized, "brakeForce", VehicleTypeBrakeForceTarget);
        SetNumber(serialized, "damageIntensity", DamageIntensityTarget);
        // Comparable vehicle setups (including Senna) set turnRadius to the
        // same degree value as NWH steering.maximumSteerAngle. This is a mod
        // target; the native rig has no Bentley rear-wheel-steer model.
        SetNumber(serialized, "turnRadius", Mathf.RoundToInt(VehicleSteeringAngleTarget));
        SetNumber(serialized, "maxCargoCapacity", 2f);
        SetBool(serialized, "fitsHandTruck", false);
        SetBool(serialized, "fitsFlatbed", false);
        SetBool(serialized, "autoParkSupported", true);
        SetBool(serialized, "hasRadio", true);
        SetBool(serialized, "isLuxuryCar", true);
        SetBool(serialized, "enclosed", true);
        SetNumber(serialized, "destinationsThatCanDeliver", 2f);
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(vehicleType);
    }

    private static void ConfigureMass(GameObject vehicleRoot)
    {
        var body = vehicleRoot.GetComponent<Rigidbody>() ??
                   throw new InvalidOperationException("Bentley prefab has no Rigidbody.");
        body.mass = UnladenMassKg;

        // Bentley reports 49:51 front/rear weight distribution. With the
        // published wheelbase, this places the longitudinal centre of mass
        // 28.5 mm rearward of axle-midpoint (vehicle forward is +Z).
        var centerOfMass = new Vector3(
            0f,
            body.centerOfMass.y,
            -(0.5f - PublishedFrontWeightFraction) * WheelbaseMeters);
        body.centerOfMass = centerOfMass;

        var massComponent = FindComponentWithProperty(vehicleRoot, "baseMass") ??
                            throw new InvalidOperationException("Bentley prefab has no NWH mass component.");
        var serialized = new SerializedObject(massComponent);
        SetBool(serialized, "useDefaultMass", false);
        SetNumber(serialized, "baseMass", UnladenMassKg);
        SetNumber(serialized, "combinedMass", UnladenMassKg);
        SetBool(serialized, "useDefaultCenterOfMass", false);
        SetVector3(serialized, "centerOfMass", centerOfMass);
        SetVector3(serialized, "combinedCenterOfMass", centerOfMass);
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void ConfigureWheels(GameObject vehicleRoot)
    {
        var wheels = new Dictionary<string, (float Radius, float Width, float LoadRating)>();
        var frontRadius = NominalTireRadiusMeters(FrontTireWidthMeters, FrontTireAspectRatio);
        var rearRadius = NominalTireRadiusMeters(RearTireWidthMeters, RearTireAspectRatio);
        wheels.Add("FrontLeft_WheelController", (frontRadius, FrontTireWidthMeters, FrontLoadRatingKg));
        wheels.Add("FrontRight_WheelController", (frontRadius, FrontTireWidthMeters, FrontLoadRatingKg));
        wheels.Add("RearLeft_WheelController", (rearRadius, RearTireWidthMeters, RearLoadRatingKg));
        wheels.Add("RearRight_WheelController", (rearRadius, RearTireWidthMeters, RearLoadRatingKg));

        foreach (var pair in wheels)
        {
            var wheelTransform = FindTransform(vehicleRoot.transform, pair.Key) ??
                                 throw new InvalidOperationException("Bentley wheel controller is missing: " + pair.Key);
            var wheelController = FindComponent(wheelTransform.gameObject, "WheelController") ??
                                  throw new InvalidOperationException("Bentley NWH wheel component is missing: " + pair.Key);
            var serialized = new SerializedObject(wheelController);
            SetRelativeNumber(serialized, "wheel.radius", pair.Value.Radius);
            SetRelativeNumber(serialized, "wheel.width", pair.Value.Width);
            SetNumber(serialized, "loadRating", pair.Value.LoadRating * GravityMetersPerSecondSquared);
            var isFront = pair.Key.StartsWith("Front", StringComparison.Ordinal);
            SetRelativeNumber(
                serialized,
                "spring.maxLength",
                isFront ? FrontSuspensionTravelMeters : RearSuspensionTravelMeters);
            SetRelativeNumber(
                serialized,
                "spring.maxForce",
                isFront ? FrontSpringForceLimitNewtons : RearSpringForceLimitNewtons);
            SetRelativeNumber(serialized, "damper.bumpRate", SuspensionBumpRate);
            SetRelativeNumber(serialized, "damper.reboundRate", SuspensionReboundRate);
            SetRelativeNumber(
                serialized,
                "sideFriction.grip",
                isFront ? FrontLateralGrip : RearLateralGrip);
            SetRelativeNumber(serialized, "forwardFriction.grip", ForwardGrip);
            SetRelativeNumber(serialized, "frictionCircleStrength", TireFrictionCircleStrength);
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }

    private static float NominalTireRadiusMeters(float widthMeters, float aspectRatio)
    {
        var rimDiameterMeters = RimDiameterInches * 0.0254f;
        var sidewallHeightMeters = widthMeters * aspectRatio;
        // This is the nominal unloaded tire radius from the published 22-inch
        // rim and 275/35 front / 315/30 rear sizes; rolling radius will differ.
        return (rimDiameterMeters + 2f * sidewallHeightMeters) * 0.5f;
    }

    private static void ConfigurePowertrain(GameObject vehicleRoot)
    {
        var controller = FindComponent(vehicleRoot, "NWH.VehiclePhysics2.VehicleController") ??
                         throw new InvalidOperationException("Bentley prefab has no NWH VehicleController.");
        var serialized = new SerializedObject(controller);

        SetRelativeNumber(serialized, "brakes.maxTorque", NwhBrakeTorqueTarget);
        SetRelativeNumber(serialized, "powertrain.engine.maxPower", SystemPowerKilowatts);
        SetRelativeNumber(serialized, "powertrain.engine.inertia", NwhEngineInertia);
        SetRelativeNumber(serialized, "powertrain.engine.engineLossPercent", NwhEngineLossPercent);
        SetRelativeNumber(serialized, "powertrain.engine.idleRPM", 900f);
        SetRelativeNumber(serialized, "powertrain.engine.revLimiterRPM", EngineRevLimiterTargetRpm);
        SetRelativeBool(serialized, "powertrain.engine.stallingEnabled", false);
        // Model the published combined system output through one NWH engine.
        // Avoid multiplying the native power curve by a launch-time ratio: that
        // incorrectly removed more than half the power at every road speed.
        SetRelativeBool(serialized, "powertrain.engine.forcedInduction.useForcedInduction", false);
        SetRelativeNumber(serialized, "powertrain.engine.forcedInduction.powerGainMultiplier", 1f);

        var powerCurve = FindRelativeProperty(serialized, "powertrain.engine.powerCurve");
        if (powerCurve?.propertyType != SerializedPropertyType.AnimationCurve)
            throw new InvalidOperationException("Bentley NWH engine power curve is missing.");
        powerCurve.animationCurveValue = CreateSystemPowerCurve();

        SetRelativeNumber(serialized, "powertrain.clutch.engagementRPM", 1200f);
        SetRelativeNumber(serialized, "powertrain.clutch.throttleEngagementOffsetRPM", 500f);
        SetRelativeNumber(serialized, "powertrain.clutch.slipTorque", NwhClutchSlipTorque);
        SetRelativeNumber(serialized, "powertrain.transmission.forwardGearCount", 8f);
        SetRelativeNumber(serialized, "powertrain.transmission.reverseGearCount", 1f);
        // No authoritative individual ratio set was found. Retain the template's
        // 8-speed ratios and use its 3.20 final drive as an explicit NWH start.
        SetRelativeNumber(serialized, "powertrain.transmission.finalGearRatio", NwhFinalDriveRatio);
        SetRelativeNumber(serialized, "powertrain.transmission.shiftDuration", 0.08f);
        SetRelativeNumber(serialized, "powertrain.transmission._downshiftRPM", 3800f);
        SetRelativeNumber(serialized, "powertrain.transmission._upshiftRPM", 6200f);
        SetRelativeNumber(serialized, "powertrain.transmission.transmissionType", 1f);
        SetRelativeNumber(serialized, "steering.maximumSteerAngle", VehicleSteeringAngleTarget);

        var wheelGroups = FindRelativeProperty(serialized, "powertrain.wheelGroups");
        if (wheelGroups == null || !wheelGroups.isArray || wheelGroups.arraySize != 2)
            throw new InvalidOperationException("Bentley NWH front and rear axle groups are missing.");
        for (var index = 0; index < wheelGroups.arraySize; index++)
        {
            var antiRoll = wheelGroups.GetArrayElementAtIndex(index)
                .FindPropertyRelative("antiRollBarForce");
            if (antiRoll?.propertyType != SerializedPropertyType.Float)
                throw new InvalidOperationException("Bentley NWH axle anti-roll setting is missing.");
            antiRoll.floatValue = AntiRollBarForce;
        }

        var differentials = FindRelativeProperty(serialized, "powertrain.differentials");
        if (differentials == null || !differentials.isArray || differentials.arraySize != 3)
            throw new InvalidOperationException("Bentley AWD front, rear and centre differentials are missing.");
        var gears = FindRelativeProperty(serialized, "powertrain.transmission.gears");
        if (gears == null || !gears.isArray || gears.arraySize < 10)
            throw new InvalidOperationException("Bentley eight-speed gear array is missing or incomplete.");
        // Bentley publishes an eight-speed DCT and active AWD, but no verified
        // individual gear ratios or fixed axle split were available here. Keep
        // the native rig's existing ratios and AWD topology; neutralize the
        // template's inherited 60:40 center bias as an explicit gameplay default.
        var centerDifferential = FindDifferentialByName(differentials, "Center Differential");
        var centerBias = centerDifferential?.FindPropertyRelative("biasAB");
        if (centerBias?.propertyType != SerializedPropertyType.Float)
            throw new InvalidOperationException("Bentley NWH center differential bias is missing.");
        centerBias.floatValue = 0.5f;

        serialized.ApplyModifiedPropertiesWithoutUndo();
        ConfigureSpeedLimiter(vehicleRoot);
    }

    private static SerializedProperty? FindDifferentialByName(SerializedProperty differentials, string name)
    {
        for (var index = 0; index < differentials.arraySize; index++)
        {
            var differential = differentials.GetArrayElementAtIndex(index);
            var differentialName = differential.FindPropertyRelative("name");
            if (differentialName?.propertyType == SerializedPropertyType.String &&
                string.Equals(differentialName.stringValue, name, StringComparison.Ordinal))
                return differential;
        }
        return null;
    }

    private static AnimationCurve CreateSystemPowerCurve()
    {
        // Approximate combined-system power shape, not a measured OEM dyno trace.
        // Peak is normalized to the actual 575 kW maxPower setting.
        return new AnimationCurve(
            new Keyframe(0f, 0f),
            new Keyframe(1000f / EngineRevLimiterTargetRpm, 0.10f),
            new Keyframe(2000f / EngineRevLimiterTargetRpm, 0.34f),
            new Keyframe(4500f / EngineRevLimiterTargetRpm, 0.82f),
            new Keyframe(SystemPowerPeakTargetRpm / EngineRevLimiterTargetRpm, 1f),
            new Keyframe(1f, 0.92f));
    }

    private static void ConfigureAerodynamics(GameObject vehicleRoot)
    {
        // Use NWH's existing quadratic drag model. These are explicit tuning
        // parameters, not claimed OEM wind-tunnel data. No artificial downforce.
        var wrapper = vehicleRoot.GetComponent<NWH.VehiclePhysics2.Modules.Aerodynamics.AerodynamicsModuleWrapper>() ??
                      vehicleRoot.AddComponent<NWH.VehiclePhysics2.Modules.Aerodynamics.AerodynamicsModuleWrapper>();
        var serialized = new SerializedObject(wrapper);
        SetRelativeBool(serialized, "module.simulateDrag", true);
        SetRelativeBool(serialized, "module.simulateDownforce", false);
        SetVector3(serialized, "module.dimensions", new Vector3(1.966f, 1.397f, 4.895f));
        SetRelativeNumber(serialized, "module.frontalCd", 0.35f);
        SetRelativeNumber(serialized, "module.sideCd", 0f);
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void ConfigureTractionControl(GameObject vehicleRoot)
    {
        // This NWH build has no configurable TCS enable flag: the module wrapper
        // registers its native power modifier with the vehicle when enabled.
        // `module.active` is runtime telemetry, so only configure its threshold.
        var wrapper = vehicleRoot.GetComponent<NWH.VehiclePhysics2.Modules.TCS.TCSModuleWrapper>() ??
                      vehicleRoot.AddComponent<NWH.VehiclePhysics2.Modules.TCS.TCSModuleWrapper>();
        var serialized = new SerializedObject(wrapper);
        SetRelativeNumber(serialized, "module.slipThreshold", TcsSlipThreshold);
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void ConfigureSpeedLimiter(GameObject vehicleRoot)
    {
        var limiter = FindComponent(vehicleRoot, "SpeedLimiterModuleWrapper") ??
                      throw new InvalidOperationException("Bentley prefab has no NWH speed limiter module.");
        var serialized = new SerializedObject(limiter);
        SetRelativeNumber(serialized, "module.speedLimit", TargetTopSpeedKph);
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void ConfigureFuelCapacity(GameObject vehicleRoot)
    {
        var fuel = FindComponent(vehicleRoot, "FuelModuleWrapper") ??
                   throw new InvalidOperationException("Bentley prefab has no NWH fuel module.");
        var serialized = new SerializedObject(fuel);
        SetRelativeBool(serialized, "module.useFuel", true);
        SetRelativeNumber(serialized, "module.amount", 80f);
        SetRelativeNumber(serialized, "module.capacity", 80f);
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static Component? FindComponent(GameObject root, string fullNameOrTypeName)
    {
        foreach (var component in root.GetComponents<MonoBehaviour>())
        {
            if (component != null &&
                (string.Equals(component.GetType().FullName, fullNameOrTypeName, StringComparison.Ordinal) ||
                 string.Equals(component.GetType().Name, fullNameOrTypeName, StringComparison.Ordinal)))
                return component;
        }
        return null;
    }

    private static Component? FindComponentWithProperty(GameObject root, string propertyName)
    {
        foreach (var component in root.GetComponents<MonoBehaviour>())
        {
            if (component == null)
                continue;
            var serialized = new SerializedObject(component);
            if (serialized.FindProperty(propertyName) != null)
                return component;
        }
        return null;
    }

    private static Transform? FindTransform(Transform root, string name)
    {
        foreach (var candidate in root.GetComponentsInChildren<Transform>(true))
        {
            if (string.Equals(candidate.name, name, StringComparison.Ordinal))
                return candidate;
        }
        return null;
    }

    private static SerializedProperty? FindRelativeProperty(SerializedObject serialized, string path)
    {
        SerializedProperty? current = null;
        foreach (var segment in path.Split('.'))
        {
            current = current == null
                ? serialized.FindProperty(segment)
                : current.FindPropertyRelative(segment);
            if (current == null)
                return null;
        }
        return current;
    }

    private static void SetString(SerializedObject serialized, string name, string value)
    {
        var property = serialized.FindProperty(name);
        if (property?.propertyType != SerializedPropertyType.String)
            throw new InvalidOperationException("Bentley VehicleType string field is missing: " + name);
        property.stringValue = value;
    }

    private static void SetBool(SerializedObject serialized, string name, bool value)
    {
        var property = serialized.FindProperty(name);
        if (property?.propertyType != SerializedPropertyType.Boolean)
            throw new InvalidOperationException("Bentley VehicleType Boolean field is missing: " + name);
        property.boolValue = value;
    }

    private static void SetNumber(SerializedObject serialized, string name, float value)
    {
        var property = serialized.FindProperty(name);
        if (property == null)
            throw new InvalidOperationException("Bentley serialized number field is missing: " + name);
        if (property.propertyType == SerializedPropertyType.Float)
            property.floatValue = value;
        else if (property.propertyType == SerializedPropertyType.Integer)
            property.intValue = Mathf.RoundToInt(value);
        else if (property.propertyType == SerializedPropertyType.Enum)
            property.intValue = Mathf.RoundToInt(value);
        else
            throw new InvalidOperationException("Bentley serialized field is not numeric: " + name);
    }

    private static void SetVector3(SerializedObject serialized, string name, Vector3 value)
    {
        var property = serialized.FindProperty(name);
        if (property?.propertyType != SerializedPropertyType.Vector3)
            throw new InvalidOperationException("Bentley serialized Vector3 field is missing: " + name);
        property.vector3Value = value;
    }

    private static void SetRelativeBool(SerializedObject serialized, string path, bool value)
    {
        var property = FindRelativeProperty(serialized, path);
        if (property?.propertyType != SerializedPropertyType.Boolean)
            throw new InvalidOperationException("Bentley serialized Boolean field is missing: " + path);
        property.boolValue = value;
    }

    private static void SetRelativeNumber(SerializedObject serialized, string path, float value)
    {
        var property = FindRelativeProperty(serialized, path);
        if (property == null)
            throw new InvalidOperationException("Bentley serialized number field is missing: " + path);
        if (property.propertyType == SerializedPropertyType.Float)
            property.floatValue = value;
        else if (property.propertyType == SerializedPropertyType.Integer)
            property.intValue = Mathf.RoundToInt(value);
        else if (property.propertyType == SerializedPropertyType.Enum)
            property.intValue = Mathf.RoundToInt(value);
        else
            throw new InvalidOperationException("Bentley serialized field is not numeric: " + path);
    }
}
