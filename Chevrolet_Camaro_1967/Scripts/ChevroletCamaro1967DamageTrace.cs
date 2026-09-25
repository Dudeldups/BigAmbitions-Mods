#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;
using DamageHandler = NWH.VehiclePhysics2.Damage.DamageHandler;

// Observation only: never changes damage, meshes, queues, collisions or physics.
// Collision callback order is not assumed; outcomes are correlated afterwards.
[DefaultExecutionOrder(-200)]
[DisallowMultipleComponent]
public sealed class ChevroletCamaro1967DamageTrace : MonoBehaviour
{
    private const string Revision = "camaro-damage-trace-v1";
    private const int DetailsPerSecond = 20;
    private const long MaximumFileBytes = 8 * 1024 * 1024;
    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly FieldInfo? VisualCounterField = typeof(ChevroletCamaro1967VisualDamageController).GetField("diagnosticLogs", Fields);
    private static readonly FieldInfo? VisualReadyField = typeof(ChevroletCamaro1967VisualDamageController).GetField("initialized", Fields);
    private static readonly FieldInfo? VisualThresholdField = typeof(ChevroletCamaro1967VisualDamageController).GetField("impactThresholdMps", Fields);
    private static readonly FieldInfo? VisualCooldownField = typeof(ChevroletCamaro1967VisualDamageController).GetField("nextCollisionTime", Fields);
    private static readonly FieldInfo? VisualFiltersField = typeof(ChevroletCamaro1967VisualDamageController).GetField("deformableFilters", Fields);
    private static readonly FieldInfo? LegacyQueueField = typeof(VehicleDeformationController).GetField("_deformationQueue", Fields);
    private static readonly FieldInfo? NativeQueueField = typeof(DamageHandler).GetField("_collisionEvents", Fields);

    private readonly List<MeshState> meshes = new List<MeshState>();
    private readonly List<Vector3> scratchVertices = new List<Vector3>();
    private VehicleController? vehicle;
    private Rigidbody? body;
    private ChevroletCamaro1967VisualDamageController? visual;
    private DamageHandler? nativeDamage;
    private VehicleDeformationController? legacy;
    private StreamWriter? writer;
    private bool session, bound, failed, fileLimitReported;
    private float nextBind, nextSummary, nextFlush, nextStayDetail, nextMeshScan, nextConsoleDetail;
    private float logWindow, scanWindowStart;
    private int windowLines, meshCursor, previousVisualCount = -1, previousSavedCount = -1;
    private int enters, stays, roadCandidates, customCallbacks, meshChanges, droppedLines, contactSequence;
    private int lastEnterSequence, lastEnterFrame = -1, scanWindowEnter;
    private float previousSavedDamage, previousNativeDamage, lastEnterTime, lastEnterFixedTime;
    private Vector3 beforeVelocity;
    private float beforeFixedTime;
    private long fileBytes;
    private string lastContact = "none", filePath = "none";

    internal void Initialize(VehicleController controller)
    {
        vehicle = controller;
        body = controller.GetComponent<Rigidbody>();
        try
        {
            if (controller.controlledByPlayer && !session) BeginSession();
        }
        catch (Exception exception) { Fail("Initialize", exception); }
    }

    private void FixedUpdate()
    {
        if (!session || vehicle == null || !vehicle.controlledByPlayer || body == null) return;
        beforeVelocity = body.velocity;
        beforeFixedTime = Time.fixedTime;
    }

    private void LateUpdate()
    {
        if (failed || vehicle == null) return;
        try
        {
            if (!vehicle.controlledByPlayer)
            {
                EndSession("not-player-controlled");
                return;
            }
            if (!session) BeginSession();
            if (!bound && Time.unscaledTime >= nextBind)
            {
                nextBind = Time.unscaledTime + 1f;
                BindControllers();
            }
            ObserveState();
            // One mesh per rendered frame, with a one-second gap between full
            // passes. Reused lists/arrays; no per-frame all-vehicle/mesh scans.
            if (bound && Time.timeScale > 0f && Time.unscaledTime >= nextMeshScan) ObserveOneMesh();
            if (Time.unscaledTime >= nextSummary)
            {
                nextSummary = Time.unscaledTime + 5f;
                Summary("heartbeat");
            }
            if (Time.unscaledTime >= nextFlush)
            {
                nextFlush = Time.unscaledTime + 1f;
                Flush();
            }
        }
        catch (Exception exception) { Fail("LateUpdate", exception); }
    }

