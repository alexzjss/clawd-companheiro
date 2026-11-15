// O que os Clawds da área de trabalho e a Central dos Clawds têm em comum:
//   config.json   as contas (uma por perfil do Claude Desktop), as cores do tema de cada
//                 uma e a aparência do Clawd dela
//   skins         acessórios em pixel art que vestem o Clawd (as de fábrica ficam aqui no
//                 código; as da pasta ~/.claude/clawd/skins/*.json entram na lista também)
//   perfis        as instâncias do Claude Desktop criadas pelo Claude Profile Switcher
//
// O mod clawd-companheiro lê o mesmo config.json para escolher o tema de cada conta.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using System.Windows.Media;

[assembly: System.Reflection.AssemblyTitle("Clawd Companheiro")]
[assembly: System.Reflection.AssemblyProduct("Clawd Companheiro")]
[assembly: System.Reflection.AssemblyVersion("1.0.0.0")]
[assembly: System.Reflection.AssemblyFileVersion("1.0.0.0")]

namespace ClawdDesktop
{
    // ------------------------------------------------------------------- json --

    static class Json
    {
        static readonly JavaScriptSerializer Ser = new JavaScriptSerializer();

        public static Dictionary<string, object> Read(string path)
        {
            try
            {
                string text;
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var sr = new StreamReader(fs, Encoding.UTF8)) text = sr.ReadToEnd();
                return Ser.DeserializeObject(text) as Dictionary<string, object>;
            }
            catch { return null; }
        }

        public static string Str(Dictionary<string, object> d, string k, string def)
        {
            object v;
            if (d != null && d.TryGetValue(k, out v) && v != null) return Convert.ToString(v, CultureInfo.InvariantCulture);
            return def;
        }

        public static bool Bool(Dictionary<string, object> d, string k, bool def)
        {
            object v;
            if (d != null && d.TryGetValue(k, out v) && v is bool) return (bool)v;
            return def;
        }

        public static int Int(Dictionary<string, object> d, string k, int def)
        {
            object v;
            if (d != null && d.TryGetValue(k, out v) && v != null)
            {
                try { return Convert.ToInt32(v, CultureInfo.InvariantCulture); } catch { }
            }
            return def;
        }

        public static Dictionary<string, object> Obj(Dictionary<string, object> d, string k)
        {
            object v;
            if (d != null && d.TryGetValue(k, out v)) return v as Dictionary<string, object>;
            return null;
        }

        public static object[] Arr(Dictionary<string, object> d, string k)
        {
            object v;
            if (d != null && d.TryGetValue(k, out v) && v is object[]) return (object[])v;
            return new object[0];
        }

        // Escreve com recuo, para dar para ler e editar à mão.
        public static string Write(object o)
        {
            var sb = new StringBuilder();
            Write(sb, o, 0);
            return sb.ToString();
        }

