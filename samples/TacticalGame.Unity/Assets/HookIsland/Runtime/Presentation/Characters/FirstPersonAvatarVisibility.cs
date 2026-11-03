using UnityEngine;

namespace Advanture.HookIsland
{
    /// <summary>
    /// 只隐藏当前客户端自己的世界角色，防止头、帽子、背包遮住眼睛处的镜头。
    /// 不停用对象或动画，也不影响其他客户端看到的模型；退出时恢复原有渲染开关。
    /// </summary>
    internal sealed class FirstPersonAvatarVisibility
    {
        private readonly Renderer[] renderers;
        private readonly bool[] previous;
        private bool hidden;

        public FirstPersonAvatarVisibility(Transform body)
        {
            renderers=body.GetComponentsInChildren<Renderer>(true);
            previous=new bool[renderers.Length];
        }

        public void SetHidden(bool value)
        {
            if(hidden==value)return;
            hidden=value;
            for(int i=0;i<renderers.Length;i++)
            {
                if(renderers[i]==null)continue;
                if(value){previous[i]=renderers[i].forceRenderingOff;renderers[i].forceRenderingOff=true;}
                else renderers[i].forceRenderingOff=previous[i];
            }
        }
    }
}
