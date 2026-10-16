using UnityEngine;

namespace Advanture.TacticalGame
{
    /// <summary>
    /// 只负责本地试玩角色的移动、碰撞和视角，不直接读取键盘鼠标。
    /// 输入层每帧调用 Step；这里依赖 Unity 物理，不能作为确定性帧同步核心使用。
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public sealed class FirstPersonMotor : MonoBehaviour
    {
        [SerializeField, Tooltip("只上下转动的相机节点，角色根节点负责左右转动。")]
        private Transform view;
        [SerializeField, Min(.1f)] private float walkSpeed = 4.5f;
        [SerializeField, Min(.1f)] private float sprintSpeed = 7f;
        [SerializeField, Range(.01f, 1f), Tooltip("鼠标每移动一个像素，视角转动多少度。")]
        private float mouseSensitivity = .12f;
        [SerializeField, Range(1, 89)] private float pitchLimit = 85f;
        [SerializeField] private float gravity = -20f;

        private CharacterController controller;
        private Vector3 spawnPosition;
        private Quaternion spawnRotation;
        private float yaw;
        private float pitch;
        private float verticalSpeed;
        private bool initialized;

        public Transform View => view;

        private void Awake() => InitializeIfNeeded();

        /// <summary>场景构建器在编辑时指定相机；运行时也可由角色生成代码调用。</summary>
        public void Configure(Transform cameraView)
        {
            view = cameraView;
            InitializeIfNeeded();
        }

        private void InitializeIfNeeded()
        {
            if (initialized) return;
            controller = GetComponent<CharacterController>();
            spawnPosition = transform.position;
            spawnRotation = transform.rotation;
            yaw = transform.eulerAngles.y;
            pitch = 0;
            verticalSpeed = 0;
            initialized = true;
        }

        /// <summary>
        /// movement 的 X 是左右、Y 是前后；mouseDelta 是本帧累计的鼠标像素位移。
        /// 移动速度乘时间；鼠标已经是位移，不能再乘时间，否则灵敏度会受帧率影响。
        /// </summary>
        public void Step(Vector2 movement, Vector2 mouseDelta, bool sprint, float deltaTime)
        {
            InitializeIfNeeded();
            if (deltaTime <= 0 || !controller.enabled) return;

            yaw = Mathf.Repeat(yaw + mouseDelta.x * mouseSensitivity, 360f);
            pitch = Mathf.Clamp(pitch - mouseDelta.y * mouseSensitivity, -pitchLimit, pitchLimit);
            transform.rotation = Quaternion.Euler(0, yaw, 0);
            if (view != null) view.localRotation = Quaternion.Euler(pitch, 0, 0);

            // 限制二维向量长度，避免同时按 W+D 比单按 W 快约 41%。
            Vector2 direction = Vector2.ClampMagnitude(movement, 1);
            // 使用角色根节点的朝向，抬头低头不会把水平移动变成飞行。
            Vector3 horizontal = transform.right * direction.x + transform.forward * direction.y;
            float speed = sprint ? sprintSpeed : walkSpeed;
            if (controller.isGrounded && verticalSpeed < 0) verticalSpeed = -2f;
            verticalSpeed += gravity * deltaTime;
            Vector3 displacement = (horizontal * speed + Vector3.up * verticalSpeed) * deltaTime;

            // 合并为一次 Move，让 Unity 处理墙面滑动和地面碰撞。Move 不自动施加重力。
            CollisionFlags flags = controller.Move(displacement);
            if ((flags & CollisionFlags.Below) != 0 && verticalSpeed < 0) verticalSpeed = -2f;
            if ((flags & CollisionFlags.Above) != 0 && verticalSpeed > 0) verticalSpeed = 0;
            if (transform.position.y < -10) Respawn();
        }

        /// <summary>回到本次角色创建时的出生位置，并清除下落速度和相机俯仰。</summary>
        public void Respawn()
        {
            InitializeIfNeeded();
            // 传送时先关闭胶囊体，避免旧碰撞接触把新位置推回原处。
            bool wasEnabled = controller.enabled;
            controller.enabled = false;
            transform.SetPositionAndRotation(spawnPosition, spawnRotation);
            controller.enabled = wasEnabled;
            yaw = spawnRotation.eulerAngles.y;
            pitch = 0;
            verticalSpeed = 0;
            if (view != null) view.localRotation = Quaternion.identity;
        }
    }
}
