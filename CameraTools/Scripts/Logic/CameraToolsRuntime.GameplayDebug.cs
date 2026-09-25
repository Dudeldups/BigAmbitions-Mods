#nullable enable
using System;
using System.Collections.Generic;
using Helpers;
using UnityEngine;

namespace CameraTools
{
    public sealed partial class CameraToolsRuntime : MonoBehaviour
    {
        private static readonly string[] GameplayZoomAnchorKeywords =
        {
            "head",
            "neck",
            "spine",
            "chest",
            "pelvis",
            "hip",
            "root",
            "camera",
            "follow",
            "look",
            "pivot"
        };

        private void DumpGameplayZoomDiagnostics(string reason)
        {
            if (settings == null || !settings.EnableGameplayZoomDebugLogging)
                return;

            var liveVirtualCamera = GetLiveVirtualCameraComponent();
            var mainCamera = Camera.main;

            LogGameplayZoomDebug("=== Gameplay zoom diagnostics start ===");
            LogGameplayZoomDebug(
                $"reason={reason}, gameplayActive={IsGameplayActive()}, cityMapOpen={IsCityMapOpen()}, " +
                $"gameplayController={(gameplayController == null ? "none" : gameplayController.GetType().FullName)}, " +
                $"liveVcam={(liveVirtualCamera == null ? "none" : GetHierarchyPath(liveVirtualCamera.transform))}");

            if (gameplayController != null)
            {
                LogGameplayZoomDebug(
                    $"gameplayController: path={GetHierarchyPath(gameplayController.transform)}, enabled={gameplayController.isActiveAndEnabled}, " +
                    $"pitch={GetCurrentGameplayPitchForLogging():0.##}");

                foreach (var member in EnumerateInterestingMembers(gameplayController, GameplayCameraKeywords))
                {
                    LogGameplayZoomDebug(
                        $"  gameplayMember: {member.DeclaringType}.{member.Name} type={member.MemberType.Name} writable={member.Writable} value={FormatMemberValue(member.Value)}");
                }

                var bounds = GetVector2Member(gameplayController, "minMaxDistance");
                LogGameplayZoomDebug($"  gameplayBounds: minMaxDistance={bounds}");
                foreach (var memberName in GameplayDistanceMemberNames)
                {
                    if (TryGetFloatMember(gameplayController, memberName, out var memberValue))
                        LogGameplayZoomDebug($"  distanceMember: {memberName}={memberValue:0.###}");
                }
            }
            else
            {
                LogGameplayZoomDebug("gameplayController: none");
            }

            if (liveVirtualCamera != null)
            {
                var follow = TryGetMemberValue(liveVirtualCamera, "Follow", out var followValue) ? followValue : null;
                var lookAt = TryGetMemberValue(liveVirtualCamera, "LookAt", out var lookAtValue) ? lookAtValue : null;
                var priority = TryGetMemberValue(liveVirtualCamera, "Priority", out var priorityValue) ? priorityValue : null;
                LogGameplayZoomDebug(
                    $"liveVcam: type={liveVirtualCamera.GetType().FullName}, priority={FormatMemberValue(priority)}, " +
                    $"follow={FormatMemberValue(follow)}, lookAt={FormatMemberValue(lookAt)}");

                var virtualCameraType = cinematachineVirtualCameraType;
                if (virtualCameraType != null)
                {
                    var pipeline = GetCinemachinePipeline(virtualCameraType, liveVirtualCamera);
                    if (pipeline != null)
                    {
                        foreach (var pipelineComponent in pipeline)
                        {
                            if (pipelineComponent == null)
                                continue;

                            LogGameplayZoomDebug($"  pipeline: {pipelineComponent.GetType().FullName}");
                            foreach (var member in EnumerateInterestingMembers(pipelineComponent, GameplayCameraKeywords))
                            {
                                LogGameplayZoomDebug(
                                    $"    pipelineMember: {member.DeclaringType}.{member.Name} type={member.MemberType.Name} writable={member.Writable} value={FormatMemberValue(member.Value)}");
                            }
                        }
                    }
                }
            }
            else
            {
                LogGameplayZoomDebug("liveVcam: none");
            }

            if (mainCamera != null)
            {
                LogGameplayZoomDebug(
                    $"Camera.main: path={GetHierarchyPath(mainCamera.transform)}, position={mainCamera.transform.position}, " +
                    $"rotation={mainCamera.transform.rotation.eulerAngles}, fov={mainCamera.fieldOfView:0.##}");
            }
            else
            {
                LogGameplayZoomDebug("Camera.main: none");
            }

            LogGameplayPlayerAnchorDiagnostics(mainCamera, liveVirtualCamera);
            LogGameplayZoomDebug("=== Gameplay zoom diagnostics end ===");
        }

        private void LogGameplayPlayerAnchorDiagnostics(Camera? mainCamera, Component? liveVirtualCamera)
        {
            var playerController = PlayerHelper.PlayerController;
            if (playerController == null)
            {
                LogGameplayZoomDebug("playerController: none");
                return;
            }

            LogGameplayZoomDebug(
                $"playerController: type={playerController.GetType().FullName}, path={GetHierarchyPath(playerController.transform)}, " +
                $"position={playerController.transform.position}");

            var followTarget = GetFollowTargetTransform(liveVirtualCamera);
            if (followTarget != null)
            {
                LogGameplayZoomDebug(
                    $"followTarget: path={GetHierarchyPath(followTarget)}, position={followTarget.position}, " +
                    $"localPosition={followTarget.localPosition}");
            }

            var anchorCandidates = new List<Transform>();
            foreach (var transform in playerController.GetComponentsInChildren<Transform>(true))
            {
                if (transform == null)
                    continue;

                var path = GetHierarchyPath(transform).ToLowerInvariant();
                if (!ContainsAny(path, GameplayZoomAnchorKeywords))
                    continue;

                anchorCandidates.Add(transform);
            }

            anchorCandidates.Sort((left, right) =>
            {
                var leftPath = GetHierarchyPath(left);
                var rightPath = GetHierarchyPath(right);
                return string.Compare(leftPath, rightPath, StringComparison.OrdinalIgnoreCase);
            });

            foreach (var candidate in anchorCandidates)
            {
                var line =
                    $"  anchorCandidate: path={GetHierarchyPath(candidate)}, position={candidate.position}, localPosition={candidate.localPosition}";

                if (mainCamera != null)
                {
                    var screenPosition = mainCamera.WorldToScreenPoint(candidate.position);
                    var distance = Vector3.Distance(mainCamera.transform.position, candidate.position);
                    line += $", screen={screenPosition}, cameraDistance={distance:0.###}";
                }

                if (followTarget != null)
                {
                    var followDistance = Vector3.Distance(followTarget.position, candidate.position);
                    line += $", followDistance={followDistance:0.###}";
                }

                LogGameplayZoomDebug(line);
            }
        }

        private static Transform? GetFollowTargetTransform(Component? liveVirtualCamera)
        {
            if (liveVirtualCamera == null)
                return null;

            if (!TryGetMemberValue(liveVirtualCamera, "Follow", out var followValue))
                return null;

            return followValue switch
            {
                Transform transform => transform,
                Component component => component.transform,
                GameObject gameObject => gameObject.transform,
                _ => null
            };
        }
    }
}
