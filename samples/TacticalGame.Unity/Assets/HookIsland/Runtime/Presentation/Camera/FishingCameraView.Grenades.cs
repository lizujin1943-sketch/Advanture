using UnityEngine;

namespace Advanture.HookIsland
{
    public sealed partial class FishingCameraView
    {
        private bool grenadeEquipped,grenadeAiming;
        private float grenadeBlend,grenadeBaseSize;
        private Vector3 grenadeOffset;
        private bool grenadePoseApplied;
        public bool GrenadePulledBack=>grenadeBlend>.01f;
        public bool GrenadeCameraOutsideBody=>grenadePoseApplied && grenadeOffset.sqrMagnitude>.36f;
        /// <summary>暂时保留当前视角。不能让“收枪恢复俯视”的常规路径抢先改变投掷观察方向。</summary>
        public void SetGrenadeEquipped(bool value)
        {
            grenadeEquipped=value;
            if(value){scopeBlend=0;ScopeMagnification=1;}
        }
        public void UpdateGrenadeAim(bool value,float deltaTime)
        {
            grenadeAiming=value;
            grenadeBlend=Mathf.MoveTowards(grenadeBlend,value?1:0,Mathf.Max(0,deltaTime)/.2f);
        }
        private void RemoveGrenadePose()
        {
            if(!grenadePoseApplied || camera==null)return;
            camera.transform.position-=grenadeOffset;
            if(camera.orthographic)camera.orthographicSize=grenadeBaseSize;
            grenadePoseApplied=false;grenadeOffset=Vector3.zero;
        }
        private void ApplyGrenadePose()
        {
            if(camera==null || grenadeBlend<=0)return;
            Vector3 backward=-camera.transform.forward;float length=2*grenadeBlend;
            // 碰撞仅保护本机镜头；投掷有效性仍由共享地图的整数规则判断。
            if(Physics.SphereCast(camera.transform.position,.12f,backward,out var hit,length,~0,QueryTriggerInteraction.Ignore))length=Mathf.Max(0,hit.distance-.08f);
            grenadeOffset=backward*length;camera.transform.position+=grenadeOffset;
            grenadeBaseSize=camera.orthographicSize;
            if(camera.orthographic)camera.orthographicSize=grenadeBaseSize*(1+.2f*grenadeBlend);
            grenadePoseApplied=true;
        }
        private void ResetGrenadeCamera()
        {RemoveGrenadePose();grenadeEquipped=grenadeAiming=false;grenadeBlend=0;}
        public void ConfirmGrenadeBlast(float strength){kickStrength=strength;kickStarted=Time.unscaledTime;}
    }
}
