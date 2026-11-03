using Advanture.HookIsland.Simulation;
using UnityEngine;
using UnityEngine.Rendering;

namespace Advanture.HookIsland
{
    /// <summary>
    /// 本地第一人称的枪和双手。与世界角色共用枪械资源，但使用独立的显示实例，
    /// 让抬头、低头不必修改其他玩家的角色姿态。只读取装备和动作，不拥有弹药或命中规则。
    /// 对象、网格和材质统一由 FishingPresentation 释放。
    /// </summary>
    public sealed class FirstPersonFirearmView
    {
        private readonly Transform root,rightHand,leftHand,rightSleeve,leftSleeve;
        private FirearmArt.Model model;
        private readonly FirearmModelSelection models;
        private readonly MeshRenderer rightSleeveRenderer,leftSleeveRenderer;
        private int weaponId=-1,reloadTicks=-1,recoveryTicks=-1,drawTicks=-1;
        private float reloadObserved,recoveryObserved,drawObserved,confirmedShot=float.NegativeInfinity;

        public bool Visible=>root.gameObject.activeSelf;
        public Transform Muzzle=>model.Muzzle;

        public FirstPersonFirearmView(Transform parent,FirearmArt art,Mesh handMesh,Material skin)
        {
            root=new GameObject("First person firearm").transform;root.SetParent(parent,false);
            models=new FirearmModelSelection(root,art);model=models.Select(FirearmCatalog.Get(0));
            rightHand=Part(model.Root,"Right grip hand",handMesh,skin,new Vector3(.005f,-.045f,-.012f),new Vector3(.075f,.085f,.075f));
            leftHand=Part(model.Root,"Left support hand",handMesh,skin,new Vector3(-.055f,-.035f,.028f),new Vector3(.065f,.07f,.09f));
            rightSleeve=Part(root,"Right sleeve",handMesh,skin,Vector3.zero,Vector3.one);
            leftSleeve=Part(root,"Left sleeve",handMesh,skin,Vector3.zero,Vector3.one);
            rightSleeveRenderer=rightSleeve.GetComponent<MeshRenderer>();leftSleeveRenderer=leftSleeve.GetComponent<MeshRenderer>();
            // 镜头内的手和枪不参与水面反射与世界投影；UI 层已被水面反射相机排除。
            foreach(var child in root.GetComponentsInChildren<Transform>(true))child.gameObject.layer=LayerMask.NameToLayer("UI");
            foreach(var renderer in root.GetComponentsInChildren<Renderer>(true))
            {renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;}
            Reset();
        }

        /// <summary>仅由已经去重的确认开火事件调用，回滚不会重复制造后坐力。</summary>
        public void ConfirmShot(){if(Visible)confirmedShot=Time.unscaledTime;}

