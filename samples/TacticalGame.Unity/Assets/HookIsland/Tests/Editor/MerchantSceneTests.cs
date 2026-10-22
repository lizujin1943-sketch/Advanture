using System.Linq;
using System.Reflection;
using Advanture.HookIsland.Simulation;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Advanture.HookIsland.Tests
{
    public sealed class MerchantSceneTests
    {
        private static void Set(object target,string field,object value)=>target.GetType()
            .GetField(field,BindingFlags.Instance|BindingFlags.NonPublic).SetValue(target,value);
        [TestCase(false)] [TestCase(true)]
        public void ShopEligibilityUsesTheMapsActualCharacterSystem(bool equipment)
        {
            var legacy=IslandMap.Decode(AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/HookIsland/Data/IslandMap.bytes").bytes);
            var map=equipment?IslandMap.CreatePlatform():new IslandMap(legacy.Shore,legacy.Pier,
                Enumerable.Repeat(new Point(0,-2900),4),legacy.FishSpots);
            var go=new GameObject("merchant-controller-contract");go.SetActive(false);
            try
            {
                var room=go.AddComponent<IslandFishingController>();Set(room,"map",map);room.HostRoom(0,1);
                var elapsed=System.Diagnostics.Stopwatch.StartNew();
                while(!room.Playing && room.Failure==null && elapsed.Elapsed.TotalSeconds<5)
                {
                    room.Host.Poll(elapsed.Elapsed);room.Client.Poll(elapsed.Elapsed,_=>default);
                    System.Threading.Thread.Sleep(1);
                }
                Assert.That(room.Failure,Is.Null);Assert.That(room.Playing,Is.True);
                var state=room.Client.Session.InspectState();Set(room,"<State>k__BackingField",state);
                Assert.That(state.Players[0].Vitals.Health>0,Is.EqualTo(equipment),"Use the real model's initialized vitals.");
                Assert.That(room.NearestMerchantId,Is.Zero);Assert.That(room.CanShopAt(0),Is.True);
                Assert.That(room.OpenShop(),Is.True);Assert.That(room.ShopMerchantId,Is.Zero);
                Assert.That(room.CanShopAt(room.ShopMerchantId),Is.True,"An open legacy shop must remain eligible.");
                Assert.That(room.CanShopAt(1),Is.False);
                if(!equipment)
                {
                    Assert.That(room.RequestUpgrade(HookUpgrade.Range),Is.False);
                    StringAssert.Contains("金币不足",room.ShopMessage,"The legacy purchase path must reach its wallet check.");
                }
                else
                {
                    var players=state.Players.ToArray();players[0].Vitals.Health=0;
                    Set(room,"<State>k__BackingField",new HookState(state.Steps,players,state.Fish,state.Coins,
                        state.Tiles,state.Driftwood,state.Tackle,state.Enemies));
                    Assert.That(room.NearestMerchantId,Is.EqualTo(-1));Assert.That(room.CanShopAt(0),Is.False);
                    room.CloseShop();Assert.That(room.OpenShop(),Is.False,"Formal downed players still cannot shop.");
                }
            }
            finally{Object.DestroyImmediate(go);}
        }
        [Test] public void MissingMerchantAssetKeepsPrototypeSingleShop()
        {
            var fishing=ScriptableObject.CreateInstance<FishingSettings>();
            try{Assert.That(fishing.CreateConfig().Merchants.Definitions.Count,Is.Zero);}
            finally{Object.DestroyImmediate(fishing);}
        }
        [Test] public void ShipConfigurationReachesMapAndCanBeDisabled()
        {
            var fishing=ScriptableObject.CreateInstance<FishingSettings>();var ships=ScriptableObject.CreateInstance<MerchantSettings>();
            var platform=ScriptableObject.CreateInstance<PlatformSettings>();
            try
            {
                fishing.merchants=ships;platform.fishing=fishing;ships.ships[0].cellX=6;
                var map=IslandMap.Decode(platform.CreateMap().Encode());
                Assert.That(map.Fishing.Merchants.Definitions.Count,Is.EqualTo(4));
                Assert.That(map.Fishing.Merchants.Definitions[0].X,Is.EqualTo(12000));
                Assert.That(map.Platform.BlockedCells.Count,Is.EqualTo(4));
                ships.useMerchants=false;Assert.That(platform.CreateMap().Fishing.Merchants.Definitions.Count,Is.Zero);
            }
            finally{Object.DestroyImmediate(fishing);Object.DestroyImmediate(ships);Object.DestroyImmediate(platform);}
        }
        [Test] public void FormalSettingsExportAllFourShips()
        {
            var fishing=AssetDatabase.LoadAssetAtPath<FishingSettings>("Assets/HookIsland/Data/FishingSettings.asset");
            Assert.That(fishing.merchants,Is.Not.Null);
            var map=IslandMap.Decode(AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/HookIsland/Data/PlatformMap.bytes").bytes);
            Assert.That(map.Fishing.Merchants.Definitions.Count,Is.EqualTo(4));
            Assert.That(map.Fishing.Encode(),Is.EqualTo(fishing.CreateConfig().Encode()));
        }
        private static MerchantState Shelf(int id,uint batch,bool sold=false)=>new MerchantState(id,true,1,batch,18001,
            new[]{new RodStock{Kind=ItemKind.Rod,Type=1},new RodStock{Kind=ItemKind.Rod,Type=2,Sold=sold}});
        [Test] public void SwitchingSameBatchShopsClearsTheSelectedOffer()
        {
            var selection=new MerchantOfferSelection();selection.Bind(Shelf(1,1));selection.Select(1);
            Assert.That(selection.Command(true).MerchantId,Is.EqualTo(1));
            selection.Bind(Shelf(2,1));Assert.That(selection.Offer,Is.EqualTo(-1));Assert.That(selection.Command(true).Action,Is.EqualTo(InventoryAction.None));
            selection.Select(0);var command=selection.Command(false);Assert.That(command.MerchantId,Is.EqualTo(2));Assert.That(command.Target,Is.Zero);
        }
        [Test] public void RestockAndCloseClearSelectionButSameShelfRefreshPreservesIt()
        {
            var selection=new MerchantOfferSelection();selection.Bind(Shelf(1,1));selection.Select(1);selection.Bind(Shelf(1,1));
            Assert.That(selection.Offer,Is.EqualTo(1));selection.Bind(Shelf(1,1,true));Assert.That(selection.Command(true).Action,Is.EqualTo(InventoryAction.None));
            selection.Bind(Shelf(1,2));Assert.That(selection.Offer,Is.EqualTo(-1));selection.Select(0);selection.Bind(null);Assert.That(selection.Offer,Is.EqualTo(-1));
        }
        [TestCase(0,"→")] [TestCase(90,"↑")] [TestCase(180,"←")] [TestCase(270,"↓")]
        public void ShipGuidanceUsesCameraDirectionAndActualDistance(int yaw,string arrow)
        {
            var go=new GameObject("Merchant camera");var camera=go.AddComponent<Camera>();
            try
            {
                camera.transform.SetPositionAndRotation(new Vector3(0,20,0),Quaternion.Euler(90,yaw,0));
                var text=MerchantHud.Guidance(camera,new FishingConfig(merchants:MerchantConfig.Default),1,default);
                StringAssert.Contains(arrow,text);StringAssert.Contains("10.0 m",text);StringAssert.Contains("东侧补给船",text);
                StringAssert.DoesNotContain("M2",text);
            }
            finally{Object.DestroyImmediate(go);}
        }
        [TestCase(640,360)] [TestCase(1280,800)] [TestCase(3440,1440)]
        public void DirectionPanelAvoidsGameplayControlsAndBlocksPointer(int width,int height)
        {
            float scale=HudLayout.Scale(width,height);var layout=new HudLayout(width/scale,height/scale);var panel=layout.Merchants;
            Assert.That(panel.Overlaps(layout.Resources(true)) || panel.Overlaps(layout.Menu) || panel.Overlaps(layout.CommissionPanel(true)) ||
                panel.Overlaps(layout.Vitals) || panel.Overlaps(layout.Tools),Is.False);
            Assert.That(layout.Contains(panel.center,4,true,true,true,true,true,true,true),Is.True);
            Assert.That(MerchantHud.LabelFits(panel,layout,4,true,true),Is.False);
            Assert.That(MerchantHud.LabelFits(new Rect(layout.Width/2-88,layout.Height/2-14,176,28),layout,4,true,true),Is.True);
        }
        [Test] public void BoatsStayInReservedFootprintsAndDoNotAddPhysicsAuthority()
        {
            var root=new GameObject("Merchant presentation test");var config=new FishingConfig(merchants:MerchantConfig.Default);
            try
            {
                using(var boats=new MerchantPresentation(root.transform,config))
                {
                    Assert.That(boats.ShipCount,Is.EqualTo(4));Assert.That(root.GetComponentsInChildren<Collider>().Length,Is.Zero);
                    var configured=root.transform.GetChild(0);
                    for(int n=0;n<4;n++)
                    {
                        var ship=configured.GetChild(n);var d=config.Merchants.Definitions[n];
                        Assert.That(ship.position.x,Is.EqualTo(d.X/1000f));Assert.That(ship.position.z,Is.EqualTo(d.Z/1000f));
                        foreach(var renderer in ship.GetComponentsInChildren<Renderer>())
                        {
                            var bounds=renderer.bounds;
                            Assert.That(Mathf.Abs(bounds.min.x-ship.position.x),Is.LessThanOrEqualTo(.801f));
                            Assert.That(Mathf.Abs(bounds.max.x-ship.position.x),Is.LessThanOrEqualTo(.801f));
                            Assert.That(Mathf.Abs(bounds.min.z-ship.position.z),Is.LessThanOrEqualTo(.801f));
                            Assert.That(Mathf.Abs(bounds.max.z-ship.position.z),Is.LessThanOrEqualTo(.801f));
                        }
                    }
                }
                Assert.That(root.transform.childCount,Is.Zero);
            }
            finally{Object.DestroyImmediate(root);}
        }
    }
}
