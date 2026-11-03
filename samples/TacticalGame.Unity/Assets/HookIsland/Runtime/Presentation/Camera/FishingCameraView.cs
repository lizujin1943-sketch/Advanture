using System;
using Advanture.HookIsland.Simulation;
using UnityEngine;

namespace Advanture.HookIsland
{
    /// <summary>Local camera and input-space conversion; never owns simulation state.</summary>
    public sealed class FishingCameraView : IDisposable
    {
        private readonly Camera camera;
        private readonly bool overheadOrthographic;
        private readonly FishingOverheadCamera stageCamera;
        private readonly FirearmCameraSettings firearmSettings;
        private readonly float fieldOfView, sensitivity, verticalSensitivityMultiplier, pivotHeight, shoulderOffset, overheadNear, overheadFov, initialPitch, initialDistance;
        private readonly Vector3 overheadPosition;
        private readonly Quaternion overheadRotation;
        private float overheadZoom, yaw, pitch, distance, firearmBlend;
        private bool orbitInitialized, thirdPerson;
        private float returnPitch;
        private Vector3 renderedFeet,lastKick;
        private float kickStarted=float.NegativeInfinity;
        private float scopeBlend,kickStrength=1;
        public int ScopeMagnification {get;private set;}=1;
        public bool Scoped=>FirstPerson && scopeBlend>0;
        public float LookSensitivityScale=>Scoped?Mathf.Tan(camera.fieldOfView*Mathf.Deg2Rad*.5f)/Mathf.Tan(Mathf.Clamp(firearmSettings.fieldOfView,50,90)*Mathf.Deg2Rad*.5f):1;
        public void ConfirmFirearmShot(float strength=1){kickStarted=Time.unscaledTime;kickStrength=strength;}
        public static float ScopedFieldOfView(float normal,int magnification)=>2*Mathf.Atan(Mathf.Tan(normal*Mathf.Deg2Rad*.5f)/Mathf.Max(1,magnification))*Mathf.Rad2Deg;
        public void UpdateScope(FirearmDefinition gun,bool held,bool allowed,float deltaTime)
        {
            if(!FirstPerson || !allowed || gun==null || gun.ScopeMagnification<=1)
            {scopeBlend=0;ScopeMagnification=1;}
            else
            {
                ScopeMagnification=gun.ScopeMagnification;
                scopeBlend=Mathf.MoveTowards(scopeBlend,held?1:0,Mathf.Max(0,deltaTime)/.16f);
            }
            if(CenterAim)UpdatePerspectivePose();
        }
        public void PrepareInput(){if(camera!=null)camera.transform.position-=lastKick;lastKick=Vector3.zero;}
        /// <summary>持枪时临时覆盖镜头模式，不覆盖玩家之前选择的俯视/第三人称模式。</summary>
        public bool FirstPerson { get; private set; }
        public bool ThirdPerson => thirdPerson && !FirstPerson;
        /// <summary>第一、第三人称都按视角移动，并使用屏幕中心取瞄准射线。</summary>
        public bool CenterAim => FirstPerson || ThirdPerson;
        public bool LookInputActive { get; private set; }
        public bool PointerCaptured => Cursor.lockState == CursorLockMode.Locked;
        public FishingCameraView(Camera camera, bool platform, float fieldOfView = 75, float sensitivity = 3,
            float pivotHeight = 1, float verticalSensitivityMultiplier = .85f, float distance = 3.6f, float shoulderOffset = .45f,
            float initialPitch = 40, FishingOverheadSettings overheadSettings = null, FirearmCameraSettings firearmSettings = null)
        {
            this.camera=camera;
            this.firearmSettings=firearmSettings??new FirearmCameraSettings();
            this.fieldOfView=Mathf.Clamp(fieldOfView,50,100);this.sensitivity=Mathf.Clamp(sensitivity,.1f,10);
            this.pivotHeight=Mathf.Clamp(pivotHeight,.5f,2);
            this.distance=Mathf.Clamp(distance,2,5);this.shoulderOffset=Mathf.Clamp(shoulderOffset,0,1);
            initialDistance=this.distance;this.initialPitch=Mathf.Clamp(initialPitch,10,70);
            this.verticalSensitivityMultiplier=Mathf.Clamp(verticalSensitivityMultiplier,.1f,1);
            overheadOrthographic=camera.orthographic;overheadZoom=camera.orthographicSize;
            overheadNear=camera.nearClipPlane;overheadFov=camera.fieldOfView;
            overheadPosition=camera.transform.position;overheadRotation=camera.transform.rotation;
            if(platform)stageCamera=new FishingOverheadCamera(camera,overheadSettings??new FishingOverheadSettings());
        }
        public void Toggle(Vector3 facing, Vector3 feet)
        {
            // 枪械固定使用第一人称；V 不改写退出持枪后应该恢复的镜头。
            if(FirstPerson)return;
            if(ThirdPerson)
            {
                RestoreOverhead();
                if(stageCamera!=null)stageCamera.Follow(feet);
                else camera.transform.position=overheadPosition;
                return;
            }
            if(stageCamera==null)overheadZoom=camera.orthographicSize;
            if(!orbitInitialized)
            {
                facing.y=0;
                yaw=facing.sqrMagnitude>.001f?Mathf.Atan2(facing.x,facing.z)*Mathf.Rad2Deg:0;
                pitch=initialPitch;orbitInitialized=true;
            }
            thirdPerson=true;
            camera.orthographic=false;camera.fieldOfView=fieldOfView;camera.nearClipPlane=.04f;
            Follow(feet);
        }
        public void Look(Vector2 delta)
        {
            if(!CenterAim)return;
            float scopedSensitivity=LookSensitivityScale;
            yaw=Mathf.Repeat(yaw+delta.x*sensitivity*scopedSensitivity,360);
            // Free look in both directions; avoid flipping through the vertical poles.
            pitch=Mathf.Clamp(pitch-delta.y*sensitivity*verticalSensitivityMultiplier*scopedSensitivity,-89,89);
            UpdatePerspectivePose();
        }
        public void Zoom(float scroll)
        {
            // 第一人称不允许用滚轮把镜头拉出身体，也不改写之前的第三人称距离。
            if(FirstPerson)return;
            if(!ThirdPerson)
            {
                if(stageCamera!=null)stageCamera.Zoom(scroll);
                else camera.orthographicSize=Mathf.Clamp(camera.orthographicSize-scroll*.7f,12,28);
                return;
            }
            distance=Mathf.Clamp(distance-scroll*.35f,2,5);
            UpdateThirdPersonPose();
        }
        public void UpdateFirearmFraming(bool equipped,float deltaTime)
        {
            if(equipped!=FirstPerson)
            {
                PrepareInput();
                if(equipped)
                {
                    if(!orbitInitialized){yaw=0;pitch=initialPitch;orbitInitialized=true;}
                    returnPitch=pitch;pitch=Mathf.Clamp(firearmSettings.initialPitch,0,35);
                    if(!thirdPerson && stageCamera==null)overheadZoom=camera.orthographicSize;
                    FirstPerson=true;camera.orthographic=false;camera.nearClipPlane=.02f;
                }
                else
                {
                    FirstPerson=false;scopeBlend=0;ScopeMagnification=1;pitch=returnPitch;
                    if(thirdPerson){camera.fieldOfView=fieldOfView;camera.nearClipPlane=.04f;}
                    else
                    {
                        RestoreOverhead();
                        if(stageCamera!=null)stageCamera.Follow(renderedFeet);
                        else camera.transform.position=overheadPosition;
                    }
                }
            }
            firearmBlend=Mathf.MoveTowards(firearmBlend,equipped?1:0,
                Mathf.Max(0,deltaTime)/Mathf.Clamp(firearmSettings.transitionSeconds,.1f,.5f));
            // 在采样输入前更新镜头；跟随角色后只更新位置，不重复推进视野角过渡。
            if(CenterAim)UpdatePerspectivePose();
        }
        public Vector2 Movement(Vector2 input)
        {
            input=Vector2.ClampMagnitude(input,1);
            if(!CenterAim)return input;
            var direction=Quaternion.Euler(0,yaw,0)*new Vector3(input.x,0,input.y);
            return new Vector2(direction.x,direction.z);
        }
        public Vector3 Aim(Ray ray, Vector3 feet, float range)
        {
            // Shoulder-camera rays meet the visible sea surface, including while swimming.
            var plane=new Plane(Vector3.up,new Vector3(0,CenterAim?.12f:.75f,0));
            if(plane.Raycast(ray,out float distance))
            {
                var target=ray.GetPoint(distance);
                if(!CenterAim)return target;
                // Near-horizontal rays meet the sea hundreds of metres away. Limit distance
                // before the adapter's world bounds check, which otherwise bends their heading.
                var offset=target-feet;offset.y=0;
                target=feet+Vector3.ClampMagnitude(offset,Mathf.Max(0,range));target.y=.12f;
                return target;
            }
            // Looking at/above the horizon still selects a fresh horizontal direction.
            var forward=new Vector3(ray.direction.x,0,ray.direction.z).normalized;
            if(forward.sqrMagnitude<.001f)forward=Quaternion.Euler(0,yaw,0)*Vector3.forward;
            return feet+forward*Mathf.Max(0,range);
        }
        public void Follow(Vector3 renderedFeet)
        {
            PrepareInput();this.renderedFeet=renderedFeet;
            if(CenterAim)
                UpdatePerspectivePose();
            else stageCamera?.Follow(renderedFeet);
            float age=Time.unscaledTime-kickStarted;
            if(camera!=null && age>=0 && age<.13f)
            {lastKick=(camera.transform.right*Mathf.Sin(age*105)+camera.transform.up*Mathf.Cos(age*85))*(.012f*kickStrength*(1-age/.13f));camera.transform.position+=lastKick;}
        }
        private void UpdatePerspectivePose()
        {
            if(!FirstPerson){UpdateThirdPersonPose();return;}
            // 跟随实际显示的本地脚底位置；转头只改变朝向，不绕角色公转。
            var eye=renderedFeet+Vector3.up*Mathf.Clamp(firearmSettings.eyeHeight,.8f,1.3f);
            eye.y=Mathf.Max(.35f,eye.y);
            camera.fieldOfView=Mathf.Lerp(fieldOfView,Mathf.Clamp(firearmSettings.fieldOfView,50,90),Mathf.SmoothStep(0,1,firearmBlend));
            camera.fieldOfView=Mathf.Lerp(camera.fieldOfView,ScopedFieldOfView(Mathf.Clamp(firearmSettings.fieldOfView,50,90),ScopeMagnification),Mathf.SmoothStep(0,1,scopeBlend));
            camera.transform.SetPositionAndRotation(eye,Quaternion.Euler(pitch,yaw,0));
        }
        private void UpdateThirdPersonPose()
        {
            var rotation=Quaternion.Euler(pitch,yaw,0);
            var right=Quaternion.Euler(0,yaw,0)*Vector3.right;
            // Keep the shoulder framing; orbit also updates before sampling the aim ray.
            var pivot=renderedFeet+Vector3.up*pivotHeight+right*shoulderOffset;
            var position=pivot-rotation*Vector3.forward*distance;
            camera.fieldOfView=fieldOfView;
            // Keep the lens above the sea/platform without restricting where the player looks.
            position.y=Mathf.Max(position.y,Mathf.Max(.35f,renderedFeet.y+.3f));
            camera.transform.SetPositionAndRotation(position,rotation);
        }
        public bool SetLookInputActive(bool active)
        {
            active&=CenterAim;
            if(!active && Scoped)UpdateScope(null,false,false,0);
            var target=active?CursorLockMode.Locked:CursorLockMode.None;
            bool changed=LookInputActive!=active || Cursor.lockState!=target || Cursor.visible==active;
            LookInputActive=active;
            // Change capture only on transitions: reapplying it every frame can reset motion.
            if(Cursor.lockState!=target)Cursor.lockState=target;
            if(Cursor.visible==active)Cursor.visible=!active;
            return changed;
        }
        private void RestoreOverhead()
        {
            thirdPerson=false;SetLookInputActive(false);
            if(camera==null)return;
            camera.orthographic=overheadOrthographic;camera.orthographicSize=overheadZoom;
            camera.nearClipPlane=overheadNear;camera.fieldOfView=overheadFov;
            camera.transform.rotation=overheadRotation;
        }
        public void Reset()
        {
            PrepareInput();kickStarted=float.NegativeInfinity;firearmBlend=scopeBlend=0;ScopeMagnification=1;FirstPerson=false;RestoreOverhead();orbitInitialized=false;distance=initialDistance;
            if(camera!=null)
            {
                if(stageCamera!=null)stageCamera.Reset();
                else camera.transform.position=overheadPosition;
            }
        }
        public void Dispose() { Reset(); }
    }
}
