using System;
using System.Linq;
using Advanture.HookIsland.Simulation;
using NUnit.Framework;
using UnityEngine;
using Object=UnityEngine.Object;

namespace Advanture.HookIsland.Tests
{
    public sealed class FirearmPresentationTests
    {
        [Test] public void RPressKeepsItsActionWhenTargetsAppearOrDisappear()
        {
            var press=new RescuePressOwnership();
            press.Capture(true,true,-1,false);
            press.Capture(false,true,1,true);
            Assert.That(press.Target,Is.EqualTo(-1),"A reload press must not turn into rescue when someone becomes nearby.");
            Assert.That(press.OwnedByRescue,Is.False);
            press.Capture(false,false,-1,false);
            press.Capture(true,true,1,true);
            Assert.That(press.OwnedByRescue,Is.True);
            press.Capture(false,true,2,false);
            Assert.That(press.Target,Is.EqualTo(-1));Assert.That(press.OwnedByRescue,Is.True,"A failed rescue cannot become reload during the same press.");
            press.Capture(false,true,2,true);Assert.That(press.Target,Is.EqualTo(-1));
            press.Capture(false,false,-1,false);
            press.Capture(true,true,2,true);Assert.That(press.Target,Is.EqualTo(2));
            press.Interrupt(true);Assert.That(press.OwnedByRescue,Is.True);
            press.Reset();Assert.That(press.OwnedByRescue,Is.False);
        }
        [Test] public void GunOffersKeepTheirBatchAndLegacyAmmunitionCannotBeSelected()
        {
            MerchantState Shelf(int guns,int packs,uint batch=9)=>new MerchantState(2,true,1,batch,18001,Array.Empty<RodStock>(),firearmStock:new[]{guns},ammunitionStock:new[]{packs});
            var selected=new MerchantOfferSelection();selected.Bind(Shelf(2,6));selected.Select(FirearmCatalog.OfferOffset);
            var buy=selected.Command(true);Assert.That(buy.Action,Is.EqualTo(InventoryAction.BuyAndEquip));Assert.That(buy.MerchantId,Is.EqualTo(2));Assert.That(buy.Batch,Is.EqualTo(9));
            Assert.That(buy.Target,Is.EqualTo(FirearmCatalog.OfferOffset));
            selected.Select(AmmunitionCatalog.OfferOffset);buy=selected.Command(true);
            Assert.That(buy.Action,Is.EqualTo(InventoryAction.None));Assert.That(selected.Offer,Is.EqualTo(-1));
            selected.Select(FoodCatalog.OfferOffset);
            Assert.That(selected.Command(false).Action,Is.EqualTo(InventoryAction.None),"Retired healing food must not create a purchase request.");
            Assert.That(selected.Offer,Is.EqualTo(-1));
            selected.Bind(Shelf(2,0));Assert.That(selected.Command(false).Action,Is.EqualTo(InventoryAction.None));
            selected.Bind(Shelf(2,6,10));Assert.That(selected.Offer,Is.EqualTo(-1));
        }
        private static HookState State(Fisher player)
        {
            var items=new[]{new InventoryItem{Id=1,Kind=ItemKind.Firearm,Type=0,Owner=0,Count=1,LoadedAmmo=3,Location=ItemLocation.Equipped},new InventoryItem{Id=2,Kind=ItemKind.Rod,Type=1,Owner=0,Count=1,Location=ItemLocation.Backpack}};
            var bar=new QuickbarBinding[QuickbarRules.SlotCount];bar[0]=new QuickbarBinding(ItemKind.Firearm,0,1);bar[1]=new QuickbarBinding(ItemKind.Rod,1,2);bar[2]=new QuickbarBinding(ItemKind.Food,0);
            var inventory=new InventoryState(1,items,3,new[]{0},quickbar:bar);
            return new HookState(0,new[]{player},Array.Empty<Fish>(),0,tackle:new TackleState(new FishingConfig(firearms:new FirearmConfig()),inventory,new[]{MerchantState.Inactive(0)}));
        }
        [Test] public void FoodAndEmptyQuickSlotsHideTheStowedGun()
        {
            var player=new Fisher{Slot=0,SelectedQuickSlot=0};var state=State(player);
            Assert.That(ActiveToolView.Firearm(state,player),Is.Not.Null);
            player.SelectedQuickSlot=2;Assert.That(ActiveToolView.Firearm(state,player),Is.Null);
            player.SelectedQuickSlot=8;Assert.That(ActiveToolView.Firearm(state,player),Is.Null);
            player.SelectedQuickSlot=-1;Assert.That(ActiveToolView.Firearm(state,player),Is.Not.Null);
        }
        [Test] public void ReticleFollowsTheActualAimSampleAndHudScale()
        {
            var viewport=new Rect(100,80,800,500);var mouse=new Vector2(780,510);
            Assert.That(FirearmReticleHud.Position(true,viewport,mouse,800,2),Is.EqualTo(new Vector2(250,235)));
            Assert.That(FirearmReticleHud.Position(false,viewport,mouse,800,2),Is.EqualTo(new Vector2(390,145)));
            var spread=FirearmCatalog.Get(0).Spread;
            Assert.That(FirearmReticleHud.Gap(spread,spread.MinimumHalfAngle),Is.EqualTo(4));
            Assert.That(FirearmReticleHud.Gap(spread,6000),Is.GreaterThan(4));
            Assert.That(FirearmReticleHud.Gap(spread,int.MaxValue),Is.EqualTo(16));
        }
        [Test] public void ReticleOnlyShowsForAnActiveUsableGunAndPersistsDuringReload()
        {
            var player=new Fisher{Slot=0,SelectedQuickSlot=0,PulledBy=-1,HookedPlayer=-1,Vitals=new CharacterVitals{Health=100}};var state=State(player);
            Assert.That(FirearmReticleHud.CanShow(state,player,false),Is.True);
            Assert.That(FirearmReticleHud.CanShow(state,player,true),Is.False,"Menus and construction must suppress aiming feedback.");
            player.Firearm.ReloadTicks=27;Assert.That(FirearmReticleHud.CanShow(state,player,false),Is.True);
            player.SelectedQuickSlot=2;Assert.That(FirearmReticleHud.CanShow(state,player,false),Is.False);
            player.SelectedQuickSlot=8;Assert.That(FirearmReticleHud.CanShow(state,player,false),Is.False);
            player.SelectedQuickSlot=0;player.Vitals.Health=0;Assert.That(FirearmReticleHud.CanShow(state,player,false),Is.False);
            player.Vitals.Health=100;player.WaitingToJoin=true;Assert.That(FirearmReticleHud.CanShow(state,player,false),Is.False);
            player.WaitingToJoin=false;player.Swimming.InWater=true;Assert.That(FirearmReticleHud.CanShow(state,player,false),Is.False);
        }
        [Test] public void ReloadAllowsOnlyAnActualRightHandChange()
        {
            var player=new Fisher{Slot=0,SelectedQuickSlot=0,Firearm=new FirearmState{ReloadTicks=27,ReloadWeaponId=1,WeaponId=1}};var state=State(player);
            bool Allowed(InventoryCommand command)=>FishingEquipmentController.CanRequestDuringFirearmAction(state,player,command);
            Assert.That(Allowed(new InventoryCommand(InventoryAction.SelectQuickSlot,2)),Is.True);
            Assert.That(Allowed(new InventoryCommand(InventoryAction.Equip,2)),Is.True);
            Assert.That(Allowed(new InventoryCommand(InventoryAction.Unequip,(int)EquipmentSlot.RightHand)),Is.True);
            Assert.That(Allowed(new InventoryCommand(InventoryAction.SelectQuickSlot,1)),Is.False);
            Assert.That(Allowed(new InventoryCommand(InventoryAction.SelectQuickSlot,3)),Is.False);
            Assert.That(Allowed(new InventoryCommand(InventoryAction.Buy,FirearmCatalog.OfferOffset)),Is.False);
            Assert.That(Allowed(new InventoryCommand(InventoryAction.Move,1,x:0,y:0)),Is.False);
            Assert.That(Allowed(new InventoryCommand(InventoryAction.Heal,0)),Is.False);
            player.Firearm.ReloadTicks=0;player.Firearm.DrawTicks=2;
            Assert.That(Allowed(new InventoryCommand(InventoryAction.Equip,2)),Is.False);
        }
        [Test] public void GunPoseAndMagazineAnimationKeepFeetAndAmmunitionUntouched()
        {
            var definition=Resources.Load<FisherCharacterDefinition>(FisherCharacterDefinition.ResourcePath);var model=Object.Instantiate(definition.model);
            try
            {
                using(var art=new FirearmArt(Shader.Find("Sprites/Default")))
                using(var animation=new FisherAnimationView(model.GetComponent<Animator>(),definition,0))
                {
                    Transform Bone(string name)=>model.GetComponentsInChildren<Transform>(true).Single(t=>t.name==name);
                    var left=Bone("Foot_L").position;var right=Bone("Foot_R").position;
                    var player=new Fisher{Slot=0,Firearm=new FirearmState{WeaponId=1}};var gun=FirearmCatalog.Get(0);var pose=new FirearmAvatarView(model.transform,art);
                    pose.Present(player,player,gun,false);Assert.That(pose.Visible,Is.True);
                    Assert.That(Vector3.Dot(pose.Muzzle.forward,model.transform.forward),Is.GreaterThan(.99f));
                    player.Firearm.ReloadTicks=27;player.Firearm.ReloadWeaponId=1;
                    animation.Present(0,0,false,false,false);pose.Present(player,player,gun,false);
                    Assert.That(Bone("Removable magazine").localPosition.y,Is.LessThan(-.10f));
                    Assert.That(player.Firearm.ReloadTicks,Is.EqualTo(27));
                    Assert.That(Vector3.Distance(left,Bone("Foot_L").position),Is.LessThan(.001f));Assert.That(Vector3.Distance(right,Bone("Foot_R").position),Is.LessThan(.001f));
                    Assert.That(model.GetComponentsInChildren<Collider>(true).Length,Is.Zero);
                    pose.Present(player,player,null,false);Assert.That(pose.Visible,Is.False);
                }
            }
            finally{Object.DestroyImmediate(model);}
        }
    }
}
