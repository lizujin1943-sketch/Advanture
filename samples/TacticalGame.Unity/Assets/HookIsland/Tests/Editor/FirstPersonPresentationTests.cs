using System;
using System.Linq;
using Advanture.HookIsland.Simulation;
using FrameSync.Abstractions;
using NUnit.Framework;
using UnityEngine;
using Object=UnityEngine.Object;

namespace Advanture.HookIsland.Tests
{
    /// <summary>验证第一人称与世界角色的隔离、退出恢复和动作表现，不注入正式场景。</summary>
    public sealed class FirstPersonPresentationTests
    {
        private GameObject root;
        private Camera camera;
        private IslandMap map;
        private HookState state;
        private FishingPresentation presentation;

        [SetUp] public void SetUp()
        {
            root=new GameObject("First person presentation test");
            camera=new GameObject("Local camera").AddComponent<Camera>();camera.transform.SetParent(root.transform,false);
            camera.aspect=16f/9;camera.nearClipPlane=.02f;camera.fieldOfView=65;
            map=UnityEditor.AssetDatabase.LoadAssetAtPath<PlatformSettings>("Assets/HookIsland/Data/PlatformSettings.asset").CreateMap();
            var simulation=new HookGame().Create();
            simulation.Initialize(new SimulationInitialization(new SessionDescriptor(Guid.NewGuid(),1,30,
                new[]{new Participant(0,Guid.NewGuid()),new Participant(1,Guid.NewGuid())}),new ByteString(map.Encode())));
            var initial=simulation.Capture();var inventory=new InventorySystem(initial.Tackle.Inventory);var players=initial.Players.ToArray();
            for(int slot=0;slot<2;slot++)
            {
                var gun=inventory.Capture().Items.Single(i=>i.Owner==slot && i.Kind==ItemKind.Firearm);
                Assert.That(inventory.Equip(slot,gun.Id,false,new Point(players[slot].X,players[slot].Z)),Is.EqualTo(InventoryResult.Success));
                players[slot].EquippedRod=0;players[slot].SelectedQuickSlot=5;players[slot].Firearm.WeaponId=gun.Id;
            }
            state=new HookState(initial.Steps,players,initial.Fish,initial.Coins,initial.Tiles,initial.Driftwood,
                new TackleState(initial.Tackle.Config,inventory.Capture(),initial.Tackle.Merchants),initial.Enemies,initial.RoomRules,
                initial.Treasure,initial.Commissions,initial.Ocean,initial.Finale,initial.Crabs);
            presentation=new FishingPresentation(root.transform,Shader.Find("Hook Island/Fishing Line"),map);
            presentation.Present(state,state,0,Vector3.forward,false,true);
            camera.transform.SetPositionAndRotation(presentation.PlayerPosition(0)+Vector3.up*1.03f,Quaternion.Euler(12,0,0));
        }
        [TearDown] public void TearDown(){presentation?.Dispose();Object.DestroyImmediate(root);}

        [Test] public void OnlyLocalBodyIsHiddenAndLeavingRestoresVisibilityWithoutChangingState()
        {
            var before=new HookGame().Encode(state);
            var local=root.transform.Find("Fishing visuals/Fisher 1/Body");
            var peer=root.transform.Find("Fishing visuals/Fisher 2/Body");
            var originalHidden=local.GetComponentsInChildren<Renderer>(true).First();originalHidden.forceRenderingOff=true;
            presentation.PresentFirstPerson(state,state,0,true,camera,new Vector3(0,.12f,8));
            Assert.That(local.gameObject.activeSelf,Is.True,"隐藏渲染不能停掉角色动画和装备节点。");
            Assert.That(local.GetComponentsInChildren<Renderer>(true).All(r=>r.forceRenderingOff),Is.True);
            Assert.That(peer.GetComponentsInChildren<Renderer>(true).All(r=>!r.forceRenderingOff),Is.True);
            Assert.That(root.transform.Find("Fishing visuals/First person firearm").gameObject.activeSelf,Is.True);
            Assert.That(root.transform.Find("Fishing visuals/Local player ground marker").gameObject.activeSelf,Is.False);
            presentation.PresentFirstPerson(state,state,0,false,camera,Vector3.forward);
            Assert.That(originalHidden.forceRenderingOff,Is.True,"恢复时不能打开原来就被其他表现隐藏的部件。");
            Assert.That(local.GetComponentsInChildren<Renderer>(true).Where(r=>r!=originalHidden).All(r=>!r.forceRenderingOff),Is.True);
            Assert.That(root.transform.Find("Fishing visuals/First person firearm").gameObject.activeSelf,Is.False);
            Assert.That(new HookGame().Encode(state),Is.EqualTo(before));
        }

        [Test] public void FirstPersonGunTracksLookReloadsAndResetsWithoutMovingTheWorldGun()
        {
            var peer=root.transform.Find("Fishing visuals/Fisher 2/Body/Desert Eagle");var peerPosition=peer.position;
            var players=state.Players.ToArray();players[0].Firearm.ReloadTicks=27;players[0].Firearm.ReloadWeaponId=players[0].Firearm.WeaponId;
            var predicted=new HookState(state.Steps,players,state.Fish,state.Coins,state.Tiles,state.Driftwood,state.Tackle);
            presentation.PresentFirstPerson(state,predicted,0,true,camera,new Vector3(0,.12f,8));
            var gun=root.transform.Find("Fishing visuals/First person firearm/Desert Eagle");
            var muzzle=gun.Find("Muzzle");var screen=camera.WorldToViewportPoint(muzzle.position);
            Assert.That(screen.x,Is.InRange(.5f,.9f));Assert.That(screen.y,Is.InRange(.05f,.5f));
            Assert.That(gun.Find("Removable magazine").localPosition.y,Is.LessThan(-.1f));
            Assert.That(players[0].Firearm.ReloadTicks,Is.EqualTo(27));
            camera.transform.rotation=Quaternion.Euler(-20,90,0);
            presentation.PresentFirstPerson(state,state,0,true,camera,camera.transform.position+camera.transform.forward*10);
            Assert.That(Vector3.Dot(muzzle.forward,camera.transform.forward),Is.GreaterThan(.97f));
            Assert.That(peer.position,Is.EqualTo(peerPosition));
            Assert.That(gun.GetComponentsInChildren<Collider>(true),Is.Empty);
            presentation.ClearFirstPerson();
            Assert.That(gun.parent.gameObject.activeSelf,Is.False);
            presentation.PresentFirstPerson(state,state,0,true,camera,camera.transform.position+camera.transform.forward*10);
            Assert.That(gun.Find("Removable magazine").localPosition,Is.EqualTo(Vector3.zero));
        }
    }
}
