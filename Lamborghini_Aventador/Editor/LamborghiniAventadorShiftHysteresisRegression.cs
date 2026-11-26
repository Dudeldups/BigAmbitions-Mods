#nullable enable
using System;
using UnityEditor;
using UnityEngine;

/// <summary>Verifies that an Aventador upshift cannot immediately cross its downshift threshold.</summary>
public static class LamborghiniAventadorShiftHysteresisRegression
{
    private const string PrefabPath = "Assets/Mods/Lamborghini_Aventador/LamborghiniAventador.prefab";
    private const float RequiredLandingMarginRpm = 600f;
    private const float MaximumCompensatedTorqueMarginNm = 15f;
    private const float DrivelineEfficiency = 0.85f;
    private const float EngineLossPercent = 0.22f;
    private static readonly float[] FactoryRatios = { -2.929f, 0f, 3.909f, 2.438f, 1.810f, 1.458f, 1.185f, 0.967f, 0.844f };

    [MenuItem("Big Ambitions Mods/Lamborghini Aventador/Verify Shift Hysteresis")]
    public static void VerifyShiftHysteresis()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab == null)
            throw new InvalidOperationException("Aventador shift hysteresis regression could not load its prefab.");

        var controller = prefab.GetComponent<NWH.VehiclePhysics2.VehicleController>();
        if (controller == null)
            throw new InvalidOperationException("Aventador shift hysteresis regression found no NWH vehicle controller.");

        var serialized = new SerializedObject(controller);
        var upshift = serialized.FindProperty("powertrain.transmission._upshiftRPM");
        var downshift = serialized.FindProperty("powertrain.transmission._downshiftRPM");
        var ratios = serialized.FindProperty("powertrain.transmission.gears");
        var clutchSlipTorque = serialized.FindProperty("powertrain.clutch.slipTorque");
        if (upshift?.propertyType != SerializedPropertyType.Float ||
            downshift?.propertyType != SerializedPropertyType.Float ||
            clutchSlipTorque?.propertyType != SerializedPropertyType.Float ||
            ratios == null || !ratios.isArray || ratios.arraySize != FactoryRatios.Length)
            throw new InvalidOperationException("Aventador transmission shift thresholds or factory ratio array are missing.");

        for (var index = 0; index < FactoryRatios.Length; index++)
        {
            var actual = ratios.GetArrayElementAtIndex(index).floatValue;
            if (Mathf.Abs(actual - FactoryRatios[index]) > 0.001f)
                throw new InvalidOperationException(
                    $"Aventador factory gear ratio changed at index={index}: expected={FactoryRatios[index]:0.###}, actual={actual:0.###}.");
        }

        if (Mathf.Abs(upshift.floatValue - LamborghiniAventadorSetup.AutomaticUpshiftRpm) > 0.5f ||
            Mathf.Abs(downshift.floatValue - LamborghiniAventadorSetup.AutomaticDownshiftRpm) > 0.5f)
            throw new InvalidOperationException(
                $"Aventador shift calibration mismatch: upshift={upshift.floatValue:0}, downshift={downshift.floatValue:0}.");

        const float authoredPeakTorqueNm = 720f;
        var maximumCompensatedTorqueNm = authoredPeakTorqueNm * (DrivelineEfficiency + EngineLossPercent);
        var requiredClutchTorqueNm = maximumCompensatedTorqueNm + MaximumCompensatedTorqueMarginNm;
        var expectedClutchTorqueNm = authoredPeakTorqueNm * LamborghiniAventadorSetup.ClutchTorqueSafetyFactor;
        if (Mathf.Abs(clutchSlipTorque.floatValue - expectedClutchTorqueNm) > 0.5f ||
            clutchSlipTorque.floatValue < requiredClutchTorqueNm)
            throw new InvalidOperationException(
                $"Aventador clutch capacity does not clear compensated peak torque: capacity={clutchSlipTorque.floatValue:0} Nm, " +
                $"compensatedPeak={maximumCompensatedTorqueNm:0.0} Nm, required={requiredClutchTorqueNm:0.0} Nm.");

        // Gear indices 2 and 3 are first and second (reverse and neutral precede them).
        var firstGear = ratios.GetArrayElementAtIndex(2).floatValue;
        var secondGear = ratios.GetArrayElementAtIndex(3).floatValue;
        var predictedSecondGearRpm = upshift.floatValue * secondGear / firstGear;
        var landingMarginRpm = predictedSecondGearRpm - downshift.floatValue;
        for (var gearIndex = 2; gearIndex < ratios.arraySize - 1; gearIndex++)
        {
            var landing = upshift.floatValue * ratios.GetArrayElementAtIndex(gearIndex + 1).floatValue /
                          ratios.GetArrayElementAtIndex(gearIndex).floatValue;
            if (landing - downshift.floatValue < RequiredLandingMarginRpm)
                throw new InvalidOperationException(
                    $"Aventador {gearIndex - 1}->{gearIndex} upshift crosses its downshift margin: landing={landing:0} rpm.");
        }
        if (landingMarginRpm < RequiredLandingMarginRpm)
            throw new InvalidOperationException(
                $"Aventador 1->2 shift has insufficient downshift hysteresis: landing={predictedSecondGearRpm:0} rpm, " +
                $"downshift={downshift.floatValue:0} rpm, margin={landingMarginRpm:0} rpm; required={RequiredLandingMarginRpm:0} rpm.");

        Debug.Log(
            $"Aventador shift hysteresis verified: first-to-second landing={predictedSecondGearRpm:0} rpm, " +
            $"downshift={downshift.floatValue:0} rpm, margin={landingMarginRpm:0} rpm, " +
            $"clutch={clutchSlipTorque.floatValue:0} Nm, compensatedPeak={maximumCompensatedTorqueNm:0.0} Nm, " +
            $"factoryRatios={FactoryRatios.Length}.");
    }
}
