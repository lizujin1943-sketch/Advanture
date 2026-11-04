using System;
using System.Collections.Generic;
using UnityEngine;

namespace Advanture.HookIsland
{
    /// <summary>
    /// 按确认稿逐段描绘的白色医疗罐：椭圆瓶口、镂空十字、宽肩、内收罐身和底部折角。
    /// 外轮廓与储液腔各缓存一张透明遮罩；使用时仅裁切液面，不缩放瓶身或重建纹理。
    /// </summary>
    public sealed class HealingCanisterPainter : IDisposable
    {
        // 设计坐标为 300 × 400，包含左下次数的留白。正式显示尺寸统一由 HudLayout 决定。
        // 坐标已经包含设计稿的前倾角度，不再额外旋转或把瓶盖简化成矩形。
        private const int Width=300,Height=400;
        private const float FillInset=4.5f;
        private static readonly Color Shadow=HudPainter.DisplayColor(new Color(.025f,.05f,.065f,.58f));
        private Texture2D outline,reservoir;
        private readonly float reservoirTop,reservoirBottom;

        public HealingCanisterPainter()
        {
            var outer=new Contour(178,1)
                .Curve(162,1,154,6,151,17).Line(141,55).Line(107,77)
                .Curve(102,80,100,83,98,89).Line(89,122)
                .Curve(88,126,90,130,92,134).Line(96,143).Line(44,328)
                .Curve(40,343,45,353,59,357).Line(192,387)
                .Curve(207,391,218,386,222,373).Line(235,330).Line(231,320)
                .Line(266,187).Line(280,177).Line(293,135)
                .Curve(295,128,294,124,291,118).Line(274,92).Line(284,56)
                .Curve(289,42,280,33,269,27).Curve(246,14,203,3,178,1);
            var cavity=new Contour(123,88)
                .Curve(119,87,114,91,112,97).Line(106,123).Line(114,139)
                .Line(61,328).Curve(57,339,64,343,76,346).Line(192,373)
                .Curve(199,375,205,370,207,363).Line(215,334).Line(211,324)
                .Line(252,174).Line(266,166).Line(275,137)
                .Curve(277,132,273,126,268,124).Line(123,88);
            var opening=new Contour(175,16)
                .Curve(184,11,245,27,260,37).Curve(283,53,198,30,177,21)
                .Curve(173,19,171,17,175,16);
            var cross=new Contour(205,41).Line(224,46).Line(219,63).Line(235,67)
                .Line(230,85).Line(214,81).Line(209,99).Line(190,94)
                .Line(195,77).Line(177,72).Line(183,56).Line(199,60);

            var shellPixels=new Color32[Width*Height];var fillPixels=new Color32[Width*Height];
            int first=Height,last=-1;
            for(int y=0;y<Height;y++)for(int x=0;x<Width;x++)
            {
                int shell=0,fill=0;
                // 四个子像素覆盖采样；透明像素同样保留白色 RGB，缩放后不会出现黑色毛边。
                for(int sy=0;sy<2;sy++)for(int sx=0;sx<2;sx++)
                {
                    var point=new Vector2(x+(sx+.5f)/2,y+(sy+.5f)/2);
                    bool inside=cavity.Contains(point);
                    if(outer.Contains(point) && !inside && !opening.Contains(point) && !cross.Contains(point))shell++;
                    // 满罐只保留约 1.2 个逻辑像素的细窄内缝，白色延伸至肩部。
                    if(inside && cavity.EdgeDistanceSquared(point)>=FillInset*FillInset)fill++;
                }
                int index=(Height-y-1)*Width+x;
                shellPixels[index]=new Color32(255,255,255,(byte)((shell*255+2)/4));
                fillPixels[index]=new Color32(255,255,255,(byte)((fill*255+2)/4));
                if(fill>0){first=Mathf.Min(first,y);last=Mathf.Max(last,y);}
            }
            outline=Create("HUD medical canister outline",shellPixels);
            reservoir=Create("HUD medical canister reservoir",fillPixels);
            reservoirTop=first/(float)Height;reservoirBottom=(last+1)/(float)Height;
        }

