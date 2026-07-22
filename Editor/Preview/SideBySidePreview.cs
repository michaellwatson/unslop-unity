using System;
using Unslop.UnityBridge.Editor.Diagnostics;
using UnityEditor;
using UnityEngine;

namespace Unslop.UnityBridge.Editor.Preview
{
    /// <summary>
    /// Side-by-side PreviewRenderUtility wrapper. Caller must Dispose.
    /// </summary>
    public sealed class SideBySidePreview : IDisposable
    {
        PreviewRenderUtility _utility;
        GameObject _left;
        GameObject _right;
        bool _disposed;
        Vector2 _drag;
        float _distance = 3.5f;

        public Texture LeftTexture { get; private set; }
        public Texture RightTexture { get; private set; }
        public bool IsReady => _utility != null && !_disposed;

        public SideBySidePreview()
        {
            _utility = new PreviewRenderUtility();
            _utility.cameraFieldOfView = 30f;
            _utility.camera.nearClipPlane = 0.01f;
            _utility.camera.farClipPlane = 100f;
        }

        public void SetLeft(GameObject instance)
        {
            Replace(ref _left, instance, new Vector3(-0.75f, 0f, 0f));
        }

        public void SetRight(GameObject instance)
        {
            Replace(ref _right, instance, new Vector3(0.75f, 0f, 0f));
        }

        public void SetFromPrefabs(GameObject leftPrefab, GameObject rightPrefab)
        {
            SetLeft(leftPrefab == null ? null : (GameObject)PrefabUtility.InstantiatePrefab(leftPrefab));
            SetRight(rightPrefab == null ? null : (GameObject)PrefabUtility.InstantiatePrefab(rightPrefab));
        }

        public void HandleInput(Rect rect)
        {
            var controlId = GUIUtility.GetControlID("UnslopSideBySidePreview".GetHashCode(), FocusType.Passive, rect);
            var evt = Event.current;
            switch (evt.GetTypeForControl(controlId))
            {
                case EventType.MouseDrag:
                    if (GUIUtility.hotControl == controlId)
                    {
                        _drag += evt.delta;
                        evt.Use();
                    }

                    break;
                case EventType.ScrollWheel:
                    if (rect.Contains(evt.mousePosition))
                    {
                        _distance = Mathf.Clamp(_distance + evt.delta.y * 0.05f, 0.5f, 20f);
                        evt.Use();
                    }

                    break;
                case EventType.MouseDown:
                    if (rect.Contains(evt.mousePosition) && evt.button == 0)
                    {
                        GUIUtility.hotControl = controlId;
                        evt.Use();
                    }

                    break;
                case EventType.MouseUp:
                    if (GUIUtility.hotControl == controlId)
                    {
                        GUIUtility.hotControl = 0;
                        evt.Use();
                    }

                    break;
            }
        }

        public void Render(Rect leftRect, Rect rightRect)
        {
            EnsureNotDisposed();
            LeftTexture = RenderHalf(leftRect, _left);
            RightTexture = RenderHalf(rightRect, _right);
        }

        public void DrawGuiLayout(float height = 220f)
        {
            EnsureNotDisposed();
            var rect = GUILayoutUtility.GetRect(10, 10000, height, height);
            var half = rect.width * 0.5f;
            var leftRect = new Rect(rect.x, rect.y, half - 2, rect.height);
            var rightRect = new Rect(rect.x + half + 2, rect.y, half - 2, rect.height);
            HandleInput(rect);
            Render(leftRect, rightRect);
            if (LeftTexture != null)
            {
                GUI.DrawTexture(leftRect, LeftTexture, ScaleMode.ScaleToFit, false);
            }

            if (RightTexture != null)
            {
                GUI.DrawTexture(rightRect, RightTexture, ScaleMode.ScaleToFit, false);
            }

            Handles.BeginGUI();
            GUI.Label(new Rect(leftRect.x + 4, leftRect.y + 4, 80, 18), "Installed");
            GUI.Label(new Rect(rightRect.x + 4, rightRect.y + 4, 80, 18), "Candidate");
            Handles.EndGUI();
        }

        Texture RenderHalf(Rect rect, GameObject focus)
        {
            if (rect.width < 2 || rect.height < 2)
            {
                return null;
            }

            _utility.BeginPreview(rect, GUIStyle.none);
            var rot = Quaternion.Euler(-_drag.y, -_drag.x, 0);
            var camPos = rot * (Vector3.forward * -_distance);
            _utility.camera.transform.position = camPos;
            _utility.camera.transform.LookAt(focus != null ? focus.transform.position : Vector3.zero);
            _utility.lights[0].intensity = 1.2f;
            _utility.lights[0].transform.rotation = Quaternion.Euler(40f, 40f, 0f);
            _utility.ambientColor = new Color(0.35f, 0.35f, 0.35f, 0f);
            if (focus != null)
            {
                foreach (var r in focus.GetComponentsInChildren<Renderer>(true))
                {
                    _utility.DrawMesh(
                        GetSharedMesh(r),
                        r.localToWorldMatrix,
                        r.sharedMaterial,
                        0);
                }
            }

            _utility.Render(true);
            return _utility.EndPreview();
        }

        static Mesh GetSharedMesh(Renderer renderer)
        {
            if (renderer is MeshRenderer)
            {
                var filter = renderer.GetComponent<MeshFilter>();
                return filter != null ? filter.sharedMesh : null;
            }

            if (renderer is SkinnedMeshRenderer skinned)
            {
                return skinned.sharedMesh;
            }

            return null;
        }

        void Replace(ref GameObject slot, GameObject instance, Vector3 offset)
        {
            EnsureNotDisposed();
            if (slot != null)
            {
                UnityEngine.Object.DestroyImmediate(slot);
            }

            slot = instance;
            if (slot == null)
            {
                return;
            }

            _utility.AddSingleGO(slot);
            slot.transform.position = offset;
        }

        void EnsureNotDisposed()
        {
            if (_disposed || _utility == null)
            {
                throw new ObjectDisposedException(nameof(SideBySidePreview));
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            try
            {
                if (_left != null)
                {
                    UnityEngine.Object.DestroyImmediate(_left);
                }

                if (_right != null)
                {
                    UnityEngine.Object.DestroyImmediate(_right);
                }

                _utility?.Cleanup();
            }
            catch (Exception ex)
            {
                BridgeLog.Warn("SideBySidePreview dispose: " + BridgeLog.Redact(ex.Message));
            }
            finally
            {
                _left = null;
                _right = null;
                _utility = null;
            }
        }
    }
}
