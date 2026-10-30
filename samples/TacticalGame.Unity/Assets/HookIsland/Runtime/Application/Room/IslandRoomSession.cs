using System;
using System.Net;
using Advanture.HookIsland.Network;
using Advanture.HookIsland.Simulation;
using FrameSync.Sessions;

namespace Advanture.HookIsland
{
    /// <summary>Owns local room connections and their lifetime; no Unity input, rendering or file IO.</summary>
    public sealed class IslandRoomSession : IDisposable
    {
        private readonly IslandMap map;
        private readonly Func<byte[]> playerKey;
        private IPAddress address;
        private int port;
        private RoomResumeTicket resumeTicket;
        private string error;
        private bool connectionFailed,hostStopped;
        public IslandRoomSession(IslandMap map,Func<byte[]> playerKey)
        {this.map=map??throw new ArgumentNullException(nameof(map));this.playerKey=playerKey??throw new ArgumentNullException(nameof(playerKey));}
        public IslandHost Host {get;private set;}
        public IslandClient Client {get;private set;}
        public string Failure=>error??Host?.Failure??Client?.Failure;
        public bool InRoom=>Client!=null;
        public bool Restarting=>Host?.Restarting==true || Client?.Restarting==true;
        public bool Playing=>Failure==null && !Restarting && Client?.Session?.Status==SessionStatus.Running && Client.Session.LocalParticipant?.IsActive==true;
        public bool CanStart=>Failure==null && Host?.CanStart==true;
        public bool Synchronizing=>InRoom && Failure==null && !Playing && (resumeTicket!=null || Host?.Session!=null || Client?.Session?.LocalParticipant!=null);
        public bool CanReconnect=>Client!=null && resumeTicket!=null && address!=null && !hostStopped && Host?.Failure==null &&
            Host?.Session?.Status!=SessionStatus.Closed && Client.Session?.Status!=SessionStatus.Closed && !Restarting && (error!=null || Client.Failure!=null);
        // Fired before any input for a new connection/epoch is sampled.
        public event Action RoundReset;
        public event Action ConnectionClosed;
        public bool Start()=>CanStart && Host.TryStart();
        public bool Restart(bool roundFinished)=>roundFinished && Failure==null && Host?.CanRestart==true && Host.TryRestart();
        public void Create(int listenPort,int capacity)
        {
            Leave();
            try{Host=new IslandHost(map,listenPort,capacity);Connect(IPAddress.Loopback,Host.Port,0);}
            catch(Exception e){Leave();error="创建房间失败："+e.Message;}
        }
        public void Join(string hostAddress,int hostPort)
        {
            Leave();
            try
            {
                if(!IPAddress.TryParse(hostAddress,out var ip) || ip.AddressFamily!=System.Net.Sockets.AddressFamily.InterNetwork)
                    throw new ArgumentException("请填写房主的 IPv4 地址");
                Connect(ip,hostPort,ushort.MaxValue);
            }
            catch(Exception e){Leave();error="加入房间失败："+e.Message;}
        }
        public void Restore(IslandSave saved,int listenPort)
        {
            // Construction validates ownership/configuration before the current session is touched.
            var candidate=new IslandHost(saved,listenPort,playerKey());
            Leave();Host=candidate;
            try{Connect(IPAddress.Loopback,Host.Port,0);}
            catch{Leave();throw;}
        }
        private void Connect(IPAddress hostAddress,int hostPort,ushort slot,RoomResumeTicket ticket=null)
        {
            var next=new IslandClient(map,hostAddress,hostPort,slot,ticket,playerKey());
            address=hostAddress;port=hostPort;Client=next;Client.RoundChanged+=OnRoundChanged;
        }
        private void OnRoundChanged()=>RoundReset?.Invoke();
        public bool Reconnect()
        {
            if(!CanReconnect)return false;
            Client.RoundChanged-=OnRoundChanged;Client.Dispose();RoundReset?.Invoke();error=null;connectionFailed=false;
            try{Connect(address,port,ushort.MaxValue,resumeTicket);return true;}
            catch(Exception e){error="重新连接失败："+e.Message;return false;}
        }
        public bool Poll(TimeSpan now,Func<ulong,HookInput> sample)
        {
            // A failed local client must not stop its independent host.
            if(Host!=null && !hostStopped)
                try{Host.Poll(now);}
                catch(Exception e){hostStopped=true;error="房主会话已停止："+e.Message;Host.Dispose();}
            if(Client==null)return false;
            if(Client.RecoveryTicket!=null)resumeTicket=Client.RecoveryTicket;
            if(error==null && Host?.Failure==null)
                try{Client.Poll(now,sample);}
                catch(Exception e){StopClient(e);}
            if(Client.RecoveryTicket!=null)resumeTicket=Client.RecoveryTicket;
            if(Failure!=null)
            {
                if(!connectionFailed)
                {
                    connectionFailed=true;
                    if(Client.Session?.Status!=SessionStatus.Closed)RoundReset?.Invoke();
                    else ConnectionClosed?.Invoke();
                }
                return false;
            }
            return Client.Session?.LocalParticipant!=null;
        }
        public void StopClient(Exception exception)
        {error="本次连接已停止："+exception.Message;Client?.Dispose();}
        public void Leave()
        {
            if(Client!=null)Client.RoundChanged-=OnRoundChanged;
            Client?.Dispose();Host?.Dispose();Client=null;Host=null;error=null;
            resumeTicket=null;address=null;port=0;connectionFailed=hostStopped=false;
            RoundReset?.Invoke();
        }
        public void Dispose()=>Leave();
    }
}
