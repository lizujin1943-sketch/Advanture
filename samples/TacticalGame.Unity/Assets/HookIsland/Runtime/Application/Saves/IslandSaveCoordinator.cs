using System;
using System.IO;
using Advanture.HookIsland.Network;
using Advanture.HookIsland.Simulation;

namespace Advanture.HookIsland
{
    /// <summary>Local save scheduling and IO; safe-point eligibility remains on the authoritative host.</summary>
    public sealed class IslandSaveCoordinator
    {
        private byte[] playerKey;
        private bool automatic,leaveAfterSave;
        private double nextAutoSave,messageUntil;
        private string message;
        public IslandSaveCoordinator(string directory){DirectoryPath=directory??throw new ArgumentNullException(nameof(directory));}
        public string DirectoryPath {get;}
        public bool Pending {get;private set;}
        public bool HasManualSave=>File.Exists(SavePath(false));
        public bool HasAutoSave=>File.Exists(SavePath(true));
        public string Message(double now)=>Pending?"等待全队收杆、结束钩救和救援后保存…":now<messageUntil?message:null;
        private string SavePath(bool auto)=>Path.Combine(DirectoryPath,auto?"auto.hisave":"manual.hisave");
        public byte[] LocalPlayerKey()
        {
            if(playerKey==null)
            {Directory.CreateDirectory(DirectoryPath);playerKey=IslandPlayerIdentity.LoadOrCreate(Path.Combine(DirectoryPath,"player.identity"));}
            return (byte[])playerKey.Clone();
        }
        public bool Request(bool eligible,bool leave)
        {if(!eligible)return false;Pending=true;automatic=false;leaveAfterSave|=leave;return true;}
        public void Cancel(double now,double interval)
        {ResetSession();SetMessage("已取消保存",now,3);nextAutoSave=now+Math.Max(10,interval);}
        public void ResetSession(){Pending=automatic=leaveAfterSave=false;nextAutoSave=0;}
        private void SetMessage(string value,double now,double duration=6){message=value;messageUntil=now+duration;}
        /// <returns>True only after a requested save-and-leave has committed to disk.</returns>
        public bool Poll(IslandHost host,string failure,double now,double interval)
        {
            if(host==null)return false;
            if(!host.CanRequestSave || failure!=null)
            {if(Pending){ResetSession();SetMessage("保存已停止：房间当前不可用",now,8);}return false;}
            if(nextAutoSave==0)nextAutoSave=now+Math.Max(10,interval);
            if(!Pending && now>=nextAutoSave){Pending=true;automatic=true;leaveAfterSave=false;}
            if(!Pending || !host.CanSave)return false;
            try
            {
                Directory.CreateDirectory(DirectoryPath);
                new IslandSaveStore(SavePath(automatic)).Save(host.CaptureSave());
                bool exit=leaveAfterSave,wasAutomatic=automatic;ResetSession();nextAutoSave=now+Math.Max(10,interval);
                SetMessage(wasAutomatic?"自动保存成功":"保存成功",now);
                return exit;
            }
            catch(Exception e)
            {
                ResetSession();nextAutoSave=now+30;
                SetMessage("保存失败，上一份存档已保留："+e.Message,now,12);
                return false;
            }
        }
        public bool Continue(IslandRoomSession room,IslandMap map,int port,bool auto,double now)
        {
            if(room==null || room.InRoom || map==null)return false;
            try
            {
                var saved=new IslandSaveStore(SavePath(auto)).Load();
                if(!map.Encode().AsSpan().SequenceEqual(saved.Map.Encode()))
                    throw new InvalidDataException("存档地图或玩法配置与当前场景不同，请使用保存时的场景配置。");
                room.Restore(saved,port);ResetSession();
                SetMessage("存档已读取，等待原队友接入；房主也可提前开始。",now,8);return true;
            }
            catch(Exception e){SetMessage("读取失败："+e.Message,now,12);return false;}
        }
    }
}
