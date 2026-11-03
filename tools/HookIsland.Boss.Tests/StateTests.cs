using System;
using System.Collections.Generic;
using System.Linq;
using Advanture.HookIsland.Simulation;

namespace Advanture.HookIsland.BossTests;
internal static partial class Program
{
    private static IEnumerable<(string,Action)> StateCases()
    {
        yield return ("boss_slam_preserves_landed_treasure_and_relocates_finale_bank",()=>{
            foreach(var phase in new[]{FinalePhase.Surfaced,FinalePhase.Submerged})
            {
                var m=Model(progression:true);Step(m);var s=m.Capture();var map=Map(progression:true);var c=map.Fishing!.Finale;
                var b=Combat().Capture();var arms=b.Limbs.ToArray();arms[1]=new BossLimbState(1,705,705,BossAttack.Slam,44,targetSlot:0);
                var site=(from tile in s.Tiles where BossGeometry.SlamContainsXZ(1,tile.X*2000,tile.Z*2000)
                    from direction in Enumerable.Range(0,4) let d=FinaleRules.Direction(direction) let position=new Point(tile.X*2000+d.X*c.EdgeOffset,tile.Z*2000+d.Z*c.EdgeOffset)
                    where FinalePlacement.Water(map,c,s.Tiles,position) select new FinaleSite(position,direction)).First();
                var finale=new FinaleState(phase,completed:1,timer:phase==FinalePhase.Submerged?30:0,spentBait:3,x:site.Position.X,z:site.Position.Z,previousX:0,previousZ:phase==FinalePhase.Surfaced?-5100:0,direction:site.Direction);
                var chest=new TreasureChest{Id=0,Kind=TreasureKind.Large,Phase=TreasurePhase.Landed,Generation=1,X=-6000,Z=14000,DirectionX=1000,Timer=100,Owner=0,RewardMembers=3};
                TreasureMotion.Rewards(map.Fishing.Treasure,chest.Kind,0,1,map.Fishing.Seed,out chest.Coins,out chest.Wood,out chest.Bones);
                var people=s.Players.ToArray();people[0].Catches=1;
                var state=new HookState(s.Steps,people,s.Fish,s.Coins,s.Tiles,s.Driftwood,s.Tackle,s.Enemies,s.RoomRules,new TreasureState(new[]{chest}),new CommissionState(1,3,new[]{new CommissionProgress(1,1)}),s.Ocean,finale,s.Crabs,With(b,limbs:arms));
                m.Restore(state);Step(m);var after=m.Capture();RoundTrip(after);var restored=Model(progression:true);restored.Restore(after);Same(after,restored.Capture());
                Require(after.Treasure.Chests[0].Phase==TreasurePhase.Landed && after.Treasure.Chests[0].RewardMembers==3 && (after.Treasure.Chests[0].X!=chest.X || after.Treasure.Chests[0].Z!=chest.Z),"Landed treasure lost ownership or terrain");
                Require(after.Finale.Phase==phase && after.Finale.Completed==1 && after.Finale.SpentBait==3 && (after.Finale.X!=finale.X || after.Finale.Z!=finale.Z),"Finale progress or bank was lost");
            }
        });
        yield return ("gameover_clears_ink_before_clock_advance_and_victory_freezes_boss",()=>{
            var m=Model();Step(m);var initial=m.Capture();
            // 正好停在商人补货边界前，全局绝对时钟冻结；仅 BOSS 自有退场钟继续。
            var s=new HookState(initial.Tackle!.NextRestock-1,initial.Players,initial.Fish,initial.Coins,initial.Tiles,initial.Driftwood,initial.Tackle,initial.Enemies,initial.RoomRules,initial.Treasure,initial.Commissions,initial.Ocean,initial.Finale,initial.Crabs,initial.Boss);
            var people=s.Players.ToArray();var revival=s.Tackle!.Config.Revival;
            for(int i=0;i<people.Length;i++)
            {
                people[i].Vitals.Health=0;people[i].Revival=new RevivalState{Dead=true,DownedTicks=revival.DownedTicks,DeathCost=revival.Cost(s.Steps,0),DeathTick=s.Steps,Deaths=1,DownedId=1};
            }
            var boss=With(Combat().Capture(),inkTicks:180,inkStart:s.Steps);m.Restore(Replace(s,boss,people));
            for(int i=0;i<300;i++){Step(m);if(i%30==0)RoundTrip(m.Capture());}Require(m.Capture().Boss.Phase==BossPhase.Dormant && m.Capture().Steps==s.Steps && m.Capture().Tackle!.NextRestock==s.Tackle.NextRestock,"Finished world advanced global clock or failed to reset challenge");
            var won=Model(progression:true);Step(won);var w=won.Capture();people=w.Players.ToArray();people[0].Catches=1;
            var finale=new FinaleState(FinalePhase.Victory,completed:3,spentBait:3,x:0,z:-5100,previousX:5100,previousZ:0,direction:0,victoryTick:w.Steps);
            var victory=new HookState(w.Steps,people,w.Fish,w.Coins,w.Tiles,w.Driftwood,w.Tackle,w.Enemies,w.RoomRules,w.Treasure,new CommissionState(1,3,new[]{new CommissionProgress(1,1)}),w.Ocean,finale,w.Crabs,Combat().Capture());
            won.Restore(victory);for(int i=0;i<5;i++)Step(won);Same(victory,won.Capture());RoundTrip(won.Capture());
        });
        yield return ("gameover_severed_limbs_finish_sinking_during_boss_death_and_save_restore",()=>{
            foreach(bool dying in new[]{false,true})
            {
                var m=Model();Step(m);var s=m.Capture();var people=s.Players.ToArray();var revival=s.Tackle!.Config.Revival;
                for(int i=0;i<people.Length;i++)
                {
                    people[i].Vitals.Health=0;
                    people[i].Revival=new RevivalState{Dead=true,DownedTicks=revival.DownedTicks,DeathCost=revival.Cost(s.Steps,0),DeathTick=s.Steps,Deaths=1,DownedId=1};
                }
                var boss=Combat();boss.Strike(0,int.MaxValue,1);if(dying)boss.Strike(0,int.MaxValue);
                var state=Replace(s,boss.Capture(),people);RoundTrip(state);m.Restore(state);var restored=Model();restored.Restore(new HookGame().Decode(new HookGame().Encode(state)));
                // 全灭冻结世界，但断肢在第 9 帧消失、第 78 帧完成下沉，恢复不能重播或停住。
                for(int i=1;i<=180;i++)
                {
                    Step(m);Step(restored);var after=m.Capture();Same(after,restored.Capture());
                    Require(after.Steps==s.Steps && after.Boss.Limbs[1].SeverTicks==Math.Min(i,78),"Finished world froze severed limb or advanced global clock");
                    if(i==8 || i==9 || i==77 || i==78 || i==179 || i==180)
                    {RoundTrip(after);restored.Restore(new HookGame().Decode(new HookGame().Encode(after)));}
                }
                var end=m.Capture();Require(dying?end.Boss.Phase==BossPhase.Defeated && end.Boss.Rewarded && end.Coins==600:end.Boss.Phase==BossPhase.Combat && end.Coins==0,"Wrong finished boss outcome");
            }
        });
        yield return ("boss_destroyed_floor_reconciles_live_and_dead_crabs_and_ground_packages",()=>{
            foreach(bool dead in new[]{false,true})
            {
                var config=new CrabConfig();var m=Model(crabs:config);var s=m.Capture();var b=Combat().Capture();var arms=b.Limbs.ToArray();arms[1]=new BossLimbState(1,705,705,BossAttack.Slam,44,targetSlot:0);
                var crab=new CrabState{Phase=dead?CrabPhase.Dead:CrabPhase.Recovering,Generation=1,X=-6000,Z=14000,DirectionX=1000000,Health=dead?0:100,Timer=dead?1800:30,Target=-1,Attackers=dead?(byte)1:(byte)0};
                var inventory=new InventorySystem(s.Tackle!.Inventory);inventory.CreditMaterial(0,ItemKind.Wood,8,new Point(0,0));
                int wood=inventory.Capture().Items.Single(i=>i.Owner==0 && i.Kind==ItemKind.Wood).Id;
                Require(inventory.Drop(0,wood,4,new Point(-6000,14000))==InventoryResult.Success && inventory.Drop(0,wood,4,new Point(-4000,14000))==InventoryResult.Success,"Ground setup failed");
                var people=s.Players.ToArray();people[0].WoodEarned=8;var tackle=new TackleState(s.Tackle.Config,inventory.Capture(),s.Tackle.Merchants);
                var state=new HookState(s.Steps,people,s.Fish,s.Coins,s.Tiles,s.Driftwood,tackle,s.Enemies,s.RoomRules,s.Treasure,s.Commissions,s.Ocean,s.Finale,new[]{crab},With(b,limbs:arms));m.Restore(state);Step(m);var after=m.Capture();RoundTrip(after);
                var restored=Model(crabs:config);restored.Restore(after);Same(after,restored.Capture());
                Require(dead?after.Crabs[0].Phase==CrabPhase.Dead && after.Crabs[0].Rewarded:after.Crabs[0].Phase==CrabPhase.Waiting,"Crab terrain reconciliation failed");
                Require(after.Tackle!.Inventory.Items.Where(i=>i.Kind==ItemKind.Wood).Sum(i=>i.Count)==8,"Lost falling materials");
            }
        });
        yield return ("slam_paid_tile_conservation_water_reconcile_save_and_no_repeat",()=>{
            var m=Model();var s=m.Capture();var b=Combat().Capture();var arms=b.Limbs.ToArray();arms[1]=new BossLimbState(1,705,705,BossAttack.Slam,44,targetSlot:0);
            var original=s.Tiles.Single(t=>t.X==-3 && t.Z==7);var tiles=s.Tiles.Select(t=>t.X==original.X && t.Z==original.Z?new PlatformTile(t.X,t.Z,t.Kind,0,4):t).ToArray();
            var loss=new[]{new BossTileLoss(original.X,original.Z,TileKind.Wood,-1,0)};
            var inv=new InventorySystem(s.Tackle!.Inventory);Require(inv.CreditMaterial(0,ItemKind.Wood,4,new Point(0,0))==4 && inv.TrySpendMaterial(0,ItemKind.Wood,4),"Material setup failed");
            var tackle=new TackleState(s.Tackle.Config,inv.Capture(),s.Tackle.Merchants);var p=s.Players.ToArray();p[0].WoodEarned+=4;p[0].X=-6000;p[0].Z=14000;
            m.Restore(Replace(s,With(b,limbs:arms,loss:loss),p,tiles,tackle));Step(m);var after=m.Capture();
            Require(after.Players[0].Swimming.InWater && !after.Tiles.Any(t=>t.X==-3 && t.Z==7),"No same-frame fall");
            Require(after.Boss.DestroyedTiles.Where(t=>t.Builder==0).Sum(t=>t.Paid)==4,"Lost paid material cost");RoundTrip(after);
            var n=Model();n.Restore(new HookGame().Decode(new HookGame().Encode(after)));int floorCount=after.Tiles.Count;
            for(int i=0;i<20;i++){Step(m);Step(n);Same(m.Capture(),n.Capture());}Require(n.Capture().Tiles.Count==floorCount,"Restore replayed impact");
            Reject(()=>new HookGame().Decode(new HookGame().Encode(Replace(after,With(after.Boss,loss:after.Boss.DestroyedTiles.Where(t=>t.Builder<0))))));
        });
        yield return ("damaged_disconnected_platform_valid_only_with_boss_loss_proof",()=>{
            var m=Model();var s=m.Capture();var sys=Combat();var p=People();var floor=s.Tiles.ToList();
            foreach(int id in new[]{0,1,2,3})
            {
                var b=sys.Capture();var arms=b.Limbs.Select(l=>new BossLimbState(l.Id,l.Health,l.MaxHealth)).ToArray();arms[id]=new BossLimbState(id,705,705,BossAttack.Slam,44,targetSlot:0);
                sys=new BossSystem(new BossConfig(),4,With(b,limbs:arms));sys.Advance(121,p,floor,_=>{});
            }
            Require(floor.Count<s.Tiles.Count-12,"Insufficient platform damage");var state=Replace(s,sys.Capture(),tiles:floor);RoundTrip(state);
            Reject(()=>new HookGame().Decode(new HookGame().Encode(Replace(state,With(state.Boss,loss:Array.Empty<BossTileLoss>())))));
            var unsupported=floor.Concat(new[]{new PlatformTile(-24,-24,TileKind.Wood,0,1)}).ToArray();Reject(()=>new HookGame().Decode(new HookGame().Encode(Replace(state,tiles:unsupported))));
        });
        yield return ("defeat_stops_attacks_rewards_contributors_once_restore_and_fishing_continues",()=>{
            var m=Model();var sys=Combat();sys.Strike(0,1,4);sys.Strike(1,int.MaxValue);m.Restore(Replace(m.Capture(),sys.Capture()));
            for(int i=0;i<180;i++)Step(m);var s=m.Capture();Require(s.Boss.Phase==BossPhase.Defeated && s.Boss.Rewarded && !s.Won,"Boss replaced finale");
            Require(s.Players[0].Coins==600 && s.Players[1].Coins==600 && s.Players[2].Coins==0 && s.Coins==1200,"Reward not personal");
            var n=Model();n.Restore(new HookGame().Decode(new HookGame().Encode(s)));for(int i=0;i<30;i++){Step(m);Step(n);}Same(m.Capture(),n.Capture());Require(n.Capture().Coins==1200,"Reward duplicated");
            var p=n.Capture().Players[0];Step(n,new HookInput(0,0,p.X,p.Z-5000,fire:true));Require(n.Capture().Players[0].Phase!=HookPhase.Ready && n.Capture().Steps>s.Steps,"Fishing stopped after optional kill");
        });
        yield return ("boss_state_validation_rejects_phase_health_support_attack_and_old_version",()=>{
            var m=Model();var s=Replace(m.Capture(),Combat().Capture());RoundTrip(s);
            Reject(()=>new HookGame().Decode(new HookGame().Encode(Replace(s,With(s.Boss,health:s.Boss.MaxHealth+1)))));
            var arms=s.Boss.Limbs.ToArray();arms[4]=new BossLimbState(4,arms[4].Health,arms[4].MaxHealth,BossAttack.Slam,1,targetSlot:0);
            Reject(()=>new HookGame().Decode(new HookGame().Encode(Replace(s,With(s.Boss,limbs:arms)))));
            Reject(()=>new HookGame().Decode(new HookGame().Encode(Replace(s,With(s.Boss,rewarded:true)))));
            Reject(()=>new HookGame().Decode(new HookGame().Encode(Replace(s,With(s.Boss,inkTicks:181)))));
            var bytes=new HookGame().Encode(s);bytes[3]=0x4b;Reject(()=>new HookGame().Decode(bytes));
        });
        yield return ("head_shot_height_event_and_deterministic_replay_all_phases",()=>{
            var m=Model(1,arenaSpawns:true);Step(m,new HookInput(0,0,bossRequest:1));
            var state=m.Capture();var players=state.Players.ToArray();players[0].X=0;players[0].Z=16000;m.Restore(Replace(state,players:players));
            var gun=m.Capture().Tackle!.Inventory.Items.Single(i=>i.Kind==ItemKind.Firearm);Step(m,new HookInput(0,0,inventory:new InventoryCommand(InventoryAction.Equip,gun.Id,requestId:1)));
            for(int i=0;i<120;i++)Step(m);
            var n=Model(1,arenaSpawns:true);n.Restore(new HookGame().Decode(new HookGame().Encode(m.Capture())));bool actualHit=false;
            for(int i=0;i<400;i++)
            {
                var s=m.Capture();var p=s.Players[0];var eye=BossGeometry.Proxies(s.Boss).Single(v=>v.Eye);int dx=eye.X-p.X,dz=eye.Z-p.Z,dy=eye.Y-BossGeometry.MuzzleHeight(p);
                int distance=HookGame.Sqrt((long)dx*dx+(long)dy*dy+(long)dz*dz);short pitch=(short)(dy*1000/Math.Max(1,distance));
                var command=new HookInput(0,0,eye.X,eye.Z,fire:true,reload:s.Tackle!.Inventory.Equipped(0,EquipmentSlot.RightHand)!.Value.LoadedAmmo==0,aimVertical:pitch);
                var events=Step(m,command);Step(n,command);Same(m.Capture(),n.Capture());
                actualHit|=events.Any(e=>e.Kind==HookGameplayEventKind.FirearmShot && e.TargetId==-7 && e.Amount>=35 && e.Y>3000 && e.OriginY==1480);
                if(i%30==0)RoundTrip(m.Capture());
            }
            Require(actualHit,"Real firearm failed to hit elevated eye or lost 3D event");
        });
    }
}
