using Advanture.HookIsland.Simulation;
using UnityEngine;

namespace Advanture.HookIsland
{
    /// <summary>Read-only equipment view. Buttons submit intents through the local command adapter.</summary>
    public sealed class FishingEquipmentOverlay : MonoBehaviour
    {
        private IslandFishingController room;
        private FishingEquipmentController input;
        private Font font;
        private PlayerVitalsOverlay vitals;
        private GUIStyle title,small,button;
        private readonly Color gold=new Color(1,.79f,.32f),mint=new Color(.35f,.88f,.73f);
        private void Awake()
        {
            room=GetComponent<IslandFishingController>();input=GetComponent<FishingEquipmentController>();
            font=Font.CreateDynamicFontFromOSFont(new[]{"PingFang SC","Microsoft YaHei","Noto Sans CJK SC","Arial"},24);
        }
        private GUIStyle Text(int size,Color color)=>new GUIStyle(GUI.skin.label){font=font,fontSize=size,wordWrap=true,normal={textColor=color}};
        private static void Fill(Rect r,Color color)
        {var old=GUI.color;GUI.color=color;GUI.DrawTexture(r,Texture2D.whiteTexture);GUI.color=old;}
        private static void Panel(Rect r)=>Fill(r,new Color(.025f,.095f,.12f,.98f));
        public void Draw()
        {
            if(room.State?.Tackle==null || !room.TryGetLocalPlayer(out var p) || !room.Playing){vitals?.Reset();return;}
            if(title==null){title=Text(24,gold);small=Text(14,new Color(.7f,.85f,.86f));button=new GUIStyle(GUI.skin.button){font=font,fontSize=17,wordWrap=true};}
            float scale=Mathf.Clamp(Mathf.Min(Screen.width/1280f,Screen.height/720f),.45f,2);
            var previous=GUI.matrix;GUI.matrix=Matrix4x4.Scale(new Vector3(scale,scale,1));
            float w=Screen.width/scale,h=Screen.height/scale;
            bool menu=GetComponent<FishingOverlay>().MenuOpen;
            if(vitals==null)vitals=new PlayerVitalsOverlay(font);
            vitals.Draw(room,scale,!menu && !room.ShopOpen && !input.InventoryOpen);
            if(!menu && (room.ShopOpen || input.InventoryOpen))DrawEquipment(p,w,h);
            GUI.matrix=previous;
        }
        private BackpackOverlay backpack;
        private CharacterPanel characterPanel;
        public bool CharacterPage { get; private set; } = true;
        public void SelectCharacterPage(bool value) { CharacterPage=value; }
        public void ResetShopSelection(){backpack?.ResetShopSelection();}
        public void ResetSession() { characterPanel?.Reset();vitals?.Reset();backpack=null;CharacterPage=true; }
        private void DrawEquipment(Fisher player,float width,float height)
        {
            if(backpack==null)backpack=new BackpackOverlay(font);
            if(characterPanel==null)characterPanel=new CharacterPanel(font);
            float x=(width-1100)/2,y=Mathf.Max(120,(height-560)/2);
            if(room.ShopOpen || !CharacterPage)
                backpack.Draw(room.State,player,room.ShopOpen,input.Pending,input.Message,width,height,input.RequestInventory,
                    ()=>{room.CloseShop();input.Close();},room.ShopMerchantId);
            else
            {
                Panel(new Rect(x,y,1100,560));Fill(new Rect(x,y,1100,3),mint);
                GUI.Label(new Rect(x+24,y+17,420,35),"角色与装备",title);
                if(GUI.Button(new Rect(x+982,y+18,92,31),"关闭",button))input.Close();
                characterPanel.Draw(new Rect(x+24,y+70,1052,454),room.State.Tackle.Config.Character,
                    room.State.Tackle.Config.StaminaBase,room.State.Tackle.Inventory,player,player.Vitals,input.Pending,
                    command=>input.RequestInventory(command),room.State.Tackle.Config.Swimming);
                GUI.Label(new Rect(x+24,y+526,1052,28),input.Message??"Tab / I 关闭面板 · 角色页不会提供购物保护",small);
            }
            if(!room.ShopOpen)
            {
                var enabled=GUI.enabled;
                GUI.enabled=!CharacterPage;
                if(GUI.Button(new Rect(x+615,y+18,144,31),"角色 · 属性",button))CharacterPage=true;
                GUI.enabled=CharacterPage;
                if(GUI.Button(new Rect(x+769,y+18,144,31),"背包 · 整理",button))CharacterPage=false;
                GUI.enabled=enabled;
            }
        }
        private void OnDestroy(){vitals?.Dispose();if(font!=null)Destroy(font);}
    }
}
