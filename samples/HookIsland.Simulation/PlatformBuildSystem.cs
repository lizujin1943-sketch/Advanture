using System.Collections.Generic;

namespace Advanture.HookIsland.Simulation;

/// <summary>Authoritative construction transactions, resolved after movement in stable slot order.</summary>
internal static class PlatformBuildSystem
{
    internal static void Resolve(IslandMap map, Fisher[] players, List<PlatformTile> tiles,
        IReadOnlyList<Fish> fish, IReadOnlyList<Driftwood> driftwood, IReadOnlyList<HookInput> commands, InventorySystem? inventory = null,IReadOnlyList<TreasureChest>? treasure=null,FinaleState? finale=null)
    {
        for (int i = 0; i < players.Length; i++)
        {
            var command = commands[i];
            var player = players[i];
            if (inventory != null) PersonalMaterialInventory.Refresh(ref player, inventory);
            if (command.Build == TileKind.None || command.BuildId <= player.LastBuildId) continue;

            // Failed receipts also consume their ID: retries cannot change their position or price.
            player.LastBuildId = command.BuildId;
            player.LastBuildResult = (command.Cancel || map.Fishing!=null && player.Vitals.Downed) ? BuildResult.Busy : PlatformRules.Check(
                map.Platform, tiles, players, fish, driftwood, player, command.Build, command.BuildX, command.BuildZ,treasure,map.Fishing?.Finale,finale);
            if (player.LastBuildResult == BuildResult.Success)
            {
                int cost = map.Platform!.Cost(command.Build);
                if (PersonalMaterialInventory.TrySpend(ref player, command.Build, cost, inventory))
                    tiles.Add(new PlatformTile(command.BuildX, command.BuildZ, command.Build, player.Slot, cost));
                else
                    player.LastBuildResult = BuildResult.InsufficientMaterials;
            }
            players[i] = player;
        }
    }
}
