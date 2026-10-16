using System.Collections.Generic;
using Advanture.Tactical.Simulation;
using UnityEngine;
namespace Advanture.TacticalGame.NetworkView
{
    /// <summary>第一/第三人称共用的武器表现。只消费确认状态，不计算伤害，不向模拟写回后坐力。</summary>
    public sealed class Ak47View : MonoBehaviour
    {
        private readonly List<Mesh> meshes=new List<Mesh>();
        private readonly List<Material> materials=new List<Material>();
        private Ak47Model model;
        private Transform rifle;
        private GameObject flash;
        private LineRenderer tracer;
        private AudioSource audioSource;
        private AudioClip shotAudio;
        private Camera worldCamera, weaponCamera;
        private int oldMask;
        private bool local;
        private ulong lastShot;
        private float effectTime, kick;
        public int PresentedShots { get; private set; }
        public bool IsVisible => rifle!=null && rifle.gameObject.activeSelf;
        public bool IsFlashing => flash!=null && flash.activeSelf;

        public static Ak47View Create(Transform parent,bool firstPerson)
        {
            var go=new GameObject(firstPerson?"LocalAK47":"RemoteAK47");go.transform.SetParent(parent,false);
            var view=go.AddComponent<Ak47View>();view.Initialize(firstPerson);return view;
        }
        private void Initialize(bool firstPerson)
        {
            local=firstPerson;model=new Ak47Model(transform,meshes,materials,firstPerson);rifle=transform.Find("AK47");
            transform.localPosition=local?new Vector3(.20f,-.23f,.43f):new Vector3(.18f,-.28f,.12f);
            transform.localRotation=Quaternion.Euler(0,local?-4:0,0);
            flash=new GameObject("MuzzleFlash");flash.transform.SetParent(model.Muzzle,false);
            var glow=Ak47Model.Material("Muzzle Glow",new Color(1,.72f,.15f),0,materials);glow.EnableKeyword("_EMISSION");glow.SetColor("_EmissionColor",new Color(3,1.3f,.15f));
            var filter=flash.AddComponent<MeshFilter>();filter.sharedMesh=meshes[0];flash.AddComponent<MeshRenderer>().sharedMaterial=glow;
            flash.transform.localPosition=new Vector3(0,0,.06f);flash.transform.localScale=new Vector3(.07f,.045f,.15f);flash.SetActive(false);
            var beam=new GameObject("ShotTrace");beam.transform.SetParent(transform,false);tracer=beam.AddComponent<LineRenderer>();
            tracer.useWorldSpace=true;tracer.positionCount=2;tracer.startWidth=.012f;tracer.endWidth=.004f;tracer.sharedMaterial=glow;
            tracer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;tracer.receiveShadows=false;tracer.enabled=false;
            audioSource=gameObject.AddComponent<AudioSource>();audioSource.playOnAwake=false;audioSource.spatialBlend=local?0:1;
            audioSource.minDistance=2;audioSource.maxDistance=45;audioSource.volume=local?.22f:.3f;shotAudio=CreateShotAudio();
            // 本地枪单独绘制，贴近墙面时不会被世界墙体切掉。31 层仅为本试玩第一人称武器保留。
            worldCamera=local?transform.parent.GetComponent<Camera>():null;
            if(worldCamera!=null)
            {
                oldMask=worldCamera.cullingMask;worldCamera.cullingMask &= ~(1<<31);
                foreach(var t in rifle.GetComponentsInChildren<Transform>(true))t.gameObject.layer=31;
                var cameraObject=new GameObject("WeaponCamera");cameraObject.transform.SetParent(transform.parent,false);
                weaponCamera=cameraObject.AddComponent<Camera>();weaponCamera.clearFlags=CameraClearFlags.Depth;
                weaponCamera.cullingMask=1<<31;weaponCamera.depth=worldCamera.depth+1;weaponCamera.fieldOfView=60;
                weaponCamera.nearClipPlane=.01f;weaponCamera.farClipPlane=5;weaponCamera.allowHDR=false;weaponCamera.allowMSAA=false;
                foreach(var renderer in rifle.GetComponentsInChildren<Renderer>())renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
            }
        }
        /// <summary>相同射击序号只播放一次。回滚时序号下降不会清除已播放记录，恢复确认状态也不会重播。</summary>
        public void Present(CombatState state,ulong confirmedTick,float delta)
        {
            if(state.ShotSequence>lastShot)
            {
                lastShot=state.ShotSequence;
                // 一次补了很多历史帧时，只表现最近的枪声，避免加入/恢复后播放过期射击。
                if(confirmedTick>=state.LastShotTick && confirmedTick-state.LastShotTick<=3)
                {
                    PresentedShots++;effectTime=.065f;kick=1;
                    tracer.SetPosition(0,model.Muzzle.position);
                    tracer.SetPosition(1,new Vector3(state.EndX/1000f,state.EndY/1000f,state.EndZ/1000f));
                    if(Application.isPlaying)audioSource.PlayOneShot(shotAudio);
                }
            }
            effectTime=Mathf.Max(0,effectTime-delta);kick=Mathf.Max(0,kick-delta*10);
            rifle.gameObject.SetActive(state.IsAlive);flash.SetActive(state.IsAlive && effectTime>0);tracer.enabled=effectTime>0;
            float reload=state.ReloadUntilTick>confirmedTick?1:0;
            rifle.localPosition=new Vector3(0,-.07f*reload,-.045f*kick);
            rifle.localRotation=Quaternion.Euler(-5*kick+18*reload,0,-22*reload);
            model.Magazine.localPosition=new Vector3(0,-.08f*reload,0);
        }
        /// <summary>原创合成短促枪声，避免依赖外部音频版权；只用于表现，不进入确定性计算。</summary>
        private static AudioClip CreateShotAudio()
        {
            const int rate=22050,count=3528;var data=new float[count];var random=new System.Random(47);
            for(int i=0;i<count;i++){float t=i/(float)rate;float envelope=Mathf.Exp(-t*42);data[i]=((float)random.NextDouble()*2-1)*envelope*.7f+Mathf.Sin(t*430)*envelope*.3f;}
            var clip=AudioClip.Create("AK47 Synthetic Shot",count,1,rate,false);clip.SetData(data,0);return clip;
        }
        private static void Release(Object value){if(value==null)return;if(Application.isPlaying)Destroy(value);else DestroyImmediate(value);}
        private void OnDestroy()
        {
            if(worldCamera!=null)worldCamera.cullingMask=oldMask;
            if(weaponCamera!=null)Release(weaponCamera.gameObject);
            Release(shotAudio);foreach(var mesh in meshes)Release(mesh);foreach(var material in materials)Release(material);
        }
    }
}
