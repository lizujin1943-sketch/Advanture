namespace Advanture.HookIsland
{
    public sealed partial class IslandFishingController
    {
        public bool CanStartRoom=>Failure==null && roomSession?.CanStart==true;
        public bool StartRoomNow()=>CanStartRoom && roomSession.Start();
        public bool Synchronizing=>roomSession?.Synchronizing==true;
        public bool CanReconnect=>roomSession?.CanReconnect==true;
        public bool Reconnect()=>roomSession?.Reconnect()==true;
        private bool EnsureRoomSession()
        {
            if(map==null)return false;
            if(saves==null)InitializeSaves();
            if(roomSession==null)
            {
                roomSession=new IslandRoomSession(map,saves.LocalPlayerKey);
                roomSession.RoundReset+=ResetLocalRoundState;
                roomSession.ConnectionClosed+=CloseInteractions;
            }
            return true;
        }
        private void CloseInteractions(){CloseShop();Equipment?.Close();Construction?.Close();}
        private bool PollConnection()
        {
            if(roomSession==null)return false;
            bool connected=roomSession.Poll(clock.Elapsed,Sample);
            if(saves.Poll(Host,Failure,UnityEngine.Time.unscaledTime,autoSaveIntervalSeconds))LeaveRoom();
            return connected && Client?.Session?.LocalParticipant!=null;
        }
    }
}
