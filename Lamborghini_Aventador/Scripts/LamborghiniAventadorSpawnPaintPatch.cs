#nullable enable
using System;
using System.Linq;
using System.Reflection;
using Helpers;

/// <summary>
/// Applies the saved player color at the native point where the vehicle instance
/// and its CarFeatures color are assigned, before the spawned vehicle is shown.
/// </summary>
internal sealed class LamborghiniAventadorSpawnPaintPatch
{
    private const string HarmonyId = "Dudeldups.Lamborghini_Aventador.SpawnPaint";
    private const string CustomColorRestoredEvent = "vehicle-repainter:color-restored";
    private static readonly BindingFlags InstanceMethods =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly MethodInfo? SetVehicleInstanceMethod = FindSetVehicleInstanceMethod();
    private static readonly MethodInfo? SpawnPostfix = typeof(LamborghiniAventadorSpawnPaintPatch)
        .GetMethod(nameof(AfterSetVehicleInstance), BindingFlags.Static | BindingFlags.NonPublic);

    private readonly object harmony;
    private readonly Type harmonyType;
    private readonly BAModAPI.ModContext context;
    private static BAModAPI.ModContext? activeContext;
    private static int lastUnresolvedCount = -1;

    private LamborghiniAventadorSpawnPaintPatch(
        object harmony, Type harmonyType, BAModAPI.ModContext context)
    {
        this.harmony = harmony;
        this.harmonyType = harmonyType;
        this.context = context;
    }

    internal static LamborghiniAventadorSpawnPaintPatch? Install(BAModAPI.ModContext context)
    {
        if (SetVehicleInstanceMethod == null || SpawnPostfix == null)
        {
            context.Logger.Warn(
                "LamborghiniAventador spawn paint: native VehicleController.SetVehicleInstance was not found; " +
                "spawn paint will rely on the next vehicle lifecycle event.");
            return null;
        }

        try
        {
            var assembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(candidate =>
                               string.Equals(candidate.GetName().Name, "0Harmony", StringComparison.OrdinalIgnoreCase))
                           ?? Assembly.Load(new AssemblyName("0Harmony"));
            var harmonyType = assembly.GetType("HarmonyLib.Harmony", throwOnError: true)!;
            var harmonyMethodType = assembly.GetType("HarmonyLib.HarmonyMethod", throwOnError: true)!;
            var harmonyConstructor = harmonyType.GetConstructor(new[] { typeof(string) });
            var harmonyMethodConstructor = harmonyMethodType.GetConstructor(new[] { typeof(MethodInfo) });
            var patchMethod = harmonyType.GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .FirstOrDefault(candidate =>
                {
                    if (!string.Equals(candidate.Name, "Patch", StringComparison.Ordinal)) return false;
                    var parameters = candidate.GetParameters();
                    return parameters.Length == 5 && parameters[0].ParameterType == typeof(MethodBase);
                });
            if (harmonyConstructor == null || harmonyMethodConstructor == null || patchMethod == null)
                throw new MissingMethodException("Harmony 2.x Harmony/Patch API signatures were not found.");

            var harmony = harmonyConstructor.Invoke(new object[] { HarmonyId });
            var postfix = harmonyMethodConstructor.Invoke(new object[] { SpawnPostfix });
            patchMethod.Invoke(harmony, new object?[] { SetVehicleInstanceMethod, null, postfix, null, null });
            activeContext = context;
            GameEvent.onGameEventTriggered -= HandleGameEvent;
            GameEvent.onGameEventTriggered += HandleGameEvent;
            LamborghiniAventadorDiagnostics.Info(context,
                $"LamborghiniAventador spawn paint: installed native saved-color assignment hook " +
                $"method={SetVehicleInstanceMethod.DeclaringType?.FullName}.{SetVehicleInstanceMethod.Name}; " +
                $"subscribed to '{CustomColorRestoredEvent}'.");
            return new LamborghiniAventadorSpawnPaintPatch(harmony, harmonyType, context);
        }
        catch (Exception exception)
        {
            context.Logger.Warn(
                "LamborghiniAventador spawn paint: failed to install native saved-color assignment hook: " +
                $"{exception.GetBaseException().GetType().Name}: {exception.GetBaseException().Message}.");
            return null;
        }
    }

    internal void Shutdown()
    {
        GameEvent.onGameEventTriggered -= HandleGameEvent;
        if (ReferenceEquals(activeContext, context))
            activeContext = null;
        lastUnresolvedCount = -1;

        try
        {
            var unpatchAll = harmonyType.GetMethod("UnpatchAll",
                BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public,
                binder: null, types: new[] { typeof(string) }, modifiers: null);
            if (unpatchAll == null)
                throw new MissingMethodException("Harmony.UnpatchAll(string) is unavailable.");
            unpatchAll.Invoke(unpatchAll.IsStatic ? null : harmony, new object[] { HarmonyId });
        }
        catch (Exception exception)
        {
            context.Logger.Warn(
                "LamborghiniAventador spawn paint hook cleanup failed: " +
                $"{exception.GetBaseException().GetType().Name}: {exception.GetBaseException().Message}.");
        }
    }

