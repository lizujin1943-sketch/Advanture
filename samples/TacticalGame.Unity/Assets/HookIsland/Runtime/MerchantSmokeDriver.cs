#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.IO;
using System.Linq;
using Advanture.HookIsland.Simulation;
using FrameSync.Runtime;
using FrameSync.Sessions;
using UnityEngine;

namespace Advanture.HookIsland
{
    public sealed class MerchantSmokeDriver:MonoBehaviour
    {
        private readonly MerchantSmokePilot pilot=new MerchantSmokePilot();
        private IslandFishingController room;
        private string report;
        private float started,finished,nextProgress;
        private bool written,closing,overview,purchaseImage;
        private int panelImages,opened;
        private ulong openedAt,receiptAt;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Attach()
        {
            if(IslandFishingController.LaunchValue("-island-merchant-smoke")!="1" || string.IsNullOrEmpty(IslandFishingController.LaunchValue("-island-report")))return;
            UnityEngine.Object.FindObjectOfType<IslandFishingController>().gameObject.AddComponent<MerchantSmokeDriver>();
        }
        private void Start()
        {
            room=GetComponent<IslandFishingController>();report=IslandFishingController.LaunchValue("-island-report");started=Time.realtimeSinceStartup;
            room.InputProvider=Sample;
        }
        private HookInput Sample(ulong tick)
        {
            if(room.State==null)return default;
            var state=room.State;var player=state.Players[room.LocalSlot];
            if(player.LastInventoryRequest>0 && (receiptAt==0 || state.Steps<receiptAt+25))
            {
                if(receiptAt==0)receiptAt=state.Steps;
                room.OpenShop();
                if(!purchaseImage && state.Steps>=receiptAt+15)
                {ScreenCapture.CaptureScreenshot(report+".purchase.png");purchaseImage=true;}
                return new HookInput(0,0,shopping:true);
            }
            if(purchaseImage && room.ShopMerchantId==MerchantSmokePilot.PurchaseMerchantId)room.CloseShop();
            var command=pilot.Sample(room.Map,state,room.LocalSlot,tick);
            if(command.Inventory.Action!=InventoryAction.None)
            {
                // Use the same request buffer and receipt feedback as the player's purchase button.
                room.OpenShop();room.Equipment.RequestInventory(command.Inventory);
                return new HookInput(0,0,shopping:true);
            }
            return command;
        }
        private void LateUpdate()
        {
            if(written){if(Time.realtimeSinceStartup-finished>2)Application.Quit();return;}
            var session=room.Client?.Session;
            if(session?.LocalParticipant!=null)
            {
                var state=session.InspectState();bool complete=pilot.Complete(state);
                if(!overview && state.Steps>60){overview=true;ScreenCapture.CaptureScreenshot(report+".landmarks.png");}
                if(opened==0)
                {
                    for(int id=1;id<=4;id++)if((panelImages&(1<<(id-1)))==0 && room.CanShopAt(id) && room.OpenShop())
                    {opened=room.ShopMerchantId;openedAt=state.Steps;break;}
                }
                else
                {
                    if(state.Steps>=openedAt+15 && (panelImages&(1<<(opened-1)))==0)
                    {ScreenCapture.CaptureScreenshot(report+".shop"+opened+".png");panelImages|=1<<(opened-1);}
                    if(state.Steps>=openedAt+25){room.CloseShop();opened=0;}
                }
                if(Time.realtimeSinceStartup>=nextProgress)
                {nextProgress=Time.realtimeSinceStartup+10;Write(report+".progress.json",session.CaptureCheckpoint(),room.LocalSlot,false);}
                if(complete && !closing && room.Host?.Session!=null)
                {ScreenCapture.CaptureScreenshot(report+".returned.png");room.Host.Session.SaveAndClose(capture=>Write(report+".server.json",capture,-1,true));closing=true;}
                if(session.Status==SessionStatus.Closed)
                {Write(report,session.CaptureCheckpoint(),room.LocalSlot,true);written=true;finished=Time.realtimeSinceStartup;room.InputProvider=null;return;}
            }
            if(room.Failure!=null || Time.realtimeSinceStartup-started>420)
            {
                if(session?.LocalParticipant!=null)Write(report+".state.json",session.CaptureCheckpoint(),room.LocalSlot,false);
                File.WriteAllText(report,JsonUtility.ToJson(new Report{error=room.Failure??"Merchant acceptance timeout"},true));Application.Quit(1);
            }
        }
        private void Write(string path,Checkpoint checkpoint,int slot,bool validate)
        {
            var state=new HookGame().Decode(checkpoint.ApplicationState.Span);
            var result=new Report{slot=slot,tick=checkpoint.Tick.ToString(),hash=CheckpointCodec.Hash(checkpoint),coins=state.Coins,
                complete=pilot.Complete(state),visited=pilot.VisitedMask,returned=pilot.ReturnedMask,purchaseTier=pilot.PurchaseTier,
                panelImages=panelImages,overview=overview,purchaseImage=purchaseImage,tiles=state.Tiles.Count,renderedShips=room.Merchants?.ShipCount??0,renderedPlayers=room.Presentation.RenderedPlayers,
                activeSmokeDrivers=room.GetComponents<MonoBehaviour>().Count(c=>c.GetType().Name.EndsWith("SmokeDriver",StringComparison.Ordinal)),
                merchants=state.Tackle.Merchants.Select(m=>new Merchant{id=m.Id,active=m.Active,activated=m.ActivatedAt.ToString(),batch=m.Batch,next=m.NextRestock.ToString(),sold=m.Stock.Count(s=>s.Sold)}).ToArray(),
                players=state.Players.Select(p=>new Player{slot=p.Slot,coins=p.Coins,earned=p.CoinsEarned,bones=p.Fishbones,bonesEarned=p.FishbonesEarned,catches=p.Catches,receipt=p.LastInventoryResult.ToString(),request=p.LastInventoryRequest,rod=FishingRules.Equipped(state,p)?.Tier??0}).ToArray()};
            if(validate && (!result.complete || !purchaseImage || panelImages!=15 || result.renderedShips!=4 || result.activeSmokeDrivers!=1 || result.tiles!=33 ||
                result.players.Count(p=>p.receipt=="Success")!=1 || result.players.Count(p=>p.receipt=="SoldOut")!=result.players.Length-1))result.error="Merchant earning, route, purchase or presentation incomplete.";
            Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllText(path,JsonUtility.ToJson(result,true));
            if(validate)File.WriteAllBytes(path+".checkpoint",CheckpointCodec.Encode(checkpoint));
        }
        [Serializable] private sealed class Report
        {
            public string error,tick,hash;public int slot,coins,visited,returned,purchaseTier,panelImages,tiles,renderedShips,renderedPlayers,activeSmokeDrivers;
            public bool complete,overview,purchaseImage;public Merchant[] merchants;public Player[] players;
        }
        [Serializable] private sealed class Merchant {public int id,sold;public bool active;public uint batch;public string activated,next;}
        [Serializable] private sealed class Player {public int slot,coins,earned,bones,bonesEarned,catches,rod;public uint request;public string receipt;}
    }
}
#endif
