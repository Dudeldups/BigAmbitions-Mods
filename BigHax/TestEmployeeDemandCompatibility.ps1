[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Exercise the actual service against new, legacy and unsupported game APIs.
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('BigHaxEmployeeTests-' + [Guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $testRoot
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Scripts/Logic/BigHaxEmployeeDemandService.cs') -Destination (Join-Path $testRoot 'BigHaxEmployeeDemandService.cs')
@'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <OutputType>Exe</OutputType>
    <Nullable>enable</Nullable>
  </PropertyGroup>
</Project>
'@ | Set-Content -LiteralPath (Join-Path $testRoot 'Tests.csproj')
@'
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using BAModAPI;
using BigHax;
using Entities;
using Helpers;
using UI.Smartphone.Apps.Contacts;
static class Program
{
    static void Check(bool pass, string message) { if (!pass) throw new Exception(message); }
    static void Drain(System.Collections.IEnumerator coroutine) { while (coroutine.MoveNext()) { } }
    static void Main()
    {
        const string demandKey = "ba:messagetype_employee_contact_message_new_demand";
        var employee = new EmployeeInstance();
        var candidate = new EmployeeInstance();
        employee.demands.Add("schedule"); candidate.demands.Add("skill");
        employee.Contact.messagesQueue.Enqueue(new TextMessage("other-first", true));
        employee.Contact.messagesQueue.Enqueue(new TextMessage(demandKey, true));
        employee.Contact.messagesQueue.Enqueue(new TextMessage("other-last", false));
        ContactsHelper.Pending.Add(employee.Contact);
        SaveGameManager.Current = new GameInstance();
        SaveGameManager.Current.EmployeeInstances.Add(employee);
        SaveGameManager.Current.CandidateEmployeeInstances.Add(candidate);
        var context = new ModContext();
        var service = new BigHaxEmployeeDemandService();
        var settings = new BigHaxSettings { RemoveEmployeeDemands = true, EnableMaximumEmployeeSatisfaction = true };
        int cleanupSchedules = 0;
        service.SetDailyCleanupScheduler(() => cleanupSchedules++);
        service.ApplyConfiguredBehavior(context, settings);
        Check(employee.demands.Count == 0 && candidate.demands.Count == 0, "initial demands must be cleared");
        Check(employee.satisfaction == 100, "satisfaction must reach 100");
        Check(SaveGameManager.Current.hasEverUsedMods && SaveGameManager.Changes > 0, "changes must be marked");
#if CONTACT_UNSUPPORTED
        Check(employee.Contact.messagesQueue.Count == 3, "unsupported API must not destroy messages");
        Check(BigHaxLogger.Warnings.Count == 1, "unsupported API must warn once");
        Drain(service.RemoveSavedDemandMessagesAfterLoad());
        Check(BigHaxLogger.Warnings.Count == 1, "unsupported API must not spam warnings");
#else
        Check(employee.Calls == 1, "initial contact call must use resolved API");
        Check(employee.Contact.messagesQueue.Select(m => m.messageKey).SequenceEqual(new[] { "other-first", "other-last" }), "non-demand messages and their order must be preserved");
        Check(ContactsHelper.Pending.Contains(employee.Contact), "unrelated unread message notification must remain");
        employee.Contact.messagesQueue.Clear();
        employee.Contact.messagesQueue.Enqueue(new TextMessage(demandKey, true));
        Drain(service.RemoveSavedDemandMessagesAfterLoad());
        Check(employee.Contact.messagesQueue.Count == 0, "post-load demand messages must be removed");
        Check(!ContactsHelper.Pending.Contains(employee.Contact), "empty contact notification must be removed");
#endif
        employee.demands.Add("daily"); employee.satisfaction = 40;
        GameEvent.onGameEventTriggered?.Invoke("ba:gameevent_newday");
        GameEvent.onGameEventTriggered?.Invoke("ba:gameevent_newday");
        Check(cleanupSchedules == 1, "daily cleanup must be scheduled once");
        Drain(service.ClearNewDemandsAfterDailyUpdate());
        Check(employee.demands.Count == 0 && employee.satisfaction == 100, "daily cleanup must still work");
        candidate.demands.Add("candidate");
        GameEvent.onGameEventTriggered?.Invoke("ba:gameevent_candidatereceived");
        Check(candidate.demands.Count == 0, "new candidate demands must be removed");
        employee.demands.Add("hire"); employee.satisfaction = 60;
        GameEvent.onGameEventTriggered?.Invoke("ba:gameevent_employeehired");
        Check(employee.demands.Count == 0 && employee.satisfaction == 100, "hire cleanup must still work");
        GameEvent.onGameEventTriggered = null;
        service.RebindAfterGameLoad(context, settings);
        Check(GameEvent.onGameEventTriggered != null, "save reload must restore event subscription");
        service.Unsubscribe();
        Check(GameEvent.onGameEventTriggered == null, "unload must release event subscription");
        Check(context.Logger.Errors.Count == 0, "cleanup must not throw an exception");
        Console.WriteLine("PASS: demand cleanup, message order/notifications, daily/candidate/hire events, save rebind and unsupported API");
    }
}
namespace BAModAPI
{
    class ModContext { public Logger Logger = new Logger(); }
    class Logger { public List<Exception> Errors = new(); public void Error(Exception e) => Errors.Add(e); }
}
namespace BigHax
{
    class BigHaxSettings { public bool RemoveEmployeeDemands; public bool EnableMaximumEmployeeSatisfaction; }
    static class BigHaxLogger
    {
        public static HashSet<string> Warnings = new();
        public static void EmployeeDiagnostic(ModContext? c, string m) { }
        public static void DiagnosticException(string s, Exception e) { }
        public static void WarnOnce(ModContext? c, string key, string message) => Warnings.Add(key);
    }
}
namespace Entities
{
    class EmployeeInstance
    {
        public List<string> demands = new(); public float satisfaction = 50;
        public Contact Contact = new(); public int Calls = 0;
#if CONTACT_CURRENT
        public Contact GetContact() { Calls++; return Contact; }
#elif CONTACT_LEGACY
        public Contact GetContact(ContactCategoryName category = ContactCategoryName.Employee, bool notify = false)
        {
            if (category != ContactCategoryName.Employee || notify) throw new Exception("wrong optional defaults");
            Calls++; return Contact;
        }
#else
        public Contact GetContact(string requiredNewParameter) => throw new Exception("must not invoke unsupported signature");
#endif
    }
    class Contact
    {
        public Queue<TextMessage> messagesQueue = new();
        public bool HasUnreadMessages => messagesQueue.Any(m => m.unread);
    }
    class TextMessage
    {
        public string messageKey; public bool unread;
        public TextMessage(string key, bool readState) { messageKey = key; unread = readState; }
    }
}
namespace Helpers
{
    static class ContactsHelper
    {
        private static List<Contact> ContactsToSendNotification = new();
        public static List<Contact> Pending => ContactsToSendNotification;
    }
}
namespace UI.Smartphone.Apps.Contacts { enum ContactCategoryName { Employee } }
namespace UnityEngine
{
    static class Mathf
    {
        public static float Min(float a, float b) => Math.Min(a,b);
        public static float Max(float a, float b) => Math.Max(a,b);
        public static float Abs(float a) => Math.Abs(a);
    }
}
class GameInstance
{
    public List<EmployeeInstance> EmployeeInstances = new();
    public List<EmployeeInstance> CandidateEmployeeInstances = new();
    public bool hasEverUsedMods;
}
static class GameEvent { public static Action<string>? onGameEventTriggered; }
static class SaveGameManager
{
    public enum SaveType { Default }
    public static GameInstance? Current;
    public static int Changes;
    public static void MarkChange() => Changes++;
    public static void Save(SaveType type, object? one, object? two) { }
}
'@ | Set-Content -LiteralPath (Join-Path $testRoot 'Program.cs')
foreach ($apiShape in @('CONTACT_CURRENT', 'CONTACT_LEGACY', 'CONTACT_UNSUPPORTED')) {
    Write-Host "[BigHax test] $apiShape"
    & dotnet run --project (Join-Path $testRoot 'Tests.csproj') --configuration Release "-p:DefineConstants=$apiShape"
    if ($LASTEXITCODE -ne 0) { throw "BigHax employee compatibility test failed for $apiShape" }
}
Write-Host '[BigHax test] All employee compatibility scenarios passed.'
