using System;
using System.Collections.Generic;
using System.Linq;
using Advanture.HookIsland.Simulation;
using Advanture.HookIsland.Network;

namespace Advanture.HookIsland.InventoryTests;
internal static partial class Program
{
    private static IEnumerable<(string, Action)> CommandCases()
    {
        yield return ("inventory_retry_survives_open_build_mode_and_resumes_after_build_receipt", () => {
            var build = new PlatformBuildRequestBuffer();
            var action = new InventoryCommand(InventoryAction.Drop, 7, amount: 2, requestId: 3);
            var input = new HookInput(0, 0, 0, 0, fire: true, cancel: true, inventory: action);
            var quiet = build.Compose(input, true);
            Require(quiet.Inventory.Encode().SequenceEqual(action.Encode()) && !quiet.Fire && !quiet.Cancel, "Open build mode swallowed inventory retry or left fishing active.");
            Require(build.TryQueue(0, TileKind.Wood, 2, 0), "Cannot queue build.");
            Require(build.Compose(input, true).Inventory.Action == InventoryAction.None, "Two transactions entered one frame.");
            Require(build.Observe(new Fisher { LastBuildId = 1, LastBuildResult = BuildResult.Success }, out _), "Build receipt missing.");
            Require(build.Compose(input, true).Inventory.Encode().SequenceEqual(action.Encode()), "Inventory retry did not resume after build acknowledgement.");
        });
        yield return ("inventory_command_encodes_exact_intent_and_rejects_noncanonical_payloads", () => {
            var command = new InventoryCommand(InventoryAction.Move, 41, x: 3, y: 8, rotated: true, requestId: 7);
            var decoded = InventoryCommand.Decode(command.Encode());
            Require(decoded.Target == 41 && decoded.X == 3 && decoded.Y == 8 && decoded.Rotated && decoded.RequestId == 7, "Encoded move lost intent.");
            bool Bad(byte[] b) { try { InventoryCommand.Decode(b); return false; } catch (System.IO.InvalidDataException) { return true; } }
            var bytes = command.Encode(); bytes[1] = 128; Require(Bad(bytes), "Unknown flags accepted.");
            bytes = command.Encode(); bytes[14] = 255; Require(Bad(bytes), "Out-of-range cell accepted.");
            bytes = new InventoryCommand(InventoryAction.Split, 41, amount: 7, requestId: 8).Encode(); bytes[10] = 255;
            Require(Bad(bytes), "Invalid split quantity accepted.");
            Require(Bad(command.Encode().Concat(new byte[] { 1 }).ToArray()), "Trailing command bytes accepted.");
        });
        yield return ("inventory_request_retries_same_id_until_failed_or_successful_receipt", () => {
            var buffer = new InventoryRequestBuffer();
            Require(buffer.TryQueue(9, new InventoryCommand(InventoryAction.Drop, 7, amount: 4)), "Cannot queue drop intent.");
            var frozen = buffer.Command.Encode();
            Require(buffer.Pending && buffer.Command.RequestId == 10, "Queue did not use next authoritative request ID.");
            Require(!buffer.TryQueue(9, new InventoryCommand(InventoryAction.Pickup, 4)), "New click replaced pending request.");
            Require(!buffer.Observe(9, InventoryResult.Success, out _) && frozen.SequenceEqual(buffer.Command.Encode()), "Old receipt consumed pending request.");
            Require(buffer.Observe(10, InventoryResult.NoSpace, out var result) && result == InventoryResult.NoSpace && !buffer.Pending, "Failure did not terminate retries.");
            Require(buffer.TryQueue(10, new InventoryCommand(InventoryAction.Pickup, 4)) && buffer.Command.RequestId == 11, "Next click reused completed request ID.");
            buffer.Reset(); Require(!buffer.Pending, "Disconnect did not discard stale session request.");
            Require(!buffer.TryQueue(uint.MaxValue, new InventoryCommand(InventoryAction.Pickup, 4)), "Request ID wrapped.");
        });
    }
}