    private void BeginSession()
    {
        if (session || failed || vehicle == null) return;
        session = true;
        bound = false;
        enters = stays = roadCandidates = customCallbacks = meshChanges = droppedLines = contactSequence = 0;
        lastEnterSequence = 0;
        lastEnterFrame = -1;
        lastEnterTime = lastEnterFixedTime = 0f;
        previousVisualCount = previousSavedCount = -1;
        previousSavedDamage = previousNativeDamage = -1f;
        lastContact = "none";
        meshes.Clear();
        meshCursor = windowLines = 0;
        fileBytes = 0;
        fileLimitReported = false;
        var now = Time.unscaledTime;
        nextBind = nextMeshScan = logWindow = now;
        nextSummary = now + 5f;
        nextFlush = now + 1f;
        nextStayDetail = nextConsoleDetail = now;
        scanWindowStart = now;
        scanWindowEnter = 0;
        beforeVelocity = body != null ? body.velocity : Vector3.zero;
        beforeFixedTime = Time.fixedTime;
        try
        {
            var directory = Path.Combine(Application.persistentDataPath, "Chevrolet_Camaro_1967", "Diagnostics");
            Directory.CreateDirectory(directory);
            filePath = Path.Combine(directory, "damage-trace-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) +
                "-" + vehicle.GetInstanceID() + "-" + Guid.NewGuid().ToString("N").Substring(0, 6) + ".log");
            writer = new StreamWriter(new FileStream(filePath, FileMode.CreateNew, FileAccess.Write, FileShare.Read), new UTF8Encoding(false), 16384);
        }
        catch (Exception exception)
        {
            writer = null;
            filePath = "unavailable";
            Debug.LogWarning("[CamaroDamageTrace] File logging unavailable; Player.log summaries remain active: " + exception.Message);
        }
        Emit("SESSION_START", "revision=" + Revision + " mode=observation-only roadProtectionApplied=false file='" + filePath + "'", true, true);
        BindControllers();
        Flush();
    }

    private void BindControllers()
    {
        if (vehicle == null) return;
        visual = vehicle.GetComponent<ChevroletCamaro1967VisualDamageController>();
        nativeDamage = vehicle.GetComponentInChildren<DamageHandler>(true);
        legacy = vehicle.GetComponentInChildren<VehicleDeformationController>(true);
        if (visual == null || !(VisualReadyField?.GetValue(visual) is bool ready) || !ready)
        {
            Emit("BIND_PENDING", "customReady=false " + State(), false);
            return;
        }
        meshes.Clear();
        if (VisualFiltersField?.GetValue(visual) is IList filters)
        {
            foreach (var entry in filters)
            {
                if (entry is MeshFilter filter && filter != null && filter.sharedMesh != null) meshes.Add(new MeshState(filter));
            }
        }
        else
        {
            Emit("BIND_UNAVAILABLE", "Custom mesh list unavailable; contacts and state still recorded.", true, true);
        }
        // Inspect the painted shell before attached small detail meshes.
        meshes.Sort((a, b) => (b.Path.IndexOf("CamaroDamageBody", StringComparison.Ordinal) >= 0 ? 1 : 0)
            .CompareTo(a.Path.IndexOf("CamaroDamageBody", StringComparison.Ordinal) >= 0 ? 1 : 0));
        previousVisualCount = ReadVisualCounter();
        previousSavedDamage = SavedDamage();
        previousNativeDamage = NativeDamage();
        previousSavedCount = SavedDeformationCount();
        bound = true;
        Emit("BOUND", "meshCount=" + meshes.Count + " visualCounterReadable=" + (VisualCounterField != null) +
            " legacyQueueReadable=" + (LegacyQueueField != null) + " nativeQueueReadable=" + (NativeQueueField != null) +
            " baseline=first-observation-not-pristine " + State(), true, true);
    }

