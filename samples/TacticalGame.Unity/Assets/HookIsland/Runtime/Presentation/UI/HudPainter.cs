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
        private readonly Texture2D atlas, coin;
        private Texture2D scopeMask, slantMask;
        private readonly Dictionary<int,GUIStyle> styles=new Dictionary<int,GUIStyle>();
        private string tooltip;
        private Rect tooltipAnchor;

        public HudPainter(Font font)
        {
            this.font=font;atlas=UnityEngine.Resources.Load<Texture2D>("UI/HudIcons");
            coin=UnityEngine.Resources.Load<Texture2D>("UI/CoinIcon");
        }
        public void BeginFrame() { tooltip=null; }
        public void Scope(Rect viewport,int magnification)
        {
            if(scopeMask==null)
            {
                const int size=512;var pixels=new Color32[size*size];
                for(int y=0;y<size;y++)for(int x=0;x<size;x++)
                {float distance=new Vector2(x+.5f-size/2f,y+.5f-size/2f).magnitude;pixels[y*size+x]=new Color32(0,0,0,(byte)Mathf.RoundToInt(Mathf.Clamp01(distance-(size/2f-2))*255));}
                scopeMask=new Texture2D(size,size,TextureFormat.RGBA32,false){name="Optical scope aperture",wrapMode=TextureWrapMode.Clamp,filterMode=FilterMode.Bilinear};
                scopeMask.SetPixels32(pixels);scopeMask.Apply(false,true);
            }
            float diameter=Mathf.Min(viewport.width,viewport.height)*.92f,radius=diameter*.5f;var center=viewport.center;
            var aperture=new Rect(center.x-radius,center.y-radius,diameter,diameter);
            Fill(new Rect(viewport.x,viewport.y,viewport.width,aperture.y-viewport.y),Color.black);
            Fill(new Rect(viewport.x,aperture.yMax,viewport.width,viewport.yMax-aperture.yMax),Color.black);
            Fill(new Rect(viewport.x,aperture.y,aperture.x-viewport.x,diameter),Color.black);
            Fill(new Rect(aperture.xMax,aperture.y,viewport.xMax-aperture.xMax,diameter),Color.black);
            var old=GUI.color;GUI.color=Color.white;GUI.DrawTexture(aperture,scopeMask);GUI.color=old;
            void Line(Rect rect){Fill(new Rect(rect.x-.5f,rect.y-.5f,rect.width+1,rect.height+1),new Color(1,1,1,.4f));Fill(rect,new Color(.02f,.025f,.025f,.96f));}
            Line(new Rect(center.x-radius*.92f,center.y-.6f,radius*.92f-4,1.2f));
            Line(new Rect(center.x+4,center.y-.6f,radius*.92f-4,1.2f));
            Line(new Rect(center.x-.6f,center.y-radius*.92f,1.2f,radius*.92f-4));
            Line(new Rect(center.x-.6f,center.y+4,1.2f,radius*.92f-4));
            for(int n=1;n<=5;n++)for(int side=-1;side<=1;side+=2)
            {float offset=side*n*radius*.1f;Line(new Rect(center.x+offset-.5f,center.y-4,1,8));Line(new Rect(center.x-4,center.y+offset-.5f,8,1));}
            Fill(new Rect(center.x-1,center.y-1,2,2),new Color(.85f,.15f,.12f));
            Text(new Rect(center.x-100,center.y+radius*.68f,200,26),$"{magnification}×  ·  松开右键退出",13,White,TextAnchor.MiddleCenter);
        }
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
            bool fullColor=icon==HudIcon.Coin && coin!=null;
            if(!fullColor && atlas==null)return;
            int index=(int)icon;var old=GUI.color;var tint=color??White;
            // The coin carries its own gold palette; retain alpha for reward fades and disabled buttons.
            if(fullColor)tint=new Color(1,1,1,tint.a);
            if(!GUI.enabled)tint.a*=.45f;GUI.color=tint;
            if(fullColor)GUI.DrawTexture(rect,coin,ScaleMode.ScaleToFit,true);
            else GUI.DrawTextureWithTexCoords(rect,atlas,new Rect(index%8/8f,1-(index/8+1)/3f,1/8f,1/3f));
            GUI.color=old;
        }
        private GUIStyle Style(int size,TextAnchor alignment,bool bold,bool italic=false)
        {
            int key=size*100+(int)alignment*4+(bold?1:0)+(italic?2:0);
            if(!styles.TryGetValue(key,out var style))
            {style=new GUIStyle(GUI.skin.label){font=font,fontSize=size,alignment=alignment,fontStyle=bold?(italic?FontStyle.BoldAndItalic:FontStyle.Bold):(italic?FontStyle.Italic:FontStyle.Normal),padding=new RectOffset(),clipping=TextClipping.Clip};styles.Add(key,style);}
            return style;
        }
        public void Text(Rect rect,string text,int size=15,Color? color=null,TextAnchor alignment=TextAnchor.MiddleLeft,bool bold=false)
        {var style=Style(size,alignment,bold);style.normal.textColor=color??White;GUI.Label(rect,text,style);}
        /// <summary>无底板文字用贴近字形的细阴影保证可读性；斜体样式缓存，避免每帧分配 GUIStyle。</summary>
        public void FloatingText(Rect rect,string text,int size=15,Color? color=null,TextAnchor alignment=TextAnchor.MiddleLeft,bool bold=true,bool italic=true)
        {
            var style=Style(size,alignment,bold,italic);style.wordWrap=false;
            var tint=color??Color.white;
            style.normal.textColor=DisplayColor(new Color(.025f,.05f,.065f,.6f*tint.a));
            GUI.Label(new Rect(rect.x+1,rect.y+1.5f,rect.width,rect.height),text,style);
            style.normal.textColor=tint;GUI.Label(rect,text,style);
        }
        public float TextWidth(string text,int size,bool bold=true,bool italic=true)=>Style(size,TextAnchor.MiddleLeft,bold,italic).CalcSize(new GUIContent(text)).x;

        /// <summary>
        /// 可重复使用的斜切遮罩。from/to 裁切原图的 UV，受伤残影与部分生命仍沿同一个斜切轮廓显示。
        /// 只在首次绘制创建贴图；不像逐帧生成网格或贴图，不随人数和帧数累积资源。
        /// </summary>
        public void Slanted(Rect rect,Color tint,float from=0,float to=1)
        {
            from=Mathf.Clamp01(from);to=Mathf.Clamp01(to);
            if(rect.width<=0 || rect.height<=0 || to<=from || Event.current.type!=EventType.Repaint)return;
            if(slantMask==null)
            {
                const int width=128,height=64;var pixels=new Color32[width*height];
                for(int y=0;y<height;y++)for(int x=0;x<width;x++)
                {
                    float left=.28f*(y+.5f)/height*width;
                    float coverage=Mathf.Clamp01(Mathf.Min(x+.5f-left,left+width*.72f-x-.5f)+.5f);
                    pixels[y*width+x]=new Color32(255,255,255,(byte)Mathf.RoundToInt(coverage*255));
                }
                slantMask=new Texture2D(width,height,TextureFormat.RGBA32,false){name="HUD slanted segment",wrapMode=TextureWrapMode.Clamp,filterMode=FilterMode.Bilinear};
                slantMask.SetPixels32(pixels);slantMask.Apply(false,true);
            }
            var previous=GUI.color;GUI.color=tint;
            GUI.DrawTextureWithTexCoords(new Rect(rect.x+rect.width*from,rect.y,rect.width*(to-from),rect.height),slantMask,new Rect(from,0,to-from,1));
            GUI.color=previous;
        }
        public static float Fraction(int value,int maximum) => maximum<=0?0:Mathf.Clamp01((float)value/maximum);
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
        {
            styles.Clear();Release(scopeMask);Release(slantMask);scopeMask=slantMask=null;
        }
        private static void Release(Texture2D texture)
        {if(texture==null)return;if(Application.isPlaying)UnityEngine.Object.Destroy(texture);else UnityEngine.Object.DestroyImmediate(texture);}
    }
}
