using System;
using System.Diagnostics;
using System.Net;
using Advanture.HookIsland.Network;
using Advanture.HookIsland.Simulation;
using FrameSync.Sessions;
using UnityEngine;

namespace Advanture.HookIsland
{
    /// <summary>Unity input/presentation adapter. All shared gameplay runs in the independent simulation.</summary>
    public sealed class IslandFishingController : MonoBehaviour
    {
        [SerializeField] private TextAsset mapData;
        [SerializeField] private Camera viewCamera;
        [SerializeField] private Shader lineShader;
        private readonly Stopwatch clock=new Stopwatch();
        private IslandMap map;
        private FishingPresentation presentation;
        private FishingOverlay overlay;
        private short moveX,moveZ;
        private bool pendingFire,pendingCancel;
        private string error;
        private Vector3 aim;
        private bool restoreBackground;
        public IslandHost Host { get; private set; }
        public IslandClient Client { get; private set; }
        public HookState State { get; private set; }
        public IslandMap Map => map;
        public Camera ViewCamera => viewCamera;
        public FishingPresentation Presentation => presentation;
        public bool InRoom=>Client!=null;
        public bool Playing=>Failure==null && Client?.Session?.Status==SessionStatus.Running;
        public string Failure=>error??Host?.Failure??Client?.Failure;
        public ushort LocalSlot=>Client!=null?Client.Slot:ushort.MaxValue;
        public Vector3 Aim=>aim;
        public Func<ulong,HookInput> InputProvider { get; set; }
        public string Status=>Failure??(!InRoom?"创建房间或加入好友":Playing?"共同金币 · 捕鱼收益全队共享":
            Host!=null?$"等待好友 {Host.JoinedCount} / {Host.Capacity} 人":"已连接，等待房间人数到齐…");