    private static MethodInfo? FindSetVehicleInstanceMethod()
    {
        foreach (var method in typeof(VehicleController).GetMethods(InstanceMethods))
            if (string.Equals(method.Name, "SetVehicleInstance", StringComparison.Ordinal))
                return method;
        return null;
    }

    private static void AfterSetVehicleInstance(VehicleController __instance)
    {
        var instance = __instance?.vehicleInstance;
        if (__instance == null || instance == null || !string.Equals(
                instance.vehicleTypeName,
                LamborghiniAventadorMod.VehicleTypeName,
                StringComparison.Ordinal))
            return;

        var context = LamborghiniAventadorVehicleRuntime.SpawnContext;
        if (context == null)
            return;

        LamborghiniAventadorVehicleRuntime.ApplyRuntimeGeometry(__instance, context);

        var glass = __instance.GetComponent<LamborghiniAventadorGlassController>() ??
                    __instance.gameObject.AddComponent<LamborghiniAventadorGlassController>();
        glass.Initialize(context);

        var paint = LamborghiniAventadorPaintController.Attach(
            __instance, context, preferSavedColor: true);
        if (!paint.HasResolvedSavedColor)
        {
            context.Logger.Warn(
                $"LamborghiniAventador spawn paint vehicle={__instance.GetInstanceID()}: " +
                $"saved color '{instance.vehicleColorName}' was not resolved " +
                "at native assignment; a later authoritative color lifecycle event may retry.");
            return;
        }

        LamborghiniAventadorDiagnostics.Info(context,
            $"LamborghiniAventador spawn paint applied vehicle={__instance.GetInstanceID()} " +
            $"savedColor='{instance.vehicleColorName}'.");
    }

    /// <summary>
    /// Vehicle Repainter owns its custom palette outside the game's global
    /// vehicle-color list. Its load restore pass broadcasts this exact event
    /// after applying saved custom colors; refresh the owned player instances
    /// at that state transition, even when no controller existed beforehand.
    /// </summary>
    private static void HandleGameEvent(string eventName)
    {
        if (!string.Equals(eventName, CustomColorRestoredEvent, StringComparison.Ordinal))
            return;

        var vehicles = VehicleHelper.AllPlayerVehicles;
        if (vehicles == null)
            return;

        var context = activeContext ?? LamborghiniAventadorVehicleRuntime.SpawnContext;
        if (context == null)
            return;

        var matched = 0;
        var refreshed = 0;
        var unresolvedSavedColors = 0;
        var changed = 0;
        foreach (var vehicle in vehicles)
        {
            if (vehicle == null || !string.Equals(
                    vehicle.vehicleInstance?.vehicleTypeName,
                    LamborghiniAventadorMod.VehicleTypeName,
                    StringComparison.Ordinal))
                continue;

            matched++;
            var paint = vehicle.GetComponent<LamborghiniAventadorPaintController>() ??
                        LamborghiniAventadorPaintController.Attach(vehicle, context, preferSavedColor: true);
            var revisionBefore = paint.AppliedRevision;
            paint.RefreshColor(preferSavedColor: true);
            var tintChanged = paint.AppliedRevision != revisionBefore;
            if (tintChanged) changed++;
            if (paint.HasResolvedSavedColor)
                refreshed++;
            else if (!string.IsNullOrEmpty(vehicle.vehicleInstance?.vehicleColorName))
                unresolvedSavedColors++;

            if (tintChanged && LamborghiniAventadorDiagnostics.DebugEnabled &&
                LamborghiniAventadorPaintController.AppearanceDebugEnabled)
                context.Logger.Info(
                    $"LamborghiniAventador saved paint refresh vehicle={vehicle.GetInstanceID()} " +
                    $"savedColor='{vehicle.vehicleInstance?.vehicleColorName}' " +
                    $"liveColor='{vehicle.CarFeatures?.VehicleColor?.name}' " +
                    $"applied={paint.HasAppliedColor} resolved={paint.HasResolvedSavedColor}.");
        }

        if (unresolvedSavedColors > 0 && unresolvedSavedColors != lastUnresolvedCount)
            context.Logger.Warn(
                $"LamborghiniAventador saved paint refresh after '{CustomColorRestoredEvent}': " +
                $"resolved {refreshed}/{matched} target vehicle colors; " +
                $"{unresolvedSavedColors} saved color name(s) remain unresolved.");

        if ((changed > 0 || unresolvedSavedColors != lastUnresolvedCount) &&
            LamborghiniAventadorDiagnostics.DebugEnabled &&
            LamborghiniAventadorPaintController.AppearanceDebugEnabled)
            context.Logger.Info(
                $"LamborghiniAventador saved paint restoration event matched={matched} " +
                $"resolved={refreshed} changed={changed} unresolvedSavedColors={unresolvedSavedColors}.");
        lastUnresolvedCount = unresolvedSavedColors;
    }
}