    private void ObserveState()
    {
        if (!bound) return;
        var count = ReadVisualCounter();
        var saved = SavedDamage();
        var nwh = NativeDamage();
        var deformations = SavedDeformationCount();
        var callbacks = count >= 0 && previousVisualCount >= 0 ? Math.Max(0, count - previousVisualCount) : 0;
        if (callbacks > 0 || Math.Abs(saved - previousSavedDamage) > 0.0000001f ||
            Math.Abs(nwh - previousNativeDamage) > 0.0000001f || deformations != previousSavedCount)
        {
            customCallbacks += callbacks;
            var details = FormattableString.Invariant($"customCallbacksCompletedDelta={callbacks} savedDamageBefore={previousSavedDamage:F7} savedDamageAfter={saved:F7} nwhDamageBefore={previousNativeDamage:F7} nwhDamageAfter={nwh:F7} savedDeformationsBefore={previousSavedCount} savedDeformationsAfter={deformations} candidateEnter={lastEnterSequence} candidateEnterFrame={lastEnterFrame} candidateEnterFixed={lastEnterFixedTime:F4} candidateAge={Time.unscaledTime - lastEnterTime:F4} counterDoesNotProveMeshChanged=true ");
            Emit("STATE_CHANGE", details + State(), ConsoleDetail());
            // Complete a scan instead of restarting it on every contact.
            nextMeshScan = Math.Min(nextMeshScan, Time.unscaledTime);
        }
        previousVisualCount = count;
        previousSavedDamage = saved;
        previousNativeDamage = nwh;
        previousSavedCount = deformations;
    }

    private void OnCollisionEnter(Collision collision) => ObserveContact(collision, false);
    private void OnCollisionStay(Collision collision) => ObserveContact(collision, true);

    private void ObserveContact(Collision collision, bool stay)
    {
        if (!session || failed || !isActiveAndEnabled || vehicle == null || !vehicle.controlledByPlayer || collision == null) return;
        try
        {
            contactSequence++;
            if (stay)
            {
                stays++;
                if (Time.unscaledTime < nextStayDetail) return;
                nextStayDetail = Time.unscaledTime + 0.5f;
            }
            else
            {
                enters++;
                lastEnterSequence = contactSequence;
                lastEnterFrame = Time.frameCount;
                lastEnterTime = Time.unscaledTime;
                lastEnterFixedTime = Time.fixedTime;
            }
            var other = collision.collider;
            var path = other != null ? Hierarchy(other.transform) : "none";
            var layer = other != null ? other.gameObject.layer : -1;
            var layerName = layer >= 0 ? LayerMask.LayerToName(layer) : "none";
            var tag = other != null ? other.tag : "none";
            var roadLike = other != null && IsRoadLike(other.name, path, tag, layerName);
            var allUpward = collision.contactCount > 0;
            var minNormalY = 1f;
            var maxNormalSpeed = 0f;
            var maxSeparation = float.NegativeInfinity;
            var contacts = new StringBuilder();
            // Inspect every normal, print at most eight contact points.
            // GetContact avoids allocating collision.contacts.
            for (var i = 0; i < collision.contactCount; i++)
            {
                var contact = collision.GetContact(i);
                var normal = contact.normal.normalized;
                minNormalY = Math.Min(minNormalY, normal.y);
                allUpward &= normal.y >= 0.65f;
                maxNormalSpeed = Math.Max(maxNormalSpeed, Math.Abs(Vector3.Dot(collision.relativeVelocity, normal)));
                maxSeparation = Math.Max(maxSeparation, contact.separation);
                if (i >= 8) continue;
                contacts.Append(" c").Append(i).Append("={own='").Append(contact.thisCollider != null ? contact.thisCollider.name : "none")
                    .Append("',world=").Append(V(contact.point)).Append(",local=").Append(V(transform.InverseTransformPoint(contact.point)))
                    .Append(",normal=").Append(V(normal)).Append(",separation=").Append(F(contact.separation)).Append('}');
            }
            var upright = Vector3.Dot(transform.up, Vector3.up);
            var roadCandidate = roadLike && allUpward && upright >= 0.7f;
            if (roadCandidate) roadCandidates++;
            lastContact = "id=" + contactSequence + " path='" + path + "' roadCandidate=" + roadCandidate;
            var relative = collision.relativeVelocity.magnitude;
            var tangent = Mathf.Sqrt(Mathf.Max(0f, relative * relative - maxNormalSpeed * maxNormalSpeed));
            var valid = DamageHandler.IsCollisionValid(collision);
            var mass = body != null ? body.mass : 0f;
            var details = FormattableString.Invariant($"id={contactSequence} other='{path}' otherId={(other != null ? other.GetInstanceID() : 0)} layer={layer} layerName='{layerName}' tag='{tag}' otherBody={(collision.rigidbody != null)} roadNameMatch={roadLike} allNormalsUp={allUpward} roadCandidate={roadCandidate} upright={upright:F4} nwhValid={valid} contactCount={collision.contactCount} minNormalY={minNormalY:F4} relativeMps={relative:F4} maxAbsNormalMps={maxNormalSpeed:F4} tangentAtMaxNormalMps={tangent:F4} impulseNs={collision.impulse.magnitude:F4} impulseOverOwnMassMps={(mass > 0f ? collision.impulse.magnitude / mass : 0f):F4} preFixedTime={beforeFixedTime:F4} preVelocity={V(beforeVelocity)} velocityNow={V(body != null ? body.velocity : Vector3.zero)} maxSeparation={maxSeparation:F5} ");
            Emit(stay ? "CONTACT_STAY_SAMPLE" : "CONTACT_ENTER", details + State() + contacts, false);
        }
        catch (Exception exception) { Fail("collision-observer", exception); }
    }

