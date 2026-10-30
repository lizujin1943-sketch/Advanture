using System.IO;
using UnityEngine;

namespace Advanture.HookIsland
{
    public sealed partial class IslandFishingController
    {
        [SerializeField,Min(10)] private float autoSaveIntervalSeconds=120;
        private IslandSaveCoordinator saves;
        public bool SavePending=>saves?.Pending==true;
        public bool CanRequestSave=>Playing && LocalSlot==0 && Host?.CanRequestSave==true;
        public string SaveMessage=>saves?.Message(Time.unscaledTime);
        public bool HasManualSave=>saves?.HasManualSave==true;
        public bool HasAutoSave=>saves?.HasAutoSave==true;
        private void InitializeSaves()
        {
            var directory=Application.isEditor?Path.GetFullPath(Path.Combine(Application.dataPath,"../../../artifacts/game-saves/editor")):
                Path.Combine(Application.persistentDataPath,"HookIslandSaves");
            saves=new IslandSaveCoordinator(directory);
        }
        public bool RequestSave(bool leave=false)=>saves?.Request(CanRequestSave,leave)==true;
        public void CancelSave()=>saves?.Cancel(Time.unscaledTime,autoSaveIntervalSeconds);
        public bool ContinueGame(int port,bool automatic=false)=>EnsureRoomSession() && saves.Continue(roomSession,map,port,automatic,Time.unscaledTime);
    }
}
