namespace Advanture.HookIsland.Simulation;

public sealed partial class InventorySystem
{
    /// <summary>只允许消费本人当前装备的那一颗；调用方完成所有投掷校验后，同帧生成投掷物。</summary>
    internal bool TryConsumeGrenade(int owner,int itemId)
    {
        if(!TryOwned(owner,itemId,out var item) || activeItems[owner]!=itemId || item.Location!=ItemLocation.Equipped || item.Kind!=ItemKind.Throwable)return false;
        var draft=Draft();draft.Items.RemoveAll(i=>i.Id==itemId);Commit(draft);return true;
    }
    /// <summary>优先回到出手前的武器；该物品已失效时由库存既有回退规则选择。</summary>
    internal void ReturnFromGrenade(int owner,int previousItemId)
    {
        if(TryOwned(owner,previousItemId,out var item) && item.Location==ItemLocation.Equipped && WeaponLoadout.IsHandSlot(item.Definition.Slot) && item.Kind!=ItemKind.Throwable)
            activeItems[owner]=item.Id;
        else activeItems[owner]=WeaponLoadout.Fallback(items,owner);
    }
}