    private void ObserveOneMesh()
    {
        if (meshes.Count == 0) return;
        if (meshCursor >= meshes.Count) meshCursor = 0;
        if (meshCursor == 0)
        {
            scanWindowStart = Time.unscaledTime;
            scanWindowEnter = contactSequence;
        }
        var state = meshes[meshCursor++];
        if (state.Filter != null && state.Filter.sharedMesh != null && !state.Unreadable)
        {
            var started = System.Diagnostics.Stopwatch.GetTimestamp();
            var mesh = state.Filter.sharedMesh;
            if (!mesh.isReadable)
            {
                state.Unreadable = true;
                Emit("MESH_UNREADABLE", "path='" + state.Path + "'", false);
            }
            else
            {
                mesh.GetVertices(scratchVertices);
                if (state.MeshId != mesh.GetInstanceID() || state.Previous == null || state.Previous.Length != scratchVertices.Count)
                {
                    Emit(state.Previous == null ? "MESH_BASELINE" : "MESH_REPLACED", "path='" + state.Path +
                        "' oldMesh=" + state.MeshId + " mesh=" + mesh.GetInstanceID() + " vertices=" + scratchVertices.Count +
                        " observedBaselineOnly=true", false);
                    state.Previous = scratchVertices.ToArray();
                    state.MeshId = mesh.GetInstanceID();
                }
                else
                {
                    var changed = 0;
                    var maxDelta = 0f;
                    for (var i = 0; i < scratchVertices.Count; i++)
                    {
                        var delta = scratchVertices[i] - state.Previous[i];
                        if (delta.sqrMagnitude > 1e-12f)
                        {
                            changed++;
                            maxDelta = Math.Max(maxDelta, state.Filter.transform.TransformVector(delta).magnitude);
                        }
                        // Private diagnostic copy; never the controller's originals or mesh data.
                        state.Previous[i] = scratchVertices[i];
                    }
                    if (changed > 0)
                    {
                        meshChanges++;
                        var details = FormattableString.Invariant($"path='{state.Path}' verticesChanged={changed} maxWorldDeltaM={maxDelta:F6} observedFrom={state.ObservedAt:F4} contactSeqFrom={state.ContactAtObservation} contactSeqTo={contactSequence} visualCounterFrom={state.CounterAtObservation} visualCounterTo={ReadVisualCounter()} correlationOnly=true ");
                        Emit("MESH_CHANGED", details + State() + " lastContact={" + lastContact + "}", ConsoleDetail());
                    }
                }
                state.ObservedAt = Time.unscaledTime;
                state.ContactAtObservation = contactSequence;
                state.CounterAtObservation = ReadVisualCounter();
            }
            var ms = (System.Diagnostics.Stopwatch.GetTimestamp() - started) * 1000d / System.Diagnostics.Stopwatch.Frequency;
            if (ms >= 8d) Emit("SCAN_COST", FormattableString.Invariant($"mesh='{state.Path}' milliseconds={ms:F3}"), false);
        }
        if (meshCursor >= meshes.Count)
        {
            meshCursor = 0;
            nextMeshScan = Time.unscaledTime + 1f;
            Emit("SCAN_COMPLETE", FormattableString.Invariant($"from={scanWindowStart:F4} contactSeqFrom={scanWindowEnter} meshCount={meshes.Count}"), false);
        }
    }

