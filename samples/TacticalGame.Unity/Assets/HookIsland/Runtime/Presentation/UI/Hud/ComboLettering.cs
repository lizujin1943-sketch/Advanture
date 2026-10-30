using UnityEngine;

namespace Advanture.HookIsland
{
    /// <summary>Original numeral face with an ink outline, extrusion and a narrow bevel. Presentation only.</summary>
    public sealed class ComboLettering
    {
        private static readonly Vector2[] OutlineDirections={Vector2.left,Vector2.right,Vector2.up,Vector2.down,
            new Vector2(-.707f,-.707f),new Vector2(.707f,-.707f),new Vector2(-.707f,.707f),new Vector2(.707f,.707f)};
        private readonly Font font=Resources.Load<Font>("UI/HookRushNumerals");
        private GUIStyle style;

        public void Draw(Rect rect,string text,int size,Color color,float strength,float flash)
        {
            if(rect.width<=0 || rect.height<=0 || color.a<=0)return;
            if(style==null)style=new GUIStyle(GUI.skin.label){font=font!=null?font:GUI.skin.label.font,
                fontStyle=FontStyle.Normal,alignment=TextAnchor.MiddleCenter,clipping=TextClipping.Overflow,
                padding=new RectOffset(),wordWrap=false,richText=false};
            style.fontSize=Mathf.Max(9,size);
            float measured=style.CalcSize(new GUIContent(text)).x;
            if(measured>rect.width-14)style.fontSize=Mathf.Max(9,Mathf.FloorToInt(style.fontSize*(rect.width-14)/measured));
            float outline=Mathf.Lerp(1.5f,2.5f,strength)*Mathf.Min(1,style.fontSize/30f);
            var ink=new Color(.015f,.025f,.04f,color.a);
            if(flash>.01f)
            {
                var halo=new Color(color.r,color.g,color.b,color.a*flash*.24f);
                foreach(var direction in OutlineDirections)Layer(rect,text,direction*(outline+2),halo);
            }
            // Solid stepped depth remains readable against both bright sea and wooden platforms.
            float depth=Mathf.Lerp(3,5.5f,strength);
            for(int i=3;i>=1;i--)Layer(rect,text,new Vector2(depth*.55f,depth)*(i/3f),ink);
            foreach(var direction in OutlineDirections)Layer(rect,text,direction*outline,ink);
            var bevel=Color.Lerp(color,Color.white,.68f);bevel.a=color.a;
            Layer(rect,text,new Vector2(-.35f,-1.05f),bevel);
            var fill=Color.Lerp(color,Color.white,flash*.20f);fill.a=color.a;
            Layer(rect,text,Vector2.zero,fill);
        }

        private void Layer(Rect rect,string text,Vector2 offset,Color color)
        {rect.position+=offset;style.normal.textColor=color;GUI.Label(rect,text,style);}
    }
}
