using Advanture.HookIsland.Simulation;
using UnityEngine;

namespace Advanture.HookIsland
{
    /// <summary>Fits illustrations to any UI footprint without stretching. Does not own or mutate inventory state.</summary>
    public sealed class ItemIconPainter
    {
        private readonly ItemIconCatalog catalog = ItemIconCatalog.Shared;

        public void Draw(Rect area, ItemKind kind, int type, bool rotated = false, Color? tint = null)
        {
            if(area.width<=0 || area.height<=0)return;
            if(kind==ItemKind.Firearm || kind==ItemKind.Ammunition)
            {DrawFirearmIcon(area,kind,type,rotated,tint??Color.white);return;}
            if(!catalog.TryGet(kind,type,out var icon))return;
            // Rotate around the final center after fitting in the unrotated available bounds.
            float availableWidth = rotated ? area.height : area.width, availableHeight = rotated ? area.width : area.height;
            float width = Mathf.Min(availableWidth, availableHeight * icon.Aspect), height = width / icon.Aspect;
            var rect = new Rect(area.center.x - width / 2, area.center.y - height / 2, width, height);
            var previousColor = GUI.color; var previousMatrix = GUI.matrix;
            GUI.color = previousColor * (tint ?? Color.white);
            if (rotated) GUIUtility.RotateAroundPivot(90, area.center);
            GUI.DrawTextureWithTexCoords(rect, catalog.TextureFor(kind), icon.Uv);
            GUI.matrix = previousMatrix; GUI.color = previousColor;
        }

        private static void DrawFirearmIcon(Rect area,ItemKind kind,int type,bool rotated,Color tint)
        {
            var color=GUI.color;var matrix=GUI.matrix;
            try
            {
                if(rotated)GUIUtility.RotateAroundPivot(90,area.center);
                float size=Mathf.Min(area.width,area.height);var origin=area.center-new Vector2(size,size)*.5f;
                void Box(float x,float y,float w,float h,Color c)
                {GUI.color=color*tint*c;GUI.DrawTexture(new Rect(origin.x+x*size,origin.y+y*size,w*size,h*size),Texture2D.whiteTexture);}
                var silver=new Color(.76f,.82f,.86f);var dark=new Color(.20f,.25f,.29f);var gold=new Color(.96f,.70f,.29f);
                if(kind==ItemKind.Firearm && FirearmCatalog.Get(type).Category!=FirearmCategory.Pistol)
                {
                    var gun=FirearmCatalog.Get(type);bool rifle=gun.Category==FirearmCategory.Rifle;
                    var furniture=rifle?new Color(.64f,.32f,.12f):gun.Id=="barrett"?new Color(.65f,.55f,.37f):new Color(.43f,.54f,.28f);
                    Box(.04f,.48f,.23f,.17f,furniture);Box(.25f,.43f,.38f,.14f,dark);Box(.53f,.44f,.22f,.12f,furniture);
                    Box(.72f,.44f,.23f,.045f,silver);Box(.91f,.42f,.06f,.09f,dark);Box(.28f,.55f,.07f,.20f,dark);
                    Box(.46f,.56f,.10f,rifle?.23f:.13f,dark);
                    if(rifle){Box(.48f,.73f,.12f,.05f,dark);Box(.79f,.35f,.025f,.11f,dark);}
                    else{Box(.29f,.30f,.40f,.07f,dark);Box(.59f,.27f,.13f,.13f,dark);Box(.34f,.36f,.04f,.08f,dark);Box(.55f,.36f,.04f,.08f,dark);Box(.68f,.29f,.025f,.08f,new Color(.24f,.69f,.76f));}
                }
                else if(kind==ItemKind.Firearm)
                {
                    Box(.12f,.27f,.77f,.23f,dark);Box(.13f,.25f,.76f,.18f,silver);Box(.15f,.25f,.70f,.035f,Color.white);
                    Box(.20f,.44f,.22f,.36f,dark);Box(.23f,.49f,.14f,.27f,new Color(.32f,.36f,.38f));
                    Box(.43f,.47f,.23f,.04f,silver);Box(.61f,.47f,.04f,.18f,silver);Box(.41f,.62f,.24f,.04f,silver);
                    Box(.50f,.47f,.035f,.12f,gold);Box(.78f,.18f,.045f,.08f,dark);Box(.18f,.21f,.055f,.05f,dark);
                }
                else for(int n=0;n<3;n++)
                {float x=.18f+n*.23f;Box(x,.35f,.16f,.43f,gold);Box(x,.75f,.17f,.05f,new Color(.65f,.41f,.15f));Box(x+.03f,.23f,.10f,.16f,silver);Box(x+.05f,.18f,.06f,.08f,silver);Box(x+.03f,.39f,.025f,.29f,new Color(1,.88f,.51f));}
            }
            finally {GUI.color=color;GUI.matrix=matrix;}
        }
        public static int Level(ItemKind kind, int type)
        {
            if(kind==ItemKind.Firearm)return FirearmCatalog.Get(type).Tier;
            if (kind == ItemKind.Weapon) return WeaponCatalog.Get(type).Tier;
            if (kind == ItemKind.Rod) return RodCatalog.Get(type).Tier;
            if (kind == ItemKind.Backpack || kind == ItemKind.SwimRing) return type;
            return 0;
        }
        public static Color Accent(ItemKind kind, int type)
        {
            int tier = kind == ItemKind.Firearm ? FirearmCatalog.Get(type).Tier : kind == ItemKind.Weapon ? WeaponCatalog.Get(type).Tier :
                kind == ItemKind.Rod ? RodCatalog.Get(type).Tier :
                kind == ItemKind.Backpack ? InventoryCatalog.Backpack(type).Tier :
                kind == ItemKind.SwimRing ? SwimRingCatalog.Get(type).Tier : 0;
            switch (tier)
            {
                case 1: return new Color(.39f, .81f, .65f);
                case 2: return new Color(.34f, .74f, .88f);
                case 3: return new Color(.61f, .57f, .91f);
                case 4: return new Color(.94f, .65f, .34f);
                case 5: return new Color(1, .84f, .46f);
                default: return new Color(.57f, .69f, .68f);
            }
        }
    }
}
