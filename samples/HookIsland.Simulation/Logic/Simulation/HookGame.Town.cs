using System;
using System.Linq;

namespace Advanture.HookIsland.Simulation;

public sealed partial class HookGame
{
    private sealed partial class Model
    {
        private TownPersonalState[] townPlayers=Array.Empty<TownPersonalState>();
        private TownZoneState[] townZones=Array.Empty<TownZoneState>();
        private int townGoals,townShortcuts,townReady,townFinish,townMap,townContracts,townContractsPaid,townMembers;
        private uint townAttempt;
        private bool townCompleted,townFinished,legacyTownSupply;
        private void InitializeTown(TownState? state=null)
        {
            if(!map.IsTown)return;
            var s=state??new TownState(true,new TownPersonalState[players.Length],new TownZoneState[6]);
            townPlayers=s.Players.ToArray();townZones=s.Zones.ToArray();townGoals=s.Goals;townShortcuts=s.Shortcuts;townReady=s.ReadyMask;townFinish=s.FinishMask;
            legacyTownSupply=!FiniteAmmunition && state!=null && townPlayers.Any(p=>p.BoundWeaponId!=0 || p.BoundReserve!=0 || p.EmergencyReserve!=0);
            townMap=s.MapKnownMask;townContracts=s.ContractMask;townContractsPaid=s.ContractsPaidMask;townAttempt=s.Attempt;townMembers=s.Members;townCompleted=s.FinaleCompleted;townFinished=s.Finished;
        }
        // Keep old checkpoints byte-exact until play advances. Rounds already in magazines become ordinary rounds.
        private void ReleaseLegacyTownSupply()
        {
            if(!legacyTownSupply)return;
            for(int i=0;i<townPlayers.Length;i++)
            {
                townPlayers[i].BoundReserve=townPlayers[i].BoundLoaded=townPlayers[i].BoundWeaponId=0;
                townPlayers[i].EmergencyReserve=0;
            }
            legacyTownSupply=false;
        }
        private TownState CaptureTown()=>!map.IsTown?TownState.Empty:new TownState(true,townPlayers,townZones,townGoals,townShortcuts,townReady,townFinish,townMap,townContracts,townContractsPaid,townAttempt,townMembers,townCompleted,townFinished);
        private bool TownNear(Fisher p,int building,int range=3000)
        {var b=TownLayout.Buildings[building-1];return IslandMap.DistanceSquared(p.X,p.Z,b.Entrance.X,b.Entrance.Z)<=(long)range*range;}
        private int TownOnlineMask()=>players.Where(p=>!p.WaitingToJoin && !p.Unavailable).Aggregate(0,(m,p)=>m|(1<<p.Slot));
        private bool FiniteAmmunition=>map.Fishing?.FiniteAmmunition==true;
        private void ApplyTownCommands(HookInput[] commands)
        {
            if(!map.IsTown)return;
            for(int i=0;i<players.Length;i++)
            {
                var c=commands[i].Town;var receipt=townPlayers[i];if(c.IsEmpty || c.RequestId<=receipt.LastRequest)continue;
                receipt.LastRequest=c.RequestId;townPlayers[i]=receipt;
                var result=TownTransact(i,c);receipt=townPlayers[i];receipt.LastResult=result;townPlayers[i]=receipt;
            }
            townReady&=TownOnlineMask();
            foreach(var p in players)if(!EldritchSystem.Eligible(p) || !TownLayout.InArena(p))townReady&=~(1<<p.Slot);
            townFinish&=TownOnlineMask();
            foreach(var p in players)if(!EldritchSystem.Eligible(p) || !TownNear(p,1))townFinish&=~(1<<p.Slot);
            int online=TownOnlineMask();
            if(townCompleted && online!=0 && (townFinish&1)!=0 && (townFinish&online)==online)townFinished=true;
            if(townGoals==7 && townReady!=0 && townReady==TownOnlineMask() && boss.Capture().Phase==BossPhase.Dormant)
            {
                foreach(var p in players)
                    if((townReady&(1<<p.Slot))!=0 && IslandMap.DistanceSquared(p.X,p.Z,TownLayout.BeaconX,TownLayout.BeaconZ)<=2500L*2500)
                    {boss.Request(p.Slot,townPlayers[p.Slot].LastRequest,players);break;}
            }
        }
        private TownResult TownTransact(int slot,TownCommand c)
        {
            var p=players[slot];if(!EldritchSystem.Eligible(p))return TownResult.Disabled;
            if(!FiniteAmmunition && (c.Action==TownAction.BuyAmmo || c.Action==TownAction.EmergencyAmmo))return TownResult.Disabled;
            if(p.Knockback.Active || p.Melee.Active || p.Grenade.Active || p.Firearm.Busy || p.PulledBy>=0 || p.Shopping || p.Swimming.InWater)return TownResult.Busy;
            int building=c.Action switch {TownAction.BuyAmmo or TownAction.BuyRifle=>2,TownAction.Heal=>3,TownAction.BuyRing=>4,TownAction.BuyGrenade=>5,TownAction.Contract=>6,TownAction.Sell=>7,TownAction.RevealMap=>8,TownAction.ShortcutOne or TownAction.ShortcutTwo or TownAction.OpenTideGate=>9,TownAction.RegisterInn=>10,TownAction.ReadArchive=>11,TownAction.ReadClues=>12,TownAction.ReturnToHarbor=>1,_=>0};
            if(building>0 && !TownNear(p,building))return TownResult.TooFar;
            if(boss.Capture().Active && c.Action!=TownAction.RepairBand && c.Action!=TownAction.ReadyBoss)return TownResult.Busy;
            int cost=c.Action switch {TownAction.Heal=>40,TownAction.BuyGrenade=>40,TownAction.ShortcutOne=>60,TownAction.ShortcutTwo=>80,TownAction.RepairBand=>40,_=>0};
            if(c.Action==TownAction.RestorePower)
            {var a=TownLayout.EncounterAreas[2];if(IslandMap.DistanceSquared(p.X,p.Z,a.X,a.Z)>5000L*5000)return TownResult.TooFar;if(!townZones[2].Cleared)return TownResult.GoalLocked;townGoals|=1;return TownResult.Success;}
            if(c.Action==TownAction.OpenTideGate){if(!townZones[4].Cleared)return TownResult.GoalLocked;townGoals|=2;return TownResult.Success;}
            if(c.Action==TownAction.ReadArchive){if(!townZones[5].Cleared)return TownResult.GoalLocked;townGoals|=4;return TownResult.Success;}
            if(c.Action==TownAction.ReadClues)return TownResult.Success;
            if(c.Action==TownAction.ReadyBoss)
            {
                if(townGoals!=7)return TownResult.GoalLocked;
                if(!TownLayout.InArena(p))return TownResult.TooFar;
                if(townCompleted)return TownResult.AlreadyCompleted;
                if(townAttempt==uint.MaxValue)return TownResult.LimitReached;
                if(boss.Capture().Active)return TownResult.Busy;
                townReady|=1<<slot;return TownResult.Success;
            }
            if(c.Action==TownAction.RevealMap){townMap|=1<<slot;return TownResult.Success;}
            if(c.Action==TownAction.Contract){townContracts|=1<<slot;return TownResult.Success;}
            if(c.Action==TownAction.RegisterInn){townPlayers[slot].InnRegistered=true;return TownResult.Success;}
            if(c.Action==TownAction.ReturnToHarbor)
            {
                if(!townCompleted)return TownResult.GoalLocked;
                townFinish|=1<<slot;
                return TownResult.Success;
            }
            if(c.Action==TownAction.EmergencyAmmo)
            {
                if(!TownNear(p,2) && !(townPlayers[slot].InnRegistered && TownNear(p,10)))return TownResult.TooFar;
                if(p.Coins>=AmmunitionCatalog.Get(FirearmCatalog.Get(0).AmmoType).Price || townPlayers[slot].EmergencyReserve!=0 || townPlayers[slot].BoundReserve!=0 || equipment!.Inventory.Capture().Items.Any(i=>i.Owner==slot && (i.Kind==ItemKind.Ammunition && i.Count>0 || i.Kind==ItemKind.Firearm && i.LoadedAmmo>0)))return TownResult.InvalidTarget;
                if(tick<townPlayers[slot].NextEmergencyTick)return TownResult.Cooldown;
                townPlayers[slot].EmergencyReserve=7;townPlayers[slot].NextEmergencyTick=tick+2700;return TownResult.Success;
            }
            if(c.Action==TownAction.Sell)
            {
                var item=equipment!.Inventory.Find(c.Target);int full=item?.Definition.Price??0,paid=full/2;
                if(p.Coins>int.MaxValue-paid || p.CoinsEarned>int.MaxValue-paid || coins>int.MaxValue-paid || townPlayers[slot].SoldValue>int.MaxValue-full)return TownResult.LimitReached;
                int protectedLimit=players.Length*3; // Starter backpack, melee and basic pistol are permanent fallback gear.
                if(!equipment.Inventory.SellTownEquipment(slot,c.Target,protectedLimit,out full,out paid))return TownResult.InvalidTarget;
                townPlayers[slot].SoldValue+=full;p.Coins+=paid;p.CoinsEarned+=paid;coins+=paid;equipment.RefreshSelection(ref p);players[slot]=p;return TownResult.Success;
            }
            if(c.Action==TownAction.BuyAmmo || c.Action==TownAction.BuyRifle || c.Action==TownAction.BuyRing || c.Action==TownAction.BuyGrenade)
            {
                ItemKind kind=c.Action==TownAction.BuyAmmo?ItemKind.Ammunition:c.Action==TownAction.BuyRifle?ItemKind.Firearm:c.Action==TownAction.BuyRing?ItemKind.SwimRing:ItemKind.Throwable;
                int type=c.Action==TownAction.BuyRifle || c.Action==TownAction.BuyRing?1:c.Action==TownAction.BuyAmmo?c.Target:0,before=p.Coins;
                if(!InventoryCatalog.IsValid(kind,type))return TownResult.InvalidTarget;
                if(p.Coins<cost)return TownResult.InsufficientCoins;
                if(townPlayers[slot].ServiceSpent>int.MaxValue-cost)return TownResult.LimitReached;
                var result=equipment!.Inventory.Purchase(slot,kind,type,c.Action==TownAction.BuyRifle || c.Action==TownAction.BuyRing,false,new Point(p.X,p.Z),ref p.Coins);
                if(result!=InventoryResult.Success)return result==InventoryResult.InsufficientCoins?TownResult.InsufficientCoins:result==InventoryResult.NoSpace?TownResult.NoSpace:TownResult.InvalidTarget;
                p.Coins-=cost;townPlayers[slot].ServiceSpent+=cost;coins-=before-p.Coins;equipment.RefreshSelection(ref p);players[slot]=p;return TownResult.Success;
            }
            if(cost==0)return TownResult.InvalidTarget;
            if(p.Coins<cost)return TownResult.InsufficientCoins;
            if(townPlayers[slot].ServiceSpent>int.MaxValue-cost)return TownResult.LimitReached;
            PlatformTile[] repair=Array.Empty<PlatformTile>();
            if(c.Action==TownAction.Heal)
            {if(p.Vitals.Health>=equipment!.Attributes(p).HealthMax && p.HealingCharges>=map.Fishing!.Healing.Charges)return TownResult.AlreadyCompleted;}
            if(c.Action==TownAction.ShortcutOne || c.Action==TownAction.ShortcutTwo)
            {int bit=c.Action==TownAction.ShortcutOne?1:2;if((townShortcuts&bit)!=0)return TownResult.AlreadyCompleted;repair=TownLayout.ShortcutTiles(bit==1?0:1);}
            if(c.Action==TownAction.RepairBand)
            {
                if(c.Attempt!=townAttempt || !boss.Capture().Active)return TownResult.InvalidTarget;
                repair=TownLayout.InitialTiles().Where(t=>TownLayout.BandForTile(t.X,t.Z)==c.Target).ToArray();
                var winch=TownLayout.WoodSegments.First(s=>s.Band==c.Target && s.Segment==3);
                if(IslandMap.DistanceSquared(p.X,p.Z,winch.X,winch.Z)>4000L*4000)return TownResult.TooFar;
                if(repair.All(t=>tiles.Any(v=>v.X==t.X && v.Z==t.Z)))return TownResult.AlreadyCompleted;
                if(boss.Capture().Limbs.Any(l=>l.Attack!=BossAttack.None) || boss.Capture().InkTicks>0)return TownResult.Unsafe;
            }
            // All validation precedes this commit; terrain, balance and receipt share one frame.
            p.Coins-=cost;coins-=cost;townPlayers[slot].ServiceSpent+=cost;
            if(c.Action==TownAction.Heal){p.Vitals.Health=equipment!.Attributes(p).HealthMax;HealingSystem.Refill(ref p,map.Fishing!.Healing);}
            if(c.Action==TownAction.ShortcutOne)townShortcuts|=1;
            if(c.Action==TownAction.ShortcutTwo)townShortcuts|=2;
            foreach(var t in repair)if(!tiles.Any(v=>v.X==t.X && v.Z==t.Z))tiles.Add(t);
            tiles=tiles.OrderBy(t=>t.Z).ThenBy(t=>t.X).ToList();players[slot]=p;return TownResult.Success;
        }
        private void AdvanceTown()
        {
            if(!map.IsTown)return;
            int previousTileCount=tiles.Count;
            var b=boss.Capture();
            if(b.Phase==BossPhase.Emerge && townMembers==0)
            {
                townAttempt++;townMembers=TownOnlineMask();townReady=0;
                if(FiniteAmmunition)GrantTownAttemptAmmo(b.ParticipantCount);
            }
            for(int i=0;FiniteAmmunition && i<players.Length;i++)
                if(!b.Active || !TownLayout.InArena(players[i]) || (townMembers&(1<<i))==0)
                {DiscardTownMagazine(i);townPlayers[i].BoundReserve=0;}
            if(b.Phase==BossPhase.Dormant && townMembers!=0)
            {
                foreach(var t in TownLayout.InitialTiles())if(!tiles.Any(v=>v.X==t.X && v.Z==t.Z))tiles.Add(t);
                for(int i=0;i<players.Length;i++)if((townMembers&(1<<i))!=0)
                {var p=players[i];if(RevivalSystem.RecoverTownFailure(map,tiles,players,ref p,equipment!.Attributes(p)))players[i]=p;}
                townMembers=0;townReady=0;
            }
            if(b.Phase==BossPhase.Defeated && b.Rewarded && !townCompleted)
            {townCompleted=true;foreach(var t in TownLayout.ReturnBridgeTiles())if(!tiles.Any(v=>v.X==t.X && v.Z==t.Z))tiles.Add(t);}
            if(tiles.Count!=previousTileCount)
            {
                tiles=tiles.OrderBy(t=>t.Z).ThenBy(t=>t.X).ToList();
                for(int i=0;i<players.Length;i++)
                {
                    if(players[i].WaitingToJoin)continue;
                    var p=players[i];p.Swimming.InWater=!SwimmingTerrain.Ground(map,tiles,p.X,p.Z);
                    if(!p.Swimming.InWater){p.Swimming.Active=false;p.Swimming.DamageTicks=0;}
                    players[i]=p;
                }
            }
            // Cleared 只记录首次清理里程碑，不再阻止激活或生成。
            // 保留已有城区的占用和离场宽限，新增城区按首次进入顺序排队。
            for(int z=0;z<townZones.Length;z++)
            {
                var zone=townZones[z];bool held=players.Any(p=>TownEncounters.NearZone(z,p));
                bool entered=players.Any(p=>TownEncounters.NearZone(z,p,0));
                if(zone.Started && !zone.Paused)
                {
                    zone.IdleTicks=held?0:Math.Min(360,zone.IdleTicks+1);
                    if(b.Active || zone.IdleTicks==360){zone.Paused=true;zone.TriggerTick=0;}
                }
                if(!zone.Started || zone.Paused)
                {
                    if(!held || b.Active)zone.TriggerTick=0;
                    else if(entered && zone.TriggerTick==0)zone.TriggerTick=tick+1;
                }
                townZones[z]=zone;
                if(!zone.Started || zone.Paused)eldritch.ActivateTownZone(z,false);
            }
            int active=0;
            for(int z=0;z<townZones.Length;z++)
                if(townZones[z].Started && !townZones[z].Paused)
                {eldritch.ActivateTownZone(z,true);active++;}
            if(!b.Active)
                foreach(int z in Enumerable.Range(0,townZones.Length)
                    .Where(z=>(!townZones[z].Started || townZones[z].Paused) && townZones[z].TriggerTick!=0)
                    .OrderBy(z=>townZones[z].TriggerTick).ThenBy(z=>z))
                {
                    if(active==2 || Enumerable.Range(0,TownEncounters.AdultCount).Count(i=>eldritch.Get(i).Damageable)>=map.Fishing!.Eldritch.MaxAlive)break;
                    var zone=townZones[z];zone.Started=true;zone.Paused=false;zone.IdleTicks=0;townZones[z]=zone;
                    eldritch.ActivateTownZone(z,true);active++;
                }
            for(int i=0;i<players.Length;i++)if((townContracts&(1<<i))!=0)
                for(int q=0;q<2;q++)
                {
                    int bit=1<<(i*2+q);if(!townZones[q==0?1:3].Cleared || (townContractsPaid&bit)!=0)continue;
                    int reward=q==0?90:120;if(coins>int.MaxValue-reward || players[i].Coins>int.MaxValue-reward || players[i].CoinsEarned>int.MaxValue-reward)continue;
                    townContractsPaid|=bit;coins+=reward;players[i].Coins+=reward;players[i].CoinsEarned+=reward;
                }
        }
        private void GrantTownAttemptAmmo(int participants)
        {
            int hp=map.Fishing!.Boss.ScaledBody(participants)+4*map.Fishing.Boss.ScaledLimb(0,participants);
            int rounds=((hp*3+2*participants*35*14-1)/(2*participants*35*14))*14;
            for(int i=0;i<players.Length;i++)if((townMembers&(1<<i))!=0)
            {townPlayers[i].GrantedAttempt=townAttempt;townPlayers[i].BoundReserve=rounds;}
        }
        private void DiscardTownMagazine(int slot)
        {
            var p=townPlayers[slot];if(p.BoundLoaded>0)equipment!.Inventory.DiscardLoaded(slot,p.BoundWeaponId,p.BoundLoaded);
            p.BoundLoaded=p.BoundWeaponId=0;townPlayers[slot]=p;
        }
        private int TownServiceReserve(int slot,InventoryItem gun,out bool attempt)
        {
            attempt=FirearmRules.UsesAttemptReserve(players[slot],townMembers,boss.Capture().Active);
            return FirearmRules.ServiceReserve(gun,townPlayers[slot],attempt);
        }
        private int ReloadTown(int slot,InventoryItem gun)
        {
            int external=TownServiceReserve(slot,gun,out bool attempt);
            int loaded=equipment!.Inventory.ReloadFinite(slot,gun.Id,external,out int used);
            if(attempt){townPlayers[slot].BoundReserve-=used;townPlayers[slot].BoundLoaded+=used;if(used>0)townPlayers[slot].BoundWeaponId=gun.Id;}
            else townPlayers[slot].EmergencyReserve-=used;
            return loaded;
        }
        private void ReconcileTownAmmo()
        {
            if(!FiniteAmmunition)return;
            for(int i=0;i<players.Length;i++)
            {
                var receipt=townPlayers[i];
                // Successful transfer/sale already reclaimed the source in the inventory commit.
                // Own backpack moves and weapon selection preserve the loaded source.
                if(receipt.BoundWeaponId>0 && equipment!.Inventory.Find(receipt.BoundWeaponId)?.Owner!=i)
                {receipt.BoundLoaded=receipt.BoundWeaponId=0;townPlayers[i]=receipt;}
                if(!boss.Capture().Active || !TownLayout.InArena(players[i]) || (townMembers&(1<<i))==0)
                {DiscardTownMagazine(i);townPlayers[i].BoundReserve=0;}
            }
        }
        private bool TownCanHitBoss(int slot)=>!map.IsTown || (townMembers&(1<<slot))!=0 && TownLayout.InArena(players[slot]);
        private void TownFired(int slot,int weapon)
        {if(map.IsTown && townPlayers[slot].BoundWeaponId==weapon && townPlayers[slot].BoundLoaded>0)townPlayers[slot].BoundLoaded--;}
    }
}
