using System.Collections.Generic;
using System.Linq;
using Advanture.HookIsland.Simulation;

namespace Advanture.HookIsland
{
    /// <summary>Shared read-only wording for character and merchant apparel details.</summary>
    internal static class ApparelDisplay
    {
        public static string Bonus(CharacterModifier value)
        {
            var parts = new List<string>();
            if (value.Health != 0) parts.Add($"生命上限 +{value.Health}");
            if (value.MoveSpeed != 0) parts.Add($"岸上移速 +{value.MoveSpeed / 1000f:0.###} 米/秒");
            if (value.Armor != 0) parts.Add($"防御 +{value.Armor} · 持续生效");
            return string.Join(" · ", parts);
        }
        public static string Comparison(CharacterConfig config, int staminaBase, InventoryState inventory, int owner, InventoryDefinition item)
        {
            var current = CharacterEquipment.Calculate(config, staminaBase, inventory, owner);
            var next = CharacterStats.Calculate(config, staminaBase, inventory.Items.Where(i => i.Owner == owner &&
                i.Location == ItemLocation.Equipped && i.Definition.Slot != item.Slot).Select(i => i.Definition.Modifiers).Append(item.Modifiers));
            var parts = new List<string>();
            if (next.HealthMax != current.HealthMax) parts.Add($"生命上限 {current.HealthMax} → {next.HealthMax} · 换装不回血");
            if (next.MoveSpeed != current.MoveSpeed) parts.Add($"岸上移速 {current.MoveSpeed / 1000f:0.###} → {next.MoveSpeed / 1000f:0.###} 米/秒");
            if (next.Armor != current.Armor) parts.Add($"防御 {current.Armor} → {next.Armor}");
            return parts.Count == 0 ? "与当前装备加成相同" : string.Join(" · ", parts);
        }
    }
}
