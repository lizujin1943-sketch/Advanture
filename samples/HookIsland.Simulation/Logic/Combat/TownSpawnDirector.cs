using System;
using System.Collections.Generic;

namespace Advanture.HookIsland.Simulation;

/// <summary>
/// 城镇持续刷怪导演：积累预算、按权重选卡、分批购买。
/// 不拥有怪物 AI 或奖励；spawn 由怪物系统提供，仅生成成功才扣除成本。
/// 每帧最多尝试一次，选卡只扫描固定的 15 个槽位，禁止无界搜索出生点。
/// </summary>
public sealed class TownSpawnDirector
{
    private TownDirectorState state;
    private readonly uint seed;
    public TownSpawnDirector(int seed,TownDirectorState restored=default)
    {this.seed=unchecked((uint)seed)^0x9E3779B9u;state=restored;}
    public TownDirectorState Capture()=>state;

    public void Advance(bool allowed,int participants,IReadOnlyList<bool> zones,
        IReadOnlyList<EldritchCreature> creatures,int maxAlive,Func<int,bool> spawn)
    {
        bool anyZone=false;for(int z=0;z<zones.Count;z++)anyZone|=zones[z];
        // 暂停时不走时钟，也不存预算；恢复仍遵循原有冷却与硬上限。
        if(!allowed || participants<=0 || !anyZone)return;
        if(state.RandomState==0)
        {
            state.RandomState=seed==0?1u:seed;
            state.CreditUnits=TownDirectorRules.InitialCredits*HookGame.Rate;
            state.WaitTicks=TownDirectorRules.InitialWaitTicks;
        }
        state.ElapsedTicks=Math.Min(TownDirectorRules.MaxElapsedTicks,state.ElapsedTicks+1);
        int tier=TownDirectorRules.Tier(state.ElapsedTicks);
        int income=(2+tier)*(5+3*(Math.Min(4,participants)-1))+state.IncomeRemainder;
        state.CreditUnits=Math.Min(TownDirectorRules.MaxCredits*HookGame.Rate,state.CreditUnits+income/5);
        state.IncomeRemainder=income%5;
        if(state.WaitTicks>0 && --state.WaitTicks>0)return;

        int alive=0;for(int i=0;i<TownEncounters.AdultCount;i++)if(creatures[i].Damageable)alive++;
        if(alive>=maxAlive)return;
        bool Available(int id)=>zones[TownEncounters.Zone(id)] && creatures[id].Phase==EldritchPhase.Dormant &&
            creatures[id].Timer==0 && creatures[id].Generation<uint.MaxValue;
        if(state.PendingCard>0 && !Available(state.PendingCard-1))state.PendingCard=0;
        if(state.PendingCard==0)
        {
            int weight=0;
            for(int i=0;i<TownEncounters.AdultCount;i++)if(Available(i))weight+=TownDirectorRules.Weight(creatures[i].Kind,tier);
            if(weight==0){state.WaitTicks=TownDirectorRules.RetryTicks;return;}
            int roll=(int)(NextRandom()%(uint)weight);
            for(int i=0;i<TownEncounters.AdultCount;i++)if(Available(i))
            {
                roll-=TownDirectorRules.Weight(creatures[i].Kind,tier);
                if(roll<0){state.PendingCard=i+1;break;}
            }
        }
        if(state.BurstRemaining==0)state.BurstRemaining=tier>=2?3:2;
        int selected=state.PendingCard-1,cost=TownDirectorRules.Cost(creatures[selected].Kind)*HookGame.Rate;
        if(state.CreditUnits<cost)return; // 贵怪已经选中，先攒够钱，不退回购买廉价怪。
        state.PendingCard=0;
        if(!spawn(selected))
        {state.WaitTicks=TownDirectorRules.RetryTicks;return;} // 港口被玩家看见/占用时，下秒重新选卡，钱保留。
        state.CreditUnits-=cost;
        state.BurstRemaining--;
        state.WaitTicks=state.BurstRemaining==0?TownDirectorRules.RestTicks(tier):TownDirectorRules.SpawnSpacingTicks;
    }

    private uint NextRandom()
    {
        // 固定 xorshift32；不依赖 System.Random、平台浮点或 Unity 随机数。
        uint value=state.RandomState;value^=value<<13;value^=value>>17;value^=value<<5;
        state.RandomState=value;return value;
    }
}
