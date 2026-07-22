using UnityEngine;

namespace Unslop.UnityBridge
{
    /// <summary>
    /// Named attachment point preserved across managed asset updates.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UnslopSocket : MonoBehaviour
    {
        [SerializeField] string socketId = string.Empty;
        [SerializeField] string displayName = string.Empty;

        public string SocketId => socketId;
        public string DisplayName => string.IsNullOrEmpty(displayName) ? socketId : displayName;

        public void Configure(string id, string name)
        {
            socketId = id ?? string.Empty;
            displayName = name ?? string.Empty;
        }
    }
}
