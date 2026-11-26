#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BAModAPI;
using Helpers;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

/// <summary>
/// Binds mod-owned runtime components to saved player vehicles from lifecycle
/// events. It does not poll the vehicle list from a frame update.
/// </summary>
internal sealed class LamborghiniAventadorVehicleRuntime : MonoBehaviour
{
    private const float BodyLocalY = -0.020f;
    private const float ExitReachabilityProbeDistance = 1.25f;
    private const float ExitSidewalkProbeDistance = 1.25f;
    private const float ExitReachabilitySampleRadius = 0.8f;
    private const float ExitCarveClearance = 0.05f;
    internal static ModContext? SpawnContext { get; private set; }
    private static readonly HashSet<int> ReportedRuntimeSetupFailures = new HashSet<int>();
    private static readonly HashSet<int> ReportedGeometryConfigurations = new HashSet<int>();
    private static readonly HashSet<int> ReportedNavMeshObstacleConfigurations = new HashSet<int>();
    private const string ExitDiagnosticsMarker = "exit-safety-diagnostics.enabled";
    private ModContext? context;
    private string vehicleTypeName = string.Empty;
    private bool isShuttingDown;
    private bool warehouseDebugEnabled;
    private Coroutine? exitedPlayerRecoveryCoroutine;

    internal static LamborghiniAventadorVehicleRuntime Initialize(
        ModContext context, string vehicleTypeName)
    {
        var runtime = FindObjectOfType<LamborghiniAventadorVehicleRuntime>();
        if (runtime == null)
        {
            var host = new GameObject(nameof(LamborghiniAventadorVehicleRuntime));
            DontDestroyOnLoad(host);
            runtime = host.AddComponent<LamborghiniAventadorVehicleRuntime>();
        }

        runtime.context = context;
        SpawnContext = context;
        runtime.vehicleTypeName = vehicleTypeName;
        runtime.isShuttingDown = false;
        runtime.Subscribe();
        var exitDiagnosticsMarker = Path.Combine(Application.persistentDataPath,
            "ModsLocal", "Lamborghini_Aventador", "Config", ExitDiagnosticsMarker);
        if (File.Exists(exitDiagnosticsMarker))
        {
            LamborghiniAventadorDiagnostics.DebugEnabled = true;
            LamborghiniAventadorDiagnostics.ExitSafetyDebugEnabled = true;
            context.Logger.Info("LamborghiniAventador exit-safety diagnostics enabled.");
        }
        var diagnosticsFolder = Path.GetDirectoryName(exitDiagnosticsMarker)!;
        if (File.Exists(Path.Combine(diagnosticsFolder, "appearance-diagnostics.enabled")))
        {
            LamborghiniAventadorDiagnostics.DebugEnabled = true;
            LamborghiniAventadorPaintController.AppearanceDebugEnabled = true;
            context.Logger.Info("LamborghiniAventador appearance diagnostics enabled.");
        }
        if (File.Exists(Path.Combine(diagnosticsFolder, "audio-diagnostics.enabled")))
        {
            LamborghiniAventadorDiagnostics.DebugEnabled = true;
            LamborghiniAventadorAudioModel.AudioDebugEnabled = true;
            context.Logger.Info("LamborghiniAventador audio diagnostics enabled.");
        }
        if (File.Exists(Path.Combine(diagnosticsFolder, "powertrain-diagnostics.enabled")))
        {
            LamborghiniAventadorDiagnostics.DebugEnabled = true;
            LamborghiniAventadorDiagnostics.PowertrainDebugEnabled = true;
            context.Logger.Info("LamborghiniAventador powertrain diagnostics enabled.");
        }
        if (File.Exists(Path.Combine(diagnosticsFolder, "caliper-diagnostics.enabled")))
        {
            LamborghiniAventadorDiagnostics.DebugEnabled = true;
            LamborghiniAventadorDiagnostics.CaliperDebugEnabled = true;
            context.Logger.Info("LamborghiniAventador caliper diagnostics enabled.");
        }
        if (File.Exists(Path.Combine(diagnosticsFolder, "damage-diagnostics.enabled")))
        {
            LamborghiniAventadorDiagnostics.DebugEnabled = true;
            LamborghiniAventadorVisualDamageController.DamageDebugEnabled = true;
            context.Logger.Info("LamborghiniAventador damage diagnostics enabled.");
        }
        if (File.Exists(Path.Combine(diagnosticsFolder, "lighting-diagnostics.enabled")))
        {
            LamborghiniAventadorDiagnostics.DebugEnabled = true;
            LamborghiniAventadorLightingController.LightingDebugEnabled = true;
            context.Logger.Info("LamborghiniAventador lighting diagnostics enabled.");
        }
        runtime.warehouseDebugEnabled = File.Exists(Path.Combine(diagnosticsFolder, "warehouse-diagnostics.enabled"));
        if (runtime.warehouseDebugEnabled)
            LamborghiniAventadorDiagnostics.DebugEnabled = true;
        if (File.Exists(Path.Combine(diagnosticsFolder, "driver-diagnostics.enabled")))
        {
            LamborghiniAventadorDiagnostics.DebugEnabled = true;
            LamborghiniAventadorDiagnostics.DriverDebugEnabled = true;
            context.Logger.Info("LamborghiniAventador driver diagnostics enabled.");
        }
        GlobalEvents.RegisterOnGameLoadedLateCallback(runtime.HandleGameLoadedLate);
        runtime.ConfigureOwnedPlayerVehicles("mod-load");
        return runtime;
    }

    internal void Shutdown()
    {
        isShuttingDown = true;
        Unsubscribe();
        LamborghiniAventadorPowertrainCalibration.ReleaseAll();
        SpawnContext = null;
        context = null;
        vehicleTypeName = string.Empty;
        Destroy(gameObject);
    }

    private void OnEnable() => Subscribe();

    private void OnDisable()
    {
        Unsubscribe();
        if (exitedPlayerRecoveryCoroutine != null)
            StopCoroutine(exitedPlayerRecoveryCoroutine);
        exitedPlayerRecoveryCoroutine = null;
    }

    private void OnDestroy()
    {
        if (exitedPlayerRecoveryCoroutine != null)
            StopCoroutine(exitedPlayerRecoveryCoroutine);
        exitedPlayerRecoveryCoroutine = null;
    }

