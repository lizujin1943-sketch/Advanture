using System.Collections.Generic;
using Advanture.HookIsland.Network;
using Advanture.HookIsland.Simulation;
using UnityEngine;

namespace Advanture.HookIsland
{
    /// <summary>Local construction interaction. Authority, request delivery and visuals are separate collaborators.</summary>
    [DefaultExecutionOrder(-50)]
    public sealed class PlatformConstruction : MonoBehaviour
    {
        [SerializeField] private GameObject woodPrefab, bonePrefab;
        [SerializeField] private Transform editorPreview;
        [SerializeField] private PlatformShoreline shoreline;
        private IslandFishingController room;
        private FishingOverlay overlay;
        private PlatformPresentation platform;
        private DriftwoodPresentation driftwood;
        private readonly PlatformBuildRequestBuffer requests = new PlatformBuildRequestBuffer();
        private readonly PlatformTile[] initialTiles = PlatformRules.InitialTiles();
        private string message;
        private float messageUntil;
        private bool pointerValid;
        public bool Open { get; private set; }
        public bool Pending => requests.Pending;
        public TileKind Selected { get; private set; } = TileKind.Wood;
        public int CellX { get; private set; }
        public int CellZ { get; private set; }
        public BuildResult PreviewResult { get; private set; } = BuildResult.Disabled;
        public int RenderedTiles => platform?.TileCount ?? 0;
        public string Message => Pending ? "铺设请求已提交…" : Time.unscaledTime < messageUntil ? message : null;

        public void Configure(GameObject wood, GameObject bone, Transform preview, PlatformShoreline waterContact)
        {
            woodPrefab = wood;
            bonePrefab = bone;
            editorPreview = preview;
            shoreline = waterContact;
        }

        private void Awake()
        {
            room = GetComponent<IslandFishingController>();
            overlay = GetComponent<FishingOverlay>();
        }

        private void Start()
        {
            if (room.Map?.Platform == null) return;
            if (editorPreview != null) editorPreview.gameObject.SetActive(false);
            platform = new PlatformPresentation(transform, new Dictionary<TileKind, GameObject>
            {
                { TileKind.Wood, woodPrefab }, { TileKind.Fishbone, bonePrefab }
            }, shoreline);
            driftwood = new DriftwoodPresentation(transform);
            platform.Synchronize(initialTiles);
        }

        public void Toggle()
        {
            if (Open) Close();
            else if (room.Playing && !room.Incapacitated)
            {
                Open = true;
                room.Equipment?.Close();
                room.CloseShop();
                overlay.MenuOpen = false;
            }
        }

        public void Close()
        {
            Open = false;
            pointerValid = false;
            platform?.HidePreview();
        }

        public void Select(TileKind kind)
        {
            if (kind == TileKind.Wood || kind == TileKind.Fishbone) Selected = kind;
        }

        public void ResetSession()
        {
            Close();
            requests.Reset();
            message = null;
        }

        private void Update()
        {
            if (room.Map?.Platform == null || !room.Playing) return;
            // Process acknowledgements even while the window is unfocused or a menu is open.
            if (room.TryGetLocalPlayer(out var player) && requests.Observe(player, out var result))
                ShowMessage(result == BuildResult.Success ? "铺设完成 · 全员可通行" : Explain(result), 4);
            pointerValid = false;
            if (!Application.isFocused || overlay.MenuOpen) return;
            if (Input.GetKeyDown(KeyCode.B)) Toggle();
            if (!Open) return;
            if (Input.GetKeyDown(KeyCode.Alpha1)) Select(TileKind.Wood);
            if (Input.GetKeyDown(KeyCode.Alpha2)) Select(TileKind.Fishbone);
            if (Input.GetMouseButtonDown(1)) { Close(); return; }
            var ray = room.ViewCamera.ScreenPointToRay(Input.mousePosition);
            var plane = new Plane(Vector3.up, Vector3.up * PlatformGeometry.DeckHeight);
            if (overlay.PointerOverHud || !plane.Raycast(ray, out float distance)) return;
            pointerValid = true;
            // Re-evaluate the shared rule against confirmed state each frame, including moving blockers.
            var point = ray.GetPoint(distance);
            CellX = PlatformRules.CellAt(Mathf.RoundToInt(point.x * 1000));
            CellZ = PlatformRules.CellAt(Mathf.RoundToInt(point.z * 1000));
            if (room.TryGetLocalPlayer(out player))
                PreviewResult = PlatformRules.Check(room.Map, room.State, player, Selected, CellX, CellZ);
            if (Input.GetMouseButtonDown(0)) RequestPlacement(Selected, CellX, CellZ);
        }

        public bool RequestPlacement(TileKind kind, int x, int z)
        {
            if (!room.Playing || room.Incapacitated || Pending || room.Equipment?.Pending == true || room.PurchasePending || room.Treasures?.Pending==true || !room.TryGetLocalPlayer(out var player)) return false;
            var result = PlatformRules.Check(room.Map, room.State, player, kind, x, z);
            if (result != BuildResult.Success)
            {
                ShowMessage(Explain(result), 3);
                return false;
            }
            return requests.TryQueue(player.LastBuildId, kind, x, z);
        }

        public HookInput Compose(HookInput input) => requests.Compose(input, Open);

        private void LateUpdate()
        {
            if (platform == null) return;
            var state = room.State;
            platform.Synchronize(state != null ? state.Tiles : initialTiles);
            driftwood.Synchronize(state?.Driftwood, Time.time);
            bool show = Open && room.Playing && !overlay.MenuOpen && !room.BeingPulled;
            platform.ShowPreview(show, pointerValid, CellX, CellZ, PreviewResult);
        }

        private void ShowMessage(string text, float duration)
        {
            message = text;
            messageUntil = Time.unscaledTime + duration;
        }

        public static string Explain(BuildResult result)
        {
            switch (result)
            {
                case BuildResult.Success: return "可铺设 · 左键确认";
                case BuildResult.Occupied: return "此格被地块或实体占用";
                case BuildResult.NotAdjacent: return "需要与平台完整一边相邻";
                case BuildResult.InsufficientMaterials: return "自己的材料不足，先回收浮木或捕鱼";
                case BuildResult.Busy: return "等钩索收回、拉拽结束后再铺设";
                case BuildResult.OutsideGrid: return "此处超出可建造海域";
                case BuildResult.LimitReached: return "平台已达到地块数量上限";
                default: return "当前场景不支持铺地";
            }
        }

        private void OnDestroy()
        {
            platform?.Dispose();
            driftwood?.Dispose();
        }
    }
}
