using UnityEngine;

namespace Advanture.HookIsland
{
    /// <summary>Confirmed one-shot upper-body animation layered over walking. Interrupts never undo healing.</summary>
    public sealed class FoodEatingView
    {
        private readonly Transform body,head,upper,forearm,hand,rod;
        private readonly Transform[] props;
        private float started=float.NegativeInfinity;
        private int type;
        public const float Duration=1.25f;
        public bool Active=>Time.unscaledTime-started<Duration;
        public int PresentedMeals {get;private set;}
        public FoodEatingView(Transform body,Transform rod,FoodArt art)
        {
            this.body=body;this.rod=rod;
            foreach(var bone in body.GetComponentsInChildren<Transform>(true))
            {if(bone.name=="UpperArm_R")upper=bone;else if(bone.name=="Forearm_R")forearm=bone;else if(bone.name=="Hand_R")hand=bone;else if(bone.name=="Head")head=bone;}
            props=new Transform[3];for(int n=0;n<props.Length;n++){props[n]=art.Create(body,n);props[n].gameObject.SetActive(false);}
        }
        public void Confirm(int foodType)
        {if(foodType<0 || foodType>=props.Length)return;type=foodType;started=Time.unscaledTime;PresentedMeals++;}
        public void Present(bool interrupted)
        {
            if(interrupted)started=float.NegativeInfinity;
            bool active=Active && upper!=null && forearm!=null && hand!=null && head!=null;
            for(int n=0;n<props.Length;n++)props[n].gameObject.SetActive(active && type==n);
            if(!active)return;
            if(rod!=null)rod.gameObject.SetActive(false);
            float t=(Time.unscaledTime-started)/Duration;
            float lift=t<.25f?Mathf.SmoothStep(0,1,t/.25f):t>.78f?1-Mathf.SmoothStep(0,1,(t-.78f)/.22f):1;
            Vector3 rest=hand.position;
            // Mouth is 4.5 cm above the authored head pivot. Hold the food below it,
            // so the straw / fish / bowl rim reaches the lips instead of covering the eyes.
            float gripBelowMouth=type==0?.12f:type==1?.03f:.06f;
            Vector3 grip=head.position+body.forward*.19f+body.up*(.045f-gripBelowMouth)-body.right*.04f;
            float bite=t>.25f && t<.78f?Mathf.Sin((t-.25f)*Mathf.PI*12)*.009f:0;
            Vector3 target=Vector3.Lerp(rest,grip+body.up*bite,lift);
            FisherArmPose.Reach(body,upper,forearm,hand,target);
            hand.rotation=Quaternion.Slerp(hand.rotation,body.rotation*Quaternion.Euler(70,0,-20),lift);
            props[type].position=hand.position+body.forward*.025f;
            props[type].rotation=body.rotation*Quaternion.Euler(type==0?Mathf.Lerp(0,22,lift):Mathf.Lerp(-10,-24,lift),0,Mathf.Sin(t*Mathf.PI*2)*4);
            head.localRotation*=Quaternion.Euler(lift*7+bite*120,0,-lift*3);
        }
    }
}
