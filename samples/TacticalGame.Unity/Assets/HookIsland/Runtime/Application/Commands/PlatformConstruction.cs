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
        private PlatformBuildPreviewPresentation preview;
        private DriftwoodPresentation driftwood;
        private readonly PlatformBuildBatch requests = new PlatformBuildBatch();
        private readonly PlatformBuildDraft draft = new PlatformBuildDraft();
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
        public bool Dragging => draft.Active;
        public IReadOnlyList<PlatformBuildPreview> PreviewCells => draft.Cells;
        public string Message => draft.Active ? $"可建 {draft.ValidCount} · 不可建 {draft.Cells.Count-draft.ValidCount}\n松开铺设 · 共 {draft.MaterialCost} 材料" :
            Pending ? $"铺设中 · 余 {requests.Remaining} 块" : Time.unscaledTime < messageUntil ? message : null;

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
            preview = new PlatformBuildPreviewPresentation(transform);
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
            CancelStroke();
            requests.CancelUnsent();
            platform?.ShowFrontier(false);
            preview?.Hide();
        }

        public void Select(TileKind kind)
        {
            if ((kind == TileKind.Wood || kind == TileKind.Fishbone) && Selected != kind)
            { CancelStroke(); Selected = kind; }
        }

        public void ResetSession()
        {
            Close();
            requests.Reset();
            message = null;
        }

        private void Update()
        {
            if (room.Map?.Platform == null) return;
            if (!room.Playing) { CancelStroke(); return; }
            bool interrupted = !Application.isFocused || overlay.MenuOpen || room.Incapacitated || room.BeingPulled;
            if (interrupted) { CancelStroke(); requests.CancelUnsent(); }
            // Process acknowledgements even while the window is unfocused or a menu is open.
            if (!room.TryGetLocalPlayer(out var player)) return;
            AdvanceBatch(player);
            pointerValid = false;
            if (interrupted) return;
            if (Input.GetKeyDown(KeyCode.B)) Toggle();
            if (!Open) return;
            if (Input.GetKeyDown(KeyCode.Alpha1)) Select(TileKind.Wood);
            if (Input.GetKeyDown(KeyCode.Alpha2)) Select(TileKind.Fishbone);
            if (Input.GetMouseButtonDown(1))
            {
                if (draft.Active) { CancelStroke(); ShowMessage("已取消预览", 2); }
                else Close();
                return;
            }
            if (Pending) return;
            var ray = room.ViewCamera.ScreenPointToRay(Input.mousePosition);
            var plane = new Plane(Vector3.up, Vector3.up * PlatformGeometry.DeckHeight);
            if (!overlay.PointerOverHud && plane.Raycast(ray, out float distance))
            {
                pointerValid = true;
                var point = ray.GetPoint(distance);
                CellX = PlatformRules.CellAt(Mathf.RoundToInt(point.x * 1000));
                CellZ = PlatformRules.CellAt(Mathf.RoundToInt(point.z * 1000));
                if (Input.GetMouseButtonDown(0)) BeginStroke(CellX, CellZ);
                else if (draft.Active && (Input.GetMouseButton(0) || Input.GetMouseButtonUp(0))) draft.Visit(CellX, CellZ);
            }
            else draft.BreakStroke();
            // Update the whole draft, including budget and moving blockers, from confirmed state.
            if (draft.Active)
            {
                draft.Evaluate(room.Map, room.State, player);
                PreviewResult = draft.ResultAt(CellX, CellZ);
                // Releasing over HUD commits the existing draft; HUD never adds a world cell.
                if (Input.GetMouseButtonUp(0)) CommitStroke();
                else if (!Input.GetMouseButton(0)) CancelStroke();
            }
            else if (pointerValid)
                PreviewResult = CanRequest ? PlatformRules.Check(room.Map, room.State, player, Selected, CellX, CellZ) : BuildResult.Busy;
        }

        private bool CanRequest => room.Playing && !room.Incapacitated && !room.BeingPulled && !Pending &&
            room.Equipment?.Pending != true && !room.PurchasePending && room.Treasures?.Pending != true && room.Revival?.Pending != true;

        public bool BeginStroke(int x, int z)
        {
            if (!Open || !CanRequest || !room.TryGetLocalPlayer(out var player)) return false;
            message = null;
            draft.Begin(Selected, x, z);
            draft.Evaluate(room.Map, room.State, player);
            return true;
        }

        public void CancelStroke() => draft.Cancel();

        private void OnApplicationFocus(bool focused)
        {
            if (focused) return;
            CancelStroke();
            requests.CancelUnsent();
            pointerValid = false;
            preview?.Hide();
        }

        public bool CommitStroke()
        {
            if (!draft.Active) return false;
            if (!CanRequest || !room.TryGetLocalPlayer(out var player)) { CancelStroke(); return false; }
            draft.Evaluate(room.Map, room.State, player);
            bool queued = requests.TryStart(draft.Cells);
            if (!queued) ShowMessage("没有可铺设的地块", 3);
            draft.Cancel();
            if (queued) AdvanceBatch(player);
            return queued;
        }

        private void AdvanceBatch(Fisher player)
        {
            if (!requests.Advance(room.Map, room.State, player)) return;
            ShowMessage(requests.Rejected > 0 ? $"已铺设 {requests.Succeeded} · 跳过 {requests.Rejected}\n{Explain(requests.LastFailure)}" :
                requests.Cancelled > 0 ? $"已铺设 {requests.Succeeded} · 已取消 {requests.Cancelled}" : $"已铺设 {requests.Succeeded} 块", 4);
        }

        public bool RequestPlacement(TileKind kind, int x, int z)
        {
            if (!CanRequest || !room.TryGetLocalPlayer(out var player)) return false;
            var result = PlatformRules.Check(room.Map, room.State, player, kind, x, z);
            if (result != BuildResult.Success)
            {
                ShowMessage(Explain(result), 3);
                return false;
            }
            bool queued = requests.TryStart(new[] { new PlatformBuildPreview(new PlatformTile(x, z, kind), result) });
            if (queued) AdvanceBatch(player);
            return queued;
        }

        public HookInput Compose(HookInput input) => requests.Compose(input, Open);

        private void LateUpdate()
        {
            if (platform == null) return;
            var state = room.State;
            platform.Synchronize(state != null ? state.Tiles : initialTiles);
            driftwood.Synchronize(state?.Driftwood, Time.time);
            bool show = Open && room.Playing && !overlay.MenuOpen && !room.BeingPulled && !room.Incapacitated;
            platform.ShowFrontier(show);
            if (!show) preview.Hide();
            else if (Pending) preview.ShowPending(requests.PendingTiles);
            else if (draft.Active) preview.ShowDraft(draft.Cells);
            else preview.ShowHover(pointerValid, CellX, CellZ, PreviewResult);
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
                case BuildResult.Success: return "按住左键拖动 · 松开铺设";
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
            preview?.Dispose();
            driftwood?.Dispose();
        }
    }
}
