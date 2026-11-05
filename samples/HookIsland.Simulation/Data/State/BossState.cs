using System;
using System.Collections.Generic;
using System.Linq;

namespace Advanture.HookIsland.Simulation;

/// <summary>休眠、登场、战斗、狂暴过渡、死亡动作、已击败；狂暴战斗仍用 Combat，另由 Enraged 标记。</summary>
public enum BossPhase : byte { Dormant,Emerge,Combat,Enrage,Dying,Defeated }
/// <summary>单条进攻触手的动作。支撑触手不使用 Slam 或 Sweep。</summary>
public enum BossAttack : byte { None,Slam,Sweep }
public enum BossRequestResult : byte { None,Success,Disabled,TooFar,Busy,Unavailable,Defeated,Exhausted }
/// <summary>每名玩家最后一次唤醒请求的处理结果；失败也会记账，防止网络重发变成新的尝试。</summary>
public readonly struct BossRequestReceipt
{
    public BossRequestReceipt(uint request,BossRequestResult result){Request=request;Result=result;}
    public uint Request {get;} public BossRequestResult Result {get;}
}
/// <summary>一条触手的独立生命与动作进度。所有计数以每秒 30 帧计算。</summary>
public readonly struct BossLimbState
{
    public BossLimbState(int id,int health,int maxHealth,BossAttack attack=BossAttack.None,int attackTicks=0,int severTicks=0,int targetSlot=-1,byte hitPlayers=0,int attackX=0,int attackZ=0)
    {Id=id;Health=health;MaxHealth=maxHealth;Attack=attack;AttackTicks=attackTicks;SeverTicks=severTicks;TargetSlot=targetSlot;HitPlayers=hitPlayers;AttackX=attackX;AttackZ=attackZ;}
    public int Id {get;} // 0～3 为 A01～A04，4～7 为 S01～S04。
    public int Health {get;}
    public int MaxHealth {get;}
    public BossAttack Attack {get;}
    public int AttackTicks {get;} // 当前攻击已经经过的帧数；不是世界帧号。
    public int SeverTicks {get;} // 断肢后经过的帧数，78 封顶；全灭退场时也继续推进。
    public int TargetSlot {get;} // 发动攻击时选中的玩家；固定攻击路线不会随后追着玩家旋转。
    public int AttackX {get;} public int AttackZ {get;} // Locked Town direction; zero retains the legacy lane.
    public byte HitPlayers {get;} // 位图：本轮动作已经打过谁，保证横扫不会逐帧重复伤害。
}
/// <summary>损毁账本保留历史材料支出；修复后再损毁按格、材料和建造者合并，不能退回收入。</summary>
public readonly struct BossTileLoss
{
    public BossTileLoss(int x,int z,TileKind kind,int builder,long paid){X=x;Z=z;Kind=kind;Builder=builder;Paid=paid;}
    public int X {get;} public int Z {get;} public TileKind Kind {get;} public int Builder {get;} public long Paid {get;}
}
/// <summary>
/// 可保存的 BOSS 完整状态。集合在构造时复制，不向 Unity 暴露模拟内部可变数据。
/// 它同时服务确认态显示、检查点、回滚和恢复，不依赖模型或动画播放器。
/// </summary>
public sealed class BossState
{
    public static BossState Empty {get;}=new BossState();
    public BossState(BossPhase phase=BossPhase.Dormant,int health=0,int maxHealth=0,int participantCount=0,byte attackers=0,bool rewarded=false,int phaseTicks=0,uint sequence=0,
        int inkTicks=0,ulong inkStartTick=0,IEnumerable<BossLimbState>? limbs=null,IEnumerable<BossRequestReceipt>? receipts=null,IEnumerable<BossTileLoss>? destroyedTiles=null,
        int idleTicks=0,int inkWindupTicks=0,int inkCooldownTicks=0,int noTargetTicks=0,bool enraged=false,bool townArena=false,int inkSector=1)
    {
        TownArena=townArena;InkSector=inkSector;Phase=phase;Health=health;MaxHealth=maxHealth;ParticipantCount=participantCount;Attackers=attackers;Rewarded=rewarded;PhaseTicks=phaseTicks;Sequence=sequence;
        InkTicks=inkTicks;InkStartTick=inkStartTick;IdleTicks=idleTicks;InkWindupTicks=inkWindupTicks;InkCooldownTicks=inkCooldownTicks;NoTargetTicks=noTargetTicks;Enraged=enraged;
        Limbs=Array.AsReadOnly((limbs??Array.Empty<BossLimbState>()).ToArray());Receipts=Array.AsReadOnly((receipts??Array.Empty<BossRequestReceipt>()).ToArray());
        DestroyedTiles=Array.AsReadOnly((destroyedTiles??Array.Empty<BossTileLoss>()).ToArray());
    }
    public bool TownArena {get;}
    public int InkSector {get;} // Town locked local ink direction, 0..2 from west to east.
    public int BeaconX=>TownArena?TownLayout.BeaconX:BossConfig.BeaconX;
    public int BeaconZ=>TownArena?TownLayout.BeaconZ:BossConfig.BeaconZ;
    public int CenterX=>TownArena?TownLayout.CenterX:BossConfig.CenterX;
    public int CenterZ=>TownArena?TownLayout.CenterZ:BossConfig.CenterZ;
    public BossPhase Phase {get;}
    public int Health {get;}
    public int MaxHealth {get;}
    public int ParticipantCount {get;} // 唤醒时冻结的人数，后续掉线不重算生命上限。
    public byte Attackers {get;} // 位图：对本体或任一触手造成过伤害的玩家，决定个人奖励归属。
    public bool Rewarded {get;} // 整笔奖励已经结算；恢复后不能再发一次。
    public int PhaseTicks {get;} // 当前阶段已过帧数。普通世界冻结时死亡动画仍可独立走完。
    public uint Sequence {get;} // 本房间的唤醒／技能序号，供稳定的攻击选择使用，重试不回退。
    public int InkTicks {get;} // 墨池剩余持续帧数，零表示不存在。
    public ulong InkStartTick {get;} // 墨池形成时的世界帧，用于固定每秒伤害脉冲。
    public int IdleTicks {get;} // 距离允许安排下一条触手动作的剩余帧数。
    public int InkWindupTicks {get;} // 喷墨动作的完整已过帧数（包括喷出与收招），零表示未播放。
    public int InkCooldownTicks {get;} // 距离允许再次喷墨的剩余帧数。
    public int NoTargetTicks {get;} // 连续没有有效目标的帧数，达到 300 则重置本次挑战。
    public bool Enraged {get;} // 本次挑战已进入狂暴；断肢和受伤不会在切阶段时恢复。
    public IReadOnlyList<BossLimbState> Limbs {get;}
    public IReadOnlyList<BossRequestReceipt> Receipts {get;}
    public IReadOnlyList<BossTileLoss> DestroyedTiles {get;}
    public bool Damageable=>Phase==BossPhase.Combat || Phase==BossPhase.Enrage;
    public bool Active=>Phase!=BossPhase.Dormant && Phase!=BossPhase.Defeated;
}
