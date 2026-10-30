using System;
using System.Collections.Generic;
using System.Linq;

namespace Advanture.HookIsland.Simulation;

public sealed partial class HookGame
{
    private sealed partial class Model
    {
        private List<int> CatchMembers(Fisher target)=>players.Where(p=>CooperativeFishing.SameTarget(p,target))
            .OrderBy(p=>p.FishingJoinOrder).ThenBy(p=>p.Slot).Select(p=>(int)p.Slot).ToList();
        private int CatchOwner(Fisher target)=>target.FishId>=0?fish[target.FishId].Owner:enemies!.GetPiranha(target.HookedPiranha-1).Owner;
        private Point CatchPosition(Fisher target)=>target.FishId>=0?new Point(fish[target.FishId].X,fish[target.FishId].Z):
            new Point(enemies!.GetPiranha(target.HookedPiranha-1).X,enemies.GetPiranha(target.HookedPiranha-1).Z);
        private void CatchOwner(Fisher target,int owner)
        {if(target.FishId>=0)fish[target.FishId].Owner=owner;else enemies!.TransferHook(target.HookedPiranha-1,owner);}
        private static void ClearCatch(ref Fisher p,bool failed=true)
        {if(failed)ComboRules.FailHarvest(ref p.Combo);p.FishId=-1;p.HookedPiranha=0;p.FishingJoinOrder=p.FishingDrainRemainder=0;p.Phase=HookPhase.Returning;}

        private bool CatchYieldsToRescue(Fisher caster,Fisher target,Point position)
        {
            // A held fish can be larger than the swimmer's hook collider. When the cast is
            // aimed at that swimmer, let it reach the player instead of turning rescue into co-fishing.
            foreach(var member in players)
                if(member.Swimming.InWater && CooperativeFishing.SameTarget(member,target) && CanHookPlayer(caster,member) &&
                    IslandMap.DistanceSquared(member.X,member.Z,position.X,position.Z)<=550L*550 &&
                    IslandMap.DistanceSquared(caster.AimX,caster.AimZ,member.X,member.Z)<=550L*550)return true;
            return false;
        }

        private bool JoinCatch(ref Fisher p,int id,bool piranha)
        {
            if(p.Stamina<=0 || p.Knockback.Active)return false;
            var target=new Fisher {FishId=piranha?-1:id,HookedPiranha=piranha?id+1:0};
            int owner=CatchOwner(target);var position=CatchPosition(target);var members=CatchMembers(target);
            // A swept hook can touch the target's radius while its center is beyond the rod's
            // line length. Reject that connection before it can cut any valid existing lines.
            if(InvalidCatchLine(p,position))return false;
            if(owner>=0 && CooperativeFishing.IsOpposite(position,new Point(players[owner].X,players[owner].Z),new Point(p.X,p.Z)))
            {
                // Atomic transfer: no interim empty group, escape, AI restart or old-leader election.
                int remainder=players[owner].ReelRemainder;
                foreach(int slot in members)ClearCatch(ref players[slot]);
                members.Clear();CatchOwner(target,p.Slot);p.ReelRemainder=remainder;
            }
            else if(owner<0)
            {
                if(piranha){if(!enemies!.TryHook(id,p.Slot))return false;}
                else {fish[id].Phase=FishPhase.Hooked;fish[id].Owner=p.Slot;}
                p.ReelRemainder=0;
            }
            p.FishId=target.FishId;p.HookedPiranha=target.HookedPiranha;p.FishingJoinOrder=members.Count;p.FishingDrainRemainder=0;
            p.HookX=position.X;p.HookZ=position.Z;p.Phase=HookPhase.Returning;return true;
        }

