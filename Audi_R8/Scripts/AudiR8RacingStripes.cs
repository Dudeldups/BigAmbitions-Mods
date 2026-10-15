#nullable enable
using System;
using System.Collections.Generic;
using BAModAPI;

internal static class AudiR8RacingStripes
{
    private const string SaveKeyPrefix = "Audi_R8:racing-stripes:";
    private static ModContext? context;
    internal static event Action<string>? Changed;

    internal static bool GetRacingStripes(GameInstance? save, string? vehicleId) =>
        save?.modData == null || string.IsNullOrEmpty(vehicleId) ||
        !save.modData.TryGetValue(SaveKeyPrefix + vehicleId, out var value) || value != "0";

    internal static bool SetRacingStripes(GameInstance save, string vehicleId, bool enabled)
    {
        if (string.IsNullOrEmpty(vehicleId) || GetRacingStripes(save, vehicleId) == enabled) return false;
        save.modData ??= new Dictionary<string, string>();
        save.modData[SaveKeyPrefix + vehicleId] = enabled ? "1" : "0";
        if (ReferenceEquals(save, SaveGameManager.Current)) SaveGameManager.MarkChange();
        Changed?.Invoke(vehicleId);
        AudiR8Diagnostics.StripesInfo(context, $"AudiR8 stripes saved vehicle='{vehicleId}': enabled={enabled}.");
        return true;
    }

    internal static void Initialize(ModContext modContext) => context = modContext;
    internal static void Shutdown() => context = null;
}
