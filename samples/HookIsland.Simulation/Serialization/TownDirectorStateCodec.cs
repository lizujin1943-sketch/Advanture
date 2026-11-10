using System.IO;

namespace Advanture.HookIsland.Simulation;

/// <summary>导演状态的固定字段顺序与边界校验。非法预算、计时或随机状态必须在恢复前拒绝。</summary>
internal static class TownDirectorStateCodec
{
    internal static void Write(BinaryWriter w,TownDirectorState s)
    {
        w.Write(s.ElapsedTicks);w.Write(s.CreditUnits);w.Write(s.IncomeRemainder);w.Write(s.WaitTicks);
        w.Write(s.PendingCard);w.Write(s.BurstRemaining);w.Write(s.RandomState);
    }
    internal static TownDirectorState Read(BinaryReader r)=>new TownDirectorState
    {
        ElapsedTicks=r.ReadUInt64(),CreditUnits=r.ReadInt32(),IncomeRemainder=r.ReadInt32(),WaitTicks=r.ReadInt32(),
        PendingCard=r.ReadInt32(),BurstRemaining=r.ReadInt32(),RandomState=r.ReadUInt32()
    };
    internal static void Validate(TownDirectorState s,bool enabled,ulong steps,EldritchCreature[] creatures)
    {
        bool empty=s.Equals(default(TownDirectorState));
        if(!enabled || s.RandomState==0)
        {if(!empty)throw new InvalidDataException("Unexpected inactive director state.");return;}
        if(s.ElapsedTicks==0 || s.ElapsedTicks>steps || s.ElapsedTicks>TownDirectorRules.MaxElapsedTicks ||
            s.CreditUnits<0 || s.CreditUnits>TownDirectorRules.MaxCredits*HookGame.Rate ||
            s.IncomeRemainder<0 || s.IncomeRemainder>=5 || s.WaitTicks<0 || s.WaitTicks>150 ||
            s.PendingCard<0 || s.PendingCard>TownEncounters.AdultCount || s.BurstRemaining<0 || s.BurstRemaining>3)
            throw new InvalidDataException("Invalid director budget or clock.");
        if(s.PendingCard>0)
        {
            var c=creatures[s.PendingCard-1];
            if(c.Phase!=EldritchPhase.Dormant || c.Timer!=0 || c.Generation==uint.MaxValue || s.BurstRemaining==0)
                throw new InvalidDataException("Invalid pending director card.");
        }
    }
}
