#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine;

namespace MobileVeterinarian
{
    /// <summary>
    /// Describes a vehicle type that should be treated as an animal by Mobile Veterinarian.
    /// Animal vehicle mods can register without Mobile Veterinarian taking a compile-time
    /// dependency on their implementation.
    /// </summary>
    public sealed class AnimalVehicleRegistration
    {
        public AnimalVehicleRegistration(
            string vehicleTypeName,
            string? animalNameKeyOrText = null,
            Vector3? treatmentPositionOffset = null)
        {
            if (string.IsNullOrWhiteSpace(vehicleTypeName))
                throw new ArgumentException("A vehicle type name is required.", nameof(vehicleTypeName));

            VehicleTypeName = vehicleTypeName.Trim();
            AnimalNameKeyOrText = string.IsNullOrWhiteSpace(animalNameKeyOrText)
                ? null
                : animalNameKeyOrText!.Trim();
            TreatmentPositionOffset = treatmentPositionOffset ?? new Vector3(1.75f, 0f, -0.2f);
        }

        public string VehicleTypeName { get; }

        /// <summary>
        /// Optional localization key or already-readable name. When omitted, the vehicle type's
        /// localized display name is used.
        /// </summary>
        public string? AnimalNameKeyOrText { get; }

        /// <summary>Vehicle-local point where the veterinarian should stand to treat the animal.</summary>
        public Vector3 TreatmentPositionOffset { get; }
    }

    public static class AnimalVehicleRegistry
    {
        public const string MootorVehicleTypeName = "mootorvehicle:vehicletype_mootorvehicle";

        private static readonly object Sync = new object();
        private static readonly Dictionary<string, AnimalVehicleRegistration> Registrations =
            new Dictionary<string, AnimalVehicleRegistration>(StringComparer.OrdinalIgnoreCase);

        static AnimalVehicleRegistry()
        {
            Register(new AnimalVehicleRegistration(
                MootorVehicleTypeName,
                "mobileveterinarian:animal_cow",
                new Vector3(1.5f, 0f, 0.25f)));
        }

        public static void Register(AnimalVehicleRegistration registration)
        {
            if (registration == null)
                throw new ArgumentNullException(nameof(registration));

            lock (Sync)
                Registrations[registration.VehicleTypeName] = registration;
        }

        public static void Register(
            string vehicleTypeName,
            string? animalNameKeyOrText = null,
            Vector3? treatmentPositionOffset = null)
        {
            Register(new AnimalVehicleRegistration(vehicleTypeName, animalNameKeyOrText, treatmentPositionOffset));
        }

        public static bool Unregister(string vehicleTypeName)
        {
            if (string.IsNullOrWhiteSpace(vehicleTypeName))
                return false;

            lock (Sync)
                return Registrations.Remove(vehicleTypeName);
        }

        public static bool TryGet(string vehicleTypeName, out AnimalVehicleRegistration registration)
        {
            if (string.IsNullOrWhiteSpace(vehicleTypeName))
            {
                registration = null!;
                return false;
            }

            lock (Sync)
                return Registrations.TryGetValue(vehicleTypeName, out registration!);
        }

        public static IReadOnlyCollection<AnimalVehicleRegistration> GetRegistrations()
        {
            lock (Sync)
                return new List<AnimalVehicleRegistration>(Registrations.Values).AsReadOnly();
        }
    }
}