    private string State() => FormattableString.Invariant($"savedDamage={SavedDamage():F7} nwhDamage={NativeDamage():F7} savedDeformations={SavedDeformationCount()} customEnabled={(visual != null && visual.enabled)} customCallbacksCompleted={ReadVisualCounter()} customThresholdMps={ReadFloat(VisualThresholdField, visual):F3} customCooldownUntil={ReadFloat(VisualCooldownField, visual):F4} nativeEnabled={(nativeDamage != null && nativeDamage.enabled)} nativeMeshDeform={(nativeDamage != null && nativeDamage.meshDeform)} legacyEnabled={(legacy != null && legacy.enabled)} legacyQueue={QueueCount(LegacyQueueField, legacy)} nativeQueue={QueueCount(NativeQueueField, nativeDamage)}");
    private float SavedDamage() => vehicle?.vehicleInstance?.damage ?? -1f;
    private float NativeDamage() => nativeDamage != null ? nativeDamage.Damage : -1f;
    private int SavedDeformationCount() => vehicle?.vehicleInstance?.deformations?.Count ?? -1;
    private int ReadVisualCounter() => visual != null && VisualCounterField?.GetValue(visual) is int count ? count : -1;
    private static float ReadFloat(FieldInfo? field, object? target) => target != null && field?.GetValue(target) is float value ? value : -1f;
    private static int QueueCount(FieldInfo? field, object? target) => target != null && field?.GetValue(target) is ICollection collection ? collection.Count : -1;

    private bool ConsoleDetail()
    {
        if (Time.unscaledTime < nextConsoleDetail) return false;
        nextConsoleDetail = Time.unscaledTime + 1f;
        return true;
    }

    private void Summary(string reason) => Emit("SUMMARY", "reason=" + reason + " enters=" + enters + " stays=" + stays +
        " roadCandidateSamples=" + roadCandidates + " customCallbacksCompleted=" + customCallbacks + " changedMeshObservations=" + meshChanges +
        " detailLinesSuppressed=" + droppedLines + " fileBytes=" + fileBytes + " " + State() + " lastContact={" + lastContact + "}", true, true);

