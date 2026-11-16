// O desenho do Clawd em pixels, usado pelos Clawds da área de trabalho e pela Central:
// a grade, as cores, a pose e as partes do corpo.

using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace Clawdesk
{
    // -------------------------------------------------------------- pixel art --

    // Uma grade de pixels: cada célula guarda o caractere da cor ('.' é transparente).
    // A origem (0,0) fica entre os pés, no chão; y negativo sobe. Com Flip a grade espelha,
    // e o Clawd olha para a esquerda.
    sealed class Pix
    {
        public const int GW = 80, GH = 66, OX = 40, OY = 54;
        public readonly char[] Cells = new char[GW * GH];
        public bool Flip;
        public int MinRow = GH, MaxRow = -1;

        public void Clear()
        {
            for (int i = 0; i < Cells.Length; i++) Cells[i] = '.';
            MinRow = GH;
            MaxRow = -1;
            Flip = false;
        }

        public void Px(int x, int y, char c)
        {
            if (c == '.' || c == ' ') return;
            int gx = Flip ? OX - 1 - x : OX + x, gy = OY + y;
            if (gx < 0 || gx >= GW || gy < 0 || gy >= GH) return;
            Cells[gy * GW + gx] = c;
            if (gy < MinRow) MinRow = gy;
            if (gy > MaxRow) MaxRow = gy;
        }

        public void Rect(int x, int y, int w, int h, char c)
        {
            for (int j = 0; j < h; j++) for (int i = 0; i < w; i++) Px(x + i, y + j, c);
        }

        // Retângulo com contorno e cantos arredondados de um pixel.
        public void Box(int x, int y, int w, int h, char fill, char line)
        {
            Rect(x + 1, y, w - 2, h, line);
            Rect(x, y + 1, w, h - 2, line);
            Rect(x + 1, y + 1, w - 2, h - 2, fill);
        }

        public void Stamp(string[] art, int x, int y)
        {
            for (int j = 0; j < art.Length; j++)
                for (int i = 0; i < art[j].Length; i++) Px(x + i, y + j, art[j][i]);
        }

        public void Line(int x0, int y0, int x1, int y1, char c)
        {
            int dx = Math.Abs(x1 - x0), dy = -Math.Abs(y1 - y0), sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1, err = dx + dy;
            while (true)
            {
                Px(x0, y0, c);
                if (x0 == x1 && y0 == y1) break;
                int e2 = 2 * err;
                if (e2 >= dy) { err += dy; x0 += sx; }
                if (e2 <= dx) { err += dx; y0 += sy; }
            }
        }

        public void Ring(int cx, int cy, int r, char c)
        {
            for (int a = 0; a < 64; a++)
            {
                double ang = a * Math.PI / 32;
                Px(cx + (int)Math.Round(r * Math.Cos(ang)), cy + (int)Math.Round(r * Math.Sin(ang)), c);
            }
        }

        public void Disc(int cx, int cy, int r, char c)
        {
            for (int j = -r; j <= r; j++)
                for (int i = -r; i <= r; i++)
                    if (i * i + j * j <= r * r + r / 2) Px(cx + i, cy + j, c);
        }
    }

    static class Art
    {
        public static readonly string[] Heart = { ".PP.PP.", "PPPPPPP", "PPwPPPP", ".PPPPP.", "..PPP..", "...P..." };
        public static readonly string[] HeartS = { "P.P", "PPP", ".P." };
        public static readonly string[] Note = { "..yyy", "..y.y", "..y..", "yyy..", "yyy.." };
        public static readonly string[] Z1 = { "ggg", ".g.", "ggg" };
        public static readonly string[] Z2 = { "gggg", "..g.", ".g..", "gggg" };
        public static readonly string[] Spark = { "..w..", ".wyw.", "wyyyw", ".wyw.", "..w.." };
        public static readonly string[] Plus = { ".y.", "yyy", ".y." };
        public static readonly string[] Bulb = { ".yyy.", "yyyyy", "yywyy", "yyyyy", ".yyy.", ".ggg.", ".GGG." };
        public static readonly string[] Question = { ".kkk.", "k...k", "....k", "..kk.", "..k..", ".....", "..k.." };
        public static readonly string[] Bang = { "rr", "rr", "rr", "rr", "..", "rr" };
        public static readonly string[] Check = { ".....n", "....nn", "n..nn.", "nnnn..", ".nn..." };
        public static readonly string[] Cross = { "rr...rr", ".rr.rr.", "..rrr..", ".rr.rr.", "rr...rr" };
        public static readonly string[] Drop = { ".c.", "ccc", "cwc", ".c." };
        // A marca das sessões que vêm de fora deste PC (nuvem, outro computador).
        public static readonly string[] Cloud = { "...kk...", ".kkwwkk.", "kwwwwwwk", "kwwwwggk", ".kkkkkk." };
        public static readonly string[] Paper = { "pp", "pq" };
        public static readonly string[] Cursor = { "k....", "kk...", "kwk..", "kwwk.", "kwwwk", "kwwkk", "k.kwk", "...k." };
        public static readonly string[] Thought =
        {
            "...qqqq.qqqq...",
            "..qwwwwqwwwwq..",
            ".qwwwwwwwwwwwq.",
            "qwwwwwwwwwwwwwq",
            "qwwwwwwwwwwwwwq",
            ".qwwwwwwwwwwwq.",
            "..qqqqqqqqqqq..",
        };
        public static readonly string[] Rain =
        {
            "....mmmmm.....",
            "..mmmmmmmmm...",
            ".mmmmmmmmmmmm.",
            "mmmmmmmmmmmmmm",
            "MMMMMMMMMMMMMM",
            ".MMMMMMMMMMMM.",
        };
        public static readonly string[] Stop =
        {
            "..kkkkk..",
            ".krrrrrk.",
            "krrrrrrrk",
            "krwwwwwrk",
            "krrrrrrrk",
            ".krrrrrk.",
            "..kkkkk..",
        };
        public static readonly string[] Trophy =
        {
            "yyyyyyy",
            "yywyyyy",
            ".yyyyy.",
            "..yyy..",
            "...y...",
            "..YYY..",
            ".YYYYY.",
        };
        public static readonly string[] Battery = { "kkkkkkk.", "krr...kk", "krr...kk", "kkkkkkk." };
        public static readonly string[] Bot =
        {
            "...y..",
            ".kkkk.",
            "kaaaak",
            "kawawk",
            "kaaaak",
            ".k..k.",
        };
        public static readonly string[] Crown = { "y.y.y", "yyyyy", "yryuy", "yyyyy" };
        public static readonly string[] Wizard =
        {
            ".....y..",
            "....vv..",
            "...vyvv.",
            "..vvvvv.",
            ".vvvvvvv",
            "VVVVVVVVV",
        };
        public static readonly string[] Sprout = { ".nn.nn", "nNNnNn", "...N..", "...N.." };
        public static readonly string[] Antenna = { ".y.", ".G.", ".G." };
        public static readonly string[] Cookie = { ".oo.", "oOoo", "ooOo", ".oo." };
        public static readonly string[] Cookie2 = { ".oo.", "oOo.", "oo..", ".o.." };
        public static readonly string[] Cookie3 = { ".o..", "oO..", "o..." };
    }

    static class Pal
    {
        public static Color C(string hex) { return (Color)ColorConverter.ConvertFromString(hex); }

        public static SolidColorBrush B(string hex, double a)
        {
            Color c = C(hex);
            var b = new SolidColorBrush(Color.FromArgb((byte)Math.Round(a * 255), c.R, c.G, c.B));
            b.Freeze();
            return b;
        }

        static readonly Dictionary<char, string> Shared = new Dictionary<char, string>
        {
            { 'e', "#17141c" }, { 'w', "#ffffff" }, { 'g', "#c9ced6" }, { 'G', "#5d6672" }, { 'h', "#2b313b" },
            { 's', "#152029" }, { 'n', "#5ee08a" }, { 'N', "#2f8f57" }, { 'y', "#ffd23f" }, { 'Y', "#c08a1e" },
            { 'r', "#e5484d" }, { 'R', "#9e2a2e" }, { 'u', "#2f7fd8" }, { 'U', "#1d4f8f" }, { 'c', "#7fd6ff" },
            { 'o', "#b07a43" }, { 'O', "#7a4f26" }, { 'p', "#f6f1e4" }, { 'q', "#9aa4b1" }, { 'P', "#ff7eb6" },
            { 'v', "#8a5cf6" }, { 'V', "#5b3aa8" }, { 'f', "#ff9f43" }, { 'm', "#b9c2cc" }, { 'M', "#7d8794" },
            { 'x', "#101014" }, { 'i', "#e8ecf2" },
        };

        public static Dictionary<char, Brush> Make(string body, string light, string dark, string ink, string accent)
        {
            var d = new Dictionary<char, Brush>();
            foreach (var kv in Shared) d[kv.Key] = B(kv.Value, 1);
            d['b'] = B(body, 1);
            d['l'] = B(light, 1);
            d['d'] = B(dark, 1);
            d['k'] = B(ink, 1);
            d['a'] = B(accent, 1);
            d['z'] = B("#000000", 0.28);
            d['Z'] = B("#ffffff", 0.55);
            return d;
        }
    }

    // ------------------------------------------------------------------- corpo --

    enum Arm { Down, Mid, Up, High, Out, Low, Type, Hidden }
    enum Legs { Stand, Walk, Sit, Tuck, Dangle, Tap }

    sealed class Pose
    {
        public int Dy, Puff, Stretch, ShakeX;
        public Legs Legs = Legs.Stand;
        public int Frame;
        public Arm ArmL = Arm.Mid, ArmR = Arm.Mid;
        public int LenL = 2, LenR = 2;
        public string Eyes = "open", Mouth = "";
        public int LookX, LookY;
        public bool Blush, Cap, Cross, Blink;
    }

    // A geometria do corpo nesse quadro: onde ficam os lados, o topo e a mão da frente.
    struct Geo
    {
        public int X0, X1, Top, Bottom;
        public int HandX, HandY;
    }

    // As partes do corpo, desenhadas na grade a partir da pose.
    static class Body
    {
        public static int F(double t, double fps, int n) { return (int)Math.Floor(t * fps) % n; }

        public static Geo GeoOf(Pose p)
        {
            var g = new Geo();
            g.X0 = -8 - p.Puff + p.ShakeX;
            g.X1 = 7 + p.Puff + p.ShakeX;
            g.Bottom = -5 + p.Dy;
            g.Top = g.Bottom - 8 - p.Stretch;
            g.HandX = g.X1 + 1 + p.LenR;
            g.HandY = g.Top + 3;
            switch (p.ArmR)
            {
                case Arm.Low: g.HandY = g.Top + 6; break;
                case Arm.Up: g.HandX = g.X1 + 3; g.HandY = g.Top - 1; break;
                case Arm.High: g.HandX = g.X1 + 2; g.HandY = g.Top - 4; break;
                case Arm.Type: g.HandX = g.X1 + 4; g.HandY = g.Top + 7; break;
            }
            return g;
        }

        // Os blocos de um braço, do lado direito; o esquerdo é o espelho.
        public static List<Int32Rect> ArmRects(Arm a, int len, Geo g, int frame)
        {
            var r = new List<Int32Rect>();
            int sx = g.X1 + 1;
            switch (a)
            {
                case Arm.Down: r.Add(new Int32Rect(sx, g.Top + 4, 2, 2)); break;
                case Arm.Mid: r.Add(new Int32Rect(sx, g.Top + 3, 2, 2)); break;
                case Arm.Out: r.Add(new Int32Rect(sx, g.Top + 3, len, 2)); break;
                case Arm.Low: r.Add(new Int32Rect(sx, g.Top + 5, len, 2)); break;
                case Arm.Up: r.Add(new Int32Rect(sx, g.Top + 1, 2, 2)); r.Add(new Int32Rect(sx + 1, g.Top - 1, 2, 2)); break;
                case Arm.High: r.Add(new Int32Rect(sx, g.Top - 3, 2, 5)); break;
                case Arm.Type:
                    r.Add(new Int32Rect(sx, g.Top + 4, 2, 2));
                    r.Add(new Int32Rect(sx + 1 + (frame % 2), g.Top + 6, 2, 2));
                    break;
            }
            return r;
        }

        public static void DrawArm(Pix g, Arm a, int len, Geo geo, int side, int frame)
        {
            if (a == Arm.Hidden) return;
            var rects = ArmRects(a, len, geo, frame);
            int cx = geo.X0 + geo.X1; // espelho em torno do centro do corpo
            foreach (var r in rects)
            {
                int x = side > 0 ? r.X : cx - r.X - r.Width + 1;
                g.Rect(x - 1, r.Y - 1, r.Width + 2, r.Height + 2, 'k');
            }
            foreach (var r in rects)
            {
                int x = side > 0 ? r.X : cx - r.X - r.Width + 1;
                g.Rect(x, r.Y, r.Width, r.Height, 'b');
            }
        }

        public static void DrawLegs(Pix g, Pose p, Geo geo, double t)
        {
            if (p.Legs == Legs.Tuck) return;
            int[] lx = { -7, -4, 2, 5 };
            int top = geo.Bottom + 1;
            for (int i = 0; i < 4; i++)
            {
                int x = lx[i] + p.ShakeX, bottom = -1, len;
                switch (p.Legs)
                {
                    case Legs.Walk:
                    {
                        bool up = (p.Frame % 4 == 0 && i % 2 == 0) || (p.Frame % 4 == 2 && i % 2 == 1);
                        if (up) bottom = -2;
                        break;
                    }
                    case Legs.Tap:
                        if (i == 3 && p.Frame % 2 == 0) bottom = -2;
                        break;
                    case Legs.Sit:
                        bottom = -1 - ((p.Frame + i) % 2);
                        break;
                    case Legs.Dangle:
                        bottom = top + 4;
                        x += (p.Frame + i) % 2 == 0 ? 0 : (i < 2 ? -1 : 1);
                        break;
                }
                len = bottom - top + 1;
                if (len <= 0) continue;
                g.Rect(x - 1, top, 4, len, 'k');
                g.Rect(x, top, 2, len - 1, 'd');
            }
        }

        public static void DrawBody(Pix g, Pose p, Geo geo)
        {
            int x0 = geo.X0, x1 = geo.X1, top = geo.Top, bot = geo.Bottom;
            int w = x1 - x0 + 1, h = bot - top + 1;
            g.Box(x0 - 1, top - 1, w + 2, h + 2, 'b', 'k');
            g.Rect(x0 + 1, top, w - 2, 1, 'l');
            g.Rect(x0, top + 1, 1, h - 2, 'l');
            g.Rect(x0 + 1, bot, w - 2, 1, 'd');
            g.Rect(x1, top + 1, 1, h - 2, 'd');
            g.Px(x0 + 1, top + 1, 'w');
            g.Px(x0 + 2, top + 1, 'w');
            g.Px(x0 + 1, top + 2, 'w');
            if (p.Cross)
            {
                g.Rect(x0 + 2, top + 5, w - 4, 3, 'k');
                g.Rect(x0 + 3, top + 6, w - 6, 1, 'b');
            }
        }

        public static void DrawEye(Pix g, string mode, int ex, int ey, int side)
        {
            switch (mode)
            {
                case "closed": g.Rect(ex, ey + 2, 2, 1, 'e'); return;
                case "happy":
                    g.Px(ex - 1, ey + 2, 'e'); g.Px(ex, ey + 1, 'e'); g.Px(ex + 1, ey + 1, 'e'); g.Px(ex + 2, ey + 2, 'e');
                    return;
                case "sad":
                    g.Rect(ex, ey + 1, 2, 2, 'e');
                    if (side < 0) { g.Px(ex, ey, 'e'); g.Px(ex + 1, ey - 1, 'e'); } else { g.Px(ex + 1, ey, 'e'); g.Px(ex, ey - 1, 'e'); }
                    return;
                case "angry":
                    g.Rect(ex, ey + 1, 2, 2, 'e');
                    if (side < 0) { g.Px(ex, ey - 1, 'e'); g.Px(ex + 1, ey, 'e'); } else { g.Px(ex + 1, ey - 1, 'e'); g.Px(ex, ey, 'e'); }
                    return;
                case "focus": g.Rect(ex, ey + 1, 2, 2, 'e'); g.Rect(ex, ey, 2, 1, 'd'); return;
                case "sleepy": g.Rect(ex, ey + 1, 2, 1, 'd'); g.Rect(ex, ey + 2, 2, 1, 'e'); return;
                case "wide": g.Rect(ex, ey - 1, 2, 4, 'e'); g.Px(ex, ey - 1, 'w'); return;
                case "star": g.Rect(ex, ey, 2, 3, 'y'); g.Px(ex, ey, 'w'); return;
                case "heart":
                {
                    int x = side < 0 ? ex - 1 : ex;
                    g.Px(x, ey, 'P'); g.Px(x + 2, ey, 'P'); g.Rect(x, ey + 1, 3, 1, 'P'); g.Px(x + 1, ey + 2, 'P');
                    return;
                }
                case "x":
                {
                    int x = side < 0 ? ex - 1 : ex;
                    g.Px(x, ey, 'e'); g.Px(x + 2, ey, 'e'); g.Px(x + 1, ey + 1, 'e'); g.Px(x, ey + 2, 'e'); g.Px(x + 2, ey + 2, 'e');
                    return;
                }
                case "spiral":
                {
                    int x = side < 0 ? ex - 1 : ex;
                    g.Rect(x, ey, 3, 1, 'e'); g.Px(x + 2, ey + 1, 'e'); g.Rect(x, ey + 2, 3, 1, 'e'); g.Px(x, ey + 1, 'e'); g.Px(x + 1, ey + 1, 'w');
                    return;
                }
            }
            g.Rect(ex, ey, 2, 3, 'e');
            g.Px(ex, ey, 'w');
        }

        public static void DrawFace(Pix g, Pose p, Geo geo)
        {
            int lx = Math.Max(-1, Math.Min(1, p.LookX)), ly = Math.Max(-1, Math.Min(1, p.LookY));
            int ey = geo.Top + 2 + ly + (p.Stretch > 0 ? 1 : 0);
            string mode = p.Blink && (p.Eyes == "open" || p.Eyes == "wide" || p.Eyes == "focus") ? "closed" : p.Eyes;
            int cx = (geo.X0 + geo.X1 + 1) / 2; // 0 quando o corpo está centrado
            DrawEye(g, mode, cx - 5 + lx, ey, -1);
            DrawEye(g, mode, cx + 3 + lx, ey, 1);
            int my = geo.Top + 6 + (p.Stretch > 0 ? 1 : 0), mx = cx - 1 + lx;
            switch (p.Mouth)
            {
                case "smile": g.Rect(mx, my, 2, 1, 'e'); g.Px(mx - 1, my - 1, 'e'); g.Px(mx + 2, my - 1, 'e'); break;
                case "open": g.Rect(mx, my - 1, 2, 2, 'x'); g.Px(mx, my, 'R'); break;
                case "o": g.Rect(mx, my, 2, 1, 'x'); break;
                case "flat": g.Rect(mx, my, 2, 1, 'e'); break;
                case "frown": g.Rect(mx, my - 1, 2, 1, 'e'); g.Px(mx - 1, my, 'e'); g.Px(mx + 2, my, 'e'); break;
                case "wobble": g.Px(mx - 1, my, 'e'); g.Px(mx, my - 1, 'e'); g.Px(mx + 1, my, 'e'); g.Px(mx + 2, my - 1, 'e'); break;
                case "tongue": g.Rect(mx, my, 2, 1, 'e'); g.Px(mx - 1, my - 1, 'e'); g.Px(mx + 2, my - 1, 'e'); g.Px(mx + 1, my + 1, 'P'); break;
            }
            if (p.Blush)
            {
                g.Rect(geo.X0 + 1, my - 1, 2, 1, 'P');
                g.Rect(geo.X1 - 2, my - 1, 2, 1, 'P');
            }
        }

        public static void DrawHat(Pix g, string model, Geo geo)
        {
            string m = (model ?? "").ToLowerInvariant();
            int cx = (geo.X0 + geo.X1 + 1) / 2, top = geo.Top - 1;
            if (m.Contains("fable")) g.Stamp(Art.Wizard, cx - 4, top - 6);
            else if (m.Contains("opus")) g.Stamp(Art.Crown, cx - 2, top - 4);
            else if (m.Contains("haiku")) g.Stamp(Art.Sprout, cx - 3, top - 4);
            else if (m.Contains("sonnet"))
            {
                // fones de ouvido
                g.Rect(geo.X0, top - 1, geo.X1 - geo.X0 + 1, 1, 'G');
                g.Rect(geo.X0 - 2, geo.Top + 1, 2, 4, 'a');
                g.Rect(geo.X1 + 1, geo.Top + 1, 2, 4, 'a');
            }
        }

        public static void DrawCap(Pix g, Geo geo)
        {
            int x0 = geo.X0 - 1, top = geo.Top - 1;
            g.Rect(x0, top - 1, 12, 2, 'w');
            g.Rect(x0 + 1, top - 3, 9, 2, 'a');
            g.Rect(x0 + 3, top - 5, 7, 2, 'a');
            g.Rect(x0 + 6, top - 6, 5, 1, 'a');
            g.Rect(x0 + 10, top - 6, 3, 3, 'w');
        }

        // Passa a grade para a tela: cada sequência de pixels iguais numa linha vira um retângulo.
        public static void Blit(DrawingContext dc, Pix g, Dictionary<char, Brush> pal, double ox, double oy, double P)
        {
            for (int r = Math.Max(0, g.MinRow); r <= Math.Min(Pix.GH - 1, g.MaxRow); r++)
            {
                int row = r * Pix.GW, c = 0;
                while (c < Pix.GW)
                {
                    char ch = g.Cells[row + c];
                    if (ch == '.') { c++; continue; }
                    int s = c;
                    while (c < Pix.GW && g.Cells[row + c] == ch) c++;
                    Brush b;
                    if (pal.TryGetValue(ch, out b)) dc.DrawRectangle(b, null, new Rect(ox + s * P, oy + r * P, (c - s) * P, P));
                }
            }
        }

        // As peças da skin daquela camada, presas ao corpo desse quadro.
        public static void DrawParts(Pix g, Skin skin, string layer, Geo geo)
        {
            if (skin == null) return;
            int cx = (geo.X0 + geo.X1 + 1) / 2;
            foreach (var part in skin.Parts)
            {
                if (part.Layer != layer) continue;
                int ox, oy = geo.Top;
                switch (part.Anchor)
                {
                    case "base": ox = cx; oy = geo.Bottom; break;
                    case "costas": ox = geo.X0 - 1; break;
                    case "frente": ox = geo.X1 + 1; break;
                    case "corpo": ox = geo.X0; break;
                    default: ox = cx; break;
                }
                g.Stamp(part.Art, ox + part.X, oy + part.Y);
            }
        }

        // O Clawd numa pose qualquer (o escritório da Home usa: sentado, digitando, acenando).
        public static void DrawPose(Pix g, Skin skin, string model, Pose p, double t)
        {
            Geo geo = GeoOf(p);
            DrawParts(g, skin, "tras", geo);
            DrawLegs(g, p, geo, t);
            DrawArm(g, p.ArmL, p.LenL, geo, -1, p.Frame);
            DrawArm(g, p.ArmR, p.LenR, geo, 1, p.Frame);
            DrawBody(g, p, geo);
            DrawParts(g, skin, "corpo", geo);
            DrawFace(g, p, geo);
            DrawParts(g, skin, "frente", geo);
            if (!string.IsNullOrEmpty(model)) DrawHat(g, model, geo);
        }

        // O Clawd parado, de frente para a direita: para as prévias da Central.
        public static void DrawStanding(Pix g, Skin skin, string model, bool blink)
        {
            var p = new Pose();
            p.Mouth = "smile";
            p.Blink = blink;
            Geo geo = GeoOf(p);
            DrawParts(g, skin, "tras", geo);
            DrawLegs(g, p, geo, 0);
            DrawArm(g, p.ArmL, p.LenL, geo, -1, 0);
            DrawArm(g, p.ArmR, p.LenR, geo, 1, 0);
            DrawBody(g, p, geo);
            DrawParts(g, skin, "corpo", geo);
            DrawFace(g, p, geo);
            DrawParts(g, skin, "frente", geo);
            if (!string.IsNullOrEmpty(model)) DrawHat(g, model, geo);
        }
    }
}
