using System;
using System.Collections.Generic;
using UnityEngine;

namespace Advanture.HookIsland
{
    // Order matches scripts/generate-hud-icons.py. All glyphs are original atlas art, not font symbols.
    public enum HudIcon { Coin, Wood, Bone, Heart, Stamina, Backpack, Build, Menu, Help, Shop, SwimRing, Skull, Rescue, Shield, Hunger, Hook, Fish, Close, Recover, Check, Alert }

    /// <summary>Owns reusable HUD drawing resources, without reading or mutating game state.</summary>
    public sealed class HudPainter : IDisposable
    {
        public static readonly Color Ink=new Color(.027f,.09f,.12f,.93f), White=new Color(.95f,.96f,.91f),
            Muted=new Color(.57f,.72f,.75f), Mint=new Color(.42f,.91f,.74f), Gold=new Color(1,.79f,.39f),
            Coral=new Color(1,.43f,.39f), Health=DisplayColor(new Color(196/255f,18/255f,28/255f)), Track=new Color(.14f,.24f,.27f);
        // IMGUI tints are converted to sRGB by the Linear project. Author semantic colors in display space.
        public static Color DisplayColor(Color srgb) => QualitySettings.activeColorSpace==ColorSpace.Linear?srgb.linear:srgb;
        private readonly Font font;
        private readonly Texture2D atlas;
        private readonly Dictionary<int,GUIStyle> styles=new Dictionary<int,GUIStyle>();
        private string tooltip;
        private Rect tooltipAnchor;

        public HudPainter(Font font)
        {
            this.font=font;atlas=UnityEngine.Resources.Load<Texture2D>("UI/HudIcons");
        }
        public void BeginFrame() { tooltip=null; }
        public void Surface(Rect rect,Color? color=null)
        {
            if(rect.width<=0 || rect.height<=0)return;
            var tint=color??Ink;if(!GUI.enabled)tint.a*=.45f;
            // Fixed nine-slice borders overflow narrow health bars and tiny player markers.
            float radius=Mathf.Min(8,Mathf.Min(rect.width,rect.height)/2);
            GUI.DrawTexture(rect,Texture2D.whiteTexture,ScaleMode.StretchToFill,true,0,tint,0,radius);
        }
        public static void Fill(Rect rect,Color color)
        { var old=GUI.color;GUI.color=color;GUI.DrawTexture(rect,Texture2D.whiteTexture);GUI.color=old; }
        public void Icon(Rect rect,HudIcon icon,Color? color=null)
        {
            if(atlas==null)return;
            int index=(int)icon;var old=GUI.color;var tint=color??White;if(!GUI.enabled)tint.a*=.45f;GUI.color=tint;
            GUI.DrawTextureWithTexCoords(rect,atlas,new Rect(index%8/8f,1-(index/8+1)/3f,1/8f,1/3f));GUI.color=old;
        }
        private GUIStyle Style(int size,TextAnchor alignment,bool bold)
        {
            int key=size*100+(int)alignment*2+(bold?1:0);
            if(!styles.TryGetValue(key,out var style))
            {style=new GUIStyle(GUI.skin.label){font=font,fontSize=size,alignment=alignment,fontStyle=bold?FontStyle.Bold:FontStyle.Normal,padding=new RectOffset(),clipping=TextClipping.Clip};styles.Add(key,style);}
            return style;
        }
        public void Text(Rect rect,string text,int size=15,Color? color=null,TextAnchor alignment=TextAnchor.MiddleLeft,bool bold=false)
        {var style=Style(size,alignment,bold);style.normal.textColor=color??White;GUI.Label(rect,text,style);}
        public static float Fraction(int value,int maximum) => maximum<=0?0:Mathf.Clamp01((float)value/maximum);
        public static Color StaminaColor(float fraction) => fraction<=.2f?Coral:fraction<=.5f?Gold:Mint;
        public void Bar(Rect rect,float progress,Color color,bool ticks=false)
        {
            progress=Mathf.Clamp01(progress);Surface(rect,Track);
            if(progress>0)Surface(new Rect(rect.x,rect.y,rect.width*progress,rect.height),color);
            if(ticks)
            {
                float pixel=1f/HudLayout.Scale(Screen.width,Screen.height);
                for(int i=1;i<20;i++)
                {
                    bool major=i%2==0;float tickHeight=major?rect.height-4:3;
                    Fill(new Rect(rect.x+rect.width*i/20f,rect.y+2,pixel,tickHeight),new Color(.025f,.12f,.15f,major?.65f:.35f));
                }
                if(progress>0 && progress<1)Fill(new Rect(rect.x+rect.width*progress-pixel,rect.y+2,pixel,rect.height-4),White);
            }
        }
        public void HealthBar(Rect rect,HealthBarFeedback.Frame frame)
        {
            Bar(rect,frame.Fill,Health);
            if(frame.WhiteTo>frame.WhiteFrom)
                Surface(new Rect(rect.x+rect.width*frame.WhiteFrom,rect.y,rect.width*(frame.WhiteTo-frame.WhiteFrom),rect.height),Color.white);
        }
        public void Hint(Rect rect,string text)
        {
            var mouse=new Vector2(Input.mousePosition.x,Screen.height-Input.mousePosition.y)/HudLayout.Scale(Screen.width,Screen.height);
            if(rect.Contains(mouse)){tooltip=text;tooltipAnchor=rect;}
        }
        public void Key(Rect rect,string text)
        {Surface(rect,new Color(.19f,.29f,.31f));Text(rect,text,10,White,TextAnchor.MiddleCenter,true);}
        public bool IconButton(Rect rect,HudIcon icon,string name,string key=null,bool selected=false)
        {
            Hint(rect,name);bool hover=tooltipAnchor==rect && tooltip==name;
            Surface(rect,selected?new Color(.13f,.38f,.35f,.97f):hover?new Color(.12f,.23f,.27f,.97f):Ink);
            Icon(new Rect(rect.center.x-12,rect.y+7,24,24),icon,selected?Mint:White);
            if(key!=null)Text(new Rect(rect.x,rect.yMax-14,rect.width,12),key,9,Muted,TextAnchor.MiddleCenter);
            return GUI.Button(rect,GUIContent.none,GUIStyle.none);
        }
        public bool ActionButton(Rect rect,string text,HudIcon icon,bool primary=false)
        {
            Surface(rect,primary?new Color(.15f,.39f,.34f):new Color(.12f,.22f,.26f));
            Icon(new Rect(rect.x+12,rect.center.y-10,20,20),icon,primary?Mint:White);
            Text(new Rect(rect.x+38,rect.y,rect.width-50,rect.height),text,15,primary?Mint:White,TextAnchor.MiddleCenter);
            return GUI.Button(rect,GUIContent.none,GUIStyle.none);
        }
        public void Tooltip(float width,float height)
        {
            if(string.IsNullOrEmpty(tooltip))return;
            float size=Mathf.Min(350,Style(12,TextAnchor.MiddleCenter,false).CalcSize(new GUIContent(tooltip)).x+24);
            var rect=new Rect(Mathf.Clamp(tooltipAnchor.center.x-size/2,8,width-size-8),tooltipAnchor.y<height/2?tooltipAnchor.yMax+8:tooltipAnchor.y-36,size,28);
            Surface(rect);Text(rect,tooltip,12,White,TextAnchor.MiddleCenter);
        }
        public void Dispose()
        {styles.Clear();}
    }
}
