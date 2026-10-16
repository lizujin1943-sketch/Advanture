using UnityEngine;
using UnityEngine.InputSystem;

namespace Advanture.TacticalGame
{
    /// <summary>
    /// 把键盘鼠标转换为角色输入。点击游戏画面才锁定鼠标；Esc、切换窗口或禁用时释放。
    /// 与运动组件分开，后续换成手柄或其他输入来源时不需要改碰撞代码。
    /// </summary>
    [RequireComponent(typeof(FirstPersonMotor))]
    public sealed class FirstPersonInput : MonoBehaviour
    {
        private FirstPersonMotor motor;
        private bool captured;
        public bool HasControl => captured && Cursor.lockState == CursorLockMode.Locked;

        private void Awake() => motor = GetComponent<FirstPersonMotor>();

        private void Update()
        {
            var keyboard = Keyboard.current;
            var mouse = Mouse.current;
            // 操作系统或 Unity 编辑器可能在 Esc / 切换窗口时主动解锁。
            if (captured && Cursor.lockState != CursorLockMode.Locked) ReleaseCursor();
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
            {
                ReleaseCursor();
                motor.Step(Vector2.zero, Vector2.zero, false, Time.deltaTime);
                return;
            }

            if (!HasControl)
            {
                if (Application.isFocused && mouse != null && mouse.leftButton.wasPressedThisFrame)
                {
                    Cursor.lockState = CursorLockMode.Locked;
                    Cursor.visible = false;
                    captured = true;
                    // 刚锁定时忽略这一帧鼠标位移，避免焦点切换造成视角突然跳动。
                }
                motor.Step(Vector2.zero, Vector2.zero, false, Time.deltaTime);
                return;
            }

            Vector2 movement = Vector2.zero;
            bool sprint = false;
            if (keyboard != null)
            {
                movement.x = (keyboard.dKey.isPressed ? 1 : 0) - (keyboard.aKey.isPressed ? 1 : 0);
                movement.y = (keyboard.wKey.isPressed ? 1 : 0) - (keyboard.sKey.isPressed ? 1 : 0);
                sprint = keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed;
                if (keyboard.rKey.wasPressedThisFrame)
                {
                    motor.Respawn();
                    return;
                }
            }
            Vector2 look = mouse != null ? mouse.delta.ReadValue() : Vector2.zero;
            motor.Step(movement, look, sprint, Time.deltaTime);
        }

        private void ReleaseCursor()
        {
            captured = false;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private void OnApplicationFocus(bool focused)
        {
            if (!focused) ReleaseCursor();
        }

        private void OnDisable() => ReleaseCursor();
    }
}
