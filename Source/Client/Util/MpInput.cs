using UnityEngine;

namespace Multiplayer.Client.Util
{
    public static class MpInput
    {
        public static Vector3? downAt;
        public static bool dragging;

        public static bool Mouse2UpWithoutDrag { get; private set; }

        public static void Update()
        {
            if (Input.GetKeyDown(KeyCode.Mouse2))
                downAt = Input.mousePosition;

            if (downAt is { } v && (Input.mousePosition - v).sqrMagnitude > 20 && Input.GetKey(KeyCode.Mouse2))
                dragging = true;

            Mouse2UpWithoutDrag = false;

            if (Input.GetKeyUp(KeyCode.Mouse2))
            {
                if (!dragging)
                    Mouse2UpWithoutDrag = true;
                downAt = null;
                dragging = false;
            }
        }
    }
}
