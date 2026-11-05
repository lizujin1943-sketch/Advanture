using System;
using System.Collections.Generic;
using System.Linq;

namespace Advanture.HookIsland.Simulation;

/// <summary>单个房间的独立 BOSS 状态机。请求、每条手臂、墨汁与结算均有独立回执。</summary>
public sealed class BossSystem
{
    private readonly BossConfig config;
    private readonly bool town;private int inkSector=1;
    private int CenterX=>town?TownLayout.CenterX:BossConfig.CenterX;
    private int CenterZ=>town?TownLayout.CenterZ:BossConfig.CenterZ;
    private int InkImpact=>town?BossConfig.TownInkWindupTicks:BossConfig.InkWindupTicks;
    private int InkFinish=>town?BossConfig.TownInkFinishTicks:BossConfig.InkFinishTicks;
    private BossPhase phase;private int health,maxHealth,participants,phaseTicks,idleTicks,inkTicks,inkAnimation,inkCooldown,noTargetTicks;
    private uint sequence;private byte attackers;private bool rewarded,enraged;private ulong inkStartTick;
    private BossLimbState[] limbs;private readonly BossRequestReceipt[] receipts;private readonly List<BossTileLoss> destroyed;
    public BossSystem(BossConfig config,int playerCount,BossState? state=null,bool town=false)
    {
        this.config=config;this.town=town;receipts=new BossRequestReceipt[playerCount];destroyed=new List<BossTileLoss>();limbs=Array.Empty<BossLimbState>();
        if(state==null){Reset();return;}
        inkSector=state.InkSector;phase=state.Phase;health=state.Health;maxHealth=state.MaxHealth;participants=state.ParticipantCount;phaseTicks=state.PhaseTicks;
        sequence=state.Sequence;attackers=state.Attackers;rewarded=state.Rewarded;enraged=state.Enraged;inkTicks=state.InkTicks;inkStartTick=state.InkStartTick;
        idleTicks=state.IdleTicks;inkAnimation=state.InkWindupTicks;inkCooldown=state.InkCooldownTicks;noTargetTicks=state.NoTargetTicks;
        limbs=state.Limbs.ToArray();for(int i=0;i<receipts.Length && i<state.Receipts.Count;i++)receipts[i]=state.Receipts[i];destroyed.AddRange(state.DestroyedTiles);
    }
    public BossState Capture()=>new BossState(phase,health,maxHealth,participants,attackers,rewarded,phaseTicks,sequence,inkTicks,inkStartTick,limbs,config.Enabled || receipts.Any(r=>r.Request!=0)?receipts:Array.Empty<BossRequestReceipt>(),destroyed,idleTicks,inkAnimation,inkCooldown,noTargetTicks,enraged,town,inkSector);
    public BossRequestResult Request(int slot,uint request,IReadOnlyList<Fisher> players)
    {
        if(request==0 || request<=receipts[slot].Request)return receipts[slot].Result;
        var p=players[slot];BossRequestResult result;
        if(!config.Enabled)result=BossRequestResult.Disabled;
        else if(!Conscious(p) || p.Swimming.InWater || p.Shopping || p.PulledBy>=0 || p.Knockback.Active)result=BossRequestResult.Unavailable;
        else if(phase==BossPhase.Defeated)result=BossRequestResult.Defeated;
        else if(phase!=BossPhase.Dormant)result=BossRequestResult.Busy;
        else if(!BossArena.NearBeacon(p,town))result=BossRequestResult.TooFar;
        else if(sequence==uint.MaxValue)result=BossRequestResult.Exhausted;
        else
        {
            participants=players.Count(v=>!v.WaitingToJoin && !v.Unavailable);maxHealth=health=config.ScaledBody(participants);
            limbs=Enumerable.Range(0,8).Select(id=>new BossLimbState(id,config.ScaledLimb(id,participants),config.ScaledLimb(id,participants))).ToArray();
            phase=BossPhase.Emerge;phaseTicks=0;sequence++;idleTicks=90;inkCooldown=360;result=BossRequestResult.Success;
        }
        receipts[slot]=new BossRequestReceipt(request,result);return result;
    }
    public int MovementPermille(Fisher p)=>inkTicks>0 && BossGeometry.InkContainsXZ(Capture(),p.X,p.Z)?BossConfig.InkSlowPermille:1000;
    public bool Strike(int slot,int damage,int limbId=-1,bool eye=false)
    {
        if(!config.Enabled || (phase!=BossPhase.Combat && phase!=BossPhase.Enrage) || slot<0 || slot>=receipts.Length || damage<=0 || limbId< -1 || limbId>=8)return false;
        if(limbId>=0)
        {
            var limb=limbs[limbId];if(limb.Health==0)return false;int remaining=Math.Max(0,limb.Health-damage);
            limbs[limbId]=new BossLimbState(limb.Id,remaining,limb.MaxHealth,remaining==0?BossAttack.None:limb.Attack,remaining==0?0:limb.AttackTicks,0,remaining==0?-1:limb.TargetSlot,remaining==0?(byte)0:limb.HitPlayers,remaining==0?0:limb.AttackX,remaining==0?0:limb.AttackZ);
        }
        else
        {
            int amount=eye?(int)Math.Min(int.MaxValue,((long)damage*3+1)/2):damage;health=Math.Max(0,health-amount);
            if(health==0){phase=BossPhase.Dying;phaseTicks=0;CancelAttacks();}
        }
        attackers|=(byte)(1<<slot);return true;
    }
    internal void AdvanceFinished()
    {
        if(!config.Enabled || phase==BossPhase.Dormant || phase==BossPhase.Defeated)return;
        AdvanceSeveredLimbs();
        if(phase==BossPhase.Dying){if(++phaseTicks>=BossConfig.DyingTicks){phase=BossPhase.Defeated;phaseTicks=0;}return;}
        CancelAttacks();
        // 已结束的正常世界不能再触发撞击，只推进退场；保留全部已支付的损毁账目。
        if(++noTargetTicks>=BossConfig.ResetTicks)Reset();
    }
    public void MarkRewarded(){if(phase==BossPhase.Defeated)rewarded=true;}
    public void Advance(ulong tick,IReadOnlyList<Fisher> players,List<PlatformTile> tiles,Action<EnemyHit> hit)
    {
        if(!config.Enabled || phase==BossPhase.Dormant || phase==BossPhase.Defeated)return;
        if(phaseTicks<int.MaxValue)phaseTicks++;
        AdvanceSeveredLimbs();
        if(phase==BossPhase.Dying)
        {if(phaseTicks>=BossConfig.DyingTicks){phase=BossPhase.Defeated;phaseTicks=0;}return;}
        bool present=players.Any(p=>Conscious(p) && IslandMap.DistanceSquared(p.X,p.Z,CenterX,CenterZ)<=(town?26000L*26000:(long)BossConfig.AggroRadius*BossConfig.AggroRadius));
        noTargetTicks=present?0:noTargetTicks+1;
        if(noTargetTicks>=BossConfig.ResetTicks){Reset();return;}
        if(phase==BossPhase.Emerge)
        {if(phaseTicks>=BossConfig.EmergeTicks){phase=BossPhase.Combat;phaseTicks=0;}return;}
        if(!enraged && (long)health*100<=(long)maxHealth*40)
        {enraged=true;phase=BossPhase.Enrage;phaseTicks=0;CancelAttacks();idleTicks=72;inkCooldown=Math.Min(inkCooldown,300);return;}
        if(phase==BossPhase.Enrage)
        {if(phaseTicks>=BossConfig.EnrageTicks){phase=BossPhase.Combat;phaseTicks=0;}return;}
        if(inkTicks>0)
        {
            if((tick-inkStartTick)%30==0)foreach(var p in players)
                if(Conscious(p) && BossGeometry.InkContainsXZ(Capture(),p.X,p.Z))hit(new EnemyHit(p.Slot,8));
            inkTicks--;
        }
        if(inkCooldown>0)inkCooldown--;
        if(inkAnimation>0)
        {
            inkAnimation++;
            if(inkAnimation==InkImpact){inkTicks=BossConfig.InkDurationTicks;inkStartTick=tick;}
            if(inkAnimation>=InkFinish)inkAnimation=0;
        }
        else if(!town && inkCooldown==0 && sequence<uint.MaxValue && players.Any(p=>Conscious(p) && BossGeometry.InkContainsXZ(p.X,p.Z)))
        {inkAnimation=1;inkCooldown=enraged?300:360;sequence++;}
        for(int i=0;i<4;i++)
        {
            var l=limbs[i];if(l.Health==0 || l.Attack==BossAttack.None)continue;
            int clock=l.AttackTicks+1;byte victims=l.HitPlayers;
            if(l.Attack==BossAttack.Slam && clock==BossConfig.SlamImpactTicks)DestroyStrip(i,tiles);
            if(l.Attack==BossAttack.Slam && clock==BossConfig.SlamImpactTicks || l.Attack==BossAttack.Sweep && clock>=BossConfig.SweepStartTicks && clock<=BossConfig.SweepEndTicks)
            {
                foreach(var p in players)
                {
                    if(!Conscious(p) || (victims&(1<<p.Slot))!=0)continue;
                    bool inside=l.Attack==BossAttack.Slam?BossGeometry.SlamContainsXZ(Capture(),i,p.X,p.Z,200):BossGeometry.SweepContainsXZ(Capture(),i,clock,p.X,p.Z,200);
                    if(!inside)continue;victims|=(byte)(1<<p.Slot);var direction=BossGeometry.AttackDirection(Capture(),i);
                    hit(new EnemyHit(p.Slot,l.Attack==BossAttack.Slam?36:24,true,direction.X,direction.Z,l.Attack==BossAttack.Slam?3000:5000));
                }
            }
            int finish=l.Attack==BossAttack.Slam?BossConfig.SlamFinishTicks:BossConfig.SweepFinishTicks;
            limbs[i]=clock>=finish?new BossLimbState(i,l.Health,l.MaxHealth):new BossLimbState(i,l.Health,l.MaxHealth,l.Attack,clock,l.SeverTicks,l.TargetSlot,victims,l.AttackX,l.AttackZ);
        }
        // Schedule new town attacks only after every existing clock has reached this frame,
        // so the path proof uses the same time origin for ink and both arms.
        if(town && inkAnimation==0 && inkCooldown==0 && sequence<uint.MaxValue)TryStartTownInk(players,tiles);
        if(idleTicks>0)idleTicks--;
        if(idleTicks>0 || sequence==uint.MaxValue || limbs.Count(l=>l.Attack!=BossAttack.None)>=(enraged?2:1))return;
        int selected=-1,target=-1;long best=long.MaxValue;
        var engaged=limbs.Where(l=>l.Attack!=BossAttack.None).Select(l=>l.TargetSlot).ToArray();
        for(int id=0;id<4;id++)
        {
            if(limbs[id].Health==0 || limbs[id].Attack!=BossAttack.None)continue;
            var d=BossGeometry.AttackDirection(id,town);
            foreach(var p in players)
            {
                if(!Conscious(p))continue;long x=p.X-CenterX,z=p.Z-CenterZ;
                long along=(x*d.X+z*d.Z)/1000,across=(x*d.Z-z*d.X)/1000;
                if(along<7000 || along>23000 || Math.Abs(across)>along*7/10)continue;
                long score=across*across*4+x*x+z*z+(engaged.Contains(p.Slot)?10000000000L:0);
                // 同分按既定关节编号和槽位顺序，不依赖容器哈希顺序。
                if(score<best){best=score;selected=id;target=p.Slot;}
            }
        }
        if(selected<0){idleTicks=15;return;}
        var arm=limbs[selected];var attack=((sequence+1)%3==0)?BossAttack.Sweep:BossAttack.Slam;
        if(town)
        {
            int impact=attack==BossAttack.Slam?BossConfig.SlamImpactTicks:BossConfig.SweepStartTicks;
            // All damage windows, including sweeps, must be separated by at least 0.6 seconds.
            if(limbs.Any(l=>l.Attack!=BossAttack.None && Math.Abs(impact-((l.Attack==BossAttack.Slam?BossConfig.SlamImpactTicks:BossConfig.SweepStartTicks)-l.AttackTicks))<18))
            {idleTicks=15;return;}
            var p=players[target];int dx=p.X-CenterX,dz=p.Z-CenterZ,length=HookGame.Sqrt((long)dx*dx+(long)dz*dz);
            limbs[selected]=new BossLimbState(selected,arm.Health,arm.MaxHealth,attack,targetSlot:target,attackX:dx*1000/length,attackZ:dz*1000/length);
            if(!players.Where(Conscious).All(p=>BossEscapeRoutes.CanEscape(Capture(),tiles,p)))
            {limbs[selected]=arm;idleTicks=15;return;}
        }
        else limbs[selected]=new BossLimbState(selected,arm.Health,arm.MaxHealth,attack,targetSlot:target);
        sequence++;idleTicks=enraged?72:90;
    }
    private void TryStartTownInk(IReadOnlyList<Fisher> players,IReadOnlyList<PlatformTile> tiles)
    {
                int previousSector=inkSector;
                // Lock one local sector once. Stable slot order breaks equal-distance ties.
                var inkTargets=players.Where(Conscious).Where(p=>p.Z<CenterZ && IslandMap.DistanceSquared(p.X,p.Z,CenterX,CenterZ)<=19500L*19500).OrderBy(p=>p.Slot).ToArray();
                if(inkTargets.Length>0)
                {
                    int dx=inkTargets[0].X-CenterX;inkSector=dx< -3500?0:dx>3500?2:1;inkAnimation=1;
                    if(players.Where(Conscious).All(p=>BossEscapeRoutes.CanEscape(Capture(),tiles,p)))
                    {inkCooldown=enraged?300:360;sequence++;}
                    else {inkAnimation=0;inkSector=previousSector;inkCooldown=15;}
                }
    }
    private void DestroyStrip(int id,List<PlatformTile> tiles)
    {
        for(int i=tiles.Count-1;i>=0;i--)
        {
            var t=tiles[i];if(town)
            {
                var segment=TownLayout.WoodSegments.FirstOrDefault(v=>v.CellX==t.X && v.CellZ==t.Z);
                if(!TownLayout.Destructible(t.X,t.Z) || !BossGeometry.SlamContainsXZ(Capture(),id,segment.X,segment.Z))continue;
            }
            else if(!BossArena.Destructible(t.X,t.Z) || !BossGeometry.SlamContainsXZ(id,t.X*2000,t.Z*2000))continue;
            int existing=destroyed.FindIndex(d=>d.X==t.X && d.Z==t.Z && d.Kind==t.Kind && d.Builder==t.Builder);
            if(existing<0)
            {if(destroyed.Count>=2048)continue;destroyed.Add(new BossTileLoss(t.X,t.Z,t.Kind,t.Builder,t.Paid));}
            else {var d=destroyed[existing];destroyed[existing]=new BossTileLoss(d.X,d.Z,d.Kind,d.Builder,d.Paid+t.Paid);}
            tiles.RemoveAt(i);
        }
    }
    private static bool Conscious(Fisher p)=>!p.WaitingToJoin && !p.Unavailable && !p.Vitals.Downed && !p.Revival.Dead;
    private void AdvanceSeveredLimbs()
    {
        // 断肢退场属于 BOSS 自有时钟；全灭冻结世界时也须完成消失和下沉。
        for(int i=0;i<limbs.Length;i++)
        {var l=limbs[i];if(l.Health==0 && l.SeverTicks<78)limbs[i]=new BossLimbState(l.Id,0,l.MaxHealth,severTicks:l.SeverTicks+1);}
    }
    private void CancelAttacks()
    {
        for(int i=0;i<limbs.Length;i++){var l=limbs[i];limbs[i]=new BossLimbState(i,l.Health,l.MaxHealth,severTicks:l.SeverTicks);}
        inkTicks=inkAnimation=0;inkStartTick=0;
    }
    private void Reset()
    {
        if(town)destroyed.Clear();
        phase=BossPhase.Dormant;health=maxHealth=config.Enabled?config.BodyHealth:0;participants=phaseTicks=idleTicks=inkTicks=inkAnimation=inkCooldown=noTargetTicks=0;
        inkStartTick=0;attackers=0;rewarded=enraged=false;
        limbs=config.Enabled?Enumerable.Range(0,8).Select(id=>new BossLimbState(id,id<4?config.LimbHealth:config.SupportHealth,id<4?config.LimbHealth:config.SupportHealth)).ToArray():Array.Empty<BossLimbState>();
    }
}
