using Advanture.Tactical.Simulation;
using UnityEngine;
namespace Advanture.TacticalGame.NetworkView
{
    /// <summary>在输入边界把相机相对方向变成整数世界方向；浮点只用于采样，不进入确定性计算。</summary>
    public static class InputQuantizer
    {
        public static TacticalInput Create(Vector2 movement, float yaw, float pitch, bool sprint = false, bool respawn = false, bool fire = false, bool reload = false)
        {
            movement = Vector2.ClampMagnitude(movement, 1);
            Vector3 world = Quaternion.Euler(0, yaw, 0) * new Vector3(movement.x, 0, movement.y);
            int x = Mathf.RoundToInt(world.x * 1000), z = Mathf.RoundToInt(world.z * 1000);
            // 四舍五入可能让斜向长度略超 1000；向内收一个单位，保证网络输入通过相同的规范验证。
            while ((long)x * x + (long)z * z > 1000000)
            {
                if (Mathf.Abs(x) >= Mathf.Abs(z)) x -= System.Math.Sign(x);
                else z -= System.Math.Sign(z);
            }
            int angle = ((Mathf.RoundToInt(Mathf.Repeat(yaw, 360) * 100) % 36000) + 36000) % 36000;
            return new TacticalInput((short)x, (short)z, (ushort)angle, (short)Mathf.RoundToInt(Mathf.Clamp(pitch, -85, 85) * 100), true, sprint, respawn, fire, reload);
        }
    }
}
