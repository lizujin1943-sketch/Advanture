using System;
using UnityEngine;

namespace Advanture.HookIsland
{
    /// <summary>枪械第一人称镜头配置；仅影响本地表现，不进入网络协议或存档。</summary>
    [Serializable]
    public sealed class FirearmCameraSettings
    {
        [Tooltip("相机相对角色脚底的高度；采用稳定高度，避免跟随头部动画上下晃动。")]
        [Range(.8f, 1.3f)] public float eyeHeight = 1.03f;
        [Tooltip("进入持枪视角时略微向下看，便于瞄准水面；切回后恢复原来的俯仰角。")]
        [Range(0, 35)] public float initialPitch = 12;
        [Tooltip("第一人称的垂直视野角。")]
        [Range(50, 90)] public float fieldOfView = 65;
        [Tooltip("视野角的过渡时间；镜头位置立即切换，避免穿过角色身体。")]
        [Range(.1f, .5f)] public float transitionSeconds = .22f;
    }
}
