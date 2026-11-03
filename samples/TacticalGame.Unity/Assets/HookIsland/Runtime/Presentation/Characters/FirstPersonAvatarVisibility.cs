using UnityEngine;
using System.Collections.Generic;

namespace Advanture.HookIsland
{
    /// <summary>
    /// 只隐藏当前客户端自己的世界角色，防止头、帽子、背包遮住眼睛处的镜头。
    /// 不停用对象或动画，也不影响其他客户端看到的模型；退出时恢复原有渲染开关。
    /// </summary>
    internal sealed class FirstPersonAvatarVisibility
    {
        private readonly Dictionary<Renderer,bool> previous=new Dictionary<Renderer,bool>();
        private bool hidden;

        public FirstPersonAvatarVisibility(Transform body)
        {
            Register(body);
        }

        public void Register(Transform additional)
        {
            foreach(var renderer in additional.GetComponentsInChildren<Renderer>(true))
            {
                if(previous.ContainsKey(renderer))continue;
                previous.Add(renderer,renderer.forceRenderingOff);
                if(hidden)renderer.forceRenderingOff=true;
            }
        }

        public void SetHidden(bool value)
        {
            if(hidden==value)return;
            hidden=value;
            // Snapshot keys only on visibility transitions, never on every rendered frame.
            foreach(var renderer in new List<Renderer>(previous.Keys))
            {
                if(renderer==null)continue;
                if(value){previous[renderer]=renderer.forceRenderingOff;renderer.forceRenderingOff=true;}
                else renderer.forceRenderingOff=previous[renderer];
            }
        }
    }
}