    private void Subscribe()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        SceneManager.sceneLoaded += HandleSceneLoaded;
        GlobalEvents.onVehicleVariablesChanged -= HandleVehicleVariablesChanged;
        GlobalEvents.onVehicleVariablesChanged += HandleVehicleVariablesChanged;
        GlobalEvents.onEnterVehicle -= HandleVehicleEntered;
        GlobalEvents.onEnterVehicle += HandleVehicleEntered;
        GlobalEvents.onExitVehicle -= HandleVehicleExited;
        GlobalEvents.onExitVehicle += HandleVehicleExited;
        GlobalEvents.onExitBuilding -= HandleBuildingExited;
        GlobalEvents.onExitBuilding += HandleBuildingExited;
        GameEvent.onGameEventTriggered -= HandleGameEvent;
        GameEvent.onGameEventTriggered += HandleGameEvent;
        GlobalEvents.onGameUnloaded -= HandleGameUnloaded;
        GlobalEvents.onGameUnloaded += HandleGameUnloaded;
    }

    private void Unsubscribe()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        GlobalEvents.onVehicleVariablesChanged -= HandleVehicleVariablesChanged;
        GlobalEvents.onEnterVehicle -= HandleVehicleEntered;
        GlobalEvents.onExitVehicle -= HandleVehicleExited;
        GlobalEvents.onExitBuilding -= HandleBuildingExited;
        GameEvent.onGameEventTriggered -= HandleGameEvent;
        GlobalEvents.onGameUnloaded -= HandleGameUnloaded;
    }

    private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        Subscribe();
        ConfigureOwnedPlayerVehicles("scene-loaded:" + scene.name);
    }

    private void HandleGameLoadedLate() => ConfigureOwnedPlayerVehicles("game-loaded-late");

    private void HandleGameUnloaded()
    {
        if (exitedPlayerRecoveryCoroutine != null)
            StopCoroutine(exitedPlayerRecoveryCoroutine);
        exitedPlayerRecoveryCoroutine = null;
        LamborghiniAventadorPowertrainCalibration.ReleaseAll();
        // Components belong to individual vehicle instances. The next late-load
        // callback or vehicle change will bind the fresh save's instances.
    }

    private void HandleVehicleVariablesChanged() =>
        ConfigureOwnedPlayerVehicles("vehicle-variables-changed");

    private void HandleBuildingExited(Address address)
    {
        if (!LamborghiniAventadorDiagnostics.DebugEnabled || !warehouseDebugEnabled || address == null)
            return;
        var vehicle = VehicleHelper.GetCurrentVehicleBase();
        if (!IsOwnedVehicle(vehicle) || vehicle == null || !vehicle.controlledByPlayer ||
            !string.Equals(BuildingHelper.GetBuilding(address)?.BuildingType,
                "ba:buildingtype_warehouse", StringComparison.Ordinal))
            return;

        var placement = vehicle.GetComponent<MeshCollider>();
        context?.Logger.Info($"LamborghiniAventador warehouse exit completed vehicle={vehicle.GetInstanceID()} " +
            $"position={vehicle.transform.position}, placementBounds={placement?.sharedMesh?.bounds.size}, " +
            $"placementColliderEnabled={placement?.enabled}.");
    }

    private void HandleVehicleEntered(VehicleController vehicle)
    {
        StopExitRecovery();
        ConfigureVehicle(vehicle, "player-entered");
        vehicle.GetComponent<LamborghiniAventadorGlassController>()?.RestoreAfterVehicleEntered();
        if (IsOwnedVehicle(vehicle))
            vehicle.GetComponent<LamborghiniAventadorDriverController>()?.RefreshOccupancy();
    }

    private void HandleVehicleExited(VehicleController vehicle)
    {
        if (!IsOwnedVehicle(vehicle))
            return;
        var audio = vehicle.GetComponent<LamborghiniAventadorAudioController>();
        if (audio != null)
            audio.enabled = false;
        vehicle.GetComponent<LamborghiniAventadorWarehouseEntryController>()?.SetOccupied(false);
        vehicle.GetComponent<LamborghiniAventadorDriverController>()?.SetOccupied(false);
        if (exitedPlayerRecoveryCoroutine != null)
            StopCoroutine(exitedPlayerRecoveryCoroutine);
        exitedPlayerRecoveryCoroutine = StartCoroutine(RecoverPlayerNavMeshAfterExit(vehicle));
    }

    private void StopExitRecovery()
    {
        if (exitedPlayerRecoveryCoroutine != null)
            StopCoroutine(exitedPlayerRecoveryCoroutine);
        exitedPlayerRecoveryCoroutine = null;
    }

    private static float GetCarveSettleDuration(VehicleController vehicle)
    {
        var duration = 0f;
        foreach (var obstacle in vehicle.GetComponentsInChildren<NavMeshObstacle>(true))
        {
            if (obstacle == null || !obstacle.enabled || !obstacle.carving ||
                !obstacle.carveOnlyStationary)
                continue;
            duration = Mathf.Max(duration, Mathf.Max(0f, obstacle.carvingTimeToStationary));
        }
        return duration;
    }

    private IEnumerator RecoverPlayerNavMeshAfterExit(VehicleController exitedVehicle)
    {
        if (exitedVehicle == null || isShuttingDown)
        {
            exitedPlayerRecoveryCoroutine = null;
            yield break;
        }

        // onExitVehicle is raised inside VehicleController.ExitVehicle before
        // vanilla enables obstacle carving. Let that synchronous method finish
        // before reading the obstacle state and its stationary delay.
        yield return null;
        if (exitedVehicle == null || isShuttingDown)
        {
            exitedPlayerRecoveryCoroutine = null;
            yield break;
        }

        // Unity delays carving for stationary-only obstacles. Wait once for the
        // configured native delay so validation sees the settled carve, rather
        // than stale NavMesh data that can change immediately after exit.
        var carveSettleDuration = GetCarveSettleDuration(exitedVehicle);
        if (carveSettleDuration > 0f)
        {
            ExitSafetyInfo(
                $"exit validation waiting once for stationary NavMesh carve: " +
                $"duration={carveSettleDuration:F3}s,vehicle={exitedVehicle.GetInstanceID()}; " +
                $"{DescribeVehicleNavMeshState(exitedVehicle)}.");
            yield return new WaitForSecondsRealtime(carveSettleDuration);
        }
        else
        {
            ExitSafetyInfo(
                $"exit validation found no enabled stationary carving obstacle after native exit; " +
                $"vehicle={exitedVehicle.GetInstanceID()}; {DescribeVehicleNavMeshState(exitedVehicle)}.");
        }
        yield return new WaitForEndOfFrame();

        if (exitedVehicle == null || isShuttingDown)
        {
            exitedPlayerRecoveryCoroutine = null;
            yield break;
        }
        var root = PlayerHelper.PlayerController?.transform;
        if (root == null)
        {
            exitedPlayerRecoveryCoroutine = null;
            yield break;
        }

        var nativePlayerAgent = PlayerHelper.PlayerController?.Character?.navmeshAgent;
        var agents = new List<NavMeshAgent>(root.GetComponentsInChildren<NavMeshAgent>(true));
        if (nativePlayerAgent != null && !agents.Contains(nativePlayerAgent))
            agents.Add(nativePlayerAgent);
        var hasActiveMovementAgent = nativePlayerAgent != null && nativePlayerAgent.enabled &&
                                     nativePlayerAgent.gameObject.activeInHierarchy;
        var hasEnabledAgentOffNavMesh = hasActiveMovementAgent && !nativePlayerAgent!.isOnNavMesh;
        var exitClear = IsPlayerExitClear(root, root.position);
        var playerRadius = GetPlayerExitRadius(root);
        var insideCarvedFootprint = IsInsideCarvedObstacleFootprint(
            exitedVehicle, root.position, playerRadius, ExitCarveClearance);
        var reachable = TryValidateExitReachability(
            root, exitedVehicle, agents, out var reachableTargets, out var reachabilityDetails);
        if (LamborghiniAventadorDiagnostics.DebugEnabled &&
            LamborghiniAventadorDiagnostics.ExitSafetyDebugEnabled)
            ExitSafetyInfo(
                $"exit validation vehicle={exitedVehicle.GetInstanceID()} player={root.position} " +
                $"agents={agents.Count} enabledAgentOffNavMesh={hasEnabledAgentOffNavMesh} " +
                $"nativeAgent={(nativePlayerAgent != null ? nativePlayerAgent.name : "<missing>")}," +
                $"activeNativeMovementAgent={hasActiveMovementAgent}," +
                $"clear={exitClear} insideCarvedFootprint={insideCarvedFootprint} " +
                $"reachableTargets={reachableTargets} reachabilityDiagnostic={reachable}; " +
                $"{DescribePlayerNavMeshState(root, agents)}; " +
                $"{DescribeVehicleNavMeshState(exitedVehicle)}; routes=[{reachabilityDetails}].");

        // The game moves the player through Character.navmeshAgent. A collider
        // component or a different child agent is not proof that the native
        // movement agent is ready to synchronize after a correction. In this
        // state, keep the native exit placement and emit evidence for diagnosis.
        if (!hasActiveMovementAgent)
        {
            context?.Logger.Warn(
                $"LamborghiniAventador exit recovery skipped because the native player NavMeshAgent " +
                $"is {(nativePlayerAgent == null ? "missing" : nativePlayerAgent.enabled ? "inactive" : "disabled")}; " +
                $"no player relocation was attempted. vehicle={exitedVehicle.GetInstanceID()}, " +
                $"player={root.position},clear={exitClear},insideCarvedFootprint={insideCarvedFootprint}," +
                $"reachabilityDiagnostic={reachable},routes=[{reachabilityDetails}]. " +
                $"{DescribePlayerNavMeshState(root, agents)}.");
            exitedPlayerRecoveryCoroutine = null;
            yield break;
        }

        // All probes are local. A single reachable direction proves egress;
        // recovery is considered only when the player has no nearby route.
        var needsRecovery = hasEnabledAgentOffNavMesh || !exitClear || insideCarvedFootprint;
        if (reachableTargets == 0)
            needsRecovery = true;
        var recoveryReason = hasEnabledAgentOffNavMesh ? "native-agent-off-navmesh" :
            !exitClear ? "capsule-overlap" : insideCarvedFootprint ? "inside-carve" :
            reachableTargets == 0 ? "no-local-egress-route" : "none";
        ExitSafetyInfo(
            $"exit recovery decision vehicle={exitedVehicle.GetInstanceID()},recover={needsRecovery}," +
            $"reason={recoveryReason},reachableTargets={reachableTargets},player={root.position}.");
        if (!needsRecovery)
        {
            exitedPlayerRecoveryCoroutine = null;
            yield break;
        }

        if (!TryFindClearExitPosition(root, exitedVehicle, agents, out var target))
        {
            context?.Logger.Warn(
                $"LamborghiniAventador exit recovery could not find a clear, reachable position " +
                $"outside the vehicle carve; player remains at {root.position}, " +
                $"vehicle={exitedVehicle.GetInstanceID()}. {DescribePlayerNavMeshState(root, agents)}; " +
                $"{DescribeVehicleNavMeshState(exitedVehicle)}.");
            exitedPlayerRecoveryCoroutine = null;
            yield break;
        }

        PlacePlayerAtSafeExit(root, agents, target, context, exitedVehicle.GetInstanceID());
        ExitSafetyInfo(
            $"exit recovery moved player to {target} for vehicle={exitedVehicle.GetInstanceID()}; " +
            $"{DescribePlayerNavMeshState(root, agents)}.");
        exitedPlayerRecoveryCoroutine = null;
    }

    private static void PlacePlayerAtSafeExit(
        Transform playerRoot, IReadOnlyList<NavMeshAgent> agents, Vector3 target,
        ModContext? context, int vehicleId)
    {
        var characterControllers = playerRoot.GetComponentsInChildren<CharacterController>(true);
        var controllerStates = new bool[characterControllers.Length];
        for (var index = 0; index < characterControllers.Length; index++)
            controllerStates[index] = characterControllers[index] != null && characterControllers[index].enabled;
        var agentStates = new bool[agents.Count];
        var agentOffsets = new Vector3[agents.Count];
        for (var index = 0; index < agents.Count; index++)
        {
            var agent = agents[index];
            agentStates[index] = agent != null && agent.enabled;
            if (agent != null)
                agentOffsets[index] = agent.transform.position - playerRoot.position;
        }
        try
        {
            foreach (var controller in characterControllers)
                if (controller != null) controller.enabled = false;
            foreach (var agent in agents)
                if (agent != null) agent.enabled = false;
            playerRoot.position = target;
            Physics.SyncTransforms();
        }
        finally
        {
            for (var index = 0; index < agents.Count; index++)
            {
                var agent = agents[index];
                if (agent == null) continue;
                // Preserve the game's lifecycle state. Recovery is entered
                // only while the native movement agent is active; never force-
                // enable an agent that the game had disabled.
                agent.enabled = agentStates[index];
                if (agent.enabled && agent.gameObject.activeInHierarchy)
                {
                    if (!agent.Warp(target + agentOffsets[index]))
                        context?.Logger.Warn(
                            $"LamborghiniAventador exit recovery could not warp agent to {target} " +
                            $"for vehicle={vehicleId}; agent remains at {agent.transform.position}.");
                    else
                        agent.ResetPath();
                }
            }
            for (var index = 0; index < characterControllers.Length; index++)
                if (characterControllers[index] != null)
                    characterControllers[index].enabled = controllerStates[index];
            Physics.SyncTransforms();
        }
    }

    private bool TryFindClearExitPosition(
        Transform playerRoot, VehicleController exitedVehicle,
        IReadOnlyList<NavMeshAgent> agents, out Vector3 target)
    {
        var vehicleTransform = exitedVehicle.transform;
        var driverMarker = FindChildTransform(vehicleTransform, "Driverside");
        var passengerMarker = FindChildTransform(vehicleTransform, "Passengerside");
        Transform? nearestExitMarker = null;
        if (driverMarker != null && passengerMarker != null)
        {
            nearestExitMarker = Vector3.Distance(playerRoot.position, driverMarker.position) <=
                                Vector3.Distance(playerRoot.position, passengerMarker.position)
                ? driverMarker
                : passengerMarker;
        }
        else
            nearestExitMarker = driverMarker ?? passengerMarker;

        // A failed native exit can be corrected only to its current-side
        // authored marker. The opposite side and broad radius fallbacks would
        // teleport through the car or onto a nearby disconnected NavMesh island.
        var candidates = new List<Vector3>(2) { playerRoot.position };
        if (nearestExitMarker != null &&
            Vector3.Distance(nearestExitMarker.position, playerRoot.position) > 0.05f)
            candidates.Add(nearestExitMarker.position);

        GetPlayerNavMeshQueryFilter(agents, out var filter, out _);
        var playerRadius = GetPlayerExitRadius(playerRoot);
        foreach (var candidate in candidates)
        {
            if (!NavMesh.SamplePosition(candidate, out var hit, 1.25f, filter))
                continue;
            var sampled = hit.position + Vector3.up * 0.05f;
            if (!IsPlayerExitClear(playerRoot, sampled))
                continue;
            if (IsInsideCarvedObstacleFootprint(
                    exitedVehicle, sampled, playerRadius, ExitCarveClearance))
                continue;
            var nativeAgent = GetPreferredPlayerAgent(agents);
            var currentPositionNeedsRecovery = nativeAgent == null || !nativeAgent.enabled ||
                !nativeAgent.gameObject.activeInHierarchy || !nativeAgent.isOnNavMesh ||
                !IsPlayerExitClear(playerRoot, playerRoot.position) ||
                IsInsideCarvedObstacleFootprint(
                    exitedVehicle, playerRoot.position, playerRadius, ExitCarveClearance);
            if (!currentPositionNeedsRecovery && nativeAgent != null &&
                !HasCompletePathToCandidate(
                    nativeAgent, sampled, filter, exitedVehicle, playerRadius,
                    out var candidatePathDetails))
            {
                if (LamborghiniAventadorDiagnostics.DebugEnabled &&
                    LamborghiniAventadorDiagnostics.ExitSafetyDebugEnabled)
                    context?.Logger.Info(
                        $"LamborghiniAventador exit candidate rejected because the native agent " +
                        $"cannot reach candidate={sampled},details={candidatePathDetails}.");
                continue;
            }
            if (!TryValidateExitReachability(
                    playerRoot, exitedVehicle, agents, sampled,
                    true, out _, out var routeDetails))
            {
                if (LamborghiniAventadorDiagnostics.DebugEnabled &&
                    LamborghiniAventadorDiagnostics.ExitSafetyDebugEnabled)
                    context?.Logger.Info(
                        $"LamborghiniAventador exit candidate rejected candidate={candidate}, " +
                        $"sampled={sampled}, routes=[{routeDetails}].");
                continue;
            }
            target = sampled;
            return true;
        }

        target = default;
        return false;
    }

    private static bool IsPlayerExitClear(Transform playerRoot, Vector3 position)
    {
        var controller = playerRoot.GetComponentInChildren<CharacterController>(true);
        var agent = playerRoot.GetComponentInChildren<NavMeshAgent>(true);
        var controllerScale = controller != null ? controller.transform.lossyScale : playerRoot.lossyScale;
        var radius = controller != null
            ? controller.radius * Mathf.Max(controllerScale.x, controllerScale.z)
            : agent != null ? agent.radius : 0.30f;
        var height = controller != null
            ? controller.height * controllerScale.y
            : agent != null ? agent.height : 1.8f;
        radius = Mathf.Max(0.1f, radius);
        height = Mathf.Max(height, radius * 2f);
        var centerOffset = controller != null
            ? controller.transform.TransformPoint(controller.center) - playerRoot.position
            : Vector3.up * (height * 0.5f);
        var center = position + centerOffset;
        var halfSegment = Mathf.Max(0f, height * 0.5f - radius);
        var axis = controller != null ? controller.transform.up : Vector3.up;
        var overlaps = Physics.OverlapCapsule(
            center - axis * halfSegment,
            center + axis * halfSegment,
            radius,
            ~0,
            QueryTriggerInteraction.Ignore);
        foreach (var overlap in overlaps)
        {
            if (overlap == null || overlap.transform.IsChildOf(playerRoot))
                continue;
            return false;
        }
        return true;
    }

    private static bool TryValidateExitReachability(
        Transform playerRoot,
        VehicleController vehicle,
        IReadOnlyList<NavMeshAgent> agents,
        out int reachableTargets,
        out string details) =>
        TryValidateExitReachability(
            playerRoot, vehicle, agents, playerRoot.position,
            false, out reachableTargets, out details);

    private static bool HasCompletePathToCandidate(
        NavMeshAgent agent,
        Vector3 candidate,
        NavMeshQueryFilter filter,
        VehicleController vehicle,
        float playerRadius,
        out string details)
    {
        if (!NavMesh.SamplePosition(
                agent.nextPosition, out var startHit, ExitReachabilitySampleRadius, filter))
        {
            details = $"agent-start-sample-failed nextPosition={agent.nextPosition}";
            return false;
        }

        var path = new NavMeshPath();
        var calculated = NavMesh.CalculatePath(startHit.position, candidate, filter, path);
        var avoidsVehicleCarve = PathAvoidsCarvedObstacleFootprints(
            vehicle, path, playerRadius, ExitCarveClearance);
        var complete = calculated && path.status == NavMeshPathStatus.PathComplete &&
                       HasPathEndpoint(path, candidate) && avoidsVehicleCarve;
        details = $"calculated={calculated},status={path.status},endpoint={HasPathEndpoint(path, candidate)}," +
                  $"avoidsVehicleCarve={avoidsVehicleCarve},start={startHit.position}," +
                  $"target={candidate},corners={path.corners.Length}";
        return complete;
    }

    private static bool TryValidateExitReachability(
        Transform playerRoot,
        VehicleController vehicle,
        IReadOnlyList<NavMeshAgent> agents,
        Vector3 playerPosition,
        bool startAtPlayerPosition,
        out int reachableTargets,
        out string details)
    {
        reachableTargets = 0;
        var reachableOutwardTargets = 0;
        var reachableSidewalkTargets = 0;
        var routeResults = new List<string>(3);
        GetPlayerNavMeshQueryFilter(agents, out var filter, out var hasAgentComponent);

        var agent = GetPreferredPlayerAgent(agents);
        var hasActiveAgent = agent != null && agent.enabled && agent.gameObject.activeInHierarchy;
        var pathStart = !startAtPlayerPosition && agent != null && agent.enabled && agent.isOnNavMesh
            ? agent.nextPosition
            : playerPosition;
        if (!NavMesh.SamplePosition(
                pathStart, out var startHit, ExitReachabilitySampleRadius, filter))
        {
            details = $"start-sample-failed agentComponent={hasAgentComponent}," +
                      $"activeAgent={hasActiveAgent},agentType={filter.agentTypeID}," +
                      $"areaMask={filter.areaMask},start={pathStart}";
            return false;
        }

        var playerRadius = GetPlayerExitRadius(playerRoot);
        if (IsInsideCarvedObstacleFootprint(
                vehicle, playerPosition, playerRadius, ExitCarveClearance))
        {
            details = $"player-inside-carve-footprint start={playerPosition}";
            return false;
        }

        var vehicleCenter = vehicle.transform.position;
        if (TryGetVehicleBounds(vehicle, out var vehicleBounds))
            vehicleCenter = vehicleBounds.center;
        var awayFromVehicle = Vector3.ProjectOnPlane(
            playerPosition - vehicleCenter, Vector3.up);
        if (awayFromVehicle.sqrMagnitude < 0.01f)
        {
            var driverMarker = FindChildTransform(vehicle.transform, "Driverside");
            awayFromVehicle = driverMarker != null
                ? Vector3.ProjectOnPlane(driverMarker.position - vehicleCenter, Vector3.up)
                : vehicle.transform.right;
        }
        if (awayFromVehicle.sqrMagnitude < 0.01f)
            awayFromVehicle = vehicle.transform.right;
        awayFromVehicle.Normalize();
        var tangent = Vector3.Cross(Vector3.up, awayFromVehicle).normalized;
        var probes = new[]
        {
            ("outward", playerPosition + awayFromVehicle * ExitReachabilityProbeDistance),
            ("sidewalk+", playerPosition + tangent * ExitSidewalkProbeDistance),
            ("sidewalk-", playerPosition - tangent * ExitSidewalkProbeDistance),
        };

        var path = new NavMeshPath();
        foreach (var (probeName, probe) in probes)
        {
            if (!NavMesh.SamplePosition(
                    probe, out var targetHit, ExitReachabilitySampleRadius, filter))
            {
                routeResults.Add($"{probeName}@{probe}:sample-failed");
                continue;
            }
            if (Vector3.Distance(targetHit.position, probe) > ExitReachabilitySampleRadius)
            {
                routeResults.Add($"{probeName}@{probe}:sample-too-far@{targetHit.position}");
                continue;
            }
            if (IsInsideCarvedObstacleFootprint(
                    vehicle, targetHit.position, playerRadius, ExitCarveClearance))
            {
                routeResults.Add($"{probeName}@{probe}:inside-carve@{targetHit.position}");
                continue;
            }

            var calculated = NavMesh.CalculatePath(startHit.position, targetHit.position, filter, path);
            var complete = calculated && path.status == NavMeshPathStatus.PathComplete &&
                           HasPathEndpoint(path, targetHit.position) &&
                           PathAvoidsCarvedObstacleFootprints(
                               vehicle, path, playerRadius, ExitCarveClearance);
            routeResults.Add(
                $"{probeName}@{probe}:calculated={calculated},status={path.status},complete={complete}," +
                $"target={targetHit.position},corners={path.corners.Length}");
            if (complete)
            {
                reachableTargets++;
                if (string.Equals(probeName, "outward", StringComparison.Ordinal))
                    reachableOutwardTargets++;
                else
                    reachableSidewalkTargets++;
            }
        }

        details = $"agentComponent={hasAgentComponent},activeAgent={hasActiveAgent}," +
                  $"agentType={filter.agentTypeID},areaMask={filter.areaMask}," +
                  $"outward={reachableOutwardTargets},sidewalk={reachableSidewalkTargets}," +
                  $"start={startHit.position}; " + string.Join("; ", routeResults);
        // The probes are local. A single reachable direction is enough to show
        // that the player has egress; walls and curbs can block any one direction.
        return reachableTargets > 0;
    }

    private static bool GetPlayerNavMeshQueryFilter(
        IReadOnlyList<NavMeshAgent> agents,
        out NavMeshQueryFilter filter,
        out bool hasAgentComponent)
    {
        var nativePlayerAgent = PlayerHelper.PlayerController?.Character?.navmeshAgent;
        if (nativePlayerAgent != null)
        {
            filter = new NavMeshQueryFilter
            {
                agentTypeID = nativePlayerAgent.agentTypeID,
                areaMask = nativePlayerAgent.areaMask,
            };
            hasAgentComponent = true;
            return true;
        }

        var agent = GetPreferredPlayerAgent(agents);
        hasAgentComponent = agent != null;
        if (agent != null)
        {
            filter = new NavMeshQueryFilter
            {
                agentTypeID = agent.agentTypeID,
                areaMask = agent.areaMask,
            };
            return true;
        }

        // The native player controller uses agent type 0 and all areas if its
        // movement Agent is temporarily absent. Keep the static path query
        // usable so a missing component is diagnosed instead of silently
        // treating any sampled NavMesh point as reachable.
        filter = new NavMeshQueryFilter
        {
            agentTypeID = 0,
            areaMask = NavMesh.AllAreas,
        };
        return false;
    }

    private static NavMeshAgent? GetPreferredPlayerAgent(IReadOnlyList<NavMeshAgent> agents)
    {
        var nativePlayerAgent = PlayerHelper.PlayerController?.Character?.navmeshAgent;
        if (nativePlayerAgent != null)
            return nativePlayerAgent;

        foreach (var agent in agents)
            if (agent != null && agent.enabled && agent.gameObject.activeInHierarchy)
                return agent;
        foreach (var agent in agents)
            if (agent != null)
                return agent;
        return null;
    }

    private static bool HasPathEndpoint(NavMeshPath path, Vector3 target)
    {
        var corners = path.corners;
        return corners.Length > 0 && Vector3.Distance(corners[corners.Length - 1], target) <= 0.5f;
    }

    private static bool PathAvoidsCarvedObstacleFootprints(
        VehicleController vehicle, NavMeshPath path, float clearance, float extraMargin)
    {
        var corners = path.corners;
        if (corners.Length == 0)
            return false;
        if (corners.Length == 1)
            return !IsInsideCarvedObstacleFootprint(
                vehicle, corners[0], clearance, extraMargin);

        for (var corner = 0; corner < corners.Length - 1; corner++)
        {
            var from = corners[corner];
            var to = corners[corner + 1];
            var steps = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(from, to) / 0.4f));
            for (var step = 0; step <= steps; step++)
            {
                var point = Vector3.Lerp(from, to, step / (float)steps);
                if (IsInsideCarvedObstacleFootprint(vehicle, point, clearance, extraMargin))
                    return false;
            }
        }
        return true;
    }

    private static bool IsInsideCarvedObstacleFootprint(
        VehicleController vehicle, Vector3 position, float clearance, float extraMargin)
    {
        foreach (var obstacle in vehicle.GetComponentsInChildren<NavMeshObstacle>(true))
        {
            if (obstacle == null || !obstacle.enabled || !obstacle.carving ||
                obstacle.shape != NavMeshObstacleShape.Box)
                continue;

            if (IsInsideExpandedObstacleFootprint(obstacle, position, clearance + extraMargin))
                return true;
        }
        return false;
    }

    private static bool IsInsideExpandedObstacleFootprint(
        NavMeshObstacle obstacle, Vector3 position, float margin)
    {
        var transform = obstacle.transform;
        var axes = new[] { transform.right, transform.up, transform.forward };
        var halfSizes = new[]
        {
            obstacle.size.x * Mathf.Abs(transform.lossyScale.x) * 0.5f,
            obstacle.size.y * Mathf.Abs(transform.lossyScale.y) * 0.5f,
            obstacle.size.z * Mathf.Abs(transform.lossyScale.z) * 0.5f,
        };
        var firstProjectedAxis = Vector3.zero;
        var secondProjectedAxis = Vector3.zero;
        var firstMagnitude = 0f;
        var secondMagnitude = 0f;
        foreach (var axis in axes)
        {
            var projected = Vector3.ProjectOnPlane(axis, Vector3.up);
            var magnitude = projected.magnitude;
            if (magnitude < 0.5f)
                continue;
            if (magnitude > firstMagnitude)
            {
                secondProjectedAxis = firstProjectedAxis;
                secondMagnitude = firstMagnitude;
                firstProjectedAxis = projected;
                firstMagnitude = magnitude;
            }
            else if (magnitude > secondMagnitude)
            {
                secondProjectedAxis = projected;
                secondMagnitude = magnitude;
            }
        }
        if (secondMagnitude < 0.5f)
            return false;

        var firstAxis = firstProjectedAxis.normalized;
        var secondAxis = Vector3.ProjectOnPlane(secondProjectedAxis, firstAxis).normalized;
        if (secondAxis.sqrMagnitude < 0.5f)
            return false;

        var center = transform.TransformPoint(obstacle.center);
        var offset = position - center;
        var firstExtent = ProjectedObstacleHalfExtent(firstAxis, axes, halfSizes);
        var secondExtent = ProjectedObstacleHalfExtent(secondAxis, axes, halfSizes);
        return Mathf.Abs(Vector3.Dot(offset, firstAxis)) <= firstExtent + margin &&
               Mathf.Abs(Vector3.Dot(offset, secondAxis)) <= secondExtent + margin;
    }

    private static float ProjectedObstacleHalfExtent(
        Vector3 basis, Vector3[] axes, float[] halfSizes)
    {
        var extent = 0f;
        for (var axis = 0; axis < axes.Length; axis++)
            extent += Mathf.Abs(Vector3.Dot(basis, axes[axis])) * halfSizes[axis];
        return extent;
    }

    private static string DescribePlayerNavMeshState(
        Transform playerRoot, IReadOnlyList<NavMeshAgent> agents)
    {
        var description = new StringBuilder();
        description.Append($"playerRoot={playerRoot.position},local={playerRoot.localPosition}");
        var controller = playerRoot.GetComponentInChildren<CharacterController>(true);
        if (controller != null)
            description.Append(
                $",characterController={controller.name},enabled={controller.enabled}," +
                $"position={controller.transform.position},radius={controller.radius}," +
                $"height={controller.height},center={controller.center}," +
                $"scale={controller.transform.lossyScale}");
        else
            description.Append(",characterController=<none>");

        if (agents.Count == 0)
        {
            description.Append(",agents=<none>");
            return description.ToString();
        }
        for (var index = 0; index < agents.Count; index++)
        {
            var agent = agents[index];
            if (agent == null)
            {
                description.Append($",agent[{index}]=<null>");
                continue;
            }
            description.Append(
                $",agent[{index}]={agent.name},enabled={agent.enabled}," +
                $"active={agent.gameObject.activeInHierarchy},onNavMesh={agent.isOnNavMesh}," +
                $"transform={agent.transform.position},next={agent.nextPosition}," +
                $"agentType={agent.agentTypeID},areaMask={agent.areaMask}," +
                $"radius={agent.radius},height={agent.height},velocity={agent.velocity}");
        }
        return description.ToString();
    }

    private static string DescribeVehicleNavMeshState(VehicleController vehicle)
    {
        var description = new StringBuilder();
        description.Append($"vehicleRoot={vehicle.transform.position},rotation={vehicle.transform.rotation}");
        foreach (var obstacle in vehicle.GetComponentsInChildren<NavMeshObstacle>(true))
        {
            if (obstacle == null)
                continue;
            var axes = new[]
            {
                obstacle.transform.right,
                obstacle.transform.up,
                obstacle.transform.forward,
            };
            var halfSizes = new[]
            {
                obstacle.size.x * Mathf.Abs(obstacle.transform.lossyScale.x) * 0.5f,
                obstacle.size.y * Mathf.Abs(obstacle.transform.lossyScale.y) * 0.5f,
                obstacle.size.z * Mathf.Abs(obstacle.transform.lossyScale.z) * 0.5f,
            };
            var horizontalX = ProjectedObstacleHalfExtent(Vector3.right, axes, halfSizes) * 2f;
            var horizontalZ = ProjectedObstacleHalfExtent(Vector3.forward, axes, halfSizes) * 2f;
            description.Append(
                $",obstacle={obstacle.name},enabled={obstacle.enabled},shape={obstacle.shape}," +
                $"carving={obstacle.carving},stationaryOnly={obstacle.carveOnlyStationary}," +
                $"moveThreshold={obstacle.carvingMoveThreshold}," +
                $"timeToStationary={obstacle.carvingTimeToStationary}," +
                $"center={obstacle.center},size={obstacle.size}," +
                $"worldCenter={obstacle.transform.TransformPoint(obstacle.center)}," +
                $"worldScale={obstacle.transform.lossyScale}," +
                $"worldAabbFootprintX={horizontalX:F3},worldAabbFootprintZ={horizontalZ:F3}," +
                $"worldRotation={obstacle.transform.rotation}," +
                $"localRotation={obstacle.transform.localRotation}");
        }
        foreach (var collider in vehicle.GetComponentsInChildren<Collider>(true))
        {
            if (collider == null)
                continue;
            var localShape = string.Empty;
            if (collider is BoxCollider box)
                localShape = $",localCenter={box.center},localSize={box.size}";
            else if (collider is WheelCollider wheel)
                localShape = $",localCenter={wheel.center},radius={wheel.radius}," +
                             $"suspensionDistance={wheel.suspensionDistance}";
            else if (collider is CapsuleCollider capsule)
                localShape = $",localCenter={capsule.center},radius={capsule.radius},height={capsule.height}";
            else if (collider is SphereCollider sphere)
                localShape = $",localCenter={sphere.center},radius={sphere.radius}";
            description.Append(
                $",collider={collider.name},type={collider.GetType().Name}," +
                $"enabled={collider.enabled},trigger={collider.isTrigger}," +
                $"position={collider.transform.position},rotation={collider.transform.rotation}," +
                $"scale={collider.transform.lossyScale},worldCenter={collider.bounds.center}," +
                $"worldBoundsSize={collider.bounds.size}{localShape}");
        }
        foreach (var markerName in new[] { "Driverside", "Passengerside", "ExitDriverAnchor", "ExitPassengerAnchor" })
        {
            var marker = FindChildTransform(vehicle.transform, markerName);
            description.Append(marker != null
                ? $",{markerName}={marker.position},local={marker.localPosition}," +
                  $"rotation={marker.rotation},scale={marker.lossyScale}"
                : $",{markerName}=<missing>");
        }
        return description.ToString();
    }

    private static bool TryGetVehicleBounds(VehicleController vehicle, out Bounds bounds)
    {
        bounds = default;
        var found = false;
        foreach (var collider in vehicle.GetComponentsInChildren<Collider>(true))
        {
            if (collider == null || !collider.enabled || collider.isTrigger)
                continue;
            if (!found)
            {
                bounds = collider.bounds;
                found = true;
            }
            else
                bounds.Encapsulate(collider.bounds);
        }
        return found;
    }

    private static Transform? FindChildTransform(Transform root, string name)
    {
        foreach (var child in root.GetComponentsInChildren<Transform>(true))
            if (string.Equals(child.name, name, StringComparison.Ordinal))
                return child;
        return null;
    }

    private static float GetPlayerExitRadius(Transform playerRoot)
    {
        var radius = 0.30f;
        foreach (var controller in playerRoot.GetComponentsInChildren<CharacterController>(true))
            if (controller != null)
            {
                var controllerScale = controller.transform.lossyScale;
                radius = Mathf.Max(radius,
                    controller.radius * Mathf.Max(controllerScale.x, controllerScale.z));
            }
        foreach (var agent in playerRoot.GetComponentsInChildren<NavMeshAgent>(true))
            if (agent != null)
            {
                var agentScale = agent.transform.lossyScale;
                radius = Mathf.Max(radius, agent.radius * Mathf.Max(agentScale.x, agentScale.z));
            }
        return radius;
    }

    private void ExitSafetyInfo(string message)
    {
        if (LamborghiniAventadorDiagnostics.DebugEnabled &&
            LamborghiniAventadorDiagnostics.ExitSafetyDebugEnabled)
            context?.Logger.Info("LamborghiniAventador " + message);
    }

    private void HandleGameEvent(string _)
    {
        var selected = InstanceBehavior<GameManager>.Instance?.selectedVehicle;
        if (IsOwnedVehicle(selected))
            LamborghiniAventadorPaintController.RefreshIfOwned(selected);
    }

    private void ConfigureOwnedPlayerVehicles(string source)
    {
        if (isShuttingDown || string.IsNullOrEmpty(vehicleTypeName))
            return;

        var vehicles = VehicleHelper.AllPlayerVehicles;
        if (vehicles == null)
            return;

        var matched = 0;
        var initialized = 0;
        foreach (var vehicle in vehicles)
        {
            if (!IsOwnedVehicle(vehicle))
                continue;
            matched++;
            if (string.Equals(source, "vehicle-variables-changed", StringComparison.Ordinal) &&
                HasRuntimeComponents(vehicle!))
            {
                // The native color preview can change after the instance was
                // configured. Refresh the owned paint adapter on that event
                // instead of skipping it with the other installed components.
                LamborghiniAventadorPaintController.RefreshIfOwned(vehicle, preferSavedColor: false);
                continue;
            }
            if (ConfigureVehicle(vehicle, source))
                initialized++;
        }

        // Native variables also change during ordinary driving. Keep useful
        // setup transitions without logging a no-op refresh on every event.
        if (matched > 0 && (initialized > 0 ||
            !string.Equals(source, "vehicle-variables-changed", StringComparison.Ordinal)))
            LamborghiniAventadorDiagnostics.Info(context,
                $"LamborghiniAventador: player vehicle bindings source='{source}' matched={matched}, " +
                $"initialized={initialized}.");
    }

    private static bool HasRuntimeComponents(VehicleController vehicle) =>
        vehicle.GetComponent<LamborghiniAventadorPaintController>() != null &&
        vehicle.GetComponent<LamborghiniAventadorGlassController>() != null &&
        vehicle.GetComponent<LamborghiniAventadorVisualDamageController>() != null &&
        vehicle.GetComponent<LamborghiniAventadorLightingController>() != null &&
        vehicle.GetComponent<LamborghiniAventadorDriverController>() != null;

    internal static void ApplyRuntimeGeometry(VehicleController vehicle, ModContext? modContext)
    {
        if (vehicle == null || !string.Equals(
                vehicle.vehicleInstance?.vehicleTypeName,
                LamborghiniAventadorMod.VehicleTypeName,
                StringComparison.Ordinal))
            return;

        var root = vehicle.transform;
        SetLocalY(root.Find("AventadorBody"), BodyLocalY);
        foreach (var wheelName in new[] { "Wheel_FL", "Wheel_FR", "Wheel_BL", "Wheel_BR" })
        {
            var front = wheelName.EndsWith("FL", StringComparison.Ordinal) ||
                        wheelName.EndsWith("FR", StringComparison.Ordinal);
            SetLocalZ(root.Find(wheelName), front ? 1.25f : -1.45f);
            SetLocalZ(root.Find("Caliper_" + wheelName.Substring("Wheel_".Length)), front ? 1.25f : -1.45f);
        }

        SetLocalZ(root.Find("FrontLeft_WheelController"), 1.25f);
        SetLocalZ(root.Find("FrontRight_WheelController"), 1.25f);
        SetLocalZ(root.Find("RearLeft_WheelController"), -1.45f);
        SetLocalZ(root.Find("RearRight_WheelController"), -1.45f);
        SetLocalX(root.Find("ExitDriverAnchor"), -1.85f);
        SetLocalX(root.Find("ExitPassengerAnchor"), 1.85f);
        SetLocalX(root.Find("Driverside"), -1.85f);
        SetLocalX(root.Find("Passengerside"), 1.85f);
        ConfigureNavMeshObstacles(vehicle.gameObject, modContext);

        var vehicleId = vehicle.GetInstanceID();
        if (ReportedGeometryConfigurations.Add(vehicleId))
            LamborghiniAventadorDiagnostics.GeometryInfo(modContext,
                $"LamborghiniAventador geometry configured vehicle={vehicleId}: bodyY={BodyLocalY:F3}m, " +
                "wheel/caliper Z=front 1.25m rear -1.45m, exit markers X=±1.85m.");
    }

    private static void SetLocalX(Transform? target, float x)
    {
        if (target == null) return;
        var position = target.localPosition;
        position.x = x;
        target.localPosition = position;
    }

    private static void SetLocalY(Transform? target, float y)
    {
        if (target == null) return;
        var position = target.localPosition;
        position.y = y;
        target.localPosition = position;
    }

    private static void SetLocalZ(Transform? target, float z)
    {
        if (target == null) return;
        var position = target.localPosition;
        position.z = z;
        target.localPosition = position;
    }

    private static void ConfigureNavMeshObstacles(GameObject root, ModContext? modContext)
    {
        var obstacles = root.GetComponentsInChildren<NavMeshObstacle>(true);
        if (obstacles.Length == 0)
            return;
        if (!TryGetBodyColliderBounds(root.transform, out var bodyBounds))
        {
            var vehicleId = root.GetInstanceID();
            if (ReportedNavMeshObstacleConfigurations.Add(vehicleId))
                modContext?.Logger.Warn(
                    $"LamborghiniAventador NavMesh obstacle setup could not find a solid BodyCollider; " +
                    $"carved obstacles were left unchanged for vehicle={vehicleId}.");
            return;
        }

        var normalized = 0;
        foreach (var obstacle in obstacles)
        {
            if (obstacle == null || obstacle.shape != NavMeshObstacleShape.Box)
                continue;

            var obstacleTransform = obstacle.transform;
            var scale = obstacleTransform.lossyScale;
            if (Mathf.Abs(scale.x) < .0001f ||
                Mathf.Abs(scale.y) < .0001f ||
                Mathf.Abs(scale.z) < .0001f)
                continue;

            var rootTransform = root.transform;
            var center = obstacleTransform.InverseTransformPoint(
                rootTransform.TransformPoint(bodyBounds.center));
            var size = new Vector3(
                ProjectBodySizeOntoAxis(bodyBounds.size, rootTransform, obstacleTransform.right) /
                Mathf.Abs(scale.x),
                ProjectBodySizeOntoAxis(bodyBounds.size, rootTransform, obstacleTransform.up) /
                Mathf.Abs(scale.y),
                ProjectBodySizeOntoAxis(bodyBounds.size, rootTransform, obstacleTransform.forward) /
                Mathf.Abs(scale.z));

            // The imported mesh holder is scaled by 95x. Leaving its original
            // obstacle dimensions in local space creates an enormous NavMesh
            // carve. Match the working Audi R8 runtime and fit it to the solid
            // chassis bounds, expressed in this scaled/rotated child's local axes.
            if ((obstacle.center - center).sqrMagnitude > .000001f ||
                (obstacle.size - size).sqrMagnitude > .000001f)
            {
                // Match the working McLaren Senna/Audi setup: changing the
                // obstacle geometry in place preserves its enabled/carving
                // lifecycle. Toggling enabled restarts Unity's stationary
                // carve countdown and can leave the exit position validated
                // against stale NavMesh data.
                obstacle.center = center;
                obstacle.size = size;
            }
            normalized++;
        }

        var vehicleIdForLog = root.GetInstanceID();
        if (normalized > 0 && ReportedNavMeshObstacleConfigurations.Add(vehicleIdForLog))
        {
            var obstacle = Array.Find(obstacles, candidate =>
                candidate != null && candidate.shape == NavMeshObstacleShape.Box);
            var worldScale = obstacle != null ? obstacle.transform.lossyScale : Vector3.one;
            LamborghiniAventadorDiagnostics.ExitSafetyInfo(modContext,
                $"LamborghiniAventador NavMesh obstacle normalized vehicle={vehicleIdForLog}: " +
                $"bodyColliderBoundsCenter={bodyBounds.center},bodyColliderBoundsSize={bodyBounds.size}," +
                $"count={normalized},obstacle={obstacle?.name},localCenter={obstacle?.center}," +
                $"localSize={obstacle?.size},worldScale={worldScale}," +
                $"worldCenter={(obstacle != null ? obstacle.transform.TransformPoint(obstacle.center) : Vector3.zero)}," +
                $"enabled={obstacle?.enabled},carving={obstacle?.carving}," +
                $"stationaryOnly={obstacle?.carveOnlyStationary}," +
                $"timeToStationary={obstacle?.carvingTimeToStationary}.");
        }
    }

    private static bool TryGetBodyColliderBounds(Transform root, out Bounds bounds)
    {
        bounds = default;
        var found = false;
        foreach (var child in root.GetComponentsInChildren<Transform>(true))
        {
            if (!string.Equals(child.name, "BodyCollider", StringComparison.Ordinal))
                continue;

            foreach (var collider in child.GetComponents<BoxCollider>())
            {
                if (collider == null || collider.isTrigger)
                    continue;

                var halfSize = collider.size * .5f;
                for (var x = -1; x <= 1; x += 2)
                for (var y = -1; y <= 1; y += 2)
                for (var z = -1; z <= 1; z += 2)
                {
                    var corner = collider.center + Vector3.Scale(
                        halfSize, new Vector3(x, y, z));
                    var rootCorner = root.InverseTransformPoint(
                        collider.transform.TransformPoint(corner));
                    if (!found)
                    {
                        bounds = new Bounds(rootCorner, Vector3.zero);
                        found = true;
                    }
                    else
                    {
                        bounds.Encapsulate(rootCorner);
                    }
                }
            }
        }

        return found;
    }

    private static float ProjectBodySizeOntoAxis(
        Vector3 bodySize, Transform root, Vector3 worldAxis)
    {
        worldAxis.Normalize();
        return Mathf.Abs(Vector3.Dot(worldAxis, root.right)) * bodySize.x +
               Mathf.Abs(Vector3.Dot(worldAxis, root.up)) * bodySize.y +
               Mathf.Abs(Vector3.Dot(worldAxis, root.forward)) * bodySize.z;
    }

    private bool ConfigureVehicle(VehicleController? vehicle, string source)
    {
        if (!IsOwnedVehicle(vehicle))
            return false;
        var modContext = context;
        if (modContext == null)
            return false;

        try
        {
            ApplyRuntimeGeometry(vehicle!, modContext);
            var nwh = vehicle!.GetComponent<NWH.VehiclePhysics2.VehicleController>();
            if (nwh == null)
                throw new InvalidOperationException("NWH VehicleController missing during powertrain calibration.");
            var powertrainCalibration = vehicle.GetComponent<LamborghiniAventadorPowertrainCalibration>() ??
                                        vehicle.gameObject.AddComponent<LamborghiniAventadorPowertrainCalibration>();
            powertrainCalibration.Initialize(nwh, modContext);
            // The layered Revuelto engine set is only needed while the player
            // controls this car. Avoid loading six WAVs and nine sources for
            // every parked owned vehicle; the native sound remains available.
            if (vehicle!.controlledByPlayer ||
                string.Equals(source, "player-entered", StringComparison.Ordinal))
            {
                var audio = vehicle.GetComponent<LamborghiniAventadorAudioController>();
                if (audio == null)
                    audio = vehicle.gameObject.AddComponent<LamborghiniAventadorAudioController>();
                audio.Initialize(vehicle, modContext);
                if (vehicle is CarController warehouseCar)
                {
                    var warehouse = warehouseCar.GetComponent<LamborghiniAventadorWarehouseEntryController>();
                    if (warehouse == null)
                    {
                        warehouse = warehouseCar.gameObject.AddComponent<LamborghiniAventadorWarehouseEntryController>();
                        warehouse.Initialize(warehouseCar, nwh, modContext, warehouseDebugEnabled);
                    }
                    warehouse.SetOccupied(true);
                }
                if (!audio.enabled)
                    audio.enabled = true;
            }
            var glass = vehicle.GetComponent<LamborghiniAventadorGlassController>() ??
                        vehicle.gameObject.AddComponent<LamborghiniAventadorGlassController>();
            glass.Initialize(modContext);

            // Normalize the imported shader/materials before the paint adapter
            // builds its property block. Otherwise a GLTF property block can
            // survive the later HDRP material replacement with the wrong color
            // property, leaving body paint untinted or partially transparent.
            var preferSavedColor = !string.Equals(
                source, "vehicle-variables-changed", StringComparison.Ordinal);
            LamborghiniAventadorPaintController.Attach(vehicle, modContext, preferSavedColor);

            var lighting = vehicle.GetComponent<LamborghiniAventadorLightingController>();
            if (lighting == null)
                lighting = vehicle.gameObject.AddComponent<LamborghiniAventadorLightingController>();
            lighting.Initialize(vehicle, modContext);

            // Lighting creates per-instance mesh overlays beneath lamp renderers.
            // Initialize damage afterward so those overlays receive the same
            // cloned, repairable ownership as the imported exterior geometry.
            var damage = vehicle.GetComponent<LamborghiniAventadorVisualDamageController>();
            if (damage == null)
                damage = vehicle.gameObject.AddComponent<LamborghiniAventadorVisualDamageController>();
            damage.Initialize(vehicle, modContext);

            var calipers = vehicle.GetComponent<LamborghiniAventadorCaliperController>();
            if (calipers == null)
                calipers = vehicle.gameObject.AddComponent<LamborghiniAventadorCaliperController>();
            calipers.Initialize(vehicle, modContext);

            var driver = vehicle.GetComponent<LamborghiniAventadorDriverController>();
            if (driver == null)
                driver = vehicle.gameObject.AddComponent<LamborghiniAventadorDriverController>();
            driver.Initialize(vehicle, modContext);
            return true;
        }
        catch (Exception exception)
        {
            var id = vehicle!.GetInstanceID();
            if (ReportedRuntimeSetupFailures.Add(id))
                context?.Logger.Warn(
                    $"LamborghiniAventador: runtime binding failed source='{source}' vehicle={id}: " +
                    $"{exception.GetType().Name}: {exception.Message}");
            return false;
        }
    }

    private bool IsOwnedVehicle(VehicleController? vehicle) =>
        vehicle != null && vehicle.vehicleInstance != null &&
        string.Equals(vehicle.vehicleInstance.vehicleTypeName, vehicleTypeName, StringComparison.Ordinal);
}
