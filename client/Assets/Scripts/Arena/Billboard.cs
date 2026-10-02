using UnityEngine;

namespace Confluence.Client.Arena
{
    /// <summary>Keeps an icon quad facing the active camera (yaw and pitch), for prop interaction icons.</summary>
    public sealed class Billboard : MonoBehaviour
    {
        private void LateUpdate()
        {
            Camera cam = Camera.main;
            if (cam == null)
            {
                return;
            }

            transform.rotation = Quaternion.LookRotation(transform.position - cam.transform.position, Vector3.up);
        }
    }
}