        private void DetachCatch(ref Fisher p)
        {
            if(!CooperativeFishing.Attached(p))return;
            var target=p;int owner=CatchOwner(target);players[p.Slot]=p;
            ClearCatch(ref p);players[p.Slot]=p;
            ReconcileCatch(target,owner);
            p=players[p.Slot];
        }
        private void ReconcileCatch(Fisher target,int previousOwner)
        {
            var members=CatchMembers(target);
            if(members.Count==0)
            {
                var position=CatchPosition(target);var previous=players[previousOwner];
                var landing=CatchEscapeMotion.Landing(map,tiles,position,new Point(previous.X,previous.Z));
                if(target.FishId>=0)
                {
                    ref var f=ref fish[target.FishId];f.Escape=CatchEscapeMotion.Begin(f.X,f.Y,f.Z,landing);
                    f.Phase=FishPhase.Escaping;f.Timer=0;f.Owner=-1;
                }
                else enemies!.Release(target.HookedPiranha-1,previousOwner,landing,previous.Swimming.InWater?100:1250);
                return;
            }
            int lead=members.OrderByDescending(i=>players[i].Stamina).ThenBy(i=>i==previousOwner?0:1)
                .ThenBy(i=>players[i].FishingJoinOrder).ThenBy(i=>i).First();
            if(lead!=previousOwner)
            {
                players[lead].ReelRemainder=players[previousOwner].ReelRemainder;
                var position=CatchPosition(target);var leadPoint=new Point(players[lead].X,players[lead].Z);
                foreach(int slot in members)
                    if(slot!=lead && CooperativeFishing.IsOpposite(position,leadPoint,new Point(players[slot].X,players[slot].Z)))
                        ClearCatch(ref players[slot]);
                CatchOwner(target,lead);
            }
            int rank=0;foreach(int slot in CatchMembers(target))players[slot].FishingJoinOrder=rank++;
        }

