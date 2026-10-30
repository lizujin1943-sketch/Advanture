using UnityEngine;

namespace Advanture.HookIsland
{
    /// <summary>A smooth, unticked, left-facing arc. Two reusable masks; no per-frame texture allocation.</summary>
    public sealed class StaminaArcPainter : System.IDisposable
    {
        public static readonly Color Ochre = HudPainter.DisplayColor(new Color(.80f, .61f, .25f));
        private const int Width = 96, Height = 192;
        private static readonly Color Trail = HudPainter.DisplayColor(new Color(.96f, .79f, .43f));
        private const float CenterX = 88, CenterY = 96, Radius = 74, Stroke = 18;
        private const float Angle = 52 * Mathf.Deg2Rad;
        private readonly Texture2D outline = CreateMask(24), fill = CreateMask(Stroke);

        public void Draw(Rect rect, float remaining, float trailing, float now)
        {
            remaining = Mathf.Clamp01(remaining);
            var old = GUI.color;
            GUI.color = HudPainter.DisplayColor(new Color(.13f, .10f, .045f, .94f)); GUI.DrawTexture(rect, outline);
            GUI.color = HudPainter.DisplayColor(new Color(.32f, .25f, .12f, .98f)); GUI.DrawTexture(rect, fill);
            if (trailing > remaining) DrawFill(rect, Mathf.Clamp01(trailing), Trail);
            if (remaining > 0)
            {
                var color = Ochre;
                if (remaining < .2f) color.a = .85f + .15f * Mathf.Sin(now * 7) * Mathf.Sin(now * 7);
                DrawFill(rect, remaining, color);
            }
            GUI.color = old;
        }

        private void DrawFill(Rect rect, float remaining, Color color)
        {
            GUI.color = color;
            if (remaining >= 1) GUI.DrawTexture(rect, fill);
            else
            {
                float angle = Mathf.Lerp(Angle, -Angle, remaining);
                float top = (CenterY + Radius * Mathf.Sin(angle)) / Height;
                // The left arc is monotonic in Y, so a UV crop reveals an exact continuous sweep.
                GUI.DrawTextureWithTexCoords(new Rect(rect.x, rect.y + top * rect.height, rect.width, (1 - top) * rect.height),
                    fill, new Rect(0, 0, 1, 1 - top));
                float x = rect.x + (CenterX - Radius * Mathf.Cos(angle)) / Width * rect.width;
                float diameter = Stroke / Height * rect.height;
                GUI.color = Color.white;
                GUI.DrawTexture(new Rect(x - diameter / 2, rect.y + top * rect.height - diameter / 2, diameter, diameter),
                    Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, color, 0, diameter / 2);
            }
        }

        private static Texture2D CreateMask(float stroke)
        {
            var pixels = new Color32[Width * Height];
            for (int y = 0; y < Height; y++) for (int x = 0; x < Width; x++)
            {
                float dx = x + .5f - CenterX, dy = y + .5f - CenterY;
                float angle = Mathf.Atan2(dy, -dx);
                float distance = Mathf.Abs(angle) <= Angle ? Mathf.Abs(Mathf.Sqrt(dx * dx + dy * dy) - Radius) :
                    Vector2.Distance(new Vector2(dx, dy), new Vector2(-Radius * Mathf.Cos(Angle), Mathf.Sign(dy) * Radius * Mathf.Sin(Angle)));
                byte alpha = (byte)Mathf.RoundToInt(255 * Mathf.Clamp01(stroke / 2 + .5f - distance));
                pixels[y * Width + x] = new Color32(255, 255, 255, alpha);
            }
            var texture = new Texture2D(Width, Height, TextureFormat.RGBA32, false)
            { name = "Stamina arc mask", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave };
            texture.SetPixels32(pixels); texture.Apply(false, true); return texture;
        }
        public void Dispose() { Release(outline); Release(fill); }
        private static void Release(Object value)
        { if (Application.isPlaying) Object.Destroy(value); else Object.DestroyImmediate(value); }
    }
}
