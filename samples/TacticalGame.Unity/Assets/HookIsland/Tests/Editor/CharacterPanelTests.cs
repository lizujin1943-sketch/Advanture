using System.Collections;
using System.Collections.Generic;
using Advanture.HookIsland.Simulation;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace Advanture.HookIsland.Tests
{
    public sealed class CharacterPanelTests
    {
        [UnityTest] public IEnumerator ApparelDragsToItsSlotAndBackWithoutMutatingAuthority()
        {
            var targets = new[] { new Vector2(76, 88), new Vector2(328, 165), new Vector2(76, 242) };
            for (int type = 0; type < targets.Length; type++)
            {
                var window = ScriptableObject.CreateInstance<CharacterPanelTestWindow>();
                try
                {
                    int coins = 1000;
                    Assert.That(window.Inventory.Purchase(0, ItemKind.Apparel, type, false, false, new Point(0, 0), ref coins), Is.EqualTo(InventoryResult.Success));
                    var item = window.Inventory.Capture().Items[2];
                    window.position = new Rect(40, 80, 1080, 490); window.ShowUtility(); window.Focus();
                    yield return null; yield return null;
                    var storage = new Vector2(860, 75); var slot = targets[type];
                    window.SendEvent(new Event { type = EventType.MouseDown, button = 0, mousePosition = storage }); yield return null;
                    window.SendEvent(new Event { type = EventType.MouseDrag, button = 0, mousePosition = slot, delta = slot - storage }); yield return null;
                    window.SendEvent(new Event { type = EventType.MouseUp, button = 0, mousePosition = slot }); yield return null;
                    Assert.That(window.Commands.Count, Is.EqualTo(1)); Assert.That(window.Commands[0].Action, Is.EqualTo(InventoryAction.Equip));
                    Assert.That(window.Commands[0].Target, Is.EqualTo(item.Id));
                    Assert.That(window.Inventory.Capture().Equipped(0, item.Definition.Slot), Is.Null, "Panel changed authority before a room receipt.");
                    window.Inventory.Equip(0, item.Id, false, new Point(0, 0)); window.Commands.Clear(); window.Repaint(); yield return null;
                    window.SendEvent(new Event { type = EventType.MouseDown, button = 0, mousePosition = slot }); yield return null;
                    window.SendEvent(new Event { type = EventType.MouseDrag, button = 0, mousePosition = storage, delta = storage - slot }); yield return null;
                    window.SendEvent(new Event { type = EventType.MouseUp, button = 0, mousePosition = storage }); yield return null;
                    Assert.That(window.Commands.Count, Is.EqualTo(1)); Assert.That(window.Commands[0].Action, Is.EqualTo(InventoryAction.Unequip));
                    Assert.That(window.Commands[0].Target, Is.EqualTo((int)item.Definition.Slot));
                }
                finally { window.Close(); }
            }
        }

        [UnityTest] public IEnumerator DraggingBetweenEquipmentAndStorageSubmitsOneIntent()
        {
            var window = ScriptableObject.CreateInstance<CharacterPanelTestWindow>();
            try
            {
                window.position = new Rect(40, 80, 1080, 490);
                window.ShowUtility();
                window.Focus();
                yield return null;
                yield return null;
                var slot = new Vector2(76, 165);
                var storage = new Vector2(860, 75);
                window.SendEvent(new Event { type = EventType.MouseDown, button = 0, mousePosition = slot });
                yield return null;
                window.SendEvent(new Event { type = EventType.MouseDrag, button = 0, mousePosition = storage, delta = storage - slot });
                yield return null;
                window.SendEvent(new Event { type = EventType.MouseUp, button = 0, mousePosition = storage });
                yield return null;
                Assert.That(window.Commands.Count, Is.EqualTo(1), string.Join("\n", window.Trace));
                Assert.That(window.Commands[0].Action, Is.EqualTo(InventoryAction.Unequip));
                Assert.That(window.Commands[0].Target, Is.EqualTo((int)EquipmentSlot.RightHand));

                window.Inventory.Unequip(0, EquipmentSlot.RightHand, new Point(0, 0));
                window.Commands.Clear(); window.Trace.Clear();
                window.Repaint(); yield return null;
                window.SendEvent(new Event { type = EventType.MouseDown, button = 0, mousePosition = storage });
                yield return null;
                window.SendEvent(new Event { type = EventType.MouseDrag, button = 0, mousePosition = slot, delta = slot - storage });
                yield return null;
                window.SendEvent(new Event { type = EventType.MouseUp, button = 0, mousePosition = slot });
                yield return null;
                Assert.That(window.Commands.Count, Is.EqualTo(1), string.Join("\n", window.Trace));
                Assert.That(window.Commands[0].Action, Is.EqualTo(InventoryAction.Equip));
                Assert.That(window.Commands[0].Target, Is.EqualTo(1));
            }
            finally { window.Close(); }
        }
    }

    // Editor-only harness: exercises real IMGUI controls without adding a scene component.
    internal sealed class CharacterPanelTestWindow : EditorWindow
    {
        public readonly InventorySystem Inventory = new InventorySystem(1);
        public readonly List<InventoryCommand> Commands = new List<InventoryCommand>();
        public readonly List<string> Trace = new List<string>();
        private CharacterPanel panel;
        private void OnGUI()
        {
            if (panel == null) panel = new CharacterPanel(null);
            var e = Event.current;
            bool mouse = e.isMouse;
            if (mouse) Trace.Add("before " + e.type + "/" + e.rawType + " " + e.mousePosition);
            var player = new Fisher { Slot = 0, PulledBy = -1, Stamina = 120 * StaminaSystem.UnitsPerPoint };
            panel.Draw(new Rect(10, 10, 1052, 454), new CharacterConfig(), 100, Inventory.Capture(),
                player, new CharacterVitals { Health = 100, Hunger = 100 }, false, Commands.Add);
            if (mouse) Trace.Add("after " + e.type + "/" + e.rawType + " " + e.mousePosition);
        }
    }
}