        /// <summary>remaining 为剩余次数／最大次数。0 仍保留瓶口、十字和空壳，储液腔完全透明。</summary>
        public void Draw(Rect rect,float remaining)
        {
            if(Event.current.type!=EventType.Repaint)return;
            remaining=Mathf.Clamp01(remaining);
            var previous=GUI.color;
            GUI.color=Shadow;
            var shadow=new Rect(rect.x+1,rect.y+1.5f,rect.width,rect.height);
            GUI.DrawTexture(shadow,outline);DrawFill(shadow,remaining);
            GUI.color=Color.white;
            GUI.DrawTexture(rect,outline);DrawFill(rect,remaining);
            GUI.color=previous;
        }

        private void DrawFill(Rect rect,float remaining)
        {
            if(remaining<=0)return;
            // 以屏幕水平面裁切液面。归一化范围仅包含储液腔，不把瓶盖、次数和画布留白计入容量。
            float top=Mathf.Lerp(reservoirBottom,reservoirTop,remaining),height=reservoirBottom-top;
            GUI.DrawTextureWithTexCoords(new Rect(rect.x,rect.y+top*rect.height,rect.width,height*rect.height),
                reservoir,new Rect(0,1-reservoirBottom,1,height));
        }

        /// <summary>一次性路径采样工具：直线保留折角，三次曲线保留瓶口与转角弧度，不参与逐帧绘制。</summary>
        private sealed class Contour
        {
            private readonly List<Vector2> points=new List<Vector2>();
            public Contour(float x,float y){points.Add(new Vector2(x,y));}
            public Contour Line(float x,float y){points.Add(new Vector2(x,y));return this;}
            public Contour Curve(float ax,float ay,float bx,float by,float x,float y)
            {
                Vector2 start=points[points.Count-1],a=new Vector2(ax,ay),b=new Vector2(bx,by),end=new Vector2(x,y);
                for(int i=1;i<=8;i++)
                {float t=i/8f,u=1-t;points.Add(u*u*u*start+3*u*u*t*a+3*u*t*t*b+t*t*t*end);}
                return this;
            }
            public bool Contains(Vector2 point)
            {
                bool inside=false;
                for(int i=0,j=points.Count-1;i<points.Count;j=i++)
                {
                    Vector2 a=points[j],b=points[i];
                    if((a.y>point.y)!=(b.y>point.y) && point.x<(b.x-a.x)*(point.y-a.y)/(b.y-a.y)+a.x)inside=!inside;
                }
                return inside;
            }
            public float EdgeDistanceSquared(Vector2 point)
            {
                float nearest=float.MaxValue;
                for(int i=0,j=points.Count-1;i<points.Count;j=i++)
                {
                    Vector2 a=points[j],edge=points[i]-a;
                    if(edge.sqrMagnitude<.0001f)continue;
                    var closest=a+edge*Mathf.Clamp01(Vector2.Dot(point-a,edge)/edge.sqrMagnitude);
                    nearest=Mathf.Min(nearest,(point-closest).sqrMagnitude);
                }
                return nearest;
            }
        }
        private static Texture2D Create(string name,Color32[] pixels)
        {
            var texture=new Texture2D(Width,Height,TextureFormat.RGBA32,false)
            {name=name,filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp,hideFlags=HideFlags.HideAndDontSave};
            texture.SetPixels32(pixels);texture.Apply(false,true);return texture;
        }
        public void Dispose(){Release(outline);Release(reservoir);outline=reservoir=null;}
        private static void Release(Texture2D texture)
        {if(texture==null)return;if(Application.isPlaying)UnityEngine.Object.Destroy(texture);else UnityEngine.Object.DestroyImmediate(texture);}
    }
}
