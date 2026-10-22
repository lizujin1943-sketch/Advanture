using Advanture.HookIsland.Simulation;
using UnityEngine;

namespace Advanture.HookIsland
{
    public readonly struct EquipmentSlotVisual
    {
        public EquipmentSlotVisual(EquipmentSlot slot, Transform root) { Slot = slot; Root = root; }
        public EquipmentSlot Slot { get; }
        public Transform Root { get; }
    }
    /// <summary>Confirmed equipment appearance only; never reads UI selection or writes simulation state.</summary>
    public sealed class CharacterEquipmentAppearance
    {
        private readonly Transform rod, backpack;
        private readonly Renderer shaft, bag;
        private readonly Material[] tiers;
        private readonly EquipmentSlotVisual[] slotVisuals;
        private int shownRod = -1, shownBackpack = -1;
        public CharacterEquipmentAppearance(Transform rod, Renderer shaft, Transform backpack, Material[] tiers, params EquipmentSlotVisual[] slotVisuals)
        { this.rod = rod; this.shaft = shaft; this.backpack = backpack; bag = backpack.GetComponent<Renderer>(); this.tiers = tiers; this.slotVisuals = slotVisuals; }
        public void Present(InventoryState inventory, int owner)
        {
            foreach (var visual in slotVisuals) visual.Root.gameObject.SetActive(inventory.Equipped(owner, visual.Slot).HasValue);
            var equippedRod = inventory.Equipped(owner, EquipmentSlot.RightHand);
            int rodType = equippedRod.HasValue ? equippedRod.Value.Type : -1;
            if (rodType != shownRod)
            {
                shownRod = rodType; rod.gameObject.SetActive(rodType >= 0);
                if (rodType >= 0) shaft.sharedMaterial = tiers[RodCatalog.Get(rodType).Tier];
            }
            // Active state is also applied on first presentation of an empty slot.
            rod.gameObject.SetActive(equippedRod.HasValue);
            var equippedBag = inventory.Equipped(owner, EquipmentSlot.Backpack);
            int bagType = equippedBag.HasValue ? equippedBag.Value.Type : -1;
            backpack.gameObject.SetActive(equippedBag.HasValue);
            if (bagType != shownBackpack)
            {
                shownBackpack = bagType;
                if (bagType >= 0) bag.sharedMaterial = tiers[(InventoryCatalog.Backpack(bagType).Level - 1) / 2];
            }
        }
    }
}
