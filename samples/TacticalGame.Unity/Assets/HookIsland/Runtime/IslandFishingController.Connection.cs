using System;
using System.Net;
using Advanture.HookIsland.Network;
using FrameSync.Sessions;

namespace Advanture.HookIsland
{
    public sealed partial class IslandFishingController
    {
        private IPAddress connectionAddress;
        private int connectionPort;
        private RoomResumeTicket resumeTicket;
        private bool connectionFailed,hostStopped;
        public bool CanStartRoom=>Failure==null && Host?.CanStart==true;
        public bool StartRoomNow()=>CanStartRoom && Host.TryStart();
        public bool Synchronizing=>InRoom && Failure==null && !Playing && (resumeTicket!=null || Host?.Session!=null || Client?.Session?.LocalParticipant!=null);
        public bool CanReconnect=>Client!=null && resumeTicket!=null && connectionAddress!=null && !hostStopped && Host?.Failure==null &&
            Host?.Session?.Status!=SessionStatus.Closed && Client.Session?.Status!=SessionStatus.Closed && !Restarting && (error!=null || Client.Failure!=null);

        private void ConnectClient(IPAddress address,int port,ushort slot,RoomResumeTicket ticket=null)
        {
            var next=new IslandClient(map,address,port,slot,ticket);
            connectionAddress=address;connectionPort=port;Client=next;Client.RoundChanged+=ResetLocalRoundState;
        }
        public bool Reconnect()
        {
            if(!CanReconnect)return false;
            Client.RoundChanged-=ResetLocalRoundState;Client.Dispose();ResetLocalRoundState();
            error=null;connectionFailed=false;
            try{ConnectClient(connectionAddress,connectionPort,ushort.MaxValue,resumeTicket);return true;}
            catch(Exception e){error="重新连接失败："+e.Message;return false;}
        }
        private bool PollConnection()
        {
            // The local player can lose its socket while the independent server keeps the room alive.
            if(Host!=null && !hostStopped)
            {
                try{Host.Poll(clock.Elapsed);}
                catch(Exception e){hostStopped=true;error="房主会话已停止："+e.Message;Host.Dispose();}
            }
            if(Client==null)return false;
            if(Client.RecoveryTicket!=null)resumeTicket=Client.RecoveryTicket;
            if(error==null && Host?.Failure==null)
            {
                try{Client.Poll(clock.Elapsed,Sample);}
                catch(Exception e){error="本次连接已停止："+e.Message;Client.Dispose();}
            }
            if(Client.RecoveryTicket!=null)resumeTicket=Client.RecoveryTicket;
            if(Failure!=null)
            {
                if(!connectionFailed)
                {
                    connectionFailed=true;
                    if(Client.Session?.Status!=SessionStatus.Closed)ResetLocalRoundState();
                    else{CloseShop();Equipment?.Close();Construction?.Close();}
                }
                return false;
            }
            return Client.Session?.LocalParticipant!=null;
        }
    }
}
