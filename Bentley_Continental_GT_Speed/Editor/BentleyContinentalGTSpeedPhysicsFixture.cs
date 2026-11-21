#nullable enable
using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Runs a disposable Play Mode check of the authored NWH rig without loading the game.
/// It reports straight-line acceleration and braking and checks that native wheel visuals
/// respond to steering and wheel rotation. Run it from Big Ambitions Mods/Scaffold.
/// </summary>
[InitializeOnLoad]
public static class BentleyContinentalGTSpeedPhysicsFixture
{
    private const string PrefabPath = "Assets/Mods/Bentley_Continental_GT_Speed/BentleyContinentalGTSpeed.prefab";
    private const string SessionKey = "BentleyContinentalGTSpeed.PhysicsFixture.State";
    private const string VehicleName = "__BentleyNwhPhysicsFixtureVehicle";
    private const string GroundName = "__BentleyNwhPhysicsFixtureGround";
    private const string CameraName = "__BentleyNwhPhysicsFixtureCamera";
    private const float KphPerMps = 3.6f;
    private const float BrakeStartMps = 70f * 0.44704f;
    private const float MaximumBrakeDistanceMeters = 32.9f;
    private const float MinimumZeroTo100Seconds = 3.1f;
    private const float MaximumZeroTo100Seconds = 3.5f;
    private const float MinimumZeroTo200Seconds = 9.0f;
    private const float MaximumZeroTo200Seconds = 9.6f;
    private const float ExpectedBrakeGrip = 0.56f;
    private const float ExpectedBaselineForwardGrip = 0.50f;
    private const float TurnRadiusToleranceMeters = 0.04f;
    private const float TargetTurnRadiusMeters = 4.15f;

    [Serializable]
    private sealed class RunState
    {
        public string phase = "";
        public float lastFixedTime = -1f;
        public int waitTicks;
        public float startTime;
        public float time100 = -1f;
        public float time200 = -1f;
        public float brakeDistance = -1f;
        public bool brakeGripActivatedObserved;
        public bool brakeGripRestoredObserved;
        public float brakeStartSpeed;
        public float brakeStartTime;
        public float brakeCommandStartedAt;
        public float brakePreviousSpeed;
        public float brakePreviousTime;
        public Vector3 brakePreviousPosition;
        public bool brakeMeasurementStarted;
        public Vector3 startPosition;
        public Vector3 forward;
        public string fixtureRootName = "";
        public string priorSceneName = "";
        public string priorScenePath = "";
        public Quaternion[] wheelRotations = Array.Empty<Quaternion>();
        public Quaternion[] steeringRotations = Array.Empty<Quaternion>();
        public float steerMovedDegrees;
        public float rollMovedDegrees;
        public int elapsedTicks;
        public int exitCode = -1;
        public bool powertrainPrepared;
        public float peakGeneratedPower;
        public float firstGearMaxRpm;
        public float secondGearMaxRpm;
        public float firstGearNearRedlineSeconds;
        public float secondGearNearRedlineSeconds;
        public float turnRadiusStartedAt;
        public float minimumTurnRadiusMeters = float.MaxValue;
        public int turnRadiusSamples;
        public float[] turnRadiusValues = Array.Empty<float>();
        public int brakeStartGear;
        public bool brakeStartTcsActive;
        public float nwhBrakeTorque;
        public string sample100 = "";
        public string sample200 = "";
    }

