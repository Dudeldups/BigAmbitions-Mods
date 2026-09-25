#nullable enable
using System.Collections.Generic;
using System.Diagnostics;
using BAModAPI;

namespace BigHax
{
    internal static class BigHaxLogger
    {
        // Enable these only while investigating a specific report.
        internal static readonly bool DebugEnabled = false;
        internal static readonly bool EmployeeDebugEnabled = false;
        internal static readonly bool SleepDebugEnabled = false;
        private static readonly HashSet<string> Warnings = new HashSet<string>();

        public static void Info(ModContext? context, string message)
        {
            context?.Logger.Info(message);
        }

        public static void WarnOnce(ModContext? context, string key, string message)
        {
            if (!Warnings.Add(key))
                return;

            context?.Logger.Warn(message);
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

        [Conditional("BIGHAX_DIAGNOSTICS")]
        public static void StartDiagnosticSession() { }

        [Conditional("BIGHAX_DIAGNOSTICS")]
        public static void Diagnostic(string message) { }

        [Conditional("BIGHAX_DIAGNOSTICS")]
        public static void DiagnosticException(string source, System.Exception exception) { }
    }
}
