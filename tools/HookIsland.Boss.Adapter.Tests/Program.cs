using System;
using Advanture.HookIsland.Network;
using Advanture.HookIsland.Simulation;

// 覆盖容易在接入层丢失的新字段：请求经历多个输入组合器和编码后，仍必须保持原意。
var tests = new (string Name, Action Run)[]
{
    ("room_rules_preserve_vertical_shot_and_boss_request", () =>
    {
        var buffer = new RoomRulesRequestBuffer();
        Check(buffer.TryQueue(new RoomRulesState(true, 0), false), "queue rules");
        var result = Round(buffer.Compose(Input()));
        Preserve(result); Check(result.Fire && result.Reload && result.RoomRules.RequestId == 1, "shot/rule lost");
    }),
    ("construction_priority_defers_boss_without_losing_aim", () =>
    {
        var buffer = new PlatformBuildRequestBuffer();
        Check(buffer.TryQueue(0, TileKind.Wood, 2, 0), "queue build");
        var result = Round(buffer.Compose(Input(), true));
        Preserve(result, false); Check(result.BuildId == 1 && !result.Fire, "build priority broken");
    }),
    ("treasure_command_tail_does_not_overlap_new_input_fields", () =>
    {
        var buffer = new TreasureRequestBuffer(); Check(buffer.TryQueue(3, 2, 11), "queue treasure");
        var result = Round(buffer.Compose(Input()));
        Preserve(result, false); Check(result.Treasure.RequestId == 4 && result.Treasure.ChestId == 2, "treasure bytes overwritten");
    }),
    ("equipment_command_preserves_new_fields", () =>
    {
        var buffer = new RodRequestBuffer(); Check(buffer.TryQueue(3, RodAction.Equip, 8, 0), "queue rod");
        var result = Round(buffer.Compose(Input())); Preserve(result, false);
        Check(result.RodTarget == 8 && result.RodRequestId == 4, "rod request changed");
    }),
    ("boss_request_retries_same_id_until_success_or_failure_receipt", () =>
    {
        var buffer = new BossRequestBuffer(); Check(buffer.TryQueue(41), "queue boss");
        var build = new HookInput(0,0,build:TileKind.Wood,buildX:2,buildId:1,aimVertical:420);
        Check(Round(buffer.Compose(build)).BossRequest == 0 && buffer.Pending, "transaction must defer rather than replace pending request");
        for (int i = 0; i < 30; i++)
        {
            var result = Round(buffer.Compose(Input()));
            Check(result.BossRequest == 42 && result.AimVertical == 420 && result.Fire && result.Reload, "retry mutated input");
        }
        Check(!buffer.Observe(new BossRequestReceipt(41, BossRequestResult.Success), out _), "stale receipt consumed request");
        Check(!buffer.TryQueue(41), "duplicate click replaced pending id");
        Check(buffer.Observe(new BossRequestReceipt(42, BossRequestResult.TooFar), out var resultCode) && resultCode == BossRequestResult.TooFar,
            "failure must settle rather than silently retry");
        Check(!buffer.Pending && buffer.Compose(default).BossRequest == 0, "failed request replayed");
        Check(buffer.TryQueue(42) && buffer.Request == 43, "explicit next click needs new id");
        buffer.Reset(); Check(!buffer.Pending && !buffer.TryQueue(uint.MaxValue), "reset/wrap failed");
    }),
    ("3d_hit_event_survives_delivery", () =>
    {
        var hit = new HookGameplayEvent(HookGameplayEventKind.FirearmShot, 0, amount: 53, targetId: -7,
            x: 0, z: 26000, itemKind: ItemKind.Firearm, itemType: 1, originX: 0, originZ: 16000, y: 6120, originY: 1480);
        var copy = HookGameplayEventCodec.Decode(HookGameplayEventCodec.Encode(hit));
        Check(copy.Y == 6120 && copy.OriginY == 1480 && copy.Amount == 53 && copy.TargetId == -7, "3D event flattened");
    })
};
int failures = 0;
foreach (var test in tests)
{
    try { test.Run(); Console.WriteLine("PASS " + test.Name); }
    catch (Exception e) { failures++; Console.Error.WriteLine("FAIL " + test.Name + ": " + e); }
}
Console.WriteLine($"Boss adapter tests: {tests.Length - failures}/{tests.Length} passed");
return failures == 0 ? 0 : 1;

static HookInput Input() => new HookInput(400, 300, 15000, 26000, fire: true, reload: true, aimVertical: 420, bossRequest: 9);
static HookInput Round(HookInput input) => HookInput.Decode(input.Encode());
static void Preserve(HookInput input, bool boss = true) => Check(input.AimVertical == 420 && input.BossRequest == (boss ? 9u : 0u) && input.MoveX == 400 && input.MoveZ == 300,
    "input composition lost aim, request or movement");
static void Check(bool success, string message) { if (!success) throw new Exception(message); }
