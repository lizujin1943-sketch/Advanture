#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.IO;
using System.Linq;
using Advanture.HookIsland.Simulation;
using FrameSync.Runtime;
using FrameSync.Sessions;
using UnityEngine;

namespace Advanture.HookIsland
{
    /// <summary>显式开发验收：真实输入挣钱、背包操作、角色装卸；不注入资源，也不改模拟状态。</summary>
    public sealed class InventorySmokeDriver : MonoBehaviour
    {
        private IslandFishingController room;
        private readonly InventorySmokePilot pilot = new InventorySmokePilot();
        private string report;
        private float started, finished, nextProgress, completeSince;
        private bool written, closing, shopCaptured, bagCaptured, captured, panelsChecked, characterChecked;
        private int characterStage, savedRod;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Attach()
        {
            if (IslandFishingController.LaunchValue("-island-inventory-smoke") != "1") return;
            UnityEngine.Object.FindObjectOfType<IslandFishingController>().gameObject.AddComponent<InventorySmokeDriver>();
        }
        private void Start()
        {
            room = GetComponent<IslandFishingController>(); report = IslandFishingController.LaunchValue("-island-report");
            started = Time.realtimeSinceStartup; room.InputProvider = Sample;
        }
        private HookInput Sample(ulong tick)
        {
            if (room.State == null) return default;
            if (pilot.Complete(room.State)) { CheckCharacterEquipment(); return default; }
            var command = pilot.Sample(room.Map, room.State, room.LocalSlot, tick);
            if (command.Inventory.Action != InventoryAction.None)
            {
                if (!panelsChecked) CheckPanelTransitions();
                if (command.Inventory.Action == InventoryAction.BuyAndEquip)
                {
                    room.OpenShop();
                    if (!shopCaptured) { ScreenCapture.CaptureScreenshot(report + ".shop.png"); shopCaptured = true; }
                }
                else
                {
                    room.CloseShop(); if (!room.Equipment.InventoryOpen) room.Equipment.ToggleInventory();
                    GetComponent<FishingEquipmentOverlay>().SelectCharacterPage(false);
                    if (!bagCaptured) { ScreenCapture.CaptureScreenshot(report + ".bag.png"); bagCaptured = true; }
                }
                room.Equipment.RequestInventory(command.Inventory); return default;
            }
            if (command.Build != TileKind.None)
            {
                if (!room.Construction.Open) room.Construction.Toggle();
                room.Construction.RequestPlacement(command.Build, command.BuildX, command.BuildZ); return default;
            }
            if (!room.Equipment.Pending) { room.CloseShop(); room.Equipment.Close(); }
            return command;
        }
        private void CheckPanelTransitions()
        {
            room.Equipment.ToggleInventory();
            if (!room.Equipment.InventoryOpen) throw new InvalidOperationException("Backpack did not open.");
            room.Construction.Toggle();
            if (!room.Construction.Open || room.Equipment.InventoryOpen) throw new InvalidOperationException("Construction did not close inventory.");
            room.Equipment.ToggleInventory();
            if (!room.Equipment.InventoryOpen || room.Construction.Open) throw new InvalidOperationException("Inventory did not close construction.");
            room.OpenShop();
            if (!room.ShopOpen || room.Equipment.InventoryOpen || room.Construction.Open) throw new InvalidOperationException("Panels are not mutually exclusive.");
            panelsChecked = true;
        }
        private void CheckCharacterEquipment()
        {
            var state = room.State; var player = state.Players[room.LocalSlot];
            if (characterStage == 0)
            {
                savedRod = player.EquippedRod;
                if (room.Equipment.RequestInventory(new InventoryCommand(InventoryAction.Unequip, (int)EquipmentSlot.RightHand))) characterStage = 1;
            }
            else if (characterStage == 1 && !room.Equipment.Pending && player.EquippedRod == 0)
            {
                if (FishingRules.MaxStamina(state, player) != state.Tackle.Config.StaminaBase * 30000) throw new InvalidOperationException("Unequipping did not remove rod stamina bonus.");
                if (room.Equipment.RequestInventory(new InventoryCommand(InventoryAction.Equip, savedRod))) characterStage = 2;
            }
            else if (characterStage == 2 && !room.Equipment.Pending && player.EquippedRod == savedRod)
            {
                if (player.Stamina >= FishingRules.MaxStamina(state, player)) throw new InvalidOperationException("Equipping refilled stamina.");
                characterStage = 3; characterChecked = true;
            }
        }
        private static bool CharactersComplete(HookState state) => state.Players.All(p => p.LastInventoryRequest >= (p.Slot == 0 ? 7u : 6u) && p.EquippedRod > 0);
        private void LateUpdate()
        {
            if (written) { if (Time.realtimeSinceStartup - finished > 2) Application.Quit(); return; }
            var session = room.Client?.Session;
            if (session?.LocalParticipant != null)
            {
                var state = session.InspectState();
                if (Time.realtimeSinceStartup >= nextProgress)
                { nextProgress = Time.realtimeSinceStartup + 8; Write(report + ".progress.json", session.CaptureCheckpoint(), room.LocalSlot, false); }
                bool complete = pilot.Complete(state) && CharactersComplete(state);
                if (complete && !captured)
                {
                    room.CloseShop(); if (!room.Equipment.InventoryOpen) room.Equipment.ToggleInventory();
                    GetComponent<FishingEquipmentOverlay>().SelectCharacterPage(true);
                    ScreenCapture.CaptureScreenshot(report + ".character.png"); captured = true; completeSince = Time.realtimeSinceStartup;
                }
                if (complete && !closing && room.Host?.Session != null && Time.realtimeSinceStartup - completeSince > 1.5f)
                { room.Host.Session.SaveAndClose(c => Write(report + ".server.json", c, -1, true)); closing = true; }
                if (session.Status == SessionStatus.Closed)
                { Write(report, session.CaptureCheckpoint(), room.LocalSlot, true); written = true; finished = Time.realtimeSinceStartup; room.InputProvider = null; return; }
            }
            if (room.Failure != null || Time.realtimeSinceStartup - started > 180)
            {
                if (session?.LocalParticipant != null) Write(report + ".state.json", session.CaptureCheckpoint(), room.LocalSlot, false);
                File.WriteAllText(report, JsonUtility.ToJson(new Report { error = room.Failure ?? "Inventory acceptance timeout" }, true));
                Debug.LogError("INVENTORY_SMOKE_FAILED " + room.Failure); Application.Quit(1);
            }
        }
        private void Write(string path, Checkpoint checkpoint, int slot, bool validate)
        {
            var state = new HookGame().Decode(checkpoint.ApplicationState.Span);
            var result = new Report { slot = slot, tick = checkpoint.Tick.ToString(), hash = CheckpointCodec.Hash(checkpoint), coins = state.Coins,
                complete = pilot.Complete(state) && CharactersComplete(state), panelsChecked = panelsChecked, characterChecked = characterChecked,
                renderedPlayers = room.Presentation.RenderedPlayers, tiles = state.Tiles.Count, ground = state.Tackle.Inventory.Items.Count(i => i.Location == ItemLocation.Ground),
                players = state.Players.Select(p => new Player { slot = p.Slot, coins = p.Coins, earned = p.CoinsEarned, spent = state.Tackle.Inventory.SpentCoins[p.Slot],
                    bones = p.Fishbones, bonesEarned = p.FishbonesEarned, catches = p.Catches, receipt = p.LastInventoryRequest, result = p.LastInventoryResult.ToString() }).ToArray() };
            if (validate && !result.complete) result.error = "Backpack/character flow incomplete.";
            Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllText(path, JsonUtility.ToJson(result, true));
            if (validate) File.WriteAllBytes(path + ".checkpoint", CheckpointCodec.Encode(checkpoint));
        }
        [Serializable] private sealed class Report
        { public string error, tick, hash; public int slot, coins, renderedPlayers, tiles, ground; public bool complete, panelsChecked, characterChecked; public Player[] players; }
        [Serializable] private sealed class Player
        { public int slot, coins, earned, spent, bones, bonesEarned, catches; public uint receipt; public string result; }
    }
}
#endif
