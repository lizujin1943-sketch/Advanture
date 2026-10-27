using System;
using UnityEngine;

namespace Advanture.HookIsland
{
    /// <summary>Local camera and input-space conversion; never owns simulation state.</summary>
    public sealed class FishingCameraView : IDisposable
    {
        private readonly Camera camera;
        private readonly bool platform, overheadOrthographic;
        private readonly float fieldOfView, sensitivity, verticalSensitivityMultiplier, pivotHeight, shoulderOffset, overheadNear, overheadFov;
        private readonly Vector3 overheadPosition;
        private readonly Quaternion overheadRotation;
        private float overheadZoom, yaw, pitch, distance;
        private Vector3 renderedFeet;
        public bool ThirdPerson { get; private set; }
        public bool PointerCaptured { get; private set; }
        public FishingCameraView(Camera camera, bool platform, float fieldOfView = 75, float sensitivity = 1.2f,
            float pivotHeight = 1, float verticalSensitivityMultiplier = .75f, float distance = 3.1f, float shoulderOffset = .6f)
        {
            this.camera=camera;this.platform=platform;
            this.fieldOfView=Mathf.Clamp(fieldOfView,50,100);this.sensitivity=Mathf.Clamp(sensitivity,.1f,10);
            this.pivotHeight=Mathf.Clamp(pivotHeight,.5f,2);
            this.distance=Mathf.Clamp(distance,2,5);this.shoulderOffset=Mathf.Clamp(shoulderOffset,0,1);
            this.verticalSensitivityMultiplier=Mathf.Clamp(verticalSensitivityMultiplier,.1f,1);
            overheadOrthographic=camera.orthographic;overheadZoom=camera.orthographicSize;
            overheadNear=camera.nearClipPlane;overheadFov=camera.fieldOfView;
            overheadPosition=camera.transform.position;overheadRotation=camera.transform.rotation;
        }
        public void Toggle(Vector3 facing, Vector3 feet)
        {
            if(ThirdPerson)
            {
                RestoreOverhead();
                camera.transform.position=OverheadPosition(feet);
                return;
            }
            overheadZoom=camera.orthographicSize;
            facing.y=0;
            yaw=facing.sqrMagnitude>.001f?Mathf.Atan2(facing.x,facing.z)*Mathf.Rad2Deg:0;
            pitch=25;ThirdPerson=true;
            camera.orthographic=false;camera.fieldOfView=fieldOfView;camera.nearClipPlane=.04f;
            Follow(feet,feet,0);
        }
        public void Look(Vector2 delta)
        {
            if(!ThirdPerson)return;
            yaw=Mathf.Repeat(yaw+delta.x*sensitivity,360);
            // Free look in both directions; avoid flipping through the vertical poles.
            pitch=Mathf.Clamp(pitch-delta.y*sensitivity*verticalSensitivityMultiplier,-89,89);
            UpdateThirdPersonPose();
        }
        public void Zoom(float scroll)
        {
            if(!ThirdPerson)return;
            distance=Mathf.Clamp(distance-scroll*.35f,2,5);
            UpdateThirdPersonPose();
        }
        public Vector2 Movement(Vector2 input)
        {
            input=Vector2.ClampMagnitude(input,1);
            if(!ThirdPerson)return input;
            var direction=Quaternion.Euler(0,yaw,0)*new Vector3(input.x,0,input.y);
            return new Vector2(direction.x,direction.z);
        }
        public Vector3 Aim(Ray ray, Vector3 feet, float range)
        {
            // Shoulder-camera rays meet the visible sea surface, including while swimming.
            var plane=new Plane(Vector3.up,new Vector3(0,ThirdPerson?.12f:.75f,0));
            if(plane.Raycast(ray,out float distance))
            {
                var target=ray.GetPoint(distance);
                if(!ThirdPerson)return target;
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
        public void Follow(Vector3 renderedFeet, Vector3 confirmedFeet, float dt)
        {
            this.renderedFeet=renderedFeet;
            if(ThirdPerson)
                UpdateThirdPersonPose();
            else if(platform)
                camera.transform.position=Vector3.Lerp(camera.transform.position,OverheadPosition(confirmedFeet),1-Mathf.Exp(-4*dt));
        }
        private void UpdateThirdPersonPose()
        {
            var rotation=Quaternion.Euler(pitch,yaw,0);
            var right=Quaternion.Euler(0,yaw,0)*Vector3.right;
            // Keep the character left of the reticle; orbit also updates before sampling the aim ray.
            var pivot=renderedFeet+Vector3.up*pivotHeight+right*shoulderOffset;
            var position=pivot-rotation*Vector3.forward*distance;
            // Keep the lens above the sea/platform without restricting where the player looks.
            position.y=Mathf.Max(position.y,Mathf.Max(.35f,renderedFeet.y+.3f));
            camera.transform.SetPositionAndRotation(position,rotation);
        }
        private Vector3 OverheadPosition(Vector3 feet)=>platform?new Vector3(feet.x,24,feet.z-20):overheadPosition;
        public bool SetPointerCapture(bool capture)
        {
            capture&=ThirdPerson;
            var lockState=capture?CursorLockMode.Locked:CursorLockMode.None;
            bool changed=PointerCaptured!=capture || Cursor.lockState!=lockState;
            PointerCaptured=capture;
            // Capture checks run every frame; only transitions should reset relative mouse motion.
            if(Cursor.lockState!=lockState)Cursor.lockState=lockState;
            if(Cursor.visible==capture)Cursor.visible=!capture;
            return changed;
        }
        private void RestoreOverhead()
        {
            ThirdPerson=false;SetPointerCapture(false);
            if(camera==null)return;
            camera.orthographic=overheadOrthographic;camera.orthographicSize=overheadZoom;
            camera.nearClipPlane=overheadNear;camera.fieldOfView=overheadFov;
            camera.transform.rotation=overheadRotation;
        }
        public void Reset() { RestoreOverhead();if(camera!=null)camera.transform.position=overheadPosition; }
        public void Dispose() { Reset(); }
    }
}
