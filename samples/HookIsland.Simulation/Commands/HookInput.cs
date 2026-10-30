using System;
using System.Buffers.Binary;
using System.IO;

namespace Advanture.HookIsland.Simulation;

/// <summary>毫米坐标、千分比移动方向。射击边沿只消费一次；取消也是确定性输入。</summary>
public readonly struct HookInput
{
    public const int ByteCount = 40 + InventoryCommand.ByteCount + RevivalCommand.ByteCount + RoomRulesCommand.ByteCount + TreasureCommand.ByteCount;
    public HookInput(short moveX, short moveZ, int aimX = 0, int aimZ = 0, bool fire = false, bool cancel = false,
        HookUpgrade upgrade = HookUpgrade.None, uint purchaseId = 0, bool shopping = false,
        TileKind build=TileKind.None,short buildX=0,short buildZ=0,uint buildId=0,
        RodAction rodAction=RodAction.None,int rodTarget=0,uint rodBatch=0,uint rodRequestId=0, InventoryCommand inventory=default,RevivalCommand revival=default,RoomRulesCommand roomRules=default,TreasureCommand treasure=default)
    {
        MoveX=moveX;MoveZ=moveZ;AimX=aimX;AimZ=aimZ;Fire=fire;Cancel=cancel;Upgrade=upgrade;PurchaseId=purchaseId;Shopping=shopping;
        Build=build;BuildX=buildX;BuildZ=buildZ;BuildId=buildId;
        RodAction=rodAction;RodTarget=rodTarget;RodBatch=rodBatch;RodRequestId=rodRequestId;Inventory=inventory;Revival=revival;RoomRules=roomRules;Treasure=treasure;
    }
    public short MoveX { get; }
    public short MoveZ { get; }
    public int AimX { get; }
    public int AimZ { get; }
    public bool Fire { get; }
    public bool Cancel { get; }
    public HookUpgrade Upgrade { get; }
    public uint PurchaseId { get; }
    public bool Shopping { get; }
    public TileKind Build { get; }
    public short BuildX { get; }
    public short BuildZ { get; }
    public uint BuildId { get; }
    public RodAction RodAction { get; }
    public int RodTarget { get; }
    public uint RodBatch { get; }
    public uint RodRequestId { get; }
    /// <summary>背包操作与购买／建造互斥；编号随输入重发，但只结算一次。</summary>
    public InventoryCommand Inventory { get; }
    public RevivalCommand Revival { get; }
    public RoomRulesCommand RoomRules { get; }
    public TreasureCommand Treasure { get; }
    public byte[] Encode()
    {
        var b = new byte[ByteCount];
        BinaryPrimitives.WriteInt16LittleEndian(b, MoveX); BinaryPrimitives.WriteInt16LittleEndian(b.AsSpan(2), MoveZ);
        BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(4), AimX); BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(8), AimZ);
        b[12] = (byte)((Fire ? 1 : 0) | (Cancel ? 2 : 0) | (Shopping ? 4 : 0));
        b[13]=(byte)Upgrade;
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(14),PurchaseId);
        b[18]=(byte)Build;BinaryPrimitives.WriteInt16LittleEndian(b.AsSpan(19),BuildX);
        BinaryPrimitives.WriteInt16LittleEndian(b.AsSpan(21),BuildZ);BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(23),BuildId);
        b[27]=(byte)RodAction;BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(28),RodTarget);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(32),RodBatch);BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(36),RodRequestId);
        Inventory.Encode().CopyTo(b,40);Revival.Encode().CopyTo(b,40+InventoryCommand.ByteCount);
        RoomRules.Encode().CopyTo(b,40+InventoryCommand.ByteCount+RevivalCommand.ByteCount);
        Treasure.Encode().CopyTo(b,ByteCount-TreasureCommand.ByteCount);
        Decode(b); return b;
    }
    public static HookInput Decode(ReadOnlySpan<byte> b)
    {
        if (b.Length != ByteCount || b[12] > 7 || b[13] > (byte)HookUpgrade.Speed || b[18]>(byte)TileKind.Fishbone)
            throw new InvalidDataException("Invalid hook input.");
        uint request=BinaryPrimitives.ReadUInt32LittleEndian(b.Slice(14));
        if((b[13]==0)!=(request==0))throw new InvalidDataException("Invalid purchase id.");
        short bx=BinaryPrimitives.ReadInt16LittleEndian(b.Slice(19)),bz=BinaryPrimitives.ReadInt16LittleEndian(b.Slice(21));
        uint buildId=BinaryPrimitives.ReadUInt32LittleEndian(b.Slice(23));
        if((b[18]==0)!=(buildId==0) || (b[18]==0 && (bx!=0 || bz!=0)))throw new InvalidDataException("Invalid build request.");
        short x = BinaryPrimitives.ReadInt16LittleEndian(b), z = BinaryPrimitives.ReadInt16LittleEndian(b.Slice(2));
        int ax = BinaryPrimitives.ReadInt32LittleEndian(b.Slice(4)), az = BinaryPrimitives.ReadInt32LittleEndian(b.Slice(8));
        if ((long)x*x + (long)z*z > 1000000 || Math.Abs((long)ax) > 100000 || Math.Abs((long)az) > 100000)
            throw new InvalidDataException("Input outside supported range.");
        var action=(RodAction)b[27];int target=BinaryPrimitives.ReadInt32LittleEndian(b.Slice(28));
        uint batch=BinaryPrimitives.ReadUInt32LittleEndian(b.Slice(32)),rodRequest=BinaryPrimitives.ReadUInt32LittleEndian(b.Slice(36));
        if(action>RodAction.Unequip || (action==RodAction.None)!=(rodRequest==0) ||
            (action==RodAction.None && (target!=0 || batch!=0)) || target<0 ||
            (action==RodAction.Unequip && target!=0) ||
            ((action==RodAction.Buy || action==RodAction.BuyAndEquip) && (batch==0 || target>=8)) ||
            ((action==RodAction.Equip || action==RodAction.Unequip) && batch!=0) ||
            (action==RodAction.Equip && target==0) || (action!=RodAction.None && (b[13]!=0 || b[18]!=0)))
            throw new InvalidDataException("Invalid rod command.");
        var inventory=InventoryCommand.Decode(b.Slice(40,InventoryCommand.ByteCount));
        var revival=RevivalCommand.Decode(b.Slice(40+InventoryCommand.ByteCount,RevivalCommand.ByteCount));
        var roomRules=RoomRulesCommand.Decode(b.Slice(40+InventoryCommand.ByteCount+RevivalCommand.ByteCount,RoomRulesCommand.ByteCount));
        var treasure=TreasureCommand.Decode(b.Slice(ByteCount-TreasureCommand.ByteCount));
        if(!treasure.IsEmpty && (!revival.IsEmpty || inventory.Action!=InventoryAction.None || action!=RodAction.None || b[13]!=0 || b[18]!=0))
            throw new InvalidDataException("Treasure cannot share another transaction.");
        if(!revival.IsEmpty && (inventory.Action!=InventoryAction.None || action!=RodAction.None || b[13]!=0 || b[18]!=0))
            throw new InvalidDataException("Revival cannot share an inventory transaction.");
        if(inventory.Action!=InventoryAction.None && (action!=RodAction.None || b[13]!=0 || b[18]!=0))
            throw new InvalidDataException("Multiple transaction families in one input.");
        return new HookInput(x,z,ax,az,(b[12]&1)!=0,(b[12]&2)!=0,(HookUpgrade)b[13],request,(b[12]&4)!=0,(TileKind)b[18],bx,bz,buildId,action,target,batch,rodRequest,inventory,revival,roomRules,treasure);
    }
}
