using System;
using Advanture.HookIsland.Network;
using Advanture.HookIsland.Simulation;
using UnityEngine;

namespace Advanture.HookIsland
{
    /// <summary>Local interaction adapter. Opening is a command, never a presentation-side reward.</summary>
    public sealed class TreasureController : IDisposable
    {
        private readonly IslandFishingController room;
        private readonly TreasureRequestBuffer requests=new TreasureRequestBuffer();
        private readonly TreasurePresentation view;
        private int previousCollected;
        private string message;
        private float messageUntil;
        public TreasureController(IslandFishingController room,Camera camera)
        {this.room=room;view=new TreasurePresentation(room.transform,camera);}
        public bool Pending=>requests.Pending;
        public string Feedback=>Pending?"正在开启宝箱…":Time.unscaledTime<messageUntil?message:null;
        public int RenderedChests=>view.Count;
        public HookInput Compose(HookInput input)=>requests.Compose(input);
        public void Synchronize(HookState state)
        {
            view.Synchronize(state);
            if(!room.TryGetLocalPlayer(out var p))return;
            if(requests.Observe(p.LastTreasureRequest,p.LastTreasureResult,out var result))
            {
                message=result==TreasureResult.Success?"宝箱已开启 · 收获者均分奖励":result==TreasureResult.TooFar?"请靠近宝箱后重新开启":
                    result==TreasureResult.LimitReached?"本局库存达到上限，宝箱尚未开启":"当前无法开启，请收钩并靠近岸上宝箱";
                messageUntil=Time.unscaledTime+4;
            }
            if(p.TreasuresCollected>previousCollected){message="宝箱收获 · 金币、木材与鱼骨已入账";messageUntil=Time.unscaledTime+4;}
            previousCollected=p.TreasuresCollected;
        }
        public int Nearest()
        {
            if(room.State?.Tackle==null || !room.TryGetLocalPlayer(out var p) || p.Swimming.InWater)return -1;
            int found=-1;long nearest=(long)room.Map.Fishing.Treasure.OpenRange*room.Map.Fishing.Treasure.OpenRange;
            foreach(var c in room.State.Treasure.Chests)
            {
                if(c.Phase!=TreasurePhase.Landed)continue;
                long distance=IslandMap.DistanceSquared(p.X,p.Z,c.X,c.Z);
                if(distance<=nearest){found=c.Id;nearest=distance;}
            }
            return found;
        }
        // True consumes E even while busy, so a nearby chest cannot unexpectedly open a merchant menu.
        public bool TryOpenNearest()
        {
            int id=Nearest();if(id<0)return false;
            if(!room.Playing || room.Incapacitated || !room.TryGetLocalPlayer(out var p) || p.Phase!=HookPhase.Ready || p.PulledBy>=0 ||
                p.Knockback.Active || Pending || room.PurchasePending || room.Equipment?.Pending==true || room.Construction?.Pending==true || room.Revival?.Pending==true)return true;
            var c=room.State.Treasure.Chests[id];room.Equipment?.Close();room.Construction?.Close();room.CloseShop();
            requests.TryQueue(p.LastTreasureRequest,c.Id+1,c.Generation);return true;
        }
        public void Draw(float scale,GUIStyle style)
        {
            view.Draw(room.State,Nearest(),scale,style);
        }
        public void Clear(){requests.Reset();previousCollected=0;message=null;messageUntil=0;view.Clear();}
        public void Dispose(){Clear();view.Dispose();}
    }
}