        public void Present(Camera camera,Fisher confirmed,Fisher motion,FirearmDefinition gun,Vector3 aim,Material sleeve,bool interrupted,bool scoped=false)
        {
            if(camera==null || gun==null){Reset();return;}
            if(weaponId!=confirmed.Firearm.WeaponId)
            {Reset();weaponId=confirmed.Firearm.WeaponId;}
            var selected=models.Select(gun,!scoped);
            if(selected!=model)
            {
                model=selected;rightHand.SetParent(model.Root,false);leftHand.SetParent(model.Root,false);
                foreach(var child in model.Root.GetComponentsInChildren<Transform>(true))child.gameObject.layer=LayerMask.NameToLayer("UI");
                foreach(var renderer in model.Root.GetComponentsInChildren<Renderer>(true))
                {renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;}
            }
            root.gameObject.SetActive(true);
            // 装备身份必须来自确认态；只接受同一把枪的预测动作，避免串用换装前后的时钟。
            var action=motion.Firearm.WeaponId==confirmed.Firearm.WeaponId?motion.Firearm:confirmed.Firearm;
            Observe(action.ReloadTicks,ref reloadTicks,ref reloadObserved);
            Observe(action.RecoveryTicks,ref recoveryTicks,ref recoveryObserved);
            Observe(action.DrawTicks,ref drawTicks,ref drawObserved);
            float recoil=interrupted?0:Mathf.Max(Remaining(action.RecoveryTicks,recoveryObserved)/gun.RecoveryTicks,
                Mathf.Clamp01(1-(Time.unscaledTime-confirmedShot)/(gun.RecoveryTicks/(float)HookGame.Rate)));
            float draw=interrupted?0:Remaining(action.DrawTicks,drawObserved)/gun.DrawTicks;
            bool reloading=!interrupted && action.Reloading;
            float phase=reloading?1-Remaining(action.ReloadTicks,reloadObserved)/gun.ReloadTicks:0;
            float remove=reloading?Mathf.Sin(Mathf.Clamp01((phase-.10f)/.70f)*Mathf.PI):0;
            recoil*=FirearmArt.Recoil(gun);
            // 靠近画面右下方，保持中心准星可见。位置跟随最终镜头，不跟随世界角色转身的插值。
            root.SetPositionAndRotation(camera.transform.position,camera.transform.rotation);
            model.Root.localPosition=new Vector3(.16f,-.27f-draw*.20f-(interrupted?.12f:0),.38f-recoil*.045f);
            var direction=aim-model.Root.position;
            var rotation=direction.sqrMagnitude>.001f && Vector3.Dot(direction,camera.transform.forward)>0?
                Quaternion.LookRotation(direction,camera.transform.up):camera.transform.rotation;
            model.Root.rotation=rotation*Quaternion.Euler(draw*35-recoil*13+(reloading?14:0),0,reloading?-14:0);
            model.Slide.localPosition=Vector3.back*(.045f*Mathf.Clamp01((recoil-.40f)/.60f));
            model.Magazine.localPosition=new Vector3(-.025f,-.17f,-.02f)*remove;
            model.Magazine.localRotation=Quaternion.Euler(0,0,-18*remove);
            leftHand.localPosition=FirearmArt.SupportGrip(gun)+new Vector3(-.025f,-.17f,-.02f)*remove;
            rightSleeveRenderer.sharedMaterial=leftSleeveRenderer.sharedMaterial=sleeve;
            Sleeve(rightSleeve,root.TransformPoint(new Vector3(.30f,-.47f,.07f)),rightHand.position);
            Sleeve(leftSleeve,root.TransformPoint(new Vector3(-.19f,-.48f,.10f)),leftHand.position);
            // The optical view uses the world camera; keep muzzle transforms alive for confirmed effects.
            model.Root.gameObject.SetActive(!scoped);rightSleeve.gameObject.SetActive(!scoped);leftSleeve.gameObject.SetActive(!scoped);
        }

        public void Reset()
        {
            root.gameObject.SetActive(false);weaponId=-1;reloadTicks=recoveryTicks=drawTicks=-1;
            confirmedShot=float.NegativeInfinity;
        }
        private static void Observe(int ticks,ref int previous,ref float when)
        {if(ticks==previous)return;previous=ticks;when=Time.unscaledTime;}
        // 只在两个逻辑观察之间平滑一个逻辑帧；网络暂停不会让本地动画提前完成换弹。
        private static float Remaining(int ticks,float observed)=>Mathf.Max(0,ticks-Mathf.Min(Time.unscaledTime-observed,1f/HookGame.Rate)*HookGame.Rate);
        private static void Sleeve(Transform sleeve,Vector3 elbow,Vector3 hand)
        {
            var delta=hand-elbow;sleeve.SetPositionAndRotation((elbow+hand)*.5f,Quaternion.LookRotation(delta));
            sleeve.localScale=new Vector3(.09f,.09f,Mathf.Max(.01f,delta.magnitude));
        }
        private static Transform Part(Transform parent,string name,Mesh mesh,Material material,Vector3 position,Vector3 scale)
        {
            var part=new GameObject(name).transform;part.SetParent(parent,false);part.localPosition=position;part.localScale=scale;
            part.gameObject.AddComponent<MeshFilter>().sharedMesh=mesh;part.gameObject.AddComponent<MeshRenderer>().sharedMaterial=material;
            return part;
        }
    }
}
