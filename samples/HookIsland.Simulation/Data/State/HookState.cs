using System;
using System.Collections.Generic;
using System.Linq;

namespace Advanture.HookIsland.Simulation;

public enum HookPhase : byte { Ready, Flying, Returning }
public enum FishPhase : byte { Waiting, Swimming, Jumping, Hooked, Diving, Escaping }

// Mutable structs are kept only inside the simulation; captures expose independent, read-only arrays.
public struct Fisher
{
    public MeleeState Melee;
    public ushort Slot;
    public CharacterVitals Vitals;
    public SwimmingState Swimming;
    public RevivalState Revival;
    public KnockbackState Knockback;
    public ComboState Combo;
    public bool Unavailable;
    public bool WaitingToJoin;
    // One-based piranha identifier; zero means no enemy attached to this hook.
    public int HookedPiranha;
    // Contiguous join rank (oldest is zero); sub-unit shared stamina cost in twelfths.
    public int FishingJoinOrder,FishingDrainRemainder;
    public int HookedChest,ChestJoinOrder,TreasuresCollected;
    public bool HookedFinale;
    public int FinaleDrainRemainder;
    public uint LastTreasureRequest;
    public TreasureResult LastTreasureResult;
    public int MoveRemainderX, MoveRemainderZ;
    public int X, Z, AimX, AimZ, RemainderX, RemainderZ;
    public HookPhase Phase;
    public int HookX, HookZ, DirectionX, DirectionZ, Distance, FishId, Cooldown;
    public int Catches, Coins, Casts;
    // Equipment mode: Coins is spendable personal balance; CoinsEarned is lifetime catch income.
    public int CoinsEarned;
    public int RangeLevel, SpeedLevel;
    public uint LastPurchaseId;
    public PurchaseResult LastPurchaseResult;
    public int HookedPlayer, PulledBy, PullTicks, ProtectionTicks;
    public int PlayerHooks, TimesHooked;
    public bool Shopping;
    public int Wood,Fishbones,WoodEarned,FishbonesEarned,DriftwoodId;
    public uint LastBuildId;
    public BuildResult LastBuildResult;
    public int EquippedRod,Stamina,RegenDelay,HookTravelRemainder,ReelRemainder,Escapes;
    public uint LastRodRequest;
    public RodResult LastRodResult;
    // 成功和失败都形成最终回执；存档恢复后，旧点击不得再次生效。
    public uint LastInventoryRequest;
    public InventoryResult LastInventoryResult;
}
public struct Fish
{
    public CatchEscapeState Escape;
    public int Id, X, Z, Y, Timer, Owner, Heading;
    public FishPhase Phase;
    public int Species,WeightGrams,Grade;
    public uint Generation;
}
public sealed class HookState
{
    public HookState(ulong steps, IEnumerable<Fisher> players, IEnumerable<Fish> fish, int coins,
        IEnumerable<PlatformTile>? tiles=null,IEnumerable<Driftwood>? driftwood=null,TackleState? tackle=null,EnemyState? enemies=null,RoomRulesState? roomRules=null,TreasureState? treasure=null,CommissionState? commissions=null,OceanState? ocean=null,FinaleState? finale=null)
    {
        Steps=steps;Players=Array.AsReadOnly(players.ToArray());Fish=Array.AsReadOnly(fish.ToArray());Coins=coins;
        Tiles=Array.AsReadOnly((tiles??Array.Empty<PlatformTile>()).ToArray());
        Driftwood=Array.AsReadOnly((driftwood??Array.Empty<Driftwood>()).ToArray());
        Tackle=tackle;
        Enemies=enemies??EnemyState.Empty;
        RoomRules=roomRules??new RoomRulesState(true);
        Treasure=treasure??TreasureState.Empty;
        Commissions=commissions??CommissionState.Empty;
        Ocean=ocean??OceanState.Empty;
        Finale=finale??FinaleState.Empty;
    }
    public bool Won=>Finale.Phase==FinalePhase.Victory;
    public bool RoundFinished=>Won || GameOver;
    public bool GameOver => !Won && Tackle!=null && RevivalSystem.GameOver(Players);
    public ulong Steps { get; }
    public IReadOnlyList<Fisher> Players { get; }
    public IReadOnlyList<Fish> Fish { get; }
    public int Coins { get; }
    public IReadOnlyList<PlatformTile> Tiles { get; }
    public IReadOnlyList<Driftwood> Driftwood { get; }
    public TackleState? Tackle { get; }
    public EnemyState Enemies { get; }
    public RoomRulesState RoomRules { get; }
    public TreasureState Treasure { get; }
    public CommissionState Commissions { get; }
    public OceanState Ocean {get;}
    public FinaleState Finale {get;}
}
