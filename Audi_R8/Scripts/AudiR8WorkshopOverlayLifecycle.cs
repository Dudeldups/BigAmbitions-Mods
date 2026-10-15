#nullable enable
using System;
using UnityEngine;

// OverlayUI.Show activates this existing UI object after assigning its source
// overlay. Observe that exact lifecycle transition; never inspect UI children.
[AddComponentMenu("")]
[ExecuteAlways] // Callback-only relay also supports real activation checks in the Editor.
public sealed class AudiR8WorkshopOverlayLifecycle : MonoBehaviour
{
    private Action? shown;
    internal void Bind(Action callback) => shown = callback;
    internal void Unbind() => shown = null;
    private void OnEnable() => shown?.Invoke();
    private void OnDestroy() => shown = null;
}