        private bool InvalidCatchLine(Fisher p,Point point)
        {
            var rod=equipment!.Equipped(p);
            return p.Unavailable || p.Vitals.Downed || p.Knockback.Active || p.Stamina<=0 || rod==null ||
                (p.Swimming.InWater && equipment.Ring(p)==null) ||
                IslandMap.DistanceSquared(p.X,p.Z,point.X,point.Z)>(long)rod.Range*rod.Range;
        }
        private void PruneCatch(Fisher target)
        {
            int owner=CatchOwner(target);if(owner<0)return;
            var position=CatchPosition(target);bool removed=false;
            foreach(int slot in CatchMembers(target))if(InvalidCatchLine(players[slot],position))
            {
                if(players[slot].Stamina==0 && players[slot].Escapes<int.MaxValue)players[slot].Escapes++;
                ClearCatch(ref players[slot]);removed=true;
            }
            if(removed)ReconcileCatch(target,owner);
        }
        private void PruneCatches()
        {
            if(equipment==null)return;
            for(int i=0;i<fish.Length;i++)if(fish[i].Owner>=0)PruneCatch(new Fisher{FishId=i});
            if(enemies!=null)for(int i=0;i<enemies.PiranhaCount;i++)if(enemies.GetPiranha(i).Owner>=0)
                PruneCatch(new Fisher{FishId=-1,HookedPiranha=i+1});
        }
        private void AdvanceCatches()
        {
            if(equipment==null)return;
            for(int i=0;i<fish.Length;i++)if(fish[i].Owner>=0)AdvanceCatch(new Fisher{FishId=i});
            if(enemies!=null)for(int i=0;i<enemies.PiranhaCount;i++)if(enemies.GetPiranha(i).Owner>=0)
                AdvanceCatch(new Fisher{FishId=-1,HookedPiranha=i+1});
        }
        private void AdvanceCatch(Fisher target)
        {
            var members=CatchMembers(target);
            int total=target.FishId>=0?FishCatalog.DrainPerTick(fish[target.FishId]):
                PiranhaFishing.DrainPerTick(map.Fishing!.Enemies,enemies!.GetPiranha(target.HookedPiranha-1));
            foreach(int slot in members)
                StaminaSystem.Spend(ref players[slot],CooperativeFishing.Drain(total,members.Count,ref players[slot].FishingDrainRemainder));
            // Everyone pays the same frame's share before exhaustion, leader election and harvest.
            PruneCatch(target);int owner=CatchOwner(target);if(owner<0)return;
            members=CatchMembers(target);ref var lead=ref players[owner];var start=CatchPosition(target);
            int speed=CooperativeFishing.Speed(map.Fishing!.ReelSpeed,members.Count,map.Fishing.CooperativeSpeedBonusPermille);
            int travel=FishingRules.Travel(speed,ref lead.ReelRemainder);
            int dx=lead.X-start.X,dz=lead.Z-start.Z,length=Sqrt((long)dx*dx+(long)dz*dz);
            var end=length<=travel?new Point(lead.X,lead.Z):new Point(start.X+(int)((long)dx*travel/length),start.Z+(int)((long)dz*travel/length));
            bool landed=TryCatchLanding(start,end,out var landing);if(landed)end=landing;
            if(target.FishId>=0)
            {ref var f=ref fish[target.FishId];f.X=end.X;f.Z=end.Z;f.Y=lead.Swimming.InWater?100:1250;}
            else enemies!.Follow(target.HookedPiranha-1,owner,end.X,end.Z);
            foreach(int slot in members){players[slot].HookX=end.X;players[slot].HookZ=end.Z;}
            // A fish moving away may overextend an assistant on this very frame, before reward membership is frozen.
            PruneCatch(target);if(CatchOwner(target)<0)return;
            if(landed)SettleCatch(target,end);
        }
        private bool TryCatchLanding(Point from,Point to,out Point landing)
        {
            int length=Sqrt(IslandMap.DistanceSquared(from.X,from.Z,to.X,to.Z)),steps=Math.Max(1,(length+49)/50);
            for(int i=0;i<=steps;i++)
            {
                var point=new Point(from.X+(int)((long)(to.X-from.X)*i/steps),from.Z+(int)((long)(to.Z-from.Z)*i/steps));
                if(CanStand(point.X,point.Z)){landing=point;return true;}
            }
            landing=to;return false;
        }
        private void SettleCatch(Fisher target,Point landing)
        {
            int owner=CatchOwner(target);var members=CatchMembers(target);
            int value=target.FishId>=0?FishCatalog.Coins(fish[target.FishId]):PiranhaFishing.Coins(map.Fishing!.Enemies,enemies!.GetPiranha(target.HookedPiranha-1));
            int bones=target.FishId>=0?FishCatalog.Bones(fish[target.FishId]):PiranhaFishing.Bones(enemies!.GetPiranha(target.HookedPiranha-1));
            int nextCombo=members.Max(slot=>ComboRules.NextCount(map.Fishing!.Combo,players[slot].Combo.Count));
            long reward=ComboRules.Gold(map.Fishing!.Combo,value,nextCombo);
            if(reward>int.MaxValue || coins>int.MaxValue-reward)return;
            value=(int)reward;
            var credits=new List<MaterialCredit>();var nextPlayers=new List<Fisher>();
            foreach(int slot in members)
            {
                var p=players[slot];int share=CooperativeFishing.Share(value,members.Count,slot==owner),boneShare=CooperativeFishing.Share(bones,members.Count,slot==owner);
                if(p.Coins>int.MaxValue-share || p.CoinsEarned>int.MaxValue-share || p.Catches==int.MaxValue || p.FishbonesEarned>int.MaxValue-boneShare)return;
                p.Coins+=share;p.CoinsEarned+=share;p.Catches++;p.FishbonesEarned+=boneShare;
                ComboRules.Succeed(ref p.Combo,map.Fishing!.Combo);
                credits.Add(new MaterialCredit(slot,ItemKind.Fishbone,boneShare,landing));ClearCatch(ref p,false);
                // Assistants still retrieve the empty line; reaching shore does not teleport their hook home.
                if(IslandMap.DistanceSquared(p.X,p.Z,landing.X,landing.Z)<=HookUpgrades.ReturnPerTick*HookUpgrades.ReturnPerTick)
                {p.Phase=HookPhase.Ready;p.Cooldown=6;p.HookX=p.X;p.HookZ=p.Z;}
                nextPlayers.Add(p);
            }
            var nextFish=target.FishId>=0?CreateFish(target.FishId,100,unchecked(fish[target.FishId].Generation+1)):default;
            var nextCommissions=CommissionSystem.RecordCatch(map.Fishing!.Commissions,commissions,
                target.FishId>=0 && FishCatalog.All[fish[target.FishId].Species].Rarity>=FishRarity.Rare);
            if(!equipment!.Inventory.CreditMaterials(credits))return;
            ocean?.RecordCatch(target.FishId,tick+1);
            coins+=value;
            commissions=nextCommissions;
            foreach(var candidate in nextPlayers)
            {
                var p=candidate;var previous=players[p.Slot];PersonalMaterialInventory.Refresh(ref p,equipment.Inventory);players[p.Slot]=p;
                int received=p.Coins-previous.Coins;if(received>0)Record(HookGameplayEventKind.CoinsReceived,p,amount:received);
                Record(HookGameplayEventKind.FishCaught,p,targetId:target.FishId>=0?target.FishId:-(target.HookedPiranha));
                RecordCombo(p,previous.Combo.Count);
            }
            if(target.FishId>=0)fish[target.FishId]=nextFish;
            else enemies!.CompleteCapture(target.HookedPiranha-1,owner);
        }
    }
}