    private void Emit(string kind, string details, bool console, bool always = false)
    {
        var now = Time.unscaledTime;
        if (now - logWindow >= 1f) { logWindow = now; windowLines = 0; }
        if (!always && windowLines >= DetailsPerSecond) { droppedLines++; return; }
        windowLines++;
        var line = "[CamaroDamageTrace] " + kind + " vehicle=" + (vehicle != null ? vehicle.GetInstanceID() : 0) +
            " time=" + F(now) + " frame=" + Time.frameCount + " fixed=" + F(Time.fixedTime) + " " + details;
        if (console) Debug.Log(line);
        if (writer == null) return;
        try
        {
            if (fileBytes >= MaximumFileBytes)
            {
                if (!fileLimitReported)
                {
                    fileLimitReported = true;
                    writer.WriteLine("[CamaroDamageTrace] FILE_LIMIT reached; Player.log summaries continue. Start a new drive session for a new file.");
                    Flush();
                    Debug.LogWarning("[CamaroDamageTrace] Detail file limit reached: " + filePath);
                }
                return;
            }
            writer.WriteLine(line);
            fileBytes += Encoding.UTF8.GetByteCount(line) + 2;
        }
        catch (Exception exception)
        {
            CloseWriter();
            Debug.LogWarning("[CamaroDamageTrace] Detail file write failed: " + exception.Message);
        }
    }

    private void Flush()
    {
        try { writer?.Flush(); }
        catch (Exception exception)
        {
            CloseWriter();
            Debug.LogWarning("[CamaroDamageTrace] Detail file flush failed: " + exception.Message);
        }
    }

    internal void EndSession(string reason)
    {
        if (!session) return;
        try { Summary(reason); }
        catch (Exception exception) { Debug.LogWarning("[CamaroDamageTrace] Final summary failed: " + exception.Message); }
        session = false;
        CloseWriter();
        meshes.Clear();
        scratchVertices.Clear();
        bound = false;
    }

    private void CloseWriter()
    {
        var closing = writer;
        writer = null;
        try { closing?.Dispose(); } catch { /* Diagnostics must not affect the car. */ }
    }

    private void Fail(string scope, Exception exception)
    {
        if (failed) return;
        failed = true;
        Debug.LogWarning("[CamaroDamageTrace] Observer disabled; vehicle unchanged. " + scope + ": " + exception);
        EndSession("observer-failure");
    }

    private void OnDisable() => EndSession("disabled-or-exited");
    private void OnDestroy() => EndSession("destroyed");
    private void OnApplicationQuit() => EndSession("quit");
    private static string F(float value) => value.ToString("0.0000", CultureInfo.InvariantCulture);
    private static string V(Vector3 value) => "(" + F(value.x) + "," + F(value.y) + "," + F(value.z) + ")";
    private static string Hierarchy(Transform target)
    {
        var path = target.name;
        for (var parent = target.parent; parent != null; parent = parent.parent) path = parent.name + "/" + path;
        return path.Replace("\r", " ").Replace("\n", " ");
    }

    // Read-only classification based on AudiRS6RRoadDamageGuard. A candidate,
    // never authority to discard a collision or roll back legitimate damage.
    private static bool IsRoadLike(string name, string path, string tag, string layerName)
    {
        var n = name.ToLowerInvariant();
        var identity = (name + " " + path + " " + tag).ToLowerInvariant();
        var layer = layerName.ToLowerInvariant();
        foreach (var obstacle in ObstacleNames) if (n.Contains(obstacle)) return false;
        if (n.Contains("road")) return true;
        if (layer.Contains("prop")) return false;
        if (layer == "ground" || layer == "terrain" || layer == "road" || layer == "roads") return true;
        return identity.Contains("groundplane") || identity.Contains("ground_plane") || identity.Contains("roadmesh") ||
            identity.Contains("road_mesh") || identity.Contains("roadsurface") || identity.Contains("road_surface") ||
            identity.Contains("asphalt") || identity.Contains("terrain");
    }
    private static readonly string[] ObstacleNames = { "tree", "pine", "trunk", "fence", "wall", "building", "pole", "lamp", "hydrant",
        "barricade", "delimiter", "barrier", "bollard", "curb", "kerb", "sidewalk", "pavement" };

    private sealed class MeshState
    {
        internal MeshState(MeshFilter filter) { Filter = filter; Path = Hierarchy(filter.transform); }
        internal readonly MeshFilter Filter;
        internal readonly string Path;
        internal Vector3[]? Previous;
        internal int MeshId, ContactAtObservation, CounterAtObservation;
        internal float ObservedAt;
        internal bool Unreadable;
    }
}