        public void Configure(TextAsset data,Camera camera,Shader lines)
        { mapData=data;viewCamera=camera;lineShader=lines; }
        private void Awake()
        {
            restoreBackground=Application.runInBackground;Application.runInBackground=true;
            Cursor.lockState=CursorLockMode.None;Cursor.visible=true;clock.Start();
            overlay=GetComponent<FishingOverlay>();
            try
            {
                map=IslandMap.Decode(mapData.bytes);
                presentation=new FishingPresentation(transform,viewCamera,lineShader,map);
            }
            catch(Exception e){error="小岛数据无法读取："+e.Message;UnityEngine.Debug.LogException(e);}
        }
        private void Start()
        {
            if(LaunchValue("-island-role")=="host")HostRoom(ParseArgument("-island-port",7778),ParseArgument("-island-players",2));
            else if(LaunchValue("-island-role")=="join")JoinRoom(LaunchValue("-island-address")??"127.0.0.1",ParseArgument("-island-port",7778));
        }
        public void HostRoom(int port,int players)
        {
            LeaveRoom();if(map==null){error="缺少小岛数据。";return;}
            try { Host=new IslandHost(map,port,players);Client=new IslandClient(map,IPAddress.Loopback,Host.Port,0); }
            catch(Exception e){LeaveRoom();error="创建房间失败："+e.Message;}
        }
        public void JoinRoom(string address,int port)
        {
            LeaveRoom();if(map==null){error="缺少小岛数据。";return;}
            try
            {
                if(!IPAddress.TryParse(address,out var ip) || ip.AddressFamily!=System.Net.Sockets.AddressFamily.InterNetwork)
                    throw new ArgumentException("请填写房主的 IPv4 地址");
                Client=new IslandClient(map,ip,port,ushort.MaxValue);
            }
            catch(Exception e){LeaveRoom();error="加入房间失败："+e.Message;}
        }
        private void Update()
        {
            CaptureInput();
            if(Client==null || error!=null)return;
            try
            {
                Host?.Poll(clock.Elapsed);
                Client.Poll(clock.Elapsed,InputProvider??Sample);
                if(Client.Session?.LocalParticipant==null || Failure!=null)return;
                State=Client.Session.InspectState();
                presentation.Present(State,Client.Session.InspectPredictedState(),LocalSlot,aim,Playing && !overlay.MenuOpen);
            }
            catch(Exception e)
            {
                error="本次连接已停止："+e.Message;
                Client?.Dispose();Host?.Dispose();UnityEngine.Debug.LogException(e);
            }
        }
        private void CaptureInput()
        {
            if(!Playing || !Application.isFocused || (overlay!=null && overlay.MenuOpen))
            { moveX=moveZ=0;pendingFire=pendingCancel=false;return; }
            Vector2 move=new Vector2((Input.GetKey(KeyCode.D)?1:0)-(Input.GetKey(KeyCode.A)?1:0),
                (Input.GetKey(KeyCode.W)?1:0)-(Input.GetKey(KeyCode.S)?1:0));
            move=Vector2.ClampMagnitude(move,1);moveX=(short)(move.x*1000);moveZ=(short)(move.y*1000);
            var plane=new Plane(Vector3.up,new Vector3(0,.75f,0));
            var ray=viewCamera.ScreenPointToRay(Input.mousePosition);
            if(plane.Raycast(ray,out float distance))aim=ray.GetPoint(distance);
            // A small screen-space assist makes clicking an airborne fish match its XZ hit shape.
            // The synchronized simulation still validates timing, range and collision.
            float nearest=27f*27f;
            if(State!=null)foreach(var f in State.Fish)
            {
                if(f.Phase!=FishPhase.Jumping)continue;
                var screen=viewCamera.WorldToScreenPoint(new Vector3(f.X/1000f,f.Y/1000f,f.Z/1000f));
                float d=((Vector2)screen-(Vector2)Input.mousePosition).sqrMagnitude;
                if(screen.z>0 && d<nearest){nearest=d;aim=new Vector3(f.X/1000f,.75f,f.Z/1000f);}
            }
            aim.x=Mathf.Clamp(aim.x,-90,90);aim.z=Mathf.Clamp(aim.z,-90,90);
            bool overUi=overlay!=null && overlay.PointerOverHud;
            if(!overUi && Input.GetMouseButtonDown(0))pendingFire=true;
            if(!overUi && Input.GetMouseButtonDown(1))pendingCancel=true;
            float scroll=Input.mouseScrollDelta.y;
            if(!overUi && Mathf.Abs(scroll)>.01f)viewCamera.orthographicSize=Mathf.Clamp(viewCamera.orthographicSize-scroll*.7f,10,19);
        }
        private HookInput Sample(ulong tick)
        {
            var result=new HookInput(moveX,moveZ,Mathf.RoundToInt(aim.x*1000),Mathf.RoundToInt(aim.z*1000),pendingFire,pendingCancel);
            pendingFire=pendingCancel=false;return result;
        }
        private void OnApplicationFocus(bool focused)
        {if(!focused){moveX=moveZ=0;pendingFire=pendingCancel=false;}}
        public void LeaveRoom()
        {
            Client?.Dispose();Host?.Dispose();Client=null;Host=null;State=null;error=null;
            moveX=moveZ=0;pendingFire=pendingCancel=false;presentation?.ClearPlayers();
            if(overlay!=null)overlay.MenuOpen=false;
        }
        private void OnDestroy()
        {LeaveRoom();presentation?.Dispose();Application.runInBackground=restoreBackground;}
        private void OnApplicationQuit(){Client?.Dispose();Host?.Dispose();}
        public static string LaunchValue(string key)
        {var args=Environment.GetCommandLineArgs();int i=Array.IndexOf(args,key);return i>=0 && i+1<args.Length?args[i+1]:null;}
        private static int ParseArgument(string key,int fallback)=>int.TryParse(LaunchValue(key),out int value)?value:fallback;
    }
}
