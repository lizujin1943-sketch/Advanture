using Advanture.HookIsland.Simulation;
using UnityEngine;

namespace Advanture.HookIsland
{
    /// <summary>Two-handed aim and moving weapon parts, driven by observed action ticks only.</summary>
    public sealed class FirearmAvatarView
    {
        private readonly Transform body,rightUpper,rightForearm,rightHand,leftUpper,leftForearm,leftHand,chest;
        private FirearmArt.Model model;
        private readonly FirearmModelSelection models;
        private int weaponId=-1;
        public event System.Action<Transform> ModelChanged;
        private int reloadTicks=-1,recoveryTicks=-1,drawTicks=-1;
        private float reloadObserved,recoveryObserved,drawObserved,confirmedShot=float.NegativeInfinity;
        public void ConfirmShot()=>confirmedShot=Time.unscaledTime;
        public Transform Muzzle=>model.Muzzle;
        public bool Visible=>model.Root.gameObject.activeSelf;
        public FirearmAvatarView(Transform body,FirearmArt art)
        {
            this.body=body;models=new FirearmModelSelection(body,art);model=models.Select(FirearmCatalog.Get(0));model.Root.gameObject.SetActive(false);
            foreach(var bone in body.GetComponentsInChildren<Transform>(true))
            {
                switch(bone.name)
                {
                    case "UpperArm_R":rightUpper=bone;break;case "Forearm_R":rightForearm=bone;break;case "Hand_R":rightHand=bone;break;
                    case "UpperArm_L":leftUpper=bone;break;case "Forearm_L":leftForearm=bone;break;case "Hand_L":leftHand=bone;break;
                    case "Chest":case "Spine_02":chest=bone;break;
                }
            }
        }
        public void Present(Fisher confirmed,Fisher motion,FirearmDefinition gun,bool interrupted)
        {
            bool visible=gun!=null && rightHand!=null && leftHand!=null;
            if(!visible){models.Select(null);return;}
            var selected=models.Select(gun);
            if(selected!=model){model=selected;ModelChanged?.Invoke(model.Root);}
            if(weaponId!=confirmed.Firearm.WeaponId)
            {weaponId=confirmed.Firearm.WeaponId;confirmedShot=float.NegativeInfinity;reloadTicks=recoveryTicks=drawTicks=-1;}
            // Equipment identity comes from the same confirmed inventory as the definition.
            // Only local action phase may be predicted; a speculative new gun cannot borrow this prop.
            var action=motion.Firearm.WeaponId==confirmed.Firearm.WeaponId?motion.Firearm:confirmed.Firearm;
            Observe(action.ReloadTicks,ref reloadTicks,ref reloadObserved);Observe(action.RecoveryTicks,ref recoveryTicks,ref recoveryObserved);Observe(action.DrawTicks,ref drawTicks,ref drawObserved);
            float recoil=interrupted?0:Mathf.Max(Remaining(action.RecoveryTicks,recoveryObserved)/(float)gun.RecoveryTicks,Mathf.Clamp01(1-(Time.unscaledTime-confirmedShot)/(gun.RecoveryTicks/(float)HookGame.Rate)));
            float draw=interrupted?0:Remaining(action.DrawTicks,drawObserved)/(float)gun.DrawTicks;
            bool reloading=!interrupted && action.Reloading;
            float phase=reloading?1-Remaining(action.ReloadTicks,reloadObserved)/gun.ReloadTicks:0;
            float lowered=draw*.17f+(reloading?.055f:0);
            float breath=interrupted?0:Mathf.Sin(Time.unscaledTime*2.2f)*.003f;
            var rightGoal=new Vector3(-.10f,.61f-lowered+breath,.29f-recoil*.055f);
            var leftGoal=new Vector3(.025f,.60f-lowered+breath,.31f-recoil*.055f);
            // A lowered free hand follows the removable magazine and returns to the support grip.
            float remove=reloading?Mathf.Sin(Mathf.Clamp01((phase-.10f)/.70f)*Mathf.PI):0;
            recoil*=FirearmArt.Recoil(gun);
            if(gun.Category!=FirearmCategory.Pistol)leftGoal=new Vector3(.015f,.59f-lowered+breath,.49f-recoil*.055f);
            leftGoal+=new Vector3(-.07f,-.17f,-.035f)*remove;
            if(interrupted){rightGoal=new Vector3(-.24f,.43f,.12f);leftGoal=new Vector3(.19f,.43f,.12f);}
            FisherArmPose.Reach(body,rightUpper,rightForearm,rightHand,body.TransformPoint(rightGoal));
            FisherArmPose.Reach(body,leftUpper,leftForearm,leftHand,body.TransformPoint(leftGoal));
            rightHand.rotation=body.rotation*Quaternion.Euler(90-recoil*14,0,-12);
            leftHand.rotation=body.rotation*Quaternion.Euler(90,0,18);
            model.Root.position=rightHand.position;
            model.Root.rotation=body.rotation*Quaternion.Euler(interrupted?48:draw*40-recoil*17+(reloading?15:0),0,reloading?-12:0);
            // Slide kick peaks immediately and returns well inside the six-tick recovery.
            model.Slide.localPosition=Vector3.back*(.045f*Mathf.Clamp01((recoil-.40f)/.60f));
            model.Magazine.localPosition=new Vector3(-.025f,-.17f,-.02f)*remove;
            model.Magazine.localRotation=Quaternion.Euler(0,0,-18*remove);
            if(chest!=null && !interrupted)chest.localRotation*=Quaternion.Euler(-recoil*3,0,0);
        }
        private static void Observe(int value,ref int previous,ref float when)
        {if(value==previous)return;previous=value;when=Time.unscaledTime;}
        private static float Remaining(int ticks,float observed)=>Mathf.Max(0,ticks-Mathf.Min(Time.unscaledTime-observed,1f/HookGame.Rate)*HookGame.Rate);
    }
}
