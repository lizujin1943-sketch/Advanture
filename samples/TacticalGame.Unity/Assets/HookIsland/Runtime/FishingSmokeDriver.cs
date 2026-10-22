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
    /// <summary>Development-only input driver. Uses the same input freezer, UDP, hook rules and rendered views as human play.</summary>
    public sealed class FishingSmokeDriver : MonoBehaviour
    {
        private IslandFishingController room;
        private string report;
        private float started,finished,nextProgress;
        private bool running,written,closing,captured,quit;
        private int targetFish=-1;
        private Point fishingPosition;
        private bool observedSwimming,observedForwardLeap;
        private Fish[] previousFish;
        private bool shopSmoke,visitingShop;
        private bool prankSmoke,observedPull,observedProtection;
        private int prankStage;
        private int upgradedCatchBaseline=-1;
        private int[] finalCatchBaseline;
        private ulong allUpgradedAt;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Attach()
        {
            if(string.IsNullOrEmpty(IslandFishingController.LaunchValue("-island-report")))return;
            var controller=UnityEngine.Object.FindObjectOfType<IslandFishingController>();
            // This legacy acceptance driver must not compete with the platform drivers.
            if(controller==null || controller.gameObject.scene.name!="HookIsland")return;
            if(controller.GetComponent<FishingSmokeDriver>()==null)controller.gameObject.AddComponent<FishingSmokeDriver>();
        }
        private void Start()
        {
            string path=IslandFishingController.LaunchValue("-island-report");
            if(!string.IsNullOrEmpty(path))Begin(path,true,IslandFishingController.LaunchValue("-island-shop-smoke")=="1",
                IslandFishingController.LaunchValue("-island-prank-smoke")=="1");
        }
        public void Begin(string path,bool quitWhenDone,bool verifyShop=false,bool verifyPrank=false)
        {
            room=GetComponent<IslandFishingController>();report=path;quit=quitWhenDone;
            shopSmoke=verifyShop;prankSmoke=verifyPrank;started=Time.realtimeSinceStartup;running=true;room.InputProvider=Sample;
        }
        private HookInput Sample(ulong tick)
        {
            if(room.State==null || room.LocalSlot>=room.State.Players.Count)return default;
            var p=room.State.Players[room.LocalSlot];
            if(prankSmoke && (prankStage>0 || room.State.Players.All(v=>v.Catches>0)))return PrankInput(tick,p);
            if(shopSmoke)
            {
                if(!visitingShop && room.State.Coins>=room.State.Players.Count*100 && room.State.Players.All(v=>v.Catches>0))
                    visitingShop=true;
                if(visitingShop && (p.RangeLevel==0 || p.SpeedLevel==0))
                {
                    if(!room.NearShop)
                    {
                        var entrances=new[]{new Point(-2800,-1600),new Point(2800,-1600),new Point(3200,3200),new Point(-3800,1050)};
                        var entrance=entrances[p.Slot];
                        int dx=entrance.X-p.X,dz=entrance.Z-p.Z,len=HookGame.Sqrt((long)dx*dx+(long)dz*dz);
                        return len>0?new HookInput((short)(dx*800/len),(short)(dz*800/len)):default;
                    }
                    room.OpenShop();
                    if(!room.PurchasePending && p.Phase==HookPhase.Ready)
                        room.RequestUpgrade(p.RangeLevel==0?HookUpgrade.Range:HookUpgrade.Speed);
                    return default;
                }
                if(p.RangeLevel>0 && p.SpeedLevel>0)
                {
                    if(upgradedCatchBaseline<0)upgradedCatchBaseline=p.Catches;
                    // Keep the store visible until the screenshot is captured, then catch again with upgraded stats.
                    if(!captured)return default;
                    room.CloseShop();
                }
            }
            // After shopping, deliberately fish the next band: every post-upgrade catch must be a newly unlocked school.
            int tier=shopSmoke && p.RangeLevel>0 && p.SpeedLevel>0?1:0;
            if(targetFish<0 || FishMotion.Tier(targetFish)!=tier)
            {
                var sector=room.Map.Spawns[p.Slot];
                targetFish=room.State.Fish.Where(f=>FishMotion.Tier(f.Id)==tier)
                    .OrderBy(f=>IslandMap.DistanceSquared(sector.X,sector.Z,room.Map.FishSpots[f.Id].X,room.Map.FishSpots[f.Id].Z)).First().Id;
                fishingPosition=FindFishingPosition(p,room.Map.FishSpots[targetFish]);
            }
            int moveX=fishingPosition.X-p.X,moveZ=fishingPosition.Z-p.Z;
            int remaining=HookGame.Sqrt((long)moveX*moveX+(long)moveZ*moveZ);
            if(remaining>220)return new HookInput((short)(moveX*650/remaining),(short)(moveZ*650/remaining));
            int speed=HookUpgrades.SpeedPerTick(p.SpeedLevel),range=HookUpgrades.Range(p.RangeLevel);
            int lead=Math.Max(0,(int)tick-(int)room.State.Steps-1);
            if(p.Phase==HookPhase.Ready && p.Cooldown==0)
            foreach(var fish in room.State.Fish.Where(f=>FishMotion.Tier(f.Id)==tier && f.Phase!=FishPhase.Hooked)
                .OrderBy(f=>IslandMap.DistanceSquared(p.X,p.Z,f.X,f.Z)))
            {
                // The automated driver leads its shot; human mouse aiming never uses this trajectory preview.
                for(int frame=1;frame<=range/speed+1;frame++)
                {
                    var future=FishMotion.Predict(fish,room.Map.FishSpots[fish.Id],frame+lead);
                    int distance=HookGame.Sqrt(IslandMap.DistanceSquared(p.X,p.Z,future.X,future.Z));
                    if(future.Phase==FishPhase.Jumping && future.Y>=400 && distance<range-200 && Math.Abs(distance-frame*speed)<250 &&
                        !HookGame.SegmentHit(p.X,p.Z,future.X,future.Z,0,1050,2150) &&
                        !HookGame.SegmentHit(p.X,p.Z,future.X,future.Z,-2150,1550,490))
                        return new HookInput(0,0,future.X,future.Z,true);
                }
            }
            var target=room.Map.FishSpots[targetFish];
            return new HookInput(0,0,target.X,target.Z);
        }
        private Point FindFishingPosition(Fisher player,Point fishHome)
        {
            var best=new Point(player.X,player.Z);long closest=long.MaxValue;
            int minX=room.Map.Shore.Concat(room.Map.Pier).Min(p=>p.X),maxX=room.Map.Shore.Concat(room.Map.Pier).Max(p=>p.X);
            int minZ=room.Map.Shore.Concat(room.Map.Pier).Min(p=>p.Z),maxZ=room.Map.Shore.Concat(room.Map.Pier).Max(p=>p.Z);
            for(int x=minX;x<=maxX;x+=250)for(int z=minZ;z<=maxZ;z+=250)
            {
                long distance=IslandMap.DistanceSquared(x,z,fishHome.X,fishHome.Z);
                if(distance>=closest || !room.Map.CanStand(x,z))continue;
                int steps=HookGame.Sqrt(IslandMap.DistanceSquared(player.X,player.Z,x,z))/200+1;
                bool walkable=true;
                for(int i=1;i<=steps;i++)if(!room.Map.CanStand(player.X+(x-player.X)*i/steps,player.Z+(z-player.Z)*i/steps))
                {walkable=false;break;}
                if(walkable){best=new Point(x,z);closest=distance;}
            }
            return best;
        }
        private static Point PrankPosition(int slot)=>new Point(slot%2==0?-5000:3000,slot<2?-5000:6000);
        private HookInput PrankInput(ulong tick,Fisher p)
        {
            var players=room.State.Players;
            if(prankStage==0)prankStage=1;
            if(prankStage==1 || prankStage==3)
            {
                bool ready=players.All(v=>{
                    var destination=PrankPosition(v.Slot);
                    return IslandMap.DistanceSquared(v.X,v.Z,destination.X,destination.Z)<400L*400 &&
                        v.Phase==HookPhase.Ready && v.PulledBy<0 && v.ProtectionTicks==0;
                });
                if(ready)prankStage++;
                else
                {
                    var destination=PrankPosition(p.Slot);
                    int dx=destination.X-p.X,dz=destination.Z-p.Z,length=HookGame.Sqrt((long)dx*dx+(long)dz*dz);
                    return length>200?new HookInput((short)(dx*650/length),(short)(dz*650/length)):default;
                }
            }
            if(prankStage==2 && players.Where(v=>v.Slot%2==0).All(v=>v.PlayerHooks>0) && players.All(v=>v.PulledBy<0))
            {prankStage=3;return default;}
            if(prankStage==4 && players.All(v=>v.PlayerHooks>0 && v.TimesHooked>0 && v.PulledBy<0))prankStage=5;
            int partner=p.Slot^1;
            bool turn=prankStage==2?p.Slot%2==0:prankStage==4 && p.Slot%2==1;
            if(turn && partner<players.Count && p.PlayerHooks==0 && p.Phase==HookPhase.Ready && tick%12==0)
                return new HookInput(0,0,players[partner].X,players[partner].Z,true);
            return default;
        }
        private void LateUpdate()
        {
            if(!running)return;
            if(written){if(quit && Time.realtimeSinceStartup-finished>2)Application.Quit();return;}
            var session=room.Client?.Session;
            if(session?.LocalParticipant!=null)
            {
                if(Time.realtimeSinceStartup>=nextProgress)
                {
                    nextProgress=Time.realtimeSinceStartup+10;
                    Directory.CreateDirectory(Path.GetDirectoryName(report));
                    var state=session.InspectState();
                    File.WriteAllText(report+".progress.json",JsonUtility.ToJson(new Progress{tick=state.Steps.ToString(),coins=state.Coins,
                        targetFish=targetFish,targetX=fishingPosition.X,targetZ=fishingPosition.Z,visitingShop=visitingShop,
                        shopOpen=room.ShopOpen,nearShop=room.NearShop,purchasePending=room.PurchasePending,prankStage=prankStage,
                        players=state.Players.Select(p=>new Player{slot=p.Slot,x=p.X,z=p.Z,catches=p.Catches,coins=p.Coins,casts=p.Casts,
                            rangeLevel=p.RangeLevel,speedLevel=p.SpeedLevel,playerHooks=p.PlayerHooks,timesHooked=p.TimesHooked,
                            pulledBy=p.PulledBy,hookedPlayer=p.HookedPlayer}).ToArray()},true));
                }
                if(room.State!=null)
                {
                    foreach(var fish in room.State.Fish)
                    {
                        if(previousFish==null)break;
                        var before=previousFish[fish.Id];
                        bool moved=IslandMap.DistanceSquared(fish.X,fish.Z,before.X,before.Z)>0;
                        observedSwimming|=moved && fish.Phase==FishPhase.Swimming && before.Phase==fish.Phase && fish.Y<0;
                        observedForwardLeap|=moved && fish.Phase==FishPhase.Jumping && before.Phase==fish.Phase && fish.Y>300;
                    }
                    previousFish=room.State.Fish.ToArray();
                }
                bool upgraded=room.State!=null && room.State.Players.All(p=>p.RangeLevel>0 && p.SpeedLevel>0);
                if(shopSmoke && upgraded && finalCatchBaseline==null)
                {finalCatchBaseline=room.State.Players.Select(p=>p.Catches).ToArray();allUpgradedAt=session.ConfirmedTick;}
                if(prankSmoke && room.State!=null)
                {
                    observedPull|=room.State.Players.Any(p=>p.PulledBy>=0);
                    observedProtection|=room.State.Players.Any(p=>p.ProtectionTicks>0);
                    if(room.State.Players.Any(p=>!room.Map.CanStand(p.X,p.Z)))throw new InvalidOperationException("Player left the walkable island during a pull.");
                }
                bool capture=prankSmoke?room.State!=null && room.State.Players.Any(p=>p.HookedPlayer>=0 && p.PullTicks>=3):
                    shopSmoke?room.TryGetLocalPlayer(out var local) && local.RangeLevel>0 && local.SpeedLevel>0 && room.ShopOpen:
                    session.ConfirmedTick>=280;
                if(!captured && capture)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(report));
                    ScreenCapture.CaptureScreenshot(report+".png");captured=true;
                }
                bool complete=prankSmoke?prankStage==5 && session.ConfirmedTick>=600:
                    shopSmoke?finalCatchBaseline!=null && session.ConfirmedTick>=allUpgradedAt+300 &&
                    room.State.Players.All(p=>p.Catches>finalCatchBaseline[p.Slot]):session.ConfirmedTick>=600;
                if(!closing && room.Host?.Session!=null && complete)
                {
                    room.Host.Session.SaveAndClose(c=>Write(report+".server.json",c,-1));closing=true;
                }
                if(session.Status==SessionStatus.Closed)
                {
                    Write(report,session.CaptureCheckpoint(),room.LocalSlot);
                    written=true;finished=Time.realtimeSinceStartup;room.InputProvider=null;return;
                }
            }
            if(room.Failure!=null || Time.realtimeSinceStartup-started>(shopSmoke || prankSmoke?120:75))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(report));
                if(session?.LocalParticipant!=null)Write(report+".state.json",session.CaptureCheckpoint(),room.LocalSlot);
                File.WriteAllText(report,JsonUtility.ToJson(new Report{error=room.Failure??"Fishing smoke timeout"},true));
                running=false;Debug.LogError("FISHING_SMOKE_FAILED "+room.Failure);
                if(quit)Application.Quit(1);
            }
        }
        private void Write(string path,Checkpoint checkpoint,int slot)
        {
            var state=new HookGame().Decode(checkpoint.ApplicationState.Span);
            var result=new Report {slot=slot,tick=checkpoint.Tick.ToString(),hash=CheckpointCodec.Hash(checkpoint),coins=state.Coins,
                renderedPlayers=room.Presentation.RenderedPlayers,presentedCatches=room.Presentation.PresentedCatches,
                presentedPlayerHooks=room.Presentation.PresentedPlayerHooks,observedPull=observedPull,observedProtection=observedProtection,
                observedSwimming=observedSwimming,observedForwardLeap=observedForwardLeap,
                postUpgradeCatches=upgradedCatchBaseline<0?0:state.Players[room.LocalSlot].Catches-upgradedCatchBaseline,
                players=state.Players.Select(p=>new Player{slot=p.Slot,x=p.X,z=p.Z,catches=p.Catches,coins=p.Coins,casts=p.Casts,
                    rangeLevel=p.RangeLevel,speedLevel=p.SpeedLevel,spent=HookUpgrades.TotalSpent(p.RangeLevel,p.SpeedLevel),
                    playerHooks=p.PlayerHooks,timesHooked=p.TimesHooked,pulledBy=p.PulledBy,hookedPlayer=p.HookedPlayer}).ToArray()};
            foreach(var p in state.Players)
            {
                var shown=room.Presentation.PlayerPosition(p.Slot);
                result.maxPositionError=Mathf.Max(result.maxPositionError,Vector2.Distance(new Vector2(shown.x,shown.z),new Vector2(p.X/1000f,p.Z/1000f)));
            }
            if(state.Coins<=0 || result.players.Any(p=>p.catches==0))result.error="A player did not finish a real fishing catch.";
            if(!observedSwimming || !observedForwardLeap)result.error="Moving underwater fish or forward leaps were not observed.";
            if(shopSmoke && (result.players.Any(p=>p.rangeLevel==0 || p.speedLevel==0) || result.postUpgradeCatches<=0))
                result.error="Shop upgrades or post-upgrade catches were not completed.";
            if(prankSmoke && (!observedPull || !observedProtection || result.presentedPlayerHooks<state.Players.Count ||
                result.players.Any(p=>p.playerHooks<1 || p.timesHooked<1 || p.pulledBy>=0 || p.hookedPlayer>=0)))
                result.error="Mutual player hooks, release, protection or presentation were not completed.";
            Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllText(path,JsonUtility.ToJson(result,true));
            Debug.Log("FISHING_SMOKE_REPORT "+path+" hash="+result.hash);
        }
        [Serializable]private sealed class Report
        {
            public string error,tick,hash;
            public int slot,coins,renderedPlayers,presentedCatches,postUpgradeCatches,presentedPlayerHooks;
            public bool observedPull,observedProtection,observedSwimming,observedForwardLeap;
            public float maxPositionError;
            public Player[] players;
        }
        [Serializable]private sealed class Player {public int slot,x,z,catches,coins,casts,rangeLevel,speedLevel,spent,playerHooks,timesHooked,pulledBy,hookedPlayer;}
        [Serializable]private sealed class Progress
        {
            public string tick;
            public int coins,targetFish,targetX,targetZ,prankStage;
            public bool visitingShop,shopOpen,nearShop,purchasePending;
            public Player[] players;
        }
    }
}
#endif
