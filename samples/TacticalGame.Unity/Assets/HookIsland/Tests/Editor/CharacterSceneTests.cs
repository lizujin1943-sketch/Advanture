using System.Linq;
using Advanture.HookIsland.Simulation;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Advanture.HookIsland.Tests
{
    public sealed class CharacterSceneTests
    {
        [Test] public void ApparelVisualsFollowConfirmedOwnerAndSlot()
        {
            var root = new GameObject("Apparel view test");
            var rod = GameObject.CreatePrimitive(PrimitiveType.Cube); rod.transform.SetParent(root.transform);
            var bag = GameObject.CreatePrimitive(PrimitiveType.Cube); bag.transform.SetParent(root.transform);
            var shader = Shader.Find("Hook Island/Baked Color Surface");
            var material = new Material(shader);
            var slots = new[] { EquipmentSlot.Hat, EquipmentSlot.Clothes, EquipmentSlot.Shoes };
            var visuals = slots.Select(slot => { var go = new GameObject(slot.ToString()); go.transform.SetParent(root.transform); return new EquipmentSlotVisual(slot, go.transform); }).ToArray();
            try
            {
                var view = new CharacterEquipmentAppearance(rod.transform, rod.GetComponent<Renderer>(), bag.transform, new[] { material }, visuals);
                var inventory = new InventorySystem(2); int coins = 1000;
                for (int type = 0; type < ApparelCatalog.All.Count; type++)
                    Assert.That(inventory.Purchase(0, ItemKind.Apparel, type, true, false, new Point(0, 0), ref coins), Is.EqualTo(InventoryResult.Success));
                byte[] before = InventoryStateCodec.Encode(inventory.Capture());
                view.Present(inventory.Capture(), 1); Assert.That(visuals.All(v => !v.Root.gameObject.activeSelf), Is.True, "Another player's clothing leaked into this avatar.");
                view.Present(inventory.Capture(), 0); Assert.That(visuals.All(v => v.Root.gameObject.activeSelf), Is.True);
                Assert.That(before.SequenceEqual(InventoryStateCodec.Encode(inventory.Capture())), Is.True);
                foreach (var slot in slots) Assert.That(inventory.Unequip(0, slot, new Point(0, 0)), Is.EqualTo(InventoryResult.Success));
                view.Present(inventory.Capture(), 0); Assert.That(visuals.All(v => !v.Root.gameObject.activeSelf), Is.True);
            }
            finally { Object.DestroyImmediate(root); Object.DestroyImmediate(material); }
        }

        [Test] public void PackagedMapUsesAuthoredCharacterConfiguration()
        {
            var settings=AssetDatabase.LoadAssetAtPath<PlatformSettings>("Assets/HookIsland/Data/PlatformSettings.asset");
            Assert.That(settings.fishing.character,Is.Not.Null,"Character base attributes must be authored in the scene configuration.");
            var character=settings.fishing.character.CreateConfig();
            Assert.That(character.Health,Is.EqualTo(100));Assert.That(character.MoveSpeed,Is.EqualTo(3000));
            Assert.That(character.Armor,Is.Zero);Assert.That(character.Hunger,Is.EqualTo(100));
            var map=settings.CreateMap();
            var saved=AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/HookIsland/Data/PlatformMap.bytes");
            Assert.That(saved.bytes.SequenceEqual(map.Encode()),Is.True,"Packaged room must include current character configuration.");
            var inventory=new InventorySystem(1).Capture();
            var stats=CharacterEquipment.Calculate(map.Fishing.Character,map.Fishing.StaminaBase,inventory,0);
            Assert.That(stats.StaminaMax,Is.EqualTo(120*StaminaSystem.UnitsPerPoint));
        }

        [Test] public void ChangingAuthoredAttributesChangesRoomCompatibility()
        {
            var character=ScriptableObject.CreateInstance<CharacterSettings>();
            var fishing=ScriptableObject.CreateInstance<FishingSettings>();
            try
            {
                fishing.character=character;var initial=fishing.CreateConfig().Encode();
                character.landMoveMetresPerSecond=3.125f;character.health=130;
                var config=fishing.CreateConfig();
                Assert.That(config.Character.MoveSpeed,Is.EqualTo(3125));Assert.That(config.Character.Health,Is.EqualTo(130));
                Assert.That(config.Encode().SequenceEqual(initial),Is.False,"Different character rules cannot share a room.");
            }
            finally{Object.DestroyImmediate(fishing);Object.DestroyImmediate(character);}
        }

        [Test] public void AppearanceFollowsConfirmedSlotsWithoutChangingInventory()
        {
            var rod=GameObject.CreatePrimitive(PrimitiveType.Cube);
            var bag=GameObject.CreatePrimitive(PrimitiveType.Cube);
            var shader=Shader.Find("Hook Island/Baked Color Surface");
            Assert.That(shader,Is.Not.Null);
            var materials=Enumerable.Range(0,4).Select(i=>new Material(shader)).ToArray();
            try
            {
                var view=new CharacterEquipmentAppearance(rod.transform,rod.GetComponent<Renderer>(),bag.transform,materials);
                var inventory=new InventorySystem(1);var initial=inventory.Capture();
                var before=InventoryStateCodec.Encode(initial);
                view.Present(initial,0);
                Assert.That(rod.activeSelf,Is.True);Assert.That(bag.activeSelf,Is.True);
                Assert.That(rod.GetComponent<Renderer>().sharedMaterial,Is.SameAs(materials[0]));
                Assert.That(before.SequenceEqual(InventoryStateCodec.Encode(inventory.Capture())),Is.True,"Appearance modified the authoritative inventory.");

                Assert.That(inventory.Unequip(0,EquipmentSlot.RightHand,new Point(0,0)),Is.EqualTo(InventoryResult.Success));
                view.Present(inventory.Capture(),0);
                Assert.That(rod.activeSelf,Is.False);Assert.That(bag.activeSelf,Is.True);
                Assert.That(inventory.Equip(0,1,false,new Point(0,0)),Is.EqualTo(InventoryResult.Success));
                view.Present(inventory.Capture(),0);Assert.That(rod.activeSelf,Is.True);

                int coins=10000;
                Assert.That(inventory.Purchase(0,ItemKind.Rod,3,true,false,new Point(0,0),ref coins),Is.EqualTo(InventoryResult.Success));
                view.Present(inventory.Capture(),0);
                Assert.That(rod.GetComponent<Renderer>().sharedMaterial,Is.SameAs(materials[RodCatalog.Get(3).Tier]));

                // Use an empty bag fixture to test its explicit removal to the ground.
                var emptyBag=new InventorySystem(1);
                Assert.That(emptyBag.Unequip(0,EquipmentSlot.Backpack,new Point(0,0)),Is.EqualTo(InventoryResult.Success));
                view.Present(emptyBag.Capture(),0);Assert.That(bag.activeSelf,Is.False);Assert.That(rod.activeSelf,Is.True);
                var ground=emptyBag.Capture().Items.Single(i=>i.Kind==ItemKind.Backpack);
                Assert.That(emptyBag.PickupAndEquip(0,ground.Id,new Point(0,0),false),Is.EqualTo(InventoryResult.Success));
                view.Present(emptyBag.Capture(),0);Assert.That(bag.activeSelf,Is.True);
            }
            finally
            {
                Object.DestroyImmediate(rod);Object.DestroyImmediate(bag);
                foreach(var material in materials)Object.DestroyImmediate(material);
            }
        }
    }
}