        static void Write(StringBuilder sb, object o, int depth)
        {
            var d = o as Dictionary<string, object>;
            var l = o as System.Collections.IList;
            string pad = new string(' ', (depth + 1) * 2), end = new string(' ', depth * 2);
            if (d != null)
            {
                if (d.Count == 0) { sb.Append("{}"); return; }
                sb.Append("{\n");
                int i = 0;
                foreach (var kv in d)
                {
                    sb.Append(pad).Append(Ser.Serialize(kv.Key)).Append(": ");
                    Write(sb, kv.Value, depth + 1);
                    sb.Append(++i < d.Count ? ",\n" : "\n");
                }
                sb.Append(end).Append('}');
            }
            else if (l != null && !(o is string))
            {
                bool flat = true;
                foreach (object x in l) if (x is Dictionary<string, object> || (x is System.Collections.IList && !(x is string))) flat = false;
                if (l.Count == 0) { sb.Append("[]"); return; }
                if (flat && l.Count <= 2) { sb.Append(Ser.Serialize(o)); return; }
                sb.Append("[\n");
                for (int i = 0; i < l.Count; i++)
                {
                    sb.Append(pad);
                    Write(sb, l[i], depth + 1);
                    sb.Append(i + 1 < l.Count ? ",\n" : "\n");
                }
                sb.Append(end).Append(']');
            }
            else sb.Append(Ser.Serialize(o));
        }
    }

    // ------------------------------------------------------------------ cores --

    static class Tint
    {
        public static bool IsHex(string s) { return s != null && Regex.IsMatch(s, "^#[0-9a-fA-F]{6}$"); }

        public static Color Parse(string hex, string fallback)
        {
            if (!IsHex(hex)) hex = fallback;
            return Color.FromRgb(Convert.ToByte(hex.Substring(1, 2), 16), Convert.ToByte(hex.Substring(3, 2), 16), Convert.ToByte(hex.Substring(5, 2), 16));
        }

        public static string Hex(Color c) { return "#" + c.R.ToString("x2") + c.G.ToString("x2") + c.B.ToString("x2"); }

        public static void ToHsl(Color c, out double h, out double s, out double l)
        {
            double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
            double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), d = max - min;
            l = (max + min) / 2;
            if (d < 1e-9) { h = 0; s = 0; return; }
            s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
            if (max == r) h = ((g - b) / d + (g < b ? 6 : 0)) * 60;
            else if (max == g) h = ((b - r) / d + 2) * 60;
            else h = ((r - g) / d + 4) * 60;
        }

        public static Color FromHsl(double h, double s, double l)
        {
            h = ((h % 360) + 360) % 360;
            s = Math.Max(0, Math.Min(1, s));
            l = Math.Max(0, Math.Min(1, l));
            double c = (1 - Math.Abs(2 * l - 1)) * s, x = c * (1 - Math.Abs((h / 60) % 2 - 1)), m = l - c / 2;
            double r = 0, g = 0, b = 0;
            if (h < 60) { r = c; g = x; }
            else if (h < 120) { r = x; g = c; }
            else if (h < 180) { g = c; b = x; }
            else if (h < 240) { g = x; b = c; }
            else if (h < 300) { r = x; b = c; }
            else { r = c; b = x; }
            return Color.FromRgb((byte)Math.Round((r + m) * 255), (byte)Math.Round((g + m) * 255), (byte)Math.Round((b + m) * 255));
        }

        public static string Hsl(double h, double s, double l) { return Hex(FromHsl(h, s, l)); }

        // As cores de um Clawd a partir de duas: a do corpo (as sombras, o brilho e o
        // contorno saem dela) e a dos detalhes.
        public static Dictionary<char, Brush> Palette(string body, string detail)
        {
            Color c = Parse(body, "#d97757");
            double h, s, l;
            ToHsl(c, out h, out s, out l);
            string light = Hsl(h, s * 0.85, Math.Min(0.95, l + 0.25));
            string dark = Hsl(h, s, Math.Max(0.04, l - 0.10));
            string ink = Hsl(h, Math.Min(1, s * 1.05), Math.Max(0.04, Math.Min(0.2, l * 0.35)));
            var pal = Pal.Make(Hex(c), light, dark, ink, IsHex(detail) ? detail : "#ffd23f");
            // num corpo escuro, olhos e boca claros para aparecerem
            if (l < 0.22) pal['e'] = Pal.B("#f4f1ea", 1);
            return pal;
        }

        // As cores de fundo e de texto de um tema a partir do matiz de uma cor. É a mesma
        // conta que o mod faz (hooks/themes.ts), para a prévia da Central bater com o app.
        public static Dictionary<string, string> Surfaces(string tint, bool dark)
        {
            double h, s, l;
            ToHsl(Parse(tint, "#888888"), out h, out s, out l);
            double k = Math.Min(1, s / 0.6);
            var d = new Dictionary<string, string>();
            if (dark)
            {
                d["surface-0"] = Hsl(h, 0.56 * k, 0.09);
                d["surface-1"] = Hsl(h, 0.50 * k, 0.12);
                d["surface-2"] = Hsl(h, 0.46 * k, 0.15);
                d["surface-3"] = Hsl(h, 0.44 * k, 0.17);
                d["text-primary"] = Hsl(h, 0.45 * k, 0.93);
                d["text-secondary"] = Hsl(h, 0.30 * k, 0.78);
                d["text-muted"] = Hsl(h, 0.22 * k, 0.64);
            }
            else
            {
                d["surface-0"] = Hsl(h, 0.40 * k, 0.93);
                d["surface-1"] = Hsl(h, 0.42 * k, 0.955);
                d["surface-2"] = Hsl(h, 0.45 * k, 0.98);
                d["text-primary"] = Hsl(h, 0.60 * k, 0.12);
                d["text-secondary"] = Hsl(h, 0.33 * k, 0.26);
                d["text-muted"] = Hsl(h, 0.25 * k, 0.36);
            }
            return d;
        }
    }

    // ------------------------------------------------------------------ skins --

    // Uma peça de pixel art presa a um ponto do corpo. As letras da arte são as da paleta:
    // b corpo, l brilho, d sombra, k contorno, a detalhe, w branco, P rosa, y amarelo...
    sealed class SkinPart
    {
        public string Layer = "frente";   // tras (atrás do corpo), corpo (por cima do corpo, sob o rosto), frente
        public string Anchor = "topo";    // topo, base (centro em x), costas, frente (a borda do corpo), corpo (canto de cima)
        public int X, Y;
        public string[] Art = new string[0];
    }

    sealed class Skin
    {
        public string Id, Name;
        public bool Custom;
        public readonly List<SkinPart> Parts = new List<SkinPart>();

        Skin Add(string layer, string anchor, int x, int y, params string[] art)
        {
            Parts.Add(new SkinPart { Layer = layer, Anchor = anchor, X = x, Y = y, Art = art });
            return this;
        }

        static Skin New(string id, string name) { return new Skin { Id = id, Name = name }; }

        // As skins de fábrica. O corpo vai de x=-8 a 7 (o centro é 0) e tem 9 linhas.
        public static List<Skin> Builtin()
        {
            var l = new List<Skin>();
            l.Add(New("classico", "Clássico"));
            l.Add(New("gato", "Gatinho")
                .Add("tras", "topo", -8, -5, "..k..", ".kbk.", "kbPbk", "kbPbk")
                .Add("tras", "topo", 3, -5, "..k..", ".kbk.", "kbPbk", "kbPbk")
                .Add("tras", "costas", -6, 2, "kk....", "kbk...", "kbk...", ".kbk..", ".kbbkk", "..kbbb", "...kkk"));
            l.Add(New("coelho", "Coelho")
                .Add("tras", "topo", -6, -8, ".k.", "kbk", "kPk", "kPk", "kPk", "kbk", "kbk")
                .Add("tras", "topo", 3, -8, ".k.", "kbk", "kPk", "kPk", "kPk", "kbk", "kbk")
                .Add("tras", "costas", -3, 5, ".kk.", "kwwk", "kwwk", ".kk."));
            l.Add(New("urso", "Ursinho")
                .Add("tras", "topo", -8, -4, ".kk.", "kbbk", "kdbk")
                .Add("tras", "topo", 4, -4, ".kk.", "kbbk", "kbdk")
                .Add("corpo", "corpo", 5, 5, ".llll.", "llllll", "llllll", ".llll."));
            l.Add(New("robo", "Robô")
                .Add("tras", "topo", -2, -6, ".aa.", ".aa.", ".kk.", ".kk.", ".kk.")
                .Add("corpo", "corpo", 0, 0, "gggggggggggggggg")
                .Add("corpo", "corpo", 0, 8, "GGGGGGGGGGGGGGGG")
                .Add("corpo", "corpo", 10, 7, "r.y.n")
                .Add("corpo", "corpo", 1, 7, "GGG"));
            l.Add(New("diabinho", "Diabinho")
                .Add("tras", "topo", -8, -4, "kk..", "kak.", "kaak")
                .Add("tras", "topo", 4, -4, "..kk", ".kak", "kaak")
                .Add("tras", "costas", -5, 2, ".k...", "kak..", "kaak.", ".kk..", "..ak.", "...ak", "....a"));
            l.Add(New("anjo", "Anjinho")
                .Add("frente", "topo", -4, -5, ".yyyyyy.", "y......y", ".yyyyyy.")
                .Add("tras", "costas", -5, 0, "..ww.", ".wwww", "wwwiw", "wwiww", ".wwww", "..wi."));
            l.Add(New("dino", "Dino")
                .Add("tras", "topo", -7, -3, ".a...a...a...a.", "aaa.aaa.aaa.aaa")
                .Add("tras", "costas", -7, 4, ".....kk", "...kkbb", ".kkbbbb", "kbbbbbb", "kkkkkkk"));
            l.Add(New("unicornio", "Unicórnio")
                .Add("frente", "topo", 2, -5, "...y", "..yw", ".yy.", "yy..")
                .Add("tras", "topo", -8, -3, ".PPcc...", "PPccvv..", "PccvvPP.")
                .Add("tras", "costas", -4, 3, ".PP.", "PccP", "cvvc", ".PP.", ".cv."));
            l.Add(New("bandana", "Bandana")
                .Add("corpo", "corpo", 0, 0, "aaaaaaaaaaaaaaaa", "aaaaaaaaaaaaaaaa")
                .Add("tras", "costas", -3, 0, "aa..", ".aaa", "aa..", "a..."));
            l.Add(New("cachecol", "Cachecol")
                .Add("corpo", "corpo", 0, 7, "aaaaaaaaaaaaaaaa", "aaaaaaaaaaaaaaaa")
                .Add("frente", "corpo", 2, 9, "aaa", "aaa", "a.a"));
            l.Add(New("flor", "Florzinha")
                .Add("frente", "topo", -7, -3, ".P.P.", "PPyPP", ".PyP.", "..n..")
                .Add("corpo", "corpo", 1, 5, "PP")
                .Add("corpo", "corpo", 13, 5, "PP"));
            return l;
        }

        static Skin FromFile(string path)
        {
            var d = Json.Read(path);
            if (d == null) return null;
            var s = new Skin();
            s.Id = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
            s.Name = Json.Str(d, "nome", s.Id);
            s.Custom = true;
            foreach (object o in Json.Arr(d, "partes"))
            {
                var x = o as Dictionary<string, object>;
                if (x == null) continue;
                var p = new SkinPart();
                p.Layer = Json.Str(x, "camada", "frente");
                p.Anchor = Json.Str(x, "ancora", "topo");
                p.X = Json.Int(x, "x", 0);
                p.Y = Json.Int(x, "y", 0);
                p.Art = Json.Arr(x, "arte").Select(r => Convert.ToString(r)).Where(r => r.Length <= 40).Take(40).ToArray();
                s.Parts.Add(p);
            }
            return s;
        }

        // Todas as skins: as de fábrica e as da pasta (que não trocam uma de fábrica).
        public static List<Skin> LoadAll(string dir)
        {
            var all = Builtin();
            try
            {
                if (Directory.Exists(dir))
                    foreach (string f in Directory.GetFiles(dir, "*.json").OrderBy(x => x))
                    {
                        var s = FromFile(f);
                        if (s != null && !all.Any(x => x.Id == s.Id)) all.Add(s);
                    }
            }
            catch { }
            return all;
        }

        public static Skin Find(List<Skin> all, string id)
        {
            foreach (var s in all) if (s.Id == id) return s;
            return all[0];
        }

        // Deixa na pasta um exemplo e as instruções para criar skins novas.
        public static void WriteGuide(string dir)
        {
            try
            {
                Directory.CreateDirectory(dir);
                string guide = Path.Combine(dir, "LEIA-ME.txt");
                if (!File.Exists(guide))
                    File.WriteAllText(guide,
                        "Skins dos Clawds\r\n================\r\n\r\n" +
                        "Cada arquivo .json desta pasta vira uma skin na Central dos Clawds (o nome do arquivo é o id).\r\n" +
                        "Uma skin é uma lista de peças de pixel art presas a um ponto do corpo:\r\n\r\n" +
                        "  camada  tras (atrás do corpo), corpo (por cima do corpo, sob o rosto) ou frente\r\n" +
                        "  ancora  topo ou base (x=0 é o centro do corpo), costas ou frente (a borda do corpo),\r\n" +
                        "          corpo (o canto de cima, do lado das costas)\r\n" +
                        "  x, y    onde fica o canto de cima da arte a partir da âncora (y negativo sobe)\r\n" +
                        "  arte    as linhas; cada letra é uma cor e o ponto é transparente\r\n\r\n" +
                        "O corpo tem 16 pixels de largura (x de -8 a 7) e 9 de altura. O Clawd olha para a direita;\r\n" +
                        "quando ele vira, a skin espelha junto.\r\n\r\n" +
                        "Cores: b corpo, l brilho, d sombra, k contorno, a detalhe (as cinco mudam com a conta),\r\n" +
                        "w branco, i gelo, g cinza claro, G cinza, h grafite, x preto, y amarelo, Y ocre, f laranja,\r\n" +
                        "r vermelho, R vinho, P rosa, v roxo, V roxo escuro, u azul, U azul escuro, c azul claro,\r\n" +
                        "n verde, N verde escuro, o madeira, O madeira escura, p papel.\r\n", Encoding.UTF8);
                string sample = Path.Combine(dir, "exemplo-cartola.json");
                if (!File.Exists(sample))
                    File.WriteAllText(sample,
                        "{\n  \"nome\": \"Cartola\",\n  \"partes\": [\n" +
                        "    { \"camada\": \"frente\", \"ancora\": \"topo\", \"x\": -5, \"y\": -7,\n" +
                        "      \"arte\": [\"..xxxxxx..\", \"..xxxxxx..\", \"..xxxxxx..\", \"..xxxxxx..\", \"..aaaaaa..\", \"xxxxxxxxxx\"] }\n" +
                        "  ]\n}\n", new UTF8Encoding(false));
            }
            catch { }
        }
    }

    // ----------------------------------------------------------------- perfis --

    // Uma instância do Claude Desktop: a padrão do app ou uma criada pelo Profile Switcher.
    sealed class ProfileInfo
    {
        public string Id, Label, Folder;
        public bool IsDefault;
    }

    static class Profiles
    {
        const string Prefix = "ClaudeProfile-";

        static string Local { get { return Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData); } }
        public static string Root { get { return Path.Combine(Local, "ClaudeProfiles"); } }
        public static string SwitcherScript { get { return Path.Combine(Root, ".switcher", "ClaudeSwitcher.ps1"); } }
        public static bool HasSwitcher { get { return File.Exists(SwitcherScript); } }

        public static List<ProfileInfo> List()
        {
            var settings = Json.Read(Path.Combine(Root, "settings.json"));
            var labels = Json.Obj(settings, "Labels");
            var list = new List<ProfileInfo>();
            string def = Json.Str(Json.Obj(settings, "InstallCache"), "DefaultProfilePath", null);
            if (string.IsNullOrEmpty(def)) def = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Claude");
            list.Add(new ProfileInfo { Id = "Default", Label = Json.Str(labels, "Default", "Claude"), Folder = def, IsDefault = true });
            var ids = new List<string>();
            try
            {
                foreach (string d in Directory.GetDirectories(Local, Prefix + "*")) ids.Add(Path.GetFileName(d).Substring(Prefix.Length));
                // perfis que ainda estão na pasta antiga do Switcher
                if (Directory.Exists(Root))
                    foreach (string d in Directory.GetDirectories(Root))
                    {
                        string n = Path.GetFileName(d);
                        if (!n.StartsWith(".") && (new DirectoryInfo(d).Attributes & FileAttributes.ReparsePoint) == 0) ids.Add(n);
                    }
            }
            catch { }
            foreach (string id in ids.Where(x => x.Length > 0 && x != "Default").Distinct().OrderBy(x => x))
            {
                string folder = Path.Combine(Local, Prefix + id);
                if (!Directory.Exists(folder)) folder = Path.Combine(Root, id);
                list.Add(new ProfileInfo { Id = id, Label = Json.Str(labels, id, id), Folder = folder });
            }
            return list;
        }

        // Quais perfis estão abertos agora, e o processo principal de cada um.
        public static Dictionary<string, int> Running()
        {
            var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using (var q = new ManagementObjectSearcher("SELECT ProcessId, CommandLine, ExecutablePath FROM Win32_Process WHERE Name='Claude.exe'"))
                    foreach (ManagementObject o in q.Get())
                    {
                        string cl = (o["CommandLine"] as string) ?? "", exe = (o["ExecutablePath"] as string) ?? "";
                        // Só a janela principal do app: os filhos têm --type= e o Claude Code também se chama claude.exe.
                        if (cl.Length == 0 || cl.Contains("--type=") || exe.IndexOf("\\WindowsApps\\Claude_", StringComparison.OrdinalIgnoreCase) < 0) continue;
                        string id = "Default";
                        Match m = Regex.Match(cl, "--user-data-dir=(?:\"([^\"]+)\"|(\\S+))");
                        if (m.Success)
                        {
                            string dir = (m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value).TrimEnd('\\');
                            string leaf = Path.GetFileName(dir);
                            if (leaf.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)) id = leaf.Substring(Prefix.Length);
                            else if (dir.IndexOf("\\Packages\\Claude_", StringComparison.OrdinalIgnoreCase) >= 0) id = "Default";
                            else id = leaf;
                        }
                        if (!map.ContainsKey(id)) map[id] = Convert.ToInt32(o["ProcessId"]);
                    }
            }
            catch { }
            return map;
        }

        static string StartMenu { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft", "Windows", "Start Menu", "Programs"); } }

        static void RunSwitcher(string args)
        {
            var psi = new ProcessStartInfo("powershell.exe", "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"" + SwitcherScript + "\" " + args);
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            Process.Start(psi);
        }

        // Abre o Claude daquele perfil: pelo atalho do menu Iniciar, que já cuida do ícone
        // na barra de tarefas, ou pelo Switcher.
        public static void Launch(string id, string label)
        {
            string lnk = Path.Combine(StartMenu, "Claude - " + label + ".lnk");
            if (File.Exists(lnk)) Process.Start(new ProcessStartInfo(lnk) { UseShellExecute = true });
            else if (HasSwitcher) RunSwitcher("-Launch \"" + id + "\"");
            else if (id == "Default") Process.Start(new ProcessStartInfo("shell:AppsFolder\\Claude_pzs8sxrjxfjjc!Claude") { UseShellExecute = true });
            else throw new InvalidOperationException("O Claude Profile Switcher não está instalado.");
        }

        public static void AddAccount() { RunSwitcher("-AddAccount"); }
        public static void OpenSwitcher() { RunSwitcher(""); }
    }

    // ----------------------------------------------------------------- config --

    sealed class AccountCfg
    {
        public string Key, Profile, Name, Emails = "";
        // tema do Claude
        public bool ThemeOn = true, ThemeDefault = true;
        public string Accent = "#d97757", Surface = "#d97757", Warning = "#c9801a", Success = "#2f8a5b", Danger = "#c23b32", Pro = "#3f6db3";
        // Clawd da área de trabalho
        public bool ShowClawd = true, Hat = true;
        public string Skin = "classico", PupSkin = "";
        public string Body = "#d97757", Detail = "#ffd23f";

        public Dictionary<string, object> ToDict()
        {
            var t = new Dictionary<string, object>();
            t["ativo"] = ThemeOn; t["padrao"] = ThemeDefault; t["principal"] = Accent; t["fundo"] = Surface;
            t["aviso"] = Warning; t["sucesso"] = Success; t["erro"] = Danger; t["pro"] = Pro;
            var c = new Dictionary<string, object>();
            c["mostrar"] = ShowClawd; c["skin"] = Skin; c["skinFilhotes"] = PupSkin; c["corpo"] = Body; c["detalhe"] = Detail; c["chapeu"] = Hat;
            var d = new Dictionary<string, object>();
            d["chave"] = Key; d["perfil"] = Profile; d["nome"] = Name; d["emails"] = Emails; d["tema"] = t; d["clawd"] = c;
            return d;
        }

        static string HexOr(Dictionary<string, object> d, string k, string def)
        {
            string v = Json.Str(d, k, def);
            return Tint.IsHex(v) ? v.ToLowerInvariant() : def;
        }

        public static AccountCfg FromDict(Dictionary<string, object> d)
        {
            var a = new AccountCfg();
            a.Key = Json.Str(d, "chave", "");
            a.Profile = Json.Str(d, "perfil", "");
            a.Name = Json.Str(d, "nome", a.Key);
            a.Emails = Json.Str(d, "emails", "");
            var t = Json.Obj(d, "tema");
            a.ThemeOn = Json.Bool(t, "ativo", true);
            a.ThemeDefault = Json.Bool(t, "padrao", true);
            a.Accent = HexOr(t, "principal", a.Accent);
            a.Surface = HexOr(t, "fundo", a.Accent);
            a.Warning = HexOr(t, "aviso", a.Warning);
            a.Success = HexOr(t, "sucesso", a.Success);
            a.Danger = HexOr(t, "erro", a.Danger);
            a.Pro = HexOr(t, "pro", a.Pro);
            var c = Json.Obj(d, "clawd");
            a.ShowClawd = Json.Bool(c, "mostrar", true);
            a.Hat = Json.Bool(c, "chapeu", true);
            a.Skin = Json.Str(c, "skin", "classico");
            a.PupSkin = Json.Str(c, "skinFilhotes", "");
            a.Body = HexOr(c, "corpo", a.Accent);
            a.Detail = HexOr(c, "detalhe", a.Detail);
            return a;
        }
    }

    sealed class Cfg
    {
        public string Corner = "direito";
        public bool Events = true;
        public readonly List<AccountCfg> Accounts = new List<AccountCfg>();

        public static string Dir { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "clawd"); } }
        public static string FilePath { get { return Path.Combine(Dir, "config.json"); } }
        public static string SkinsDir { get { return Path.Combine(Dir, "skins"); } }

        // Cores de partida para contas novas, em rodízio: principal, detalhe.
        public static readonly string[][] Starters =
        {
            new[] { "#d97757", "#ffd23f" }, new[] { "#2f9e6b", "#ffd23f" }, new[] { "#e0559a", "#7fd6ff" },
            new[] { "#3f6db3", "#ff9f43" }, new[] { "#c2412f", "#ffd23f" }, new[] { "#5d6672", "#5ee08a" },
        };

        static string Slug(string s)
        {
            string n = (s ?? "").Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder();
            foreach (char ch in n)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
                if (ch < 128 && char.IsLetterOrDigit(ch)) sb.Append(char.ToLowerInvariant(ch));
                else if (sb.Length > 0 && sb[sb.Length - 1] != '-') sb.Append('-');
            }
            string r = sb.ToString().Trim('-');
            return r.Length == 0 ? "conta" : r;
        }

        // A conta de um perfil que ainda não está na config. USP e Pessoal nascem com as
        // cores que sempre tiveram.
        AccountCfg NewFor(ProfileInfo p)
        {
            var a = new AccountCfg();
            string key = Slug(p.Label);
            if (key == "outra") key = "conta";
            string k = key;
            for (int i = 2; Accounts.Any(x => x.Key == k); i++) k = key + "-" + i;
            a.Key = k;
            a.Profile = p.Id;
            a.Name = p.Label;
            if (k == "usp")
            {
                a.Emails = "usp.br";
                a.Accent = a.Surface = a.Body = "#1094ab";
                a.Warning = a.Detail = "#fcb421"; a.Success = "#2f8a5b"; a.Danger = "#c23b32"; a.Pro = "#3f6db3";
            }
            else if (k == "pessoal")
            {
                a.Emails = Accounts.Any(x => x.Emails.Trim() == "*") ? "" : "*";
                a.Accent = a.Surface = a.Body = "#8b30e6";
                a.Warning = "#c9801a"; a.Success = "#3d8b5f"; a.Danger = "#c23a4b"; a.Pro = "#d03fe0"; a.Detail = "#ff7eb6";
            }
            else
            {
                string[] st = Starters[Accounts.Count(x => x.Key != "usp" && x.Key != "pessoal") % Starters.Length];
                a.Accent = a.Surface = a.Body = st[0];
                a.Detail = st[1];
                a.ThemeDefault = false;
            }
            return a;
        }

        // Lê a config e a acerta com os perfis que existem: perfil novo ganha uma conta,
        // conta de perfil apagado sai, e o nome acompanha o do Switcher.
        public static Cfg Load()
        {
            var c = new Cfg();
            var d = Json.Read(FilePath);
            if (d == null)
            {
                // antes da Central, o canto ficava em desktop.json
                c.Corner = Json.Str(Json.Read(Path.Combine(Dir, "desktop.json")), "canto", "direito");
            }
            else
            {
                c.Corner = Json.Str(d, "canto", "direito") == "esquerdo" ? "esquerdo" : "direito";
                c.Events = Json.Bool(d, "eventos", true);
                foreach (object o in Json.Arr(d, "contas"))
                {
                    var x = o as Dictionary<string, object>;
                    if (x == null) continue;
                    var a = AccountCfg.FromDict(x);
                    if (a.Key.Length > 0 && Regex.IsMatch(a.Key, "^[a-z0-9-]{1,40}$") && !c.Accounts.Any(y => y.Key == a.Key)) c.Accounts.Add(a);
                }
            }
            var profiles = Profiles.List();
            c.Accounts.RemoveAll(a => !profiles.Any(p => p.Id == a.Profile));
            foreach (var p in profiles)
            {
                var a = c.Accounts.FirstOrDefault(x => x.Profile == p.Id);
                if (a == null) c.Accounts.Add(c.NewFor(p));
                else a.Name = p.Label;
            }
            return c;
        }

        public void Save()
        {
            var d = new Dictionary<string, object>();
            d["versao"] = 1;
            d["canto"] = Corner;
            d["eventos"] = Events;
            d["contas"] = Accounts.Select(a => (object)a.ToDict()).ToList();
            Directory.CreateDirectory(Dir);
            string tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, Json.Write(d) + "\n", new UTF8Encoding(false));
            // troca de uma vez, para ninguém ler o arquivo pela metade
            if (File.Exists(FilePath)) File.Replace(tmp, FilePath, null);
            else File.Move(tmp, FilePath);
        }

        public static DateTime Stamp()
        {
            try { return File.Exists(FilePath) ? File.GetLastWriteTimeUtc(FilePath) : DateTime.MinValue; }
            catch { return DateTime.MinValue; }
        }
    }
}
