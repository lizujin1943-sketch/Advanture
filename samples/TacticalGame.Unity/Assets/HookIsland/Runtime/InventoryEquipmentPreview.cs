#nullable enable
using System.Linq;
using Advanture.HookIsland.Simulation;

namespace Advanture.HookIsland
{
    /// <summary>缓存只读换装预览；确认帧不断更新时，内容和选择未变就不重复执行矩形搜索。</summary>
    internal sealed class InventoryEquipmentPreview
    {
        internal sealed class Preview
        {
            internal readonly InventoryResult Result;
            internal readonly InventoryState? State;
            internal Preview(InventoryResult result, InventoryState? state) { Result = result; State = state; }
        }
        private InventoryState? previous;
        private Preview? cached;
        private (int, int, int, int, ItemKind, int, int, bool, bool) key;
        internal Preview Evaluate(InventoryState state, int owner, int coins, Point position,
            ItemKind kind, int type, int id, bool buying, bool dropOld)
        {
            var nextKey = (owner, coins, position.X, position.Z, kind, type, id, buying, dropOld);
            if (cached != null && previous != null && key.Equals(nextKey) && previous.PlayerCount == state.PlayerCount &&
                previous.NextId == state.NextId && previous.SpentCoins.SequenceEqual(state.SpentCoins) && previous.Items.SequenceEqual(state.Items)) return cached;
            previous = state; key = nextKey;
            var copy = new InventorySystem(state);
            var result = buying ? copy.Purchase(owner, kind, type, true, dropOld, position, ref coins) : copy.Equip(owner, id, dropOld, position);
            cached = new Preview(result, result == InventoryResult.Success ? copy.Capture() : null);
            return cached;
        }
    }
}
