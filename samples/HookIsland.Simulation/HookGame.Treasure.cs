using System;
using System.Collections.Generic;
using System.Linq;

namespace Advanture.HookIsland.Simulation;

public sealed partial class HookGame
{
    private sealed partial class Model
    {
        private TreasureChest[] chests=Array.Empty<TreasureChest>();
        private void InitializeTreasure()
        {
            var config=map.Fishing?.Treasure;
            chests=map.Platform==null || config==null?Array.Empty<TreasureChest>():Enumerable.Range(0,config.Count(players.Length))
                .Select(i=>TreasureMotion.Waiting(i,config.Kind(i),config.InitialDelayTicks+i*config.SlotDelayTicks)).ToArray();
        }
        private List<int> TreasureMembers(int id)=>players.Where(p=>p.HookedChest==id+1)
            .OrderBy(p=>p.ChestJoinOrder).ThenBy(p=>p.Slot).Select(p=>(int)p.Slot).ToList();
        private bool InvalidTreasureLine(Fisher p,TreasureChest c)
        {
            var rod=equipment!.Equipped(p);
            return p.Unavailable || p.Vitals.Downed || p.Knockback.Active || rod==null ||
                (p.Swimming.InWater && equipment.Ring(p)==null) ||
                IslandMap.DistanceSquared(p.X,p.Z,c.X,c.Z)>(long)rod.Range*rod.Range;
        }
        private bool CanJoinTreasure(TreasureChest c)=>c.Phase==TreasurePhase.Floating ||
            c.Phase==TreasurePhase.Hooked && c.Kind==TreasureKind.Large;
        private void JoinTreasure(ref Fisher p,int id)
        {
            ref var c=ref chests[id];
            if(!CanJoinTreasure(c) || InvalidTreasureLine(p,c))return;
            var members=TreasureMembers(id);
            if(members.Count==0){c.Owner=p.Slot;c.Phase=TreasurePhase.Hooked;c.TravelRemainder=0;}
            p.HookedChest=id+1;p.ChestJoinOrder=members.Count;p.HookX=c.X;p.HookZ=c.Z;p.Phase=HookPhase.Returning;
        }
        private static void ClearTreasureLine(ref Fisher p)
        {p.HookedChest=0;p.ChestJoinOrder=0;p.Phase=HookPhase.Returning;}
        private void ReconcileTreasure(int id)
        {
            ref var c=ref chests[id];var members=TreasureMembers(id);
            if(members.Count==0){c.Owner=-1;c.Phase=TreasurePhase.Floating;c.TravelRemainder=0;return;}
            c.Owner=members[0];int rank=0;
            foreach(int slot in members)players[slot].ChestJoinOrder=rank++;
        }
        private void DetachTreasure(ref Fisher p)
        {
            if(p.HookedChest==0)return;
            int id=p.HookedChest-1;ClearTreasureLine(ref p);players[p.Slot]=p;ReconcileTreasure(id);
        }
        private void PruneTreasure(int id)
        {
            bool removed=false;
            foreach(int slot in TreasureMembers(id))if(InvalidTreasureLine(players[slot],chests[id]))
            {ClearTreasureLine(ref players[slot]);removed=true;}
            if(removed)ReconcileTreasure(id);
        }
        private void PruneTreasures()
        {for(int i=0;i<chests.Length;i++)if(chests[i].Phase==TreasurePhase.Hooked)PruneTreasure(i);}
        private void DriftTreasures()
        {
            for(int i=0;i<chests.Length;i++)TreasureMotion.Advance(ref chests[i],map.Fishing!.Treasure,tiles,map.Fishing.Seed,chests);
        }
        private void ReelTreasures()
        {
            var config=map.Fishing?.Treasure;if(config==null)return;
            PruneTreasures();
            for(int id=0;id<chests.Length;id++)
            {
                ref var c=ref chests[id];if(c.Phase!=TreasurePhase.Hooked)continue;
                var members=TreasureMembers(id);if(c.Kind==TreasureKind.Large && members.Count<2)continue;
                var lead=players[c.Owner];var from=new Point(c.X,c.Z);
                int travel=FishingRules.Travel(c.Kind==TreasureKind.Large?config.LargeReelSpeed:config.SmallReelSpeed,ref c.TravelRemainder);
                int dx=lead.X-c.X,dz=lead.Z-c.Z,length=Sqrt((long)dx*dx+(long)dz*dz);
                var to=length<=travel?new Point(lead.X,lead.Z):new Point(c.X+(int)((long)dx*travel/length),c.Z+(int)((long)dz*travel/length));
                bool landed=TryCatchLanding(from,to,out var landing);if(landed)to=landing;
                c.X=to.X;c.Z=to.Z;
                foreach(int slot in members){players[slot].HookX=c.X;players[slot].HookZ=c.Z;}
                // Lines overextended by this movement cannot become reward recipients.
                PruneTreasure(id);if(c.Phase!=TreasurePhase.Hooked || !landed)continue;
                int mask=0;foreach(int slot in TreasureMembers(id))mask|=1<<slot;
                if(c.Kind!=TreasureKind.Large){SettleTreasure(id,mask);continue;}
                c.RewardMembers=mask;c.Phase=TreasurePhase.Landed;c.TravelRemainder=0;
                foreach(int slot in TreasureMembers(id))ClearTreasureLine(ref players[slot]);
            }
        }
        private bool SettleTreasure(int id,int mask)
        {
            var c=chests[id];int count=0;for(int i=0;i<players.Length;i++)if((mask&(1<<i))!=0)count++;
            if(count==0 || coins>int.MaxValue-c.Coins)return false;
            var credits=new List<MaterialCredit>();var recipients=new List<Fisher>();var landing=new Point(c.X,c.Z);
            for(int i=0;i<players.Length;i++)if((mask&(1<<i))!=0)
            {
                var p=players[i];bool lead=i==c.Owner;
                int money=c.Coins/count+(lead?c.Coins%count:0),wood=c.Wood/count+(lead?c.Wood%count:0),bones=c.Bones/count+(lead?c.Bones%count:0);
                if(p.Coins>int.MaxValue-money || p.CoinsEarned>int.MaxValue-money || p.WoodEarned>int.MaxValue-wood ||
                    p.FishbonesEarned>int.MaxValue-bones || p.TreasuresCollected==int.MaxValue)return false;
                p.Coins+=money;p.CoinsEarned+=money;p.WoodEarned+=wood;p.FishbonesEarned+=bones;p.TreasuresCollected++;
                if(p.HookedChest==id+1)ClearTreasureLine(ref p);
                recipients.Add(p);credits.Add(new MaterialCredit(i,ItemKind.Wood,wood,landing));
                credits.Add(new MaterialCredit(i,ItemKind.Fishbone,bones,landing));
            }
            if(!equipment!.Inventory.CreditMaterials(credits))return false;
            coins+=c.Coins;
            foreach(var recipient in recipients){var p=recipient;PersonalMaterialInventory.Refresh(ref p,equipment.Inventory);players[p.Slot]=p;}
            chests[id]=TreasureMotion.Waiting(id,c.Kind,map.Fishing!.Treasure.RespawnTicks,c.Generation);return true;
        }
        private void OpenTreasures(IReadOnlyList<HookInput> commands)
        {
            for(int i=0;i<players.Length;i++)
            {
                var command=commands[i].Treasure;
                if(command.IsEmpty || command.RequestId<=players[i].LastTreasureRequest)continue;
                players[i].LastTreasureRequest=command.RequestId;
                players[i].LastTreasureResult=OpenTreasure(players[i],command);
            }
        }
        private TreasureResult OpenTreasure(Fisher p,TreasureCommand command)
        {
            if(map.Platform==null || map.Fishing?.Treasure.Enabled!=true)return TreasureResult.Disabled;
            int id=command.ChestId-1;
            if(id>=chests.Length || chests[id].Generation!=command.Generation)return TreasureResult.NotFound;
            var c=chests[id];
            if(c.Phase!=TreasurePhase.Landed)return TreasureResult.NotLanded;
            if(p.Unavailable || p.Vitals.Downed || p.Knockback.Active || p.Phase!=HookPhase.Ready || p.PulledBy>=0 || p.Shopping || p.Swimming.InWater)
                return TreasureResult.Busy;
            if(IslandMap.DistanceSquared(p.X,p.Z,c.X,c.Z)>(long)map.Fishing.Treasure.OpenRange*map.Fishing.Treasure.OpenRange)return TreasureResult.TooFar;
            return SettleTreasure(id,c.RewardMembers)?TreasureResult.Success:TreasureResult.LimitReached;
        }
    }
}