    static BentleyContinentalGTSpeedPhysicsFixture()
    {
        EditorApplication.update += Tick;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    [MenuItem("Big Ambitions Mods/Scaffold/Run Bentley NWH Physics Fixture")]
    private static void Start()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isPlaying)
            throw new InvalidOperationException("Stop Play Mode before starting the Bentley physics fixture.");

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab == null)
            throw new InvalidOperationException("Bentley physics fixture cannot load prefab: " + PrefabPath);

        var priorScene = EditorSceneManager.GetActiveScene();
        var fixtureSceneName = "BentleyPhysicsFixture_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        var fixtureScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,
            Application.isBatchMode ? NewSceneMode.Single : NewSceneMode.Additive);
        EditorSceneManager.SetActiveScene(fixtureScene);
        var ground = new GameObject(GroundName);
        ground.name = GroundName + "_" + fixtureSceneName;
        ground.transform.position = new Vector3(0f, -0.0955f, 0f);
        var groundCollider = ground.AddComponent<BoxCollider>();
        groundCollider.size = new Vector3(80f, 0.2f, 1200f);
        ground.isStatic = true;
        var roadLayer = LayerMask.NameToLayer("Roads");
        if (roadLayer >= 0)
            ground.layer = roadLayer;

        var cameraObject = new GameObject(CameraName);
        cameraObject.transform.position = new Vector3(0f, 2f, -6f);
        cameraObject.transform.rotation = Quaternion.LookRotation(new Vector3(0f, 0.5f, 2f).normalized);
        var fixtureCamera = cameraObject.AddComponent<Camera>();
        fixtureCamera.enabled = false;

        var state = new RunState
        {
            phase = "settle-steer",
            startTime = 0f,
            fixtureRootName = ground.name,
            priorSceneName = priorScene.name,
            priorScenePath = priorScene.path
        };
        SessionState.SetString(SessionKey, JsonUtility.ToJson(state));
        SpawnVehicle(prefab);
        Debug.Log("[Bentley physics fixture] Temporary flat-ground NWH Play Mode fixture started.");
        EditorApplication.isPlaying = true;
    }

    public static void RunBatch()
    {
        try
        {
            Start();
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            if (Application.isBatchMode)
                EditorApplication.Exit(1);
            throw;
        }
    }

    private static void Tick()
    {
        if (!EditorApplication.isPlaying)
            return;
        var json = SessionState.GetString(SessionKey, "");
        if (string.IsNullOrEmpty(json))
            return;

        var state = JsonUtility.FromJson<RunState>(json);
        var vehicle = GameObject.Find(VehicleName);
        if (vehicle == null)
        {
            Fail(state, "temporary vehicle disappeared");
            return;
        }

        var controller = vehicle.GetComponentsInChildren<Component>(true)
            .FirstOrDefault(component => component != null &&
                string.Equals(component.GetType().FullName, "NWH.VehiclePhysics2.VehicleController", StringComparison.Ordinal));
        var body = vehicle.GetComponent<Rigidbody>() ?? vehicle.GetComponentInChildren<Rigidbody>(true);
        var input = controller == null ? null : ReadMember(controller, "input");
        if (controller == null || body == null || input == null)
        {
            Fail(state, $"required NWH components missing: controller={controller != null}, rigidbody={body != null}, input={input != null}");
            return;
        }

        WriteMember(input, "autoSetInput", false);
        var stateSettings = ReadMember(controller, "stateSettings");
        if (stateSettings == null)
        {
            Fail(state, "NWH VehicleController has no StateSettings asset");
            return;
        }
        WriteMemberIfPresent(controller, "lodCamera", GameObject.Find(CameraName)?.GetComponent<Camera>());
        var wheels = GetBoundWheelVisuals(vehicle);
        if (wheels.Length != 4 || wheels.Any(transform => transform == null))
        {
            Fail(state, "expected four NWH WheelController visual bindings; found " + wheels.Length);
            return;
        }
        if (wheels.Any(transform => !transform.GetComponentsInChildren<Renderer>(true)
                .Any(renderer => renderer != null && renderer.enabled && renderer.gameObject.activeInHierarchy)))
        {
            Fail(state, "one or more bound native wheel visuals have no active enabled renderer");
            return;
        }

        if (Time.fixedTime <= 0f || Mathf.Approximately(Time.fixedTime, state.lastFixedTime))
            return;
        var observedFixedDelta = state.lastFixedTime < 0f
            ? Time.fixedDeltaTime
            : Mathf.Clamp(Time.fixedTime - state.lastFixedTime, 0f, 0.1f);
        state.lastFixedTime = Time.fixedTime;
        state.elapsedTicks++;

        if (!state.powertrainPrepared)
        {
            PreparePowertrain(controller, input);
            if (!ConfigureBrakeGripForFixture(vehicle))
            {
                Fail(state, "runtime brake-grip controller could not be initialized after entering Play Mode");
                return;
            }
            state.powertrainPrepared = true;
            Debug.Log("[Bentley physics fixture] NWH active state and runtime brake-grip controller prepared: stateSettings=" +
                (stateSettings as UnityEngine.Object)?.name + ", engine running=" + ReadMember(ReadMember(ReadMember(controller, "powertrain"), "engine")!, "IsRunning") + ".");
        }

        var speed = body.velocity.magnitude;
        var speedKph = Mathf.Max(0f, Vector3.Dot(body.velocity, state.forward)) * KphPerMps;
        switch (state.phase)
        {
            case "settle-steer":
                SetInput(input, 0f, 0f, state.waitTicks is >= 60 and < 90 ? 0.45f : 0f);
                if (state.waitTicks == 60)
                    state.steeringRotations = wheels.Select(wheel => wheel.rotation).ToArray();
                if (state.waitTicks == 75)
                    state.steerMovedDegrees = MaxFrontRotationDelta(state.steeringRotations, wheels);
                if (state.waitTicks == 90)
                {
                    state.phase = "settle-start-accel";
                    state.waitTicks = 0;
                }
                state.waitTicks++;
                break;

            case "settle-start-accel":
                SetInput(input, 0f, 0f, 0f);
                if (++state.waitTicks >= 30)
                {
                    var settledEngine = ReadMember(ReadMember(controller, "powertrain")!, "engine");
                    var engineRunning = ReadMember(settledEngine!, "IsRunning") as bool? ?? false;
                    var engineRpm = ReadNumber(settledEngine, "OutputRPM");
                    if (!engineRunning || engineRpm <= 0f)
                    {
                        Fail(state, $"NWH engine did not initialize at idle: running={engineRunning}, rpm={engineRpm:F0}");
                        return;
                    }
                    Debug.Log($"[Bentley physics fixture] NWH engine ready in idle neutral: running={engineRunning}, rpm={engineRpm:F0}.");
                    state.phase = "engage-drive";
                    state.waitTicks = 0;
                }
                break;

            case "engage-drive":
                SetInput(input, 0.2f, 1f, 0f);
                var transmission = ReadMember(ReadMember(controller, "powertrain")!, "transmission");
                var gear = ReadNumber(transmission, "Gear");
                var gearRatio = ReadNumber(transmission, "currentGearRatio");
                if (state.waitTicks >= 30 && gear > 0f && Mathf.Abs(gearRatio) > 0.001f)
                {
                    var engagedEngine = ReadMember(ReadMember(controller, "powertrain")!, "engine");
                    var engineRpm = ReadNumber(engagedEngine, "OutputRPM");
                    body.velocity = Vector3.zero;
                    body.angularVelocity = Vector3.zero;
                    SetInput(input, 1f, 0f, 0f);
                    state.phase = "accelerate";
                    state.startTime = Time.fixedTime;
                    state.startPosition = body.position;
                    state.forward = vehicle.transform.forward;
                    state.wheelRotations = wheels.Select(wheel => wheel.rotation).ToArray();
                    state.time100 = -1f;
                    state.time200 = -1f;
                    state.elapsedTicks = 0;
                    Debug.Log($"[Bentley physics fixture] NWH drive engaged under brake hold: gear={gear:F0}, ratio={gearRatio:F3}, rpm={engineRpm:F0}; acceleration timing begins now.");
                }
                else
                {
                    if (state.waitTicks % 30 == 0)
                        ShiftIntoFirst(controller);
                    if (++state.waitTicks > 300)
                    {
                        Fail(state, $"NWH transmission did not leave neutral after shift requests: engineRunning={ReadMember(ReadMember(ReadMember(controller, "powertrain"), "engine")!, "IsRunning")}, gear={gear:F0}, ratio={gearRatio:F3}");
                        return;
                    }
                }
                break;

            case "accelerate":
                SetInput(input, 1f, 0f, 0f);
                var acceleratingEngine = ReadMember(ReadMember(controller, "powertrain")!, "engine");
                var acceleratingTransmission = ReadMember(ReadMember(controller, "powertrain")!, "transmission");
                var accelerationGear = (int)ReadNumber(acceleratingTransmission, "Gear");
                var accelerationRpm = ReadNumber(acceleratingEngine, "OutputRPM");
                if (accelerationGear == 1)
                {
                    state.firstGearMaxRpm = Mathf.Max(state.firstGearMaxRpm, accelerationRpm);
                    if (accelerationRpm >= 6370f)
                        state.firstGearNearRedlineSeconds += observedFixedDelta;
                }
                else if (accelerationGear == 2)
                {
                    state.secondGearMaxRpm = Mathf.Max(state.secondGearMaxRpm, accelerationRpm);
                    if (accelerationRpm >= 6370f)
                        state.secondGearNearRedlineSeconds += observedFixedDelta;
                }
                state.peakGeneratedPower = Mathf.Max(state.peakGeneratedPower, ReadNumber(acceleratingEngine, "generatedPower"));
                if (state.time100 < 0f && speedKph >= 100f)
                {
                    state.time100 = Time.fixedTime - state.startTime;
                    state.sample100 = BuildPhysicsSample(controller, body, state.forward);
                }
                if (state.time200 < 0f && speedKph >= 200f)
                {
                    state.time200 = Time.fixedTime - state.startTime;
                    state.sample200 = BuildPhysicsSample(controller, body, state.forward);
                    state.rollMovedDegrees = MaxRotationDelta(state.wheelRotations, wheels);
                    Debug.Log($"[Bentley physics fixture] Accel measurements: 0-100 km/h={state.time100:F2}s; sample={state.sample100}; 0-200 km/h={state.time200:F2}s; sample={state.sample200}; 1st max RPM={state.firstGearMaxRpm:F0}, time >=6370rpm={state.firstGearNearRedlineSeconds:F2}s; 2nd max RPM={state.secondGearMaxRpm:F0}, time >=6370rpm={state.secondGearNearRedlineSeconds:F2}s; peak generatedPower={state.peakGeneratedPower:F1}; wheel visual rotation={state.rollMovedDegrees:F1} deg; steering visual rotation={state.steerMovedDegrees:F1} deg.");
                    if (Time.fixedTime - state.startTime > 90f)
                    {
                        Fail(state, "acceleration exceeded 90 seconds before 200 km/h");
                        return;
                    }
                    state.phase = "prepare-brake";
                    state.waitTicks = 0;
                }
                else if (Time.fixedTime - state.startTime > 90f)
                {
                    Fail(state, $"acceleration timeout at {speedKph:F1} km/h");
                    return;
                }
                break;

            case "prepare-brake":
                state.forward = vehicle.transform.forward;
                state.phase = "brake";
                state.brakeCommandStartedAt = Time.fixedTime;
                state.brakePreviousSpeed = Vector3.Dot(body.velocity, state.forward);
                state.brakePreviousPosition = body.position;
                state.brakePreviousTime = Time.fixedTime;
                state.brakeMeasurementStarted = false;
                SetInput(input, 0f, 1f, 0f);
                Debug.Log($"[Bentley physics fixture] Continuous braking started from {state.brakePreviousSpeed * KphPerMps:F1} km/h; no vehicle/wheel teleportation. Waiting for 70 mph threshold; {FormatDynamicState(controller, body, input)}");
                break;

            case "brake":
                SetInput(input, 0f, 1f, 0f);
                var brakeModule = ReadMember(controller, "brakes");
                if (!state.brakeGripActivatedObserved &&
                    ReadMember(brakeModule!, "IsBraking") is bool nativeBraking && nativeBraking)
                {
                    state.brakeGripActivatedObserved = AllWheelForwardGripMatches(
                        vehicle, ExpectedBrakeGrip);
                    if (state.brakeGripActivatedObserved)
                        Debug.Log($"[Bentley physics fixture] Brake-only grip observed at {ExpectedBrakeGrip:F2} across all four wheels.");
                }
                var brakeForwardSpeed = Mathf.Max(0f, Vector3.Dot(body.velocity, state.forward));
                if (!state.brakeMeasurementStarted &&
                    state.brakePreviousSpeed > BrakeStartMps && brakeForwardSpeed <= BrakeStartMps)
                {
                    var speedDrop = state.brakePreviousSpeed - brakeForwardSpeed;
                    var interpolation = speedDrop > 0.0001f
                        ? Mathf.Clamp01((state.brakePreviousSpeed - BrakeStartMps) / speedDrop)
                        : 1f;
                    var brakingTransmission = ReadMember(ReadMember(controller, "powertrain")!, "transmission");
                    state.brakeStartGear = (int)ReadNumber(brakingTransmission, "Gear");
                    state.brakeStartTcsActive = ReadTcsActive(vehicle);
                    state.nwhBrakeTorque = ReadNwhBrakeTorque(controller);
                    state.brakeStartSpeed = BrakeStartMps;
                    state.brakeStartTime = Mathf.Lerp(state.brakePreviousTime, Time.fixedTime, interpolation);
                    state.startPosition = Vector3.Lerp(state.brakePreviousPosition, body.position, interpolation);
                    state.brakeMeasurementStarted = true;
                    Debug.Log($"[Bentley physics fixture] Brake measurement started at 70 mph: gear={state.brakeStartGear}; tcsActive={state.brakeStartTcsActive}; brakeTorque={state.nwhBrakeTorque:F0}Nm; {FormatDynamicState(controller, body, input)}");
                }
                var travelled = state.brakeMeasurementStarted
                    ? Vector3.Dot(body.position - state.startPosition, state.forward)
                    : 0f;
                if (state.brakeMeasurementStarted && brakeForwardSpeed < 0.25f)
                {
                    state.brakeDistance = travelled;
                    state.phase = "turn-radius-engage";
                    state.turnRadiusStartedAt = Time.fixedTime;
                    state.minimumTurnRadiusMeters = float.MaxValue;
                    state.turnRadiusSamples = 0;
                    state.turnRadiusValues = Array.Empty<float>();
                    state.elapsedTicks = 0;
                    Debug.Log($"[Bentley physics fixture] Brake measurement ended: initial={state.brakeStartSpeed * KphPerMps:F1} km/h; stop distance={state.brakeDistance:F2} m (target <= {MaximumBrakeDistanceMeters:F1} m); stop time={Time.fixedTime - state.brakeStartTime:F2}s; startGear={state.brakeStartGear}; startTcsActive={state.brakeStartTcsActive}; brakeTorque={state.nwhBrakeTorque:F0}Nm; {FormatDynamicState(controller, body, input)}. Re-engaging first gear for the full-lock circle.");
                }
                state.brakePreviousSpeed = brakeForwardSpeed;
                state.brakePreviousTime = Time.fixedTime;
                state.brakePreviousPosition = body.position;
                if (Time.fixedTime - state.brakeCommandStartedAt > 20f)
                {
                    Fail(state, $"braking timeout at {speedKph:F1} km/h; distance={travelled:F2}m");
                    return;
                }
                break;

            case "turn-radius-engage":
                SetInput(input, 0.2f, 1f, 0f);
                var turningTransmission = ReadMember(ReadMember(controller, "powertrain")!, "transmission");
                var turningGear = ReadNumber(turningTransmission, "Gear");
                var turningRatio = ReadNumber(turningTransmission, "currentGearRatio");
                if (state.waitTicks >= 30 && turningGear == 1f && Mathf.Abs(turningRatio) > 0.001f)
                {
                    SetInput(input, 0.12f, 0f, 1f);
                    state.phase = "turn-radius-approach";
                    state.turnRadiusStartedAt = Time.fixedTime;
                    state.waitTicks = 0;
                    Debug.Log($"[Bentley physics fixture] First gear re-engaged for a physically continuous full-lock approach: {FormatDynamicState(controller, body, input)}");
                }
                else
                {
                    if (state.waitTicks % 30 == 0)
                        ShiftIntoFirst(controller);
                    if (++state.waitTicks > 300)
                    {
                        Fail(state, $"could not re-engage first gear before turning: gear={turningGear:F0}, ratio={turningRatio:F3}; {FormatDynamicState(controller, body, input)}");
                        return;
                    }
                }
                break;

            case "turn-radius-approach":
                if (!state.brakeGripRestoredObserved &&
                    ReadMember(ReadMember(controller, "brakes")!, "IsBraking") is bool nativeBrakesReleased &&
                    !nativeBrakesReleased)
                {
                    state.brakeGripRestoredObserved = AllWheelForwardGripMatches(
                        vehicle, ExpectedBaselineForwardGrip);
                    if (state.brakeGripRestoredObserved)
                        Debug.Log($"[Bentley physics fixture] Baseline forward grip restored to {ExpectedBaselineForwardGrip:F2} after brake release.");
                }
                var approachSpeed = new Vector2(body.velocity.x, body.velocity.z).magnitude;
                var approachThrottle = Mathf.Clamp(0.12f + (3.5f - approachSpeed) * 0.35f, 0f, 0.5f);
                SetInput(input, approachThrottle, 0f, 0f);
                if (approachSpeed >= 3.0f && approachSpeed <= 4.5f &&
                    CountGroundedWheels(ReadMember(controller, "powertrain")) == 4)
                {
                    state.phase = "turn-radius";
                    state.turnRadiusStartedAt = Time.fixedTime;
                    state.minimumTurnRadiusMeters = float.MaxValue;
                    state.turnRadiusSamples = 0;
                    state.turnRadiusValues = Array.Empty<float>();
                    SetInput(input, approachThrottle, 0f, 1f);
                    Debug.Log($"[Bentley physics fixture] Full-lock circle started after the first-gear approach: {FormatDynamicState(controller, body, input)}");
                }
                else if (Time.fixedTime - state.turnRadiusStartedAt > 8f)
                {
                    Fail(state, $"could not reach a grounded low-speed turning window: {FormatDynamicState(controller, body, input)}");
                    return;
                }
                break;

            case "turn-radius":
                var turnSpeed = new Vector2(body.velocity.x, body.velocity.z).magnitude;
                var turnThrottle = Mathf.Clamp(0.12f + (3.5f - turnSpeed) * 0.35f, 0f, 0.5f);
                SetInput(input, turnThrottle, 0f, 1f);
                var yawRate = Mathf.Abs(body.angularVelocity.y);
                // Allow yaw/velocity to settle into a steady full-lock arc before
                // retaining samples; avoid counting the approach transition.
                if (Time.fixedTime - state.turnRadiusStartedAt >= 2f &&
                    turnSpeed >= 1f && yawRate >= 0.02f)
                {
                    var radius = turnSpeed / yawRate;
                    if (radius < state.minimumTurnRadiusMeters)
                        state.minimumTurnRadiusMeters = radius;
                    state.turnRadiusSamples++;
                    Array.Resize(ref state.turnRadiusValues, state.turnRadiusValues.Length + 1);
                    state.turnRadiusValues[state.turnRadiusValues.Length - 1] = radius;
                }
                if (Time.fixedTime - state.turnRadiusStartedAt >= 8f)
                {
                    Debug.Log($"[Bentley physics fixture] Full-lock circle ended: minRadius={(state.turnRadiusSamples > 0 ? state.minimumTurnRadiusMeters.ToString("F2") : "n/a")}m, samples={state.turnRadiusSamples}; {FormatDynamicState(controller, body, input)}");
                    SetInput(input, 0f, 1f, 0f);
                    state.phase = "turn-radius-settle";
                    state.turnRadiusStartedAt = Time.fixedTime;
                    state.waitTicks = 0;
                    Debug.Log($"[Bentley physics fixture] Turn-radius arc ended: valid samples={state.turnRadiusSamples}, minimum body-center radius={(state.turnRadiusSamples > 0 ? state.minimumTurnRadiusMeters.ToString("F2") : "n/a")}m. Waiting at most 3 seconds for the vehicle to settle.");
                }
                break;

            case "turn-radius-settle":
                SetInput(input, 0f, 1f, 0f);
                if ((speed < 0.25f && ++state.waitTicks >= 5) ||
                    Time.fixedTime - state.turnRadiusStartedAt >= 3f)
                {
                    var turnRadiusMedian = Median(state.turnRadiusValues);
                    Debug.Log($"[Bentley physics fixture] Drivetrain/steering results: 1st max RPM={state.firstGearMaxRpm:F0}, time >=6370rpm={state.firstGearNearRedlineSeconds:F2}s; 2nd max RPM={state.secondGearMaxRpm:F0}, time >=6370rpm={state.secondGearNearRedlineSeconds:F2}s; low-speed full-lock body-center radius min/median={(state.turnRadiusSamples > 0 ? state.minimumTurnRadiusMeters.ToString("F2") + "/" + turnRadiusMedian.ToString("F2") : "n/a")}m from {state.turnRadiusSamples} valid samples.");
                    if (state.time100 < MinimumZeroTo100Seconds || state.time100 > MaximumZeroTo100Seconds ||
                        state.time200 < MinimumZeroTo200Seconds || state.time200 > MaximumZeroTo200Seconds ||
                        state.brakeDistance < 0f || state.brakeDistance > MaximumBrakeDistanceMeters ||
                        !state.brakeGripActivatedObserved || !state.brakeGripRestoredObserved ||
                        state.steerMovedDegrees < 1f || state.rollMovedDegrees < 10f || state.turnRadiusSamples == 0 ||
                        state.firstGearNearRedlineSeconds > 0.3001f ||
                        state.secondGearNearRedlineSeconds > 0.3001f ||
                        state.turnRadiusSamples < 50 ||
                        Mathf.Abs(turnRadiusMedian - TargetTurnRadiusMeters) > TurnRadiusToleranceMeters)
                    {
                        Fail(state,
                            $"measurements missed target or visual-motion checks failed: " +
                            $"0-100={state.time100:F2}s ({MinimumZeroTo100Seconds:F2}-{MaximumZeroTo100Seconds:F2}), " +
                            $"0-200={state.time200:F2}s ({MinimumZeroTo200Seconds:F2}-{MaximumZeroTo200Seconds:F2}), " +
                            $"70 mph brake distance={state.brakeDistance:F2}m (<= {MaximumBrakeDistanceMeters:F2}), " +
                            $"brakeGripActive={state.brakeGripActivatedObserved} ({ExpectedBrakeGrip:F2}), " +
                            $"brakeGripRestored={state.brakeGripRestoredObserved} ({ExpectedBaselineForwardGrip:F2}), " +
                            $"1st time >=6370rpm={state.firstGearNearRedlineSeconds:F2}s (<=0.30), " +
                            $"2nd time >=6370rpm={state.secondGearNearRedlineSeconds:F2}s (<=0.30), " +
                            $"turn radius median={(state.turnRadiusSamples > 0 ? turnRadiusMedian.ToString("F2") : "n/a")}m over {state.turnRadiusSamples} samples " +
                            $"(>=50 samples, target={TargetTurnRadiusMeters:F2}±{TurnRadiusToleranceMeters:F2})");
                        return;
                    }
                    Debug.Log("[Bentley physics fixture] PASS: acceleration/braking measurements completed; first/second-gear near-redline proxy and low-speed full-lock turn radius met their regression targets; rolling/front-steering visuals moved.");
                    state.phase = "passed";
                    state.exitCode = 0;
                    SessionState.SetString(SessionKey, JsonUtility.ToJson(state));
                    EditorApplication.isPlaying = false;
                    return;
                }
                break;

            default:
                Fail(state, "unknown fixture phase " + state.phase);
                return;
        }

        SessionState.SetString(SessionKey, JsonUtility.ToJson(state));
    }

    private static float Median(float[] values)
    {
        if (values == null || values.Length == 0)
            return float.MaxValue;
        var sorted = (float[])values.Clone();
        Array.Sort(sorted);
        var middle = sorted.Length / 2;
        return sorted.Length % 2 == 0
            ? (sorted[middle - 1] + sorted[middle]) * 0.5f
            : sorted[middle];
    }

    private static void SpawnVehicle(GameObject prefab)
    {
        var clone = PrefabUtility.InstantiatePrefab(prefab) as GameObject ?? UnityEngine.Object.Instantiate(prefab);
        clone.name = VehicleName;
        clone.transform.position = Vector3.zero;
        clone.transform.rotation = Quaternion.identity;
        clone.SetActive(false);
        foreach (var behaviour in clone.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (behaviour == null)
                continue;
            var typeName = behaviour.GetType().FullName ?? "";
            if (!typeName.StartsWith("NWH.", StringComparison.Ordinal))
                UnityEngine.Object.DestroyImmediate(behaviour);
        }
        foreach (var wheel in clone.GetComponentsInChildren<Component>(true)
                     .Where(component => component != null && component.GetType().Name == "WheelController"))
        {
            var serialized = new SerializedObject(wheel);
            serialized.FindProperty("wheel.autoSetInput")?.SetValueIfExists(false);
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
        clone.SetActive(true);
    }

    private static bool ConfigureBrakeGripForFixture(GameObject vehicle)
    {
        const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        var componentType = AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType(
                "BentleyContinentalGTSpeedRoadDynamics", throwOnError: false))
            .FirstOrDefault(type => type != null);
        var configure = componentType?.GetMethods(flags).FirstOrDefault(method =>
            string.Equals(method.Name, "ConfigureForPhysicsFixture", StringComparison.Ordinal) &&
            method.GetParameters().Length == 1 &&
            method.GetParameters()[0].ParameterType == typeof(GameObject));
        return configure?.Invoke(null, new object[] { vehicle }) is Component;
    }

    private static bool AllWheelForwardGripMatches(GameObject vehicle, float expectedGrip)
    {
        var wheelControllers = vehicle.GetComponentsInChildren<Component>(true)
            .Where(component => component != null &&
                string.Equals(component.GetType().Name, "WheelController", StringComparison.Ordinal))
            .ToArray();
        return wheelControllers.Length == 4 && wheelControllers.All(wheel =>
            Mathf.Abs(ReadNumber(ReadMember(wheel, "forwardFriction"), "grip") - expectedGrip) <= 0.005f);
    }

    private static Transform[] GetBoundWheelVisuals(GameObject vehicle)
    {
        var order = new[] { "3DWheel Front L", "3DWheel Front R", "3DWheel Rear L", "3DWheel Rear R" };
        var controllers = vehicle.GetComponentsInChildren<Component>(true)
            .Where(component => component != null && component.GetType().Name == "WheelController")
            .ToArray();
        var visuals = controllers.Select(component =>
            {
                var wheelData = ReadMember(component, "wheel");
                var visual = wheelData == null ? null : ReadMember(wheelData, "visual");
                var transform = visual switch
                {
                    Transform wheelTransform => wheelTransform,
                    GameObject gameObject => gameObject.transform,
                    _ => null
                };
                return transform;
            })
            .Where(transform => transform != null)
            .Cast<Transform>()
            .ToArray();
        return order.Select(name => visuals.FirstOrDefault(transform =>
                string.Equals(transform.name, name, StringComparison.Ordinal)))
            .Where(transform => transform != null)
            .Cast<Transform>()
            .ToArray();
    }

    private static float MaxRotationDelta(Quaternion[] baseline, Transform[] wheels)
    {
        if (baseline == null || baseline.Length != wheels.Length)
            return 0f;
        return Enumerable.Range(0, wheels.Length)
            .Max(index => Quaternion.Angle(baseline[index], wheels[index].rotation));
    }

    private static float MaxFrontRotationDelta(Quaternion[] baseline, Transform[] wheels)
    {
        if (baseline == null || baseline.Length != wheels.Length || wheels.Length < 2)
            return 0f;
        return Mathf.Max(Quaternion.Angle(baseline[0], wheels[0].rotation),
            Quaternion.Angle(baseline[1], wheels[1].rotation));
    }

    private static object? ReadMember(object instance, string name)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var type = instance.GetType();
        return type.GetProperty(name, flags)?.GetValue(instance) ?? type.GetField(name, flags)?.GetValue(instance);
    }

    private static void WriteMember(object instance, string name, object value)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var type = instance.GetType();
        var property = type.GetProperty(name, flags);
        if (property != null && property.CanWrite)
        {
            property.SetValue(instance, value);
            return;
        }
        var field = type.GetField(name, flags);
        if (field != null)
        {
            field.SetValue(instance, value);
            return;
        }
        throw new MissingMemberException(type.FullName, name);
    }

    private static void SetInput(object input, float throttle, float brakes, float steering)
    {
        WriteMember(input, "Throttle", throttle);
        WriteMember(input, "Brakes", brakes);
        WriteMember(input, "Steering", steering);
        InvokeMethodIfPresent(input, "VC_Update");
    }

    private static void PreparePowertrain(Component controller, object input)
    {
        var powertrain = ReadMember(controller, "powertrain") ??
                         throw new MissingMemberException(controller.GetType().FullName, "powertrain");
        ActivateIfInactive(controller);
        ActivateIfInactive(powertrain);
        if (ReadMember(powertrain, "Components") is IEnumerable components)
            foreach (var component in components)
                if (component != null)
                    ActivateIfInactive(component);

        var engine = ReadMember(powertrain, "engine") ??
                     throw new MissingMemberException(powertrain.GetType().FullName, "engine");
        WriteMember(engine, "ignition", true);
        WriteMember(engine, "canRun", true);
        WriteMember(engine, "flyingStartEnabled", true);
        InvokeMethod(engine, "StartEngine");

        var transmission = ReadMember(powertrain, "transmission") ??
                           throw new MissingMemberException(powertrain.GetType().FullName, "transmission");
        WriteMember(input, "autoSetInput", false);
        SetInput(input, 0f, 0f, 0f);
    }

    private static void ShiftIntoFirst(Component controller)
    {
        var powertrain = ReadMember(controller, "powertrain") ??
                         throw new MissingMemberException(controller.GetType().FullName, "powertrain");
        var transmission = ReadMember(powertrain, "transmission") ??
                           throw new MissingMemberException(powertrain.GetType().FullName, "transmission");
        InvokeMethod(transmission, "ShiftInto", 1, true);
    }

    private static void ActivateIfInactive(object component)
    {
        if (ReadMember(component, "IsActive") is bool active && active)
            return;
        InvokeMethodIfPresent(component, "VC_Enable", true);
    }

    private static void WriteMemberIfPresent(object instance, string name, object? value)
    {
        if (value == null)
            return;
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var type = instance.GetType();
        var property = type.GetProperty(name, flags);
        if (property != null && property.CanWrite)
        {
            property.SetValue(instance, value);
            return;
        }
        type.GetField(name, flags)?.SetValue(instance, value);
    }

    private static float ReadNumber(object? instance, string name)
    {
        if (instance == null)
            return 0f;
        var value = ReadMember(instance, name);
        return value == null ? 0f : Convert.ToSingle(value);
    }

    private static string BuildPhysicsSample(Component controller, Rigidbody body, Vector3 forward)
    {
        var powertrain = ReadMember(controller, "powertrain");
        var engine = ReadMember(powertrain!, "engine");
        var nativeSpeed = TryReadAny(controller, "SpeedKph", "speedKph", "Speed", "speed");
        var grounded = CountGroundedWheels(powertrain);
        var longitudinalMps = Vector3.Dot(body.velocity, forward);
        var aero = controller.GetComponent<NWH.VehiclePhysics2.Modules.Aerodynamics.AerodynamicsModuleWrapper>();
        return $"mass={body.mass:F1}kg, grounded={grounded}/4, rbForward={longitudinalMps:F2}m/s ({longitudinalMps * KphPerMps:F1}km/h), " +
               $"nwhSpeed={nativeSpeed}, generatedPower={ReadNumber(engine, "generatedPower"):F1}, " +
               $"aeroDragN={(aero != null ? ReadNumber(ReadMember(aero, "module"), "longitudinalDragForce") : 0f):F1}";
    }

    private static int CountGroundedWheels(object? powertrain)
    {
        if (powertrain == null || ReadMember(powertrain, "wheels") is not IEnumerable wheels)
            return -1;
        var count = 0;
        foreach (var wheel in wheels)
        {
            var api = wheel == null ? null : ReadMember(wheel, "wheelUAPI");
            if (api != null && ReadMember(api, "IsGrounded") is bool grounded && grounded)
                count++;
        }
        return count;
    }

    private static bool ReadTcsActive(GameObject vehicle)
    {
        var wrapper = vehicle.GetComponent<NWH.VehiclePhysics2.Modules.TCS.TCSModuleWrapper>();
        var module = wrapper == null ? null : ReadMember(wrapper, "module");
        return module != null && ReadMember(module, "active") is bool active && active;
    }

    private static float ReadNwhBrakeTorque(Component controller)
    {
        var serialized = new SerializedObject(controller);
        var property = serialized.FindProperty("brakes.maxTorque");
        return property?.propertyType == SerializedPropertyType.Float ? property.floatValue : -1f;
    }

    private static string FormatDynamicState(Component controller, Rigidbody body, object input)
    {
        var powertrain = ReadMember(controller, "powertrain");
        var transmission = ReadMember(powertrain!, "transmission");
        var engine = ReadMember(powertrain!, "engine");
        return $"speed={body.velocity.magnitude * KphPerMps:F1}km/h, gear={ReadNumber(transmission, "Gear"):F0}, ratio={ReadNumber(transmission, "currentGearRatio"):F3}, grounded={CountGroundedWheels(powertrain)}/4, tcsActive={ReadTcsActive(controller.gameObject)}, rpm={ReadNumber(engine, "OutputRPM"):F0}, throttle={ReadNumber(input, "Throttle"):F2}, brake={ReadNumber(input, "Brakes"):F2}, steer={ReadNumber(input, "Steering"):F2}";
    }

    private static string TryReadAny(object instance, params string[] names)
    {
        foreach (var name in names)
        {
            var value = ReadMember(instance, name);
            if (value != null)
                return name + "=" + Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
        }
        return "unavailable";
    }

    private static void InvokeMethod(object instance, string name, params object[] args)
    {
        if (!InvokeMethodIfPresent(instance, name, args))
            throw new MissingMethodException(instance.GetType().FullName, name);
    }

    private static bool InvokeMethodIfPresent(object instance, string name, params object[] args)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var method = instance.GetType().GetMethods(flags).FirstOrDefault(candidate =>
            string.Equals(candidate.Name, name, StringComparison.Ordinal) &&
            candidate.GetParameters().Length == args.Length);
        if (method == null)
            return false;
        method.Invoke(instance, args);
        return true;
    }

    private static void Fail(RunState state, string reason)
    {
        Debug.LogError("[Bentley physics fixture] FAIL in " + state.phase + ": " + reason);
        state.phase = "failed: " + reason;
        state.exitCode = 1;
        SessionState.SetString(SessionKey, JsonUtility.ToJson(state));
        EditorApplication.isPlaying = false;
    }

    private static void OnPlayModeChanged(PlayModeStateChange change)
    {
        if (change != PlayModeStateChange.EnteredEditMode)
            return;
        var json = SessionState.GetString(SessionKey, "");
        if (string.IsNullOrEmpty(json))
            return;
        var state = JsonUtility.FromJson<RunState>(json);
        SessionState.EraseString(SessionKey);
        var fixtureScene = Enumerable.Range(0, SceneManager.sceneCount).Select(SceneManager.GetSceneAt)
            .FirstOrDefault(scene => scene.IsValid() && scene.isLoaded &&
            scene.GetRootGameObjects().Any(root => string.Equals(root.name, state.fixtureRootName, StringComparison.Ordinal)));
        if (fixtureScene.IsValid() && fixtureScene.isLoaded)
            EditorSceneManager.CloseScene(fixtureScene, true);
        var priorScene = string.IsNullOrEmpty(state.priorScenePath)
            ? EditorSceneManager.GetSceneByName(state.priorSceneName)
            : EditorSceneManager.GetSceneByPath(state.priorScenePath);
        if (priorScene.IsValid() && priorScene.isLoaded)
            EditorSceneManager.SetActiveScene(priorScene);
        if (Application.isBatchMode && state.exitCode >= 0)
            EditorApplication.Exit(state.exitCode);
    }
}

internal static class BentleyFixtureSerializedPropertyExtensions
{
    public static void SetValueIfExists(this SerializedProperty? property, bool value)
    {
        if (property != null && property.propertyType == SerializedPropertyType.Boolean)
            property.boolValue = value;
    }
}
