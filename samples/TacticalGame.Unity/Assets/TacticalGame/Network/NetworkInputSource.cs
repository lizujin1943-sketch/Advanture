using Advanture.Tactical.Simulation;
using UnityEngine;
using UnityEngine.InputSystem;
namespace Advanture.TacticalGame.NetworkView
{
    /// <summary>键鼠采样与帧冻结分开：每个渲染帧更新意图，只有会话请求新逻辑帧时才生成输入。</summary>
    public sealed class NetworkInputSource : MonoBehaviour
    {
        private Vector2 movement;
        private bool captured, sprint, respawn;
        private float spawnYaw;
        public float Yaw { get; private set; }
        public float Pitch { get; private set; }
        public bool HasControl => captured && Cursor.lockState == CursorLockMode.Locked && Application.isFocused;

        public void InitializeLook(float yaw)
        { spawnYaw = yaw; Yaw = yaw; Pitch = 0; movement = Vector2.zero; respawn = false; }

        public void CaptureDevice(bool playing, bool allowCapture = true)
        {
            var keyboard = Keyboard.current; var mouse = Mouse.current;
            if (!playing || !Application.isFocused || (captured && Cursor.lockState != CursorLockMode.Locked) ||
                (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)) { Release(); return; }
            if (!HasControl)
            {
                movement = Vector2.zero;
                if (allowCapture && mouse != null && mouse.leftButton.wasPressedThisFrame)
                { Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; captured = true; }
                return; // 锁定当帧忽略鼠标跳变。
            }
            Vector2 look = mouse != null ? mouse.delta.ReadValue() : Vector2.zero;
            Yaw = Mathf.Repeat(Yaw + look.x * .12f, 360);
            Pitch = Mathf.Clamp(Pitch - look.y * .12f, -85, 85);
            movement = keyboard == null ? Vector2.zero : new Vector2((keyboard.dKey.isPressed ? 1 : 0) - (keyboard.aKey.isPressed ? 1 : 0),
                (keyboard.wKey.isPressed ? 1 : 0) - (keyboard.sKey.isPressed ? 1 : 0));
            sprint = keyboard != null && (keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed);
            if (keyboard != null && keyboard.rKey.wasPressedThisFrame)
            { respawn = true; Yaw = spawnYaw; Pitch = 0; }
        }

        public TacticalInput Sample(ulong tick)
        {
            if (!HasControl) return default; // 失焦/菜单也显式冻结中性输入，后台会话照常推进。
            bool reset = respawn; respawn = false;
            return InputQuantizer.Create(reset ? Vector2.zero : movement, Yaw, Pitch, sprint, reset);
        }
        public void Release()
        { captured = false; movement = Vector2.zero; sprint = false; respawn = false; Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
        private void OnApplicationFocus(bool focused) { if (!focused) Release(); }
        private void OnDisable() => Release();
    }
}
