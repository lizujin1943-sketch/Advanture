using Advanture.HookIsland.Simulation;

namespace Advanture.HookIsland
{
    /// <summary>Read-only counterpart of tool selection: selecting food or an empty slot hides the stowed right-hand tool.</summary>
    public static class ActiveToolView
    {
        public static bool RightHand(HookState state,Fisher player)
        {
            if(state.Tackle==null || player.SelectedQuickSlot<0)return true;
            var item=QuickbarRules.Resolve(state.Tackle.Inventory,player.Slot,player.SelectedQuickSlot);
            return item.HasValue && item.Value.Location==ItemLocation.Equipped && item.Value.Id==state.Tackle.Inventory.ActiveItem(player.Slot)?.Id;
        }
        public static bool UsesFirstPerson(HookState state,Fisher player)=>RightHand(state,player) &&
            (FirearmRules.Equipped(state,player)!=null || MeleeRules.Equipped(state,player)!=null);
        public static FirearmDefinition Firearm(HookState state,Fisher player)=>RightHand(state,player)?FirearmRules.Equipped(state,player):null;
    }
}
