#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using BAModAPI;
using UnityEngine;

namespace BigHax
{
    internal static class BigHaxLogger
    {
        // All diagnostics default to off. Opt in locally without changing release code.
        internal static bool DebugEnabled { get; private set; }
        internal static bool EmployeeDebugEnabled { get; private set; }
        internal static bool SleepDebugEnabled { get; private set; }
        internal static bool BusinessDirtDebugEnabled { get; private set; }
        internal static bool TrafficDebugEnabled { get; private set; }
        internal static bool HappinessDebugEnabled { get; private set; }
        internal static bool UiDebugEnabled { get; private set; }
        private static readonly HashSet<string> Warnings = new HashSet<string>();

        [Serializable]
        private sealed class DiagnosticSettings
        {
            public bool debug = false;
            public bool employee = false;
            public bool sleep = false;
            public bool businessDirt = false;
            public bool traffic = false;
            public bool happiness = false;
            public bool ui = false;
        }

        public static void StartDiagnosticSession(ModContext context)
        {
            Warnings.Clear();
            var settings = new DiagnosticSettings();
            var path = Path.Combine(Application.persistentDataPath, "ModsLocal", "BigHax", "BigHax.Diagnostics.json");
            try
            {
                if (File.Exists(path))
                    settings = JsonUtility.FromJson<DiagnosticSettings>(File.ReadAllText(path)) ?? settings;
            }
            catch (Exception exception)
            {
                context.Logger.Warn("BigHax: could not read local diagnostics configuration: " + exception.Message);
            }
            DebugEnabled = settings.debug;
            EmployeeDebugEnabled = settings.employee;
            SleepDebugEnabled = settings.sleep;
            BusinessDirtDebugEnabled = settings.businessDirt;
            TrafficDebugEnabled = settings.traffic;
            HappinessDebugEnabled = settings.happiness;
            UiDebugEnabled = settings.ui;
            Info(context, "BigHax: diagnostics configured: employee=" + EmployeeDebugEnabled +
                ", happiness=" + HappinessDebugEnabled + ", ui=" + UiDebugEnabled +
                ", gameAssembly=" + typeof(GameInstance).Assembly.GetName().Version +
                ", gameModule=" + typeof(GameInstance).Module.ModuleVersionId +
                ", modVersion=" + typeof(BigHaxLogger).Assembly.GetName().Version + ".");
        }

        public static void Info(ModContext? context, string message)
        {
            if (DebugEnabled)
                context?.Logger.Info(message);
        }

        public static void WarnOnce(ModContext? context, string key, string message)
        {
            if (context == null || !Warnings.Add(key))
                return;
            context.Logger.Warn(message);
        }

        public static void EmployeeDiagnostic(ModContext? context, string message)
        {
            if (DebugEnabled && EmployeeDebugEnabled)
                context?.Logger.Info("BigHax: " + message);
        }

        public static void SleepDiagnostic(ModContext? context, string message)
        {
            if (DebugEnabled && SleepDebugEnabled)
                context?.Logger.Info("BigHax: " + message);
        }

        public static void HappinessDiagnostic(ModContext? context, string message)
        {
            if (DebugEnabled && HappinessDebugEnabled)
                context?.Logger.Info("BigHax: " + message);
        }

        public static void UiDiagnostic(ModContext? context, string message)
        {
            if (DebugEnabled && UiDebugEnabled)
                context?.Logger.Info("BigHax: " + message);
        }

        public static void BusinessDirtDiagnostic(ModContext? context, string message)
        {
            if (DebugEnabled && BusinessDirtDebugEnabled)
                context?.Logger.Info("BigHax: " + message);
        }

        public static void TrafficDiagnostic(ModContext? context, string message)
        {
            if (DebugEnabled && TrafficDebugEnabled)
                context?.Logger.Info("BigHax: " + message);
        }

        [Conditional("BIGHAX_DIAGNOSTICS")]
        public static void Diagnostic(string message) { }

        [Conditional("BIGHAX_DIAGNOSTICS")]
        public static void DiagnosticException(string source, Exception exception) { }
    }
}
