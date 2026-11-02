using Advanture.HookIsland.Simulation;
using UnityEngine;

namespace Advanture.HookIsland
{
    /// <summary>Cosmetic two-bone arm pose layered after the fisher's authored animation.</summary>
    public sealed class MeleeAvatarView
    {
        private readonly Transform body,upper,forearm,hand,pan;
        private readonly FryingPanArt art;
        private readonly LineRenderer trail,flash;
        private int remaining;
        private float observationTime,flashUntil;
        public MeleeAvatarView(Transform body,FryingPanArt art)
        {
            this.body=body;this.art=art;
            foreach(var bone in body.GetComponentsInChildren<Transform>(true))
            {if(bone.name=="UpperArm_R")upper=bone;else if(bone.name=="Forearm_R")forearm=bone;else if(bone.name=="Hand_R")hand=bone;}
            pan=art.Create(body);pan.gameObject.SetActive(false);
            trail=Line("Pan sweep",.042f);flash=Line("Pan contact",.026f);
        }
        private LineRenderer Line(string name,float width)
        {
            var t=new GameObject(name).transform;t.SetParent(body,false);var line=t.gameObject.AddComponent<LineRenderer>();
            line.sharedMaterial=art.TrailMaterial;line.useWorldSpace=false;line.startWidth=width;line.endWidth=width*.2f;line.numCapVertices=4;line.enabled=false;line.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;return line;
        }
        public void Present(Fisher confirmed,Fisher motion,WeaponDefinition weapon,bool interrupted)
        {
            bool visible=weapon!=null && upper!=null && forearm!=null && hand!=null;
            pan.gameObject.SetActive(visible);trail.enabled=flash.enabled=false;
            if(!visible)return;
            if(motion.Melee.RemainingTicks!=remaining){remaining=motion.Melee.RemainingTicks;observationTime=Time.unscaledTime;}
            float elapsed=(weapon.DurationTicks-remaining)/(float)HookGame.Rate+Mathf.Min(Time.unscaledTime-observationTime,1f/HookGame.Rate);
            float progress=Mathf.Clamp01(elapsed/(weapon.DurationTicks/(float)HookGame.Rate));
            bool active=motion.Melee.Active && !interrupted;
            Vector3 resting=new Vector3(-.33f,.52f,.23f),windup=new Vector3(-.43f,.72f,.12f),strike=new Vector3(.045f,.55f,.29f);
            Vector3 target=resting;float yaw=-22,tilt=55;
            if(active)
            {
                if(progress<.24f){float t=Ease(progress/.24f);target=Vector3.Lerp(resting,windup,t);yaw=Mathf.Lerp(-22,-100,t);tilt=Mathf.Lerp(55,22,t);}
                else if(progress<.46f){float t=Ease((progress-.24f)/.22f);target=Vector3.Lerp(windup,strike,t);yaw=Mathf.Lerp(-100,75,t);tilt=Mathf.Lerp(22,8,t);}
                else {float t=Ease((progress-.46f)/.54f);target=Vector3.Lerp(strike,resting,t);yaw=Mathf.Lerp(75,-22,t);tilt=Mathf.Lerp(8,55,t);}
            }
            PoseArm(body.TransformPoint(target));
            hand.rotation=body.rotation*Quaternion.Euler(90,yaw,0);
            // Imported bones have a 100× basis; the prop stays under the metre-scaled model root.
            pan.position=hand.position;pan.rotation=body.rotation*Quaternion.Euler(tilt,yaw,0);
            if(active && progress>.22f && progress<.52f)
            {
                trail.enabled=true;trail.positionCount=14;float fade=Mathf.Sin(Mathf.InverseLerp(.22f,.52f,progress)*Mathf.PI);
                trail.startColor=new Color(1,.91f,.61f,.65f*fade);trail.endColor=new Color(1,1,1,0);
                float head=Mathf.Lerp(-95,85,Mathf.InverseLerp(.24f,.46f,progress));
                for(int i=0;i<14;i++){float angle=(head-65*i/13f)*Mathf.Deg2Rad;trail.SetPosition(i,new Vector3(Mathf.Sin(angle)*.88f,.55f,Mathf.Cos(angle)*.88f));}
            }
            if(Time.unscaledTime<flashUntil)
            {
                flash.enabled=true;flash.positionCount=9;float t=1-(flashUntil-Time.unscaledTime)/.14f;
                flash.startColor=flash.endColor=new Color(1,.94f,.66f,1-t);Vector3 center=new Vector3(0,.5f,.93f);
                for(int i=0;i<9;i++){float a=i*Mathf.PI*.5f;float radius=i%2==0?.19f*(1+t):.04f;flash.SetPosition(i,center+new Vector3(Mathf.Cos(a)*radius,0,Mathf.Sin(a)*radius));}
            }
        }
        public void ConfirmHit()=>flashUntil=Time.unscaledTime+.14f;
        private void PoseArm(Vector3 goal)=>FisherArmPose.Reach(body,upper,forearm,hand,goal);
        private static float Ease(float t){t=Mathf.Clamp01(t);return t*t*(3-2*t);}
    }
}
