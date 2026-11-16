// Os Clawds em pixel art: um por conta do Claude, nas cores de cada uma. Fora da Home,
// moram numa faixa no canto da tela, bem acima da barra de tarefas; com a Home do app
// aberta, mudam-se para lá (AttachHome) e voltam quando ela fecha (DetachHome).
//
// Lê o que as sessões do mod clawd-companheiro gravam em ~/.claude/clawd:
//   sessions/<id>.json     estado de cada sessão: ferramenta, tipo de comando/arquivo,
//                          subagentes, modelo, contexto, custo
//   memoria-<conta>.json   memória da conta (humor)
//   uso-<conta>.json       limites de uso da conta
// e avisa o app a cada leitura (SessionsRead), para a Home, os avisos e o histórico.
//
// O Clawd grande é a sessão mais antiga aberta na conta; cada outra sessão ativa vira um
// filhote. Passar o mouse: painel curto com sessões, limites e custo. Clique: carinho.
// Duplo clique: abre o Claude da conta. Arrastar: ele balança e cai. Botão direito: menu.
//
// Tudo é desenhado numa grade de pixels e ampliado por um fator inteiro, sem suavização.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace ClawdDesktop
{
    // ------------------------------------------------------------------ Windows --

    static class Native
    {
        [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int i);
        [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr h, int i, int v);
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder s, int n);
        [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr h, uint flags);
        [DllImport("user32.dll")] static extern bool GetMonitorInfo(IntPtr m, ref MONITORINFO mi);

        [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] struct MONITORINFO { public int cbSize; public RECT rcMonitor; public RECT rcWork; public uint dwFlags; }

        // WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW: não rouba o foco nem aparece no Alt+Tab.
        public static void MakeOverlay(Window w)
        {
            IntPtr h = new WindowInteropHelper(w).Handle;
            SetWindowLong(h, -20, GetWindowLong(h, -20) | 0x08000000 | 0x80);
        }

        public static void KeepOnTop(Window w)
        {
            IntPtr h = new WindowInteropHelper(w).Handle;
            if (h != IntPtr.Zero) SetWindowPos(h, new IntPtr(-1), 0, 0, 0, 0, 0x0013);
        }

        public static bool ForegroundIsFullscreen(params Window[] mine)
        {
            try
            {
                IntPtr h = GetForegroundWindow();
                if (h == IntPtr.Zero) return false;
                foreach (Window w in mine) if (new WindowInteropHelper(w).Handle == h) return false;
                var sb = new StringBuilder(64);
                GetClassName(h, sb, 64);
                string cls = sb.ToString();
                if (cls == "Progman" || cls == "WorkerW" || cls == "Shell_TrayWnd") return false;
                RECT r;
                if (!GetWindowRect(h, out r)) return false;
                var mi = new MONITORINFO();
                mi.cbSize = Marshal.SizeOf(typeof(MONITORINFO));
                if (!GetMonitorInfo(MonitorFromWindow(h, 2), ref mi)) return false;
                return r.Left <= mi.rcMonitor.Left && r.Top <= mi.rcMonitor.Top && r.Right >= mi.rcMonitor.Right && r.Bottom >= mi.rcMonitor.Bottom;
            }
            catch { return false; }
        }
    }

    // ------------------------------------------------------------------- dados --

    sealed class SessionView
    {
        public string Id, Account, Label, State, Tool, Detail, Model;
        public double StartedAt, UpdatedAt, EndedAt, TurnStartedAt, Turns, Agents;
        public double Cost = double.NaN, Ctx = double.NaN;
        public double BusyMs = double.NaN, Done = double.NaN, Failed = double.NaN;
        public Dictionary<string, double> Tools;   // "Bash:git" -> chamadas (mods antigos não mandam)

        public bool IsActive(double nowMs) { return EndedAt <= 0 && nowMs - UpdatedAt < 180000; }

        public static SessionView From(Dictionary<string, object> d)
        {
            if (d == null) return null;
            var s = new SessionView();
            s.Id = Data.Str(d, "id", null);
            if (s.Id == null) return null;
            s.Account = Data.Str(d, "account", "");
            s.Label = Data.Str(d, "label", "sessão");
            s.State = Data.Str(d, "state", "ocioso");
            s.Tool = Data.Str(d, "tool", "");
            s.Detail = Data.Str(d, "detail", "");
            s.Model = Data.Str(d, "model", "");
            s.StartedAt = Data.Num(d, "startedAt", 0);
            s.UpdatedAt = Data.Num(d, "updatedAt", 0);
            s.EndedAt = Data.Num(d, "endedAt", 0);
            s.TurnStartedAt = Data.Num(d, "turnStartedAt", 0);
            s.Turns = Data.Num(d, "turns", 0);
            s.Agents = Data.Num(d, "agents", 0);
            s.Cost = Data.Num(d, "costUsd", double.NaN);
            s.Ctx = Data.Num(d, "contextPercent", double.NaN);
            s.BusyMs = Data.Num(d, "busyMs", double.NaN);
            s.Done = Data.Num(d, "done", double.NaN);
            s.Failed = Data.Num(d, "failed", double.NaN);
            object tools;
            var td = d.TryGetValue("tools", out tools) ? tools as Dictionary<string, object> : null;
            if (td != null)
            {
                s.Tools = new Dictionary<string, double>();
                foreach (var kv in td.Take(200)) s.Tools[kv.Key] = Data.Num(td, kv.Key, 0);
            }
            return s;
        }
    }

    sealed class Limit
    {
        public string Kind;
        public double Pct;
        public DateTime? Resets;
    }

    sealed class Usage
    {
        public double UpdatedAt;
        public List<Limit> Limits = new List<Limit>();

        public double FiveHour()
        {
            foreach (var l in Limits) if (l.Kind == "five_hour") return l.Pct;
            return 0;
        }
    }

    static class Data
    {
        static readonly JavaScriptSerializer Json = new JavaScriptSerializer();
        static readonly Dictionary<string, Dictionary<string, object>> cache = new Dictionary<string, Dictionary<string, object>>();

        public static double NowMs() { return (DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds; }

        // Lê um JSON pequeno; se o arquivo estiver sendo escrito agora, usa a última leitura boa.
        public static Dictionary<string, object> Read(string path)
        {
            try
            {
                string text;
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var sr = new StreamReader(fs, Encoding.UTF8)) text = sr.ReadToEnd();
                var d = Json.DeserializeObject(text) as Dictionary<string, object>;
                if (d != null) { cache[path] = d; return d; }
            }
            catch { }
            Dictionary<string, object> old;
            return cache.TryGetValue(path, out old) ? old : null;
        }

        public static double Num(Dictionary<string, object> d, string k, double def)
        {
            object v;
            if (d != null && d.TryGetValue(k, out v) && v != null)
            {
                try { return Convert.ToDouble(v, CultureInfo.InvariantCulture); } catch { }
            }
            return def;
        }

        public static string Str(Dictionary<string, object> d, string k, string def)
        {
            object v;
            if (d != null && d.TryGetValue(k, out v) && v != null) return Convert.ToString(v, CultureInfo.InvariantCulture);
            return def;
        }

        public static Usage UsageFrom(Dictionary<string, object> d)
        {
            if (d == null) return null;
            var u = new Usage();
            u.UpdatedAt = Num(d, "updatedAt", 0);
            object raw;
            if (d.TryGetValue("rateLimits", out raw) && raw is object[])
            {
                foreach (object o in (object[])raw)
                {
                    var x = o as Dictionary<string, object>;
                    if (x == null) continue;
                    var l = new Limit();
                    l.Kind = Str(x, "kind", "?");
                    l.Pct = Num(x, "percentUsed", 0);
                    string r = Str(x, "resetsAt", null);
                    DateTime dt;
                    if (r != null && DateTime.TryParse(r, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out dt)) l.Resets = dt.ToLocalTime();
                    u.Limits.Add(l);
                }
            }
            return u;
        }
    }

    static class Words
    {
        public static readonly CultureInfo PtBR = new CultureInfo("pt-BR");

        // A animação de trabalho para a ferramenta e o detalhe que o mod mandou.
        public static string WorkAnim(string tool, string detail)
        {
            string t = (tool ?? "").ToLowerInvariant();
            if (t == "bash" || t == "powershell")
            {
                switch (detail)
                {
                    case "git": return "git";
                    case "teste": return "test";
                    case "instalar": return "install";
                    case "compilar": return "build";
                    case "apagar": return "trash";
                    case "docker": return "stack";
                    case "rede": return "net";
                    case "olhar": return "peek";
                    case "rodar": return "rocket";
                }
                return t == "powershell" ? "ps" : "bash";
            }
            if (t == "bashoutput" || t == "killshell" || t == "monitor") return "bash";
            if (t == "read" || t == "notebookread") return "read";
            if (t == "grep") return "grep";
            if (t == "glob" || t == "ls") return "glob";
            if (t == "edit" || t == "multiedit") return detail == "imagem" ? "paint" : "edit";
            if (t == "write") return detail == "imagem" ? "paint" : "write";
            if (t == "notebookedit") return "grid";
            if (t == "websearch") return "search";
            if (t == "webfetch") return "fetch";
            if (t.Contains("computer-use") || t.Contains("computer_use")) return t.Contains("screenshot") || t.Contains("zoom") ? "camera" : "mouse";
            if (t.Contains("browser") || t.Contains("chrome") || t.Contains("navigate") || t.Contains("playwright")) return "browser";
            if (t == "agent" || t == "task") return "agent";
            if (t == "workflow") return "conduct";
            if (Regex.IsMatch(t, "todowrite|update_plan|wrap_up|^task(create|update|list|get)$")) return "plan";
            if (t == "skill") return "spell";
            if (t == "toolsearch") return "toolbox";
            if (t.StartsWith("mcp__")) return "plug";
            return "tool";
        }

        public static string ShortTool(string tool)
        {
            if (string.IsNullOrEmpty(tool)) return "";
            if (tool.StartsWith("mcp__"))
            {
                string[] parts = tool.Split(new[] { "__" }, StringSplitOptions.RemoveEmptyEntries);
                return parts.Length > 0 ? parts[parts.Length - 1].Replace('_', ' ') : tool;
            }
            return tool;
        }

        static readonly Dictionary<string, string> AnimSay = new Dictionary<string, string>
        {
            { "bash", "No terminal…" }, { "ps", "No PowerShell…" }, { "git", "Mexendo no git…" }, { "test", "Rodando os testes…" },
            { "install", "Instalando pacotes…" }, { "build", "Compilando…" }, { "trash", "Faxina!" }, { "stack", "Empilhando contêineres…" },
            { "net", "Falando com a rede…" }, { "peek", "Dando uma olhada…" }, { "rocket", "Rodando o programa!" },
            { "read", "Lendo…" }, { "grep", "Procurando no código…" }, { "glob", "Procurando arquivos…" }, { "edit", "Editando…" },
            { "paint", "Mexendo na imagem…" }, { "write", "Escrevendo um arquivo…" }, { "grid", "No notebook…" },
            { "search", "Pesquisando na web…" }, { "fetch", "Baixando uma página…" }, { "browser", "Navegando…" },
            { "camera", "Tirando um print…" }, { "mouse", "Clicando por aí…" }, { "agent", "Chamei reforços!" },
            { "conduct", "Regendo a equipe!" }, { "plan", "Atualizando o plano…" }, { "spell", "Abrindo uma skill…" },
            { "toolbox", "Procurando ferramentas…" }, { "plug", "Conectando…" }, { "tool", "Usando ferramentas…" },
        };

        public static string Say(string anim)
        {
            string s;
            return AnimSay.TryGetValue(anim, out s) ? s : "Trabalhando…";
        }

        static readonly Dictionary<string, string> DetailWord = new Dictionary<string, string>
        {
            { "codigo", "código" }, { "doc", "texto" }, { "dados", "dados" }, { "imagem", "imagem" }, { "pdf", "PDF" },
            { "web", "página" }, { "notebook", "notebook" }, { "arquivo", "arquivo" },
        };

        // O estado de uma sessão em poucas palavras, para o painel.
        public static string Short(SessionView s, double nowMs)
        {
            if (!s.IsActive(nowMs)) return s.EndedAt > 0 ? "encerrada" : "parada " + Ago(nowMs - s.UpdatedAt);
            string since = s.TurnStartedAt > 0 ? " " + Dur(nowMs - s.TurnStartedAt) : "";
            switch (s.State)
            {
                case "trabalhando":
                {
                    string anim = WorkAnim(s.Tool, s.Detail);
                    string what;
                    string dw;
                    if (anim == "read" || anim == "edit" || anim == "write")
                        what = (anim == "read" ? "lendo " : anim == "edit" ? "editando " : "criando ") + (DetailWord.TryGetValue(s.Detail ?? "", out dw) ? dw : "arquivo");
                    else if (anim == "agent" || anim == "conduct")
                        what = s.Agents > 0 ? (int)s.Agents + (s.Agents == 1 ? " subagente" : " subagentes") : "subagentes";
                    else if ((s.Tool == "Bash" || s.Tool == "PowerShell") && !string.IsNullOrEmpty(s.Detail) && s.Detail != "comando")
                        what = "terminal: " + s.Detail;
                    else what = Say(anim).TrimEnd('…', '!').ToLowerInvariant();
                    return what + since;
                }
                case "pensando": return "pensando" + since;
                case "aguardando": return s.Tool == "AskUserQuestion" ? "te fez uma pergunta" : s.Tool == "ExitPlanMode" ? "plano para aprovar" : "pede permissão";
                case "compactando": return "resumindo a conversa";
                case "feito": return "terminou";
                case "erro": return "deu erro";
                case "interrompido": return "interrompida";
                case "recusa": return "recusou";
                case "dormindo": return "dormindo";
                case "oi": return "começando";
                default: return "livre";
            }
        }

        public static string Ago(double ms) { return ms < 60000 ? "agora" : "há " + Dur(ms); }

        public static string Dur(double ms)
        {
            double min = Math.Max(0, ms / 60000);
            if (min < 1) return Math.Max(1, (int)(ms / 1000)) + "s";
            if (min < 60) return (int)min + " min";
            int h = (int)(min / 60), m = (int)(min % 60);
            if (h < 24) return m > 0 ? h + "h" + m.ToString("00") : h + "h";
            return (h / 24) + "d " + (h % 24) + "h";
        }

        public static string LimitName(string kind)
        {
            string k = (kind ?? "").ToLowerInvariant();
            if (k == "five_hour") return "5 horas";
            if (k == "seven_day") return "Semanal";
            if (k.Contains("fable")) return "Fable";
            if (k.Contains("opus")) return "Opus";
            if (k.Contains("sonnet")) return "Sonnet";
            if (k.Contains("spend")) return "Créditos";
            return k.Replace('_', ' ');
        }

        public static string Resets(DateTime? when)
        {
            if (!when.HasValue) return "";
            TimeSpan left = when.Value - DateTime.Now;
            if (left.TotalMinutes < 1) return "agora";
            if (left.TotalHours < 24) return Dur(left.TotalMilliseconds);
            return when.Value.ToString("ddd HH:mm", PtBR);
        }

        public static string Money(double usd) { return "US$ " + usd.ToString("0.00", PtBR); }
    }

    // --------------------------------------------------------------- criaturas --

    sealed class Account
    {
        public string Key, Name, Profile;
        public AccountCfg Cfg;
        public Skin Skin, PupSkin;
        public Dictionary<char, Brush> Pal;
        public Brush Ui;
        public bool Running, Visible;
        public double HiddenUntil;
        public List<SessionView> Sessions = new List<SessionView>();
        public List<SessionView> Active = new List<SessionView>();
        public Dictionary<string, object> Memory;
        public string Mood = "tranquilo";
        public string FallbackState = "ocioso";
        public Usage Usage;
        public Creature Main;
        public List<Creature> Pups = new List<Creature>();
        public double HomeMin = 40, HomeMax = 380;
    }

    sealed class Creature
    {
        public Account Acc;
        public bool Pup;
        public string SessionId;
        public int Slot, P;
        public double X, Lift, VY;
        public int Facing = -1;
        public bool Falling, Leaving;
        public double BornAt = -10, LeaveT;
        public string Anim = "stand";
        public double AnimStart;
        public string React;
        public double ReactUntil;
        public string Idle = "stand";
        public double IdleUntil;
        public double WalkTarget = double.NaN;
        public string State = "ocioso", Tool = "", Detail = "", Model = "", LastWorkAnim = "";
        public double Agents, Ctx = double.NaN, TurnStartedAt;
        public bool Trophy;
        public string BubbleText;
        public double BubbleUntil, BubbleFrom;
        public string PendingText;
        public double PendingAt;
        public double NextBlink, BlinkUntil;
        public int LookX, LookY;
        public double Swing, LastWave = -100, NextEmit;
        public Rect Bounds;
        public double TopY;
        public double FreeUntil;  // chamado para um ponto (na Home): pode sair da faixa dele
        public Scene Scene;     // o evento em que ele está, se houver
        public bool NoShadow;   // montado em outro: sem sombra no chão
        public readonly Pix Grid = new Pix();
    }

    // Um evento entre Clawds (ou de um sozinho, como a borboleta), passo a passo.
    sealed class Scene
    {
        public string Kind;
        public Creature A, B;
        public int Phase, Count, Dir = 1;
        public double Start, PhaseAt, AX, BX, Gap, FX, SpeedA, SpeedB;
        public bool Flag;
        public string[] Lines;
        public Creature Winner;
        public double BallX = double.NaN, BallLift, BugX = double.NaN, BugY, JumpAt, LastJump;

        public List<Creature> Members()
        {
            var l = new List<Creature>();
            if (A != null) l.Add(A);
            if (B != null) l.Add(B);
            return l;
        }
    }

    sealed class Particle
    {
        public string Kind;
        public double X, Y, VX, VY, Life, Max;
        public char Color;
        public Account Acc;
        public int P;
    }

    // ------------------------------------------------------------------- palco --

    sealed class StageView : FrameworkElement
    {
        readonly Stage stage;
        public StageView(Stage s)
        {
            stage = s;
            RenderOptions.SetEdgeMode(this, EdgeMode.Aliased);
            RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.NearestNeighbor);
            SnapsToDevicePixels = true;
        }
        protected override void OnRender(DrawingContext dc) { stage.Render(dc); }
    }

    sealed class Stage : Window
    {
        // A faixa onde os Clawds vivem: limitada, no canto, com o chão exatamente na borda de
        // cima da barra de tarefas (a janela termina onde a área de trabalho termina).
        public static double W = 420;   // cresce se houver mais de duas contas
        public const double H = 200, Side = 10;
        public const double Ground = H;
        const int BigP = 3, PupP = 2;

        static readonly string[] Dones = { "Pronto!", "Feito!", "Mais uma!", "Terminei!", "Prontinho." };
        static readonly string[] Pets = { "Hehe!", "Obrigado!", "De novo!", "<3", "Tô feliz!" };
        static readonly string[] Falls = { "Uau!", "Ufa!", "Eita!", "Tô tonto…" };
        static readonly string[] Thinks = { "Hmm…", "Deixa eu pensar…", "Interessante…" };
        static readonly string[] Hellos = { "Oi!", "Opa!", "Tô aqui!", "E aí?" };
        static readonly string[] NewPup = { "Mais uma sessão!", "Olha, um filhote!", "Mais um!" };

        readonly StageView view;
        readonly InfoPanel panel;
        readonly List<Account> accounts = new List<Account>();
        readonly List<Particle> parts = new List<Particle>();
        readonly Stopwatch sw = Stopwatch.StartNew();
        Random rng = new Random();
        bool demo, festa;
        readonly bool photo;   // fotos do README: tempo e contas de mentira, sem janela
        readonly string shared, sessionsDir;
        string corner = "direito";
        Cfg cfg;
        DateTime cfgStamp;
        double lastT;
        // perfis do Claude abertos agora (a thread que vigia os processos troca o mapa inteiro)
        volatile Dictionary<string, int> running = new Dictionary<string, int>();
        bool fullscreen, shown;
        double pixelsPerDip = 1;
        readonly Typeface font = new Typeface(new FontFamily("Consolas"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
        readonly Pix scratch = new Pix();
        readonly Dictionary<char, Brush> neutral = Pal.Make("#888888", "#aaaaaa", "#666666", "#222222", "#ffd23f");
        static readonly Brush BubbleFill = Pal.B("#ffffff", 0.97), BubbleInk = Pal.B("#1b1b22", 1);

        Creature drag, pressC;
        bool pressed;
        Point pressPt, dragOff, lastDragPt;
        double lastDragT;
        Creature hover;
        double hoverSince, hideAt = -1;
        Scene scene;
        double nextEvent = 40;
        int festaIdx;
        readonly List<string> recent = new List<string>();
        int petCount;
        double lastPet;

        // A Home do app: enquanto ela está aberta, os Clawds saem da barra de tarefas e vivem
        // lá dentro, numa faixa da largura da janela.
        FrameworkElement homeView;
        public bool InHome { get { return homeView != null; } }
        public double HomeOffsetY { get { return homeView != null ? Math.Max(0, homeView.ActualHeight - Ground - HomeStageView.Floor) : 0; } }
        FrameworkElement Surface { get { return homeView ?? (FrameworkElement)view; } }

        // Todas as sessões lidas (de todas as contas) a cada leitura, para a Home, os avisos e o histórico.
        public event Action<List<SessionView>, double> SessionsRead;
        // O Clawd sob o mouse na Home (a Home destaca a sessão dele).
        public event Action<Creature> HoverChanged;
        // Pedidos do menu do Clawd para o app.
        public event Action OpenAppRequested, QuitRequested;
        public List<Account> Accounts { get { return accounts; } }
        public Dictionary<string, int> Running { get { return running; } }
        public Cfg Config { get { return cfg; } }

        public double T { get { return fakeT >= 0 ? fakeT : sw.Elapsed.TotalSeconds; } }

        public Stage(bool demo, bool festa, bool photo)
        {
            this.photo = photo;
            this.demo = demo;
            this.festa = festa;
            if (festa) nextEvent = 4;
            shared = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "clawd");
            sessionsDir = System.IO.Path.Combine(shared, "sessions");
            Directory.CreateDirectory(sessionsDir);
            LoadConfig();

            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            ShowActivated = false;
            ResizeMode = ResizeMode.NoResize;
            Width = W;
            Height = H;
            Title = "Clawds";
            UseLayoutRounding = true;

            view = new StageView(this);
            Content = view;
            panel = new InfoPanel();

            SourceInitialized += delegate { Native.MakeOverlay(this); };
            Loaded += delegate { pixelsPerDip = VisualTreeHelper.GetDpi(view).PixelsPerDip; };
            view.MouseLeftButtonDown += OnDown;
            view.MouseMove += OnMove;
            view.MouseLeftButtonUp += OnUp;
            view.MouseRightButtonUp += OnRight;

            if (photo) return;
            new Thread(WatchProcesses) { IsBackground = true, Name = "claude-watch" }.Start();

            var frame = new DispatcherTimer(DispatcherPriority.Render);
            frame.Interval = TimeSpan.FromMilliseconds(33);
            frame.Tick += delegate { Safe(Frame); };
            var poll = new DispatcherTimer();
            poll.Interval = TimeSpan.FromMilliseconds(1500);
            poll.Tick += delegate { Safe(Poll); };
            Safe(Poll);
            frame.Start();
            poll.Start();
        }

        // Lê a config da Central: quais contas têm Clawd, com que cores e com que skin.
        // Quem já está na tela continua onde está; só troca de roupa.
        void LoadConfig()
        {
            cfg = photo ? Cfg.Sample() : Cfg.Load();
            // primeira vez: grava a config, que é por onde o mod sabe das contas
            if (cfg.IsNew && !photo) { try { cfg.Save(); } catch { } }
            cfgStamp = Cfg.Stamp();
            corner = cfg.Corner;
            var skins = photo ? Skin.Builtin() : Skin.LoadAll(Cfg.SkinsDir);
            var next = new List<Account>();
            foreach (var ac in cfg.Accounts)
            {
                if (!ac.ShowClawd) continue;
                var a = accounts.FirstOrDefault(x => x.Key == ac.Key) ?? new Account();
                a.Key = ac.Key; a.Name = ac.Name; a.Profile = ac.Profile; a.Cfg = ac;
                a.Pal = Tint.Palette(ac.Body, ac.Detail);
                a.Ui = a.Pal['b'];
                a.Skin = Skin.Find(skins, ac.Skin);
                a.PupSkin = string.IsNullOrEmpty(ac.PupSkin) ? a.Skin : Skin.Find(skins, ac.PupSkin);
                next.Add(a);
            }
            accounts.Clear();
            accounts.AddRange(next);
            if (!InHome) W = TaskbarWidth();
            if (scene != null && scene.Members().Any(m => !accounts.Contains(m.Acc))) scene = null;
            if (panel != null && panel.Acc != null && !accounts.Contains(panel.Acc)) panel.Close2();
        }

        double TaskbarWidth() { return Math.Max(420, Math.Min(210 * accounts.Count, SystemParameters.WorkArea.Width - 2 * Side)); }

        // ---------------------------------------------------------------- Home --

        public void AttachHome(FrameworkElement v)
        {
            if (homeView == v) return;
            if (homeView != null) DetachHome();
            homeView = v;
            v.MouseLeftButtonDown += OnDown;
            v.MouseMove += OnMove;
            v.MouseLeftButtonUp += OnUp;
            v.MouseRightButtonUp += OnRight;
            v.SizeChanged += OnHomeSize;
            if (IsVisible) Hide();
            panel.Close2();
            Resize(Math.Max(300, v.ActualWidth > 0 ? v.ActualWidth : 760));
            double t = T;
            // pulam para dentro da janela: caem do alto
            foreach (var c in AllCreatures()) DropIn(c, 90 + rng.NextDouble() * 60);
            nextEvent = t + 4;
            Safe(Poll);
        }

        public void DetachHome()
        {
            var v = homeView;
            if (v == null) return;
            v.MouseLeftButtonDown -= OnDown;
            v.MouseMove -= OnMove;
            v.MouseLeftButtonUp -= OnUp;
            v.MouseRightButtonUp -= OnRight;
            v.SizeChanged -= OnHomeSize;
            if (pressed) { pressed = false; v.ReleaseMouseCapture(); }
            drag = null;
            homeView = null;
            if (hover != null) { hover = null; if (HoverChanged != null) HoverChanged(null); }
            Resize(TaskbarWidth());
            // e voltam para cima da barra de tarefas
            foreach (var c in AllCreatures()) DropIn(c, 60 + rng.NextDouble() * 50);
            if (nextEvent < T + 20) nextEvent = T + 20;
            Safe(Poll);
        }

        void OnHomeSize(object sender, SizeChangedEventArgs e)
        {
            if (e.WidthChanged && homeView != null) Resize(Math.Max(300, homeView.ActualWidth));
        }

        // Muda a largura da faixa e leva cada Clawd junto, na mesma proporção.
        void Resize(double w)
        {
            double old = W;
            W = w;
            if (Math.Abs(old - w) < 0.5) return;
            foreach (var c in AllCreatures()) { c.X = Clamp(c.X * w / old, 24, w - 24); c.WalkTarget = double.NaN; }
            Homes();
        }

        void DropIn(Creature c, double from)
        {
            if (c == drag) return;
            c.Lift = from;
            c.VY = 0;
            c.Falling = true;
            c.React = null;
        }

        // Clique no chão vazio da Home: quem está livre vem ver.
        void CallTo(double x, double t)
        {
            int n = 0;
            foreach (var c in AllCreatures().Where(c => Free(c, t)).OrderBy(c => Math.Abs(c.X - x)).Take(4))
            {
                double off = (n % 2 == 0 ? -1 : 1) * (12 + 26 * ((n + 1) / 2));
                c.WalkTarget = Clamp(x + off, 24, W - 24);
                c.Idle = "walk";
                c.FreeUntil = t + 14;
                if (n == 0) Say(c, rng.NextDouble() < 0.5 ? "Tô indo!" : "O que foi?", 1.6, t);
                n++;
            }
            if (n == 0) Sparkle(x, Ground - 6, 3);
        }

        // Começa uma brincadeira agora (botão da Home).
        public void PlayNow()
        {
            if (scene == null) nextEvent = T;
        }

        // A Home pede: o Clawd daquela sessão dá um alô.
        public void Ping(string sessionId)
        {
            double t = T;
            var c = AllCreatures().FirstOrDefault(x => x.SessionId == sessionId);
            if (c == null || c.Falling || c == drag) return;
            React(c, "wave", 1.6, t);
            Say(c, c.Pup ? "Tô aqui!" : "Sou eu!", 1.6, t);
        }

        // A Home pede: desenha os Clawds com o chão no fundo da área dela.
        public void RenderHome(DrawingContext dc)
        {
            dc.PushTransform(new TranslateTransform(0, HomeOffsetY));
            Render(dc);
            dc.Pop();
        }

        static void Safe(Action f)
        {
            try { f(); } catch (Exception e) { Program.Log(e.ToString()); }
        }

        // ----------------------------------------------------------- leitura --

        void WatchProcesses()
        {
            while (true)
            {
                running = Profiles.Running();
                Thread.Sleep(4000);
            }
        }

        void Poll()
        {
            double t = T;
            if (Cfg.Stamp() != cfgStamp) LoadConfig();
            ReadData(t);
            if (InHome)
            {
                if (IsVisible) Hide();
                panel.Close2();
                return;
            }
            fullscreen = Native.ForegroundIsFullscreen(this, panel);
            bool any = false;
            foreach (var a in accounts) any |= a.Visible;
            if (any && !fullscreen && cfg.Taskbar)
            {
                Place();
                if (!shown) { Show(); shown = true; }
                else if (!IsVisible) Show();
                Native.KeepOnTop(this);
                if (panel.IsVisible) Native.KeepOnTop(panel);
            }
            else if (IsVisible)
            {
                Hide();
                panel.Close2();
            }
            if (panel.IsVisible && panel.Acc != null) panel.Build(panel.Acc, panel.FocusId, Data.NowMs());
        }

        void Place()
        {
            Rect wa = SystemParameters.WorkArea;
            double left = corner == "esquerdo" ? wa.Left + Side : wa.Right - W - Side;
            double top = wa.Bottom - H;
            // Left/Top começam NaN, e NaN nunca é "diferente o bastante".
            if (double.IsNaN(Left) || Math.Abs(Left - left) > 0.5) Left = left;
            if (double.IsNaN(Top) || Math.Abs(Top - top) > 0.5) Top = top;
            if (Width != W) Width = W;
            if (Height != H) Height = H;
        }

        void ReadData(double t)
        {
            double now = Data.NowMs();
            var by = new Dictionary<string, List<SessionView>>();
            var all = new List<SessionView>();
            foreach (var a in accounts) by[a.Key] = new List<SessionView>();
            if (demo) { DemoSessions(t, now, by); all = by.Values.SelectMany(x => x).ToList(); }
            else
            {
                foreach (string f in Directory.GetFiles(sessionsDir, "*.json"))
                {
                    var fi = new FileInfo(f);
                    if (fi.LastWriteTimeUtc < DateTime.UtcNow.AddDays(-7)) { try { fi.Delete(); } catch { } continue; }
                    if (fi.LastWriteTimeUtc < DateTime.UtcNow.AddDays(-1)) continue;
                    var s = SessionView.From(Data.Read(f));
                    if (s == null) continue;
                    all.Add(s);
                    if (by.ContainsKey(s.Account)) by[s.Account].Add(s);
                }
            }

            foreach (var a in accounts)
            {
                a.Running = demo || running.ContainsKey(a.Profile);
                a.Sessions = by[a.Key].OrderByDescending(s => s.IsActive(now)).ThenByDescending(s => s.UpdatedAt).ToList();
                a.Active = by[a.Key].Where(s => s.IsActive(now)).OrderBy(s => s.StartedAt).ToList();
                if (!demo)
                {
                    a.Memory = Data.Read(System.IO.Path.Combine(shared, "memoria-" + a.Key + ".json"));
                    a.Usage = Data.UsageFrom(Data.Read(System.IO.Path.Combine(shared, "uso-" + a.Key + ".json")));
                }
                a.Mood = MoodOf(a.Memory, now);
                double last = 0;
                foreach (var s in a.Sessions) last = Math.Max(last, s.UpdatedAt);
                // Sem sessão ativa o Clawd descansa: fica sentado uns minutos e depois dorme.
                a.FallbackState = now - last < 300000 ? "descansando" : "dormindo";

                // na Home todos aparecem (sem sessão, descansam); na barra, só quem está em uso
                bool want = InHome || ((a.Running || now - last < 900000) && t >= a.HiddenUntil);
                if (want && !a.Visible) Appear(a, t);
                else if (!want && a.Visible) { a.Visible = false; a.Main = null; a.Pups.Clear(); }
                if (a.Visible) SyncCreatures(a, t);
            }
            Homes();
            if (SessionsRead != null && !photo)
            {
                try { SessionsRead(all, now); } catch (Exception e) { Program.Log("sessões: " + e); }
            }
        }

        static string MoodOf(Dictionary<string, object> m, double now)
        {
            if (m == null) return "tranquilo";
            double fail = Data.Num(m, "lastFailAt", 0);
            if (fail > 0 && now - fail < 600000) return "triste";
            if (DateTime.Now.Hour < 6) return "sonolento";
            string today = DateTime.UtcNow.ToString("yyyy-MM-dd");
            double tasksToday = Data.Str(m, "today", "") == today ? Data.Num(m, "tasksToday", 0) : 0;
            if (Data.Num(m, "streak", 0) >= 3 || tasksToday >= 5) return "feliz";
            return "tranquilo";
        }

        void Appear(Account a, double t)
        {
            a.Visible = true;
            var c = NewCreature(a, false, null, t);
            // a primeira conta fica do lado do canto
            int n = Math.Max(1, accounts.Count), i = Math.Max(0, accounts.IndexOf(a));
            c.X = W * ((corner == "esquerdo" ? i : n - 1 - i) + 0.5) / n;
            a.Main = c;
            if (InHome) DropIn(c, 80 + rng.NextDouble() * 60);
            React(c, "greet", 2.4, t);
            Say(c, Greeting(a), 3.5, t);
        }

        string Greeting(Account a)
        {
            int h = DateTime.Now.Hour;
            string hi = h >= 5 && h < 12 ? "Bom dia!" : h >= 12 && h < 18 ? "Boa tarde!" : "Boa noite!";
            double done = Data.Num(a.Memory, "tasksDone", 0);
            return done > 0 ? hi + " " + (int)done + " tarefas juntos." : hi;
        }

        Creature NewCreature(Account a, bool pup, string sid, double t)
        {
            var c = new Creature();
            c.Acc = a; c.Pup = pup; c.SessionId = sid;
            c.P = pup ? PupP : BigP;
            c.BornAt = t;
            c.NextBlink = t + 1 + rng.NextDouble() * 3;
            c.IdleUntil = t + 2;
            return c;
        }

        void SyncCreatures(Account a, double t)
        {
            if (a.Main == null) return;
            SessionView first = a.Active.Count > 0 ? a.Active[0] : null;
            a.Main.SessionId = first != null ? first.Id : null;
            if (first != null) ApplyState(a.Main, first, t);
            else ApplyState(a.Main, null, t);

            var wanted = a.Active.Skip(1).Take(4).ToList();
            foreach (var p in a.Pups)
                if (!p.Leaving && !wanted.Any(s => s.Id == p.SessionId)) { p.Leaving = true; p.LeaveT = t; Say(p, "Tchau!", 1, t); }
            foreach (var s in wanted)
            {
                var p = a.Pups.FirstOrDefault(x => x.SessionId == s.Id && !x.Leaving);
                if (p == null)
                {
                    p = NewCreature(a, true, s.Id, t);
                    p.Slot = FreeSlot(a);
                    p.X = Clamp(a.Main.X + PupOffset(p.Slot), 20, W - 20);
                    p.Facing = a.Main.Facing;
                    a.Pups.Add(p);
                    Puff(p, 8);
                    if (a.Main.React == null && BusyAnim(a.Main) == null) React(a.Main, "surprise", 1.2, t);
                    Say(a.Main, NewPup[rng.Next(NewPup.Length)], 2, t);
                }
                ApplyState(p, s, t);
            }
        }

        int FreeSlot(Account a)
        {
            for (int i = 0; i < 8; i++) if (!a.Pups.Any(p => p.Slot == i && !p.Leaving)) return i;
            return 0;
        }

        // Filhotes pares ficam atrás do Clawd grande; os ímpares, na frente, depois dos objetos
        // que ele usa no trabalho (que vão até uns 72 DIP do centro).
        static double PupOffset(int slot) { return slot % 2 == 0 ? -(52 + (slot / 2) * 38) : 96 + (slot / 2) * 38; }

        void ApplyState(Creature c, SessionView s, double t)
        {
            string old = c.State;
            c.State = s != null ? s.State : c.Acc.FallbackState;
            c.Tool = s != null ? s.Tool : "";
            c.Detail = s != null ? s.Detail : "";
            c.Model = s != null ? s.Model : "";
            c.Agents = s != null ? s.Agents : 0;
            c.Ctx = s != null ? s.Ctx : double.NaN;
            c.TurnStartedAt = s != null ? s.TurnStartedAt : 0;
            if (c == drag || c.Falling) return;
            if (c.State == old) { if (c.State == "trabalhando") SayWork(c, t); return; }
            switch (c.State)
            {
                case "feito":
                    c.Trophy = rng.NextDouble() < 0.35;
                    React(c, "done", 2.8, t);
                    Confetti(c, c.Pup ? 14 : 30);
                    Say(c, Dones[rng.Next(Dones.Length)], 2.6, t);
                    if (!c.Pup) Cheer(c.Acc, t);
                    break;
                case "erro":
                    React(c, "error", 3.6, t);
                    Say(c, "Ops… deu erro.", 3, t);
                    break;
                case "interrompido":
                    React(c, "halt", 2.4, t);
                    Say(c, "Opa, parei!", 2.2, t);
                    break;
                case "recusa":
                    React(c, "refuse", 3, t);
                    Say(c, "Esse eu não faço.", 2.6, t);
                    break;
                case "aguardando":
                    c.React = null;
                    Say(c, c.Tool == "AskUserQuestion" ? "Tenho uma pergunta!" : c.Tool == "ExitPlanMode" ? "Aprova o plano?" : "Posso fazer isso?", 3, t);
                    break;
                case "compactando":
                    c.React = null;
                    if (!c.Pup) Say(c, "Resumindo a conversa…", 2.4, t);
                    break;
                case "pensando":
                    if (old != "trabalhando" && !c.Pup) Say(c, Thinks[rng.Next(Thinks.Length)], 1.8, t);
                    break;
                case "trabalhando":
                    if (c.React == "greet" || c.React == "surprise") c.React = null;
                    SayWork(c, t);
                    break;
            }
            if (c.State == "descansando") { c.Idle = "sit"; c.IdleUntil = t + 12; c.WalkTarget = double.NaN; }
            if (c.State != "trabalhando") c.LastWorkAnim = "";
            if (BusyAnim(c) != null) c.WalkTarget = double.NaN;
        }

        void SayWork(Creature c, double t)
        {
            string wa = Words.WorkAnim(c.Tool, c.Detail);
            if (wa == c.LastWorkAnim) return;
            c.LastWorkAnim = wa;
            if (!c.Pup) Say(c, Words.Say(wa), 2.2, t);
        }

        void Cheer(Account who, double t)
        {
            foreach (var o in accounts)
            {
                if (o == who || !o.Visible || o.Main == null) continue;
                if (o.Main.React != null || BusyAnim(o.Main) != null || o.Main == drag || o.Main.Scene != null) continue;
                o.Main.Facing = who.Main.X > o.Main.X ? 1 : -1;
                React(o.Main, "cheer", 1.6, t);
                o.Main.PendingText = "Boa, " + who.Name + "!";
                o.Main.PendingAt = t + 0.5;
            }
        }

        // Faixa de cada conta: a primeira do lado do canto, as outras mais para dentro.
        void Homes()
        {
            var vis = accounts.Where(a => a.Visible).ToList();
            foreach (var a in vis)
            {
                var live = a.Pups.Where(p => !p.Leaving).ToList();
                int back = live.Count(p => p.Slot % 2 == 0), front = live.Count - back;
                // atrás: os filhotes; na frente: os objetos do trabalho e os filhotes de lá
                double extL = back == 0 ? 0 : 52 + (back - 1) * 38 + 18;
                double extR = front == 0 ? 72 : 96 + (front - 1) * 38 + 18;
                // a faixa é dividida em partes iguais; a primeira conta fica do lado do canto
                int n = vis.Count, i = vis.IndexOf(a), zi = corner == "esquerdo" ? i : n - 1 - i;
                double lo = W * zi / n + (zi == 0 ? 30 : 24) + extL, hi = W * (zi + 1) / n - (zi == n - 1 ? 30 : 24) - extR;
                if (lo > hi) lo = hi = (lo + hi) / 2;
                a.HomeMin = lo;
                a.HomeMax = hi;
            }
        }

        // ------------------------------------------------------------ quadro --

        void Frame()
        {
            if (!IsVisible && !photo && !InHome) return;
            double t = T;
            double dt = Math.Min(0.1, t - lastT);
            lastT = t;
            Point cur = CursorStage();
            UpdateHover(t, cur);
            foreach (var a in accounts)
            {
                if (!a.Visible || a.Main == null) continue;
                UpdateMain(a, a.Main, t, dt, cur);
                for (int i = a.Pups.Count - 1; i >= 0; i--)
                {
                    var p = a.Pups[i];
                    UpdatePup(a, p, t, dt, cur);
                    if (p.Leaving && t - p.LeaveT > 0.9) { Puff(p, 8); a.Pups.RemoveAt(i); }
                }
            }
            Events(t, dt);
            UpdateParticles(dt);
            Surface.InvalidateVisual();
        }

        Point CursorStage()
        {
            Native.POINT p;
            if (!Native.GetCursorPos(out p)) return new Point(-9999, -9999);
            if (InHome)
            {
                try
                {
                    if (PresentationSource.FromVisual(homeView) == null) return new Point(-9999, -9999);
                    Point local = homeView.PointFromScreen(new Point(p.X, p.Y));
                    return new Point(local.X, local.Y - HomeOffsetY);
                }
                catch { return new Point(-9999, -9999); }
            }
            var src = PresentationSource.FromVisual(this);
            if (src == null || src.CompositionTarget == null) return new Point(-9999, -9999);
            Point dip = src.CompositionTarget.TransformFromDevice.Transform(new Point(p.X, p.Y));
            return new Point(dip.X - Left, dip.Y - Top);
        }

        void UpdateHover(double t, Point cur)
        {
            Creature over = null;
            if (drag == null)
                foreach (var c in AllCreatures().Reverse())
                    if (Inflate(c.Bounds, 4).Contains(cur)) { over = c; break; }
            if (over != hover)
            {
                hover = over;
                hoverSince = t;
                if (InHome && HoverChanged != null) HoverChanged(hover);
            }
            if (InHome) return;   // na Home, quem mostra a sessão é o cartão dela

            Point screen = new Point(cur.X + Left, cur.Y + Top);
            bool onPanel = panel.IsVisible && new Rect(panel.Left, panel.Top, panel.ActualWidth, panel.ActualHeight).Contains(screen);
            if (hover != null && t - hoverSince > 0.35 && drag == null)
            {
                if (!panel.IsVisible || panel.Acc != hover.Acc || panel.FocusId != hover.SessionId)
                    panel.Open(hover.Acc, hover.SessionId, Data.NowMs(), Left + hover.X, Top + Math.Min(hover.Bounds.Top, hover.TopY) - 4);
                hideAt = -1;
            }
            else if (panel.IsVisible && hover == null && !onPanel)
            {
                if (hideAt < 0) hideAt = t + 0.45;
                else if (t >= hideAt) { panel.Close2(); hideAt = -1; }
            }
            else if (onPanel) hideAt = -1;
        }

        IEnumerable<Creature> AllCreatures()
        {
            foreach (var a in accounts)
            {
                if (!a.Visible || a.Main == null) continue;
                yield return a.Main;
                foreach (var p in a.Pups) yield return p;
            }
        }

        static Rect Inflate(Rect r, double d) { r.Inflate(d, d); return r; }
        static double Clamp(double v, double lo, double hi) { return Math.Max(lo, Math.Min(hi, v)); }

        static string BusyAnim(Creature c)
        {
            switch (c.State)
            {
                case "trabalhando": return Words.WorkAnim(c.Tool, c.Detail);
                case "pensando": return c.TurnStartedAt > 0 && Data.NowMs() - c.TurnStartedAt > 60000 ? "thinkhard" : "think";
                case "aguardando": return c.Tool == "AskUserQuestion" ? "ask" : c.Tool == "ExitPlanMode" ? "askplan" : "askperm";
                case "compactando": return "compact";
                case "dormindo": return "sleep";
                default: return null;
            }
        }

        void SetAnim(Creature c, string name, double t)
        {
            if (c.Anim != name) { c.Anim = name; c.AnimStart = t; }
        }

        void React(Creature c, string anim, double secs, double t)
        {
            c.React = anim;
            c.ReactUntil = t + secs;
            c.Anim = anim;
            c.AnimStart = t;
        }

        void Say(Creature c, string text, double secs, double t)
        {
            c.BubbleText = text;
            c.BubbleFrom = t;
            c.BubbleUntil = t + secs;
        }

        // Arrastado ou caindo: a física manda.
        bool Physics(Creature c, double t, double dt)
        {
            if (c == drag) { SetAnim(c, "drag", t); return true; }
            if (!c.Falling) return false;
            c.VY += 2300 * dt;
            c.Lift -= c.VY * dt;
            if (c.Lift <= 0)
            {
                c.Lift = 0;
                if (c.VY > 430) { c.VY = -c.VY * 0.3; Puff(c, 4); }
                else
                {
                    c.Falling = false;
                    c.VY = 0;
                    React(c, "dizzy", 1.8, t);
                    Say(c, Falls[rng.Next(Falls.Length)], 2, t);
                }
            }
            SetAnim(c, "fall", t);
            return true;
        }

        void UpdateMain(Account a, Creature c, double t, double dt, Point cur)
        {
            Common(c, t, cur);
            if (Physics(c, t, dt)) return;
            if (c.Scene != null) { Emit(c, t); return; } // o evento comanda
            if (c.React != null && t >= c.ReactUntil) c.React = null;
            string busy = BusyAnim(c);
            if (c.React != null) SetAnim(c, c.React, t);
            else if (busy != null) { c.Facing = 1; SetAnim(c, busy, t); }
            else if (scene != null) { c.Idle = "stand"; c.WalkTarget = double.NaN; Spectate(c, t, dt); }
            else Wander(a, c, t, dt, cur);
            Emit(c, t);
        }

        void UpdatePup(Account a, Creature p, double t, double dt, Point cur)
        {
            Common(p, t, cur);
            if (Physics(p, t, dt)) return;
            if (p.Leaving) { SetAnim(p, "wave", t); return; }
            if (p.Scene != null) { Emit(p, t); return; }
            if (p.React != null && t >= p.ReactUntil) p.React = null;
            double target = Clamp(a.Main.X + PupOffset(p.Slot), 20, W - 20);
            if (scene != null) target = AvoidScene(a, p, target);
            double dx = target - p.X;
            string busy = BusyAnim(p);
            if (p.React != null) SetAnim(p, p.React, t);
            else if ((busy == null && Math.Abs(dx) > 4) || Math.Abs(dx) > 36)
            {
                p.Facing = dx > 0 ? 1 : -1;
                p.X += Math.Sign(dx) * Math.Min(Math.Abs(dx), Math.Max(26, Math.Abs(dx) * 2.4) * dt);
                SetAnim(p, "walk", t);
            }
            else if (busy != null) { p.Facing = a.Main.X > p.X ? -1 : 1; SetAnim(p, busy, t); }
            else
            {
                if (t >= p.IdleUntil || p.Idle == "walk")
                {
                    string[] opts = { "stand", "look", "hop", "sit", "stand", "watch", "juggle" };
                    p.Idle = opts[rng.Next(opts.Length)];
                    p.IdleUntil = t + 2 + rng.NextDouble() * 4;
                    if (rng.NextDouble() < 0.5) p.Facing = a.Main.X > p.X ? 1 : -1;
                }
                SetAnim(p, p.Idle, t);
            }
            Emit(p, t);
        }

        // O que todo Clawd faz a cada quadro: piscar, olhar para o mouse, falas agendadas.
        void Common(Creature c, double t, Point cur)
        {
            if (t > c.NextBlink) { c.BlinkUntil = t + 0.12; c.NextBlink = t + 2.4 + rng.NextDouble() * 3.6; }
            if (c.PendingText != null && t >= c.PendingAt) { Say(c, c.PendingText, 2.2, t); c.PendingText = null; }
            double dx = cur.X - c.X, dy = cur.Y - (c.Bounds.Top + c.Bounds.Height * 0.3);
            double dist = Math.Sqrt(dx * dx + dy * dy);
            if (dist < 260)
            {
                c.LookX = Math.Abs(dx) < 14 ? 0 : (dx * c.Facing > 0 ? 1 : -1);
                c.LookY = dy < -30 ? -1 : dy > 20 ? 1 : 0;
            }
            else { c.LookX = 0; c.LookY = 0; }
        }

        static double Speed(string mood)
        {
            switch (mood) { case "feliz": return 34; case "triste": return 14; case "sonolento": return 10; default: return 24; }
        }

        void Wander(Account a, Creature c, double t, double dt, Point cur)
        {
            bool outOfHome = t >= c.FreeUntil && (c.X < a.HomeMin - 2 || c.X > a.HomeMax + 2);
            if (outOfHome && c.Idle != "walk") { c.Idle = "walk"; c.WalkTarget = Clamp(c.X, a.HomeMin, a.HomeMax); }
            if (c.Idle == "walk")
            {
                if (double.IsNaN(c.WalkTarget)) c.WalkTarget = PickTarget(a, c);
                double dx = c.WalkTarget - c.X;
                if (Math.Abs(dx) < 1.5)
                {
                    c.WalkTarget = double.NaN;
                    c.Idle = "stand";
                    c.IdleUntil = t + 1.5 + rng.NextDouble() * 3;
                }
                else
                {
                    c.Facing = dx > 0 ? 1 : -1;
                    double sp = Speed(a.Mood) * (outOfHome ? 1.8 : 1);
                    c.X += Math.Sign(dx) * Math.Min(Math.Abs(dx), sp * dt);
                }
            }
            else if (t >= c.IdleUntil) NextIdle(a, c, t);

            if (hover == c && t - hoverSince > 1.1 && t - c.LastWave > 40 && c.Idle != "walk")
            {
                c.LastWave = t;
                c.Facing = cur.X >= c.X ? 1 : -1;
                React(c, "wave", 1.6, t);
                Say(c, Hellos[rng.Next(Hellos.Length)], 1.6, t);
                return;
            }
            string anim = c.Idle;
            if (anim == "stand" && hover == c) anim = "watch";
            if (anim == "stand" && c.Acc.Usage != null && c.Acc.Usage.FiveHour() >= 90) anim = "lowbat";
            SetAnim(c, anim, t);
        }

        void NextIdle(Account a, Creature c, double t)
        {
            if (c.State == "descansando")
            {
                // sem sessão ativa: fica quieto, sentado ou cochilando, sem passear
                string[] rest = { "sit", "sit", "sit", "nod", "nod", "yawn", "stand", "book" };
                string r = rest[rng.Next(rest.Length)];
                if (r == c.Idle && r != "sit") r = "sit";
                c.Idle = r;
                c.IdleUntil = t + (r == "yawn" ? 2.4 : r == "stand" ? 4 + rng.NextDouble() * 4 : 14 + rng.NextDouble() * 16);
                return;
            }
            string[] pool;
            switch (a.Mood)
            {
                case "feliz": pool = new[] { "walk", "walk", "walk", "dance", "dance", "hop", "hop", "stand", "look", "sit", "stretch", "wave", "juggle", "juggle" }; break;
                case "triste": pool = new[] { "stand", "stand", "sit", "sit", "walk", "walk", "look", "yawn" }; break;
                case "sonolento": pool = new[] { "sit", "sit", "yawn", "yawn", "nod", "nod", "stand", "walk" }; break;
                default: pool = new[] { "walk", "walk", "stand", "stand", "stand", "look", "look", "sit", "sit", "stretch", "hop", "yawn", "juggle", "book" }; break;
            }
            string next = pool[rng.Next(pool.Length)];
            if (next == c.Idle && next != "walk") next = "walk";
            c.Idle = next;
            double dur;
            switch (next)
            {
                case "walk": c.WalkTarget = PickTarget(a, c); dur = 99; break;
                case "sit": dur = 6 + rng.NextDouble() * 8; break;
                case "book": dur = 8 + rng.NextDouble() * 8; break;
                case "dance": dur = 3.5 + rng.NextDouble() * 2.5; break;
                case "juggle": dur = 4 + rng.NextDouble() * 3; break;
                case "stretch": dur = 2.2; break;
                case "yawn": dur = 2.4; break;
                case "hop": dur = 1.1; break;
                case "wave": dur = 1.6; break;
                case "look": dur = 2.6; break;
                case "nod": dur = 4 + rng.NextDouble() * 3; break;
                default: dur = 2 + rng.NextDouble() * 3; break;
            }
            c.IdleUntil = t + dur;
        }

        double PickTarget(Account a, Creature c)
        {
            for (int i = 0; i < 6; i++)
            {
                double x = a.HomeMin + rng.NextDouble() * (a.HomeMax - a.HomeMin);
                if (Math.Abs(x - c.X) < 24 && a.HomeMax - a.HomeMin > 60) continue;
                return x;
            }
            return Clamp(c.X, a.HomeMin, a.HomeMax);
        }

        // ------------------------------------------------------------ eventos --
        // De tempo em tempo (30 s a 75 s), se estiverem livres, dois Clawds fazem algo juntos:
        // um toca aqui, uma conversa, pega-pega, bola, pula-sela, corrida, duelo de dança,
        // lanche, cabo de guerra, cavalinho ou abraço. Sozinho, ele corre atrás de uma borboleta.
        // Se alguém é chamado para trabalhar (ou é arrastado), o evento acaba na hora.

        static readonly string[] PairKinds = { "highfive", "talk", "chase", "ball", "leapfrog", "race", "danceoff", "share", "tug", "ride", "hug" };
        static readonly string[] FestaKinds = { "highfive", "talk", "chase", "ball", "leapfrog", "race", "danceoff", "share", "tug", "ride", "hug", "butterfly" };

        static readonly string[][] Talks =
        {
            new[] { "Quantos tokens hoje?", "Nem me fala…", "Perdi a conta!" },
            new[] { "Viu meu ponto e vírgula?", "Tava no fim da linha!" },
            new[] { "Compilou de primeira!", "Mentira!", "Juro!" },
            new[] { "Bora um café?", "Café de pixel?", "O melhor que tem!" },
            new[] { "Achei um bug.", "Grande?", "Ele tem patinhas…" },
            new[] { "Tô com sono…", "Só mais um commit.", "Tá bom, unzinho." },
            new[] { "Já testou?", "Testei no olho.", "Hmm…" },
            new[] { "Gostei do seu chapéu!", "Valeu! Cadê o seu?", "Esqueci em casa." },
            new[] { "Sabe uma piada de recursão?", "Não.", "Sabe uma piada de recursão?" },
            new[] { "Quem fechou a aba?", "Não fui eu!", "…fui eu." },
            new[] { "Olha a barra de tarefas!", "Que vista bonita." },
            new[] { "Tabs ou espaços?", "Espaços.", "Hmpf." },
            new[] { "Quanto falta pro limite?", "Melhor nem olhar.", "Olhei. Ai." },
            new[] { "Funciona na minha máquina!", "A gente MORA na sua máquina." },
        };

        string Pick(params string[] opts) { return opts[rng.Next(opts.Length)]; }
        static double Half(Creature c) { return 9 * c.P; }
        static double Hop(double u, double h) { u = Clamp(u, 0, 1); return 4 * h * u * (1 - u); }
        static void Face(Creature a, Creature b) { a.Facing = b.X >= a.X ? 1 : -1; }
        static double RideH(Creature rider, Creature carrier) { return 14 * carrier.P - 3 * rider.P; }

        static void Next(Scene s, double t) { s.Phase++; s.PhaseAt = t; s.Flag = false; }

        bool Free(Creature c, double t)
        {
            return c.Scene == null && c.State != "descansando" && c.React == null && BusyAnim(c) == null && c != drag && !c.Falling && !c.Leaving
                && c.Lift == 0 && t - c.BornAt > 3;
        }

        bool Alive(Creature c)
        {
            return c != null && c.Acc.Visible && (c == c.Acc.Main || c.Acc.Pups.Contains(c));
        }

        bool Broken(Scene s)
        {
            foreach (var c in s.Members())
                if (!Alive(c) || c.Leaving || c == drag || c.Falling || c.React != null || BusyAnim(c) != null) return true;
            return false;
        }

        void Events(double t, double dt)
        {
            if (scene != null)
            {
                if (Broken(scene)) EndScene(t, true);
                else if (t - scene.Start > 45 || !Step(scene, t, dt)) EndScene(t, false);
                return;
            }
            if (t < nextEvent || (!cfg.Events && !festa && !InHome)) return;
            var free = AllCreatures().Where(c => Free(c, t)).ToList();
            string kind = null;
            if (festa)
            {
                kind = FestaKinds[festaIdx % FestaKinds.Length];
                if (free.Count < (kind == "butterfly" ? 1 : 2)) { nextEvent = t + 1; return; }
                festaIdx++;
            }
            else if (free.Count >= 2) kind = rng.NextDouble() < 0.1 ? "butterfly" : PickKind();
            else if (free.Count == 1 && rng.NextDouble() < 0.5) kind = "butterfly";
            if (kind == null) { nextEvent = t + 15; return; }

            Creature a, b = null;
            if (kind == "butterfly") a = free[rng.Next(free.Count)];
            else
            {
                // de preferência os dois grandes, um de cada conta
                var mains = free.Where(c => !c.Pup).ToList();
                if (mains.Count >= 2 && rng.NextDouble() < 0.6) { a = mains[0]; b = mains[1]; }
                else
                {
                    a = free[rng.Next(free.Count)];
                    var rest = free.Where(c => c != a).ToList();
                    b = rest[rng.Next(rest.Count)];
                }
                if (rng.NextDouble() < 0.5) { var x = a; a = b; b = x; }
                // no cavalinho, quem carrega (B) é o maior
                if (kind == "ride" && b.P < a.P) { var x = a; a = b; b = x; }
            }
            StartScene(kind, a, b, t);
        }

        string PickKind()
        {
            var pool = PairKinds.Where(x => !recent.Contains(x)).ToList();
            string k = pool[rng.Next(pool.Count)];
            recent.Add(k);
            if (recent.Count > 5) recent.RemoveAt(0);
            return k;
        }

        void StartScene(string kind, Creature a, Creature b, double t)
        {
            var s = new Scene();
            s.Kind = kind; s.A = a; s.B = b; s.Start = s.PhaseAt = t;
            foreach (var c in s.Members()) { c.Scene = s; c.WalkTarget = double.NaN; c.Idle = "stand"; }
            switch (kind)
            {
                case "highfive": PlanMeet(s, Half(a) + Half(b) + 8); break;
                case "talk":
                {
                    var lines = Talks[rng.Next(Talks.Length)].ToList();
                    if (a.Acc != b.Acc && rng.NextDouble() < 0.5) { lines.Insert(0, "Oi, " + b.Acc.Name + "!"); lines.Insert(1, "Oi, " + a.Acc.Name + "!"); }
                    s.Lines = lines.ToArray();
                    PlanMeet(s, Half(a) + Half(b) + 26);
                    break;
                }
                case "ball": PlanMeet(s, 124); break;
                case "leapfrog":
                {
                    // começa num canto e vai pulando em direção ao outro
                    double gap = Half(a) + Half(b) + 14;
                    int dir = (a.X + b.X) / 2 < W / 2 ? 1 : -1;
                    double mid = dir > 0 ? 34 + gap : W - 34 - gap;
                    s.AX = mid - dir * gap / 2;
                    s.BX = mid + dir * gap / 2;
                    s.Gap = gap;
                    break;
                }
                case "race":
                    s.Dir = a.X <= b.X ? 1 : -1;   // 1: A larga da esquerda
                    s.SpeedA = 70 + rng.NextDouble() * 30;
                    s.SpeedB = 70 + rng.NextDouble() * 30;
                    break;
                case "danceoff": PlanMeet(s, Half(a) + Half(b) + 30); break;
                case "tug": PlanMeet(s, Half(a) + Half(b) + 60); s.Winner = rng.NextDouble() < 0.5 ? a : b; break;
                case "hug": PlanMeet(s, Half(a) + Half(b) - 4); break;
                case "butterfly": s.FX = Clamp(a.X, 130, W - 130); break;
            }
            scene = s;
        }

        void EndScene(double t, bool broken)
        {
            var s = scene;
            scene = null;
            foreach (var c in s.Members())
            {
                c.Scene = null;
                c.NoShadow = false;
                c.Idle = "stand";
                c.IdleUntil = t + 1.5 + rng.NextDouble() * 2;
                if (c.Lift > 0 && c != drag && !c.Falling) { c.Falling = true; c.VY = 0; }
                if (broken && Alive(c) && c.React == null && BusyAnim(c) == null && c != drag && !c.Leaving)
                    Say(c, Pick("Ah…", "Fica pra depois!", "Ué?"), 1.4, t);
            }
            nextEvent = t + (festa ? 2.5 : InHome ? 10 + rng.NextDouble() * 15 : 30 + rng.NextDouble() * 45);
        }

        void PlanMeet(Scene s, double gap)
        {
            double mid = Clamp((s.A.X + s.B.X) / 2, 30 + gap / 2, W - 30 - gap / 2);
            int sg = s.A.X <= s.B.X ? 1 : -1;
            s.AX = mid - sg * gap / 2;
            s.BX = mid + sg * gap / 2;
            s.Gap = gap;
        }

        bool Go(Creature c, double x, double sp, double dt, double t, string anim)
        {
            x = Clamp(x, 26, W - 26);
            double dx = x - c.X;
            if (Math.Abs(dx) < 1) { c.X = x; return true; }
            c.Facing = dx > 0 ? 1 : -1;
            c.X += Math.Sign(dx) * Math.Min(Math.Abs(dx), sp * dt);
            SetAnim(c, anim, t);
            return false;
        }

        bool Meet(Scene s, double t, double dt)
        {
            bool a = Go(s.A, s.AX, 42, dt, t, "walk");
            bool b = Go(s.B, s.BX, 42, dt, t, "walk");
            if (a && b) { Face(s.A, s.B); Face(s.B, s.A); return true; }
            if (a) { Face(s.A, s.B); SetAnim(s.A, "watch", t); }
            if (b) { Face(s.B, s.A); SetAnim(s.B, "watch", t); }
            return false;
        }

        // Um passo do evento; false quando acabou.
        bool Step(Scene s, double t, double dt)
        {
            double pt = t - s.PhaseAt;
            Creature A = s.A, B = s.B;
            switch (s.Kind)
            {
                case "highfive": return HighFive(s, A, B, pt, t, dt);
                case "talk": return Talk(s, A, B, pt, t, dt);
                case "chase": return Chase(s, A, B, pt, t, dt);
                case "ball": return BallGame(s, A, B, pt, t, dt);
                case "leapfrog": return Leapfrog(s, A, B, pt, t, dt);
                case "race": return Race(s, A, B, pt, t, dt);
                case "danceoff": return DanceOff(s, A, B, pt, t, dt);
                case "share": return Share(s, A, B, pt, t, dt);
                case "tug": return Tug(s, A, B, pt, t, dt);
                case "ride": return Ride(s, A, B, pt, t, dt);
                case "hug": return Hug(s, A, B, pt, t, dt);
                case "butterfly": return Butterfly(s, A, pt, t, dt);
            }
            return false;
        }

        bool HighFive(Scene s, Creature A, Creature B, double pt, double t, double dt)
        {
            switch (s.Phase)
            {
                case 0:
                    if (Meet(s, t, dt)) { Next(s, t); Say(A, Pick("Toca aqui!", "Bate aqui!", "Hi-five!"), 1.6, t); }
                    return true;
                case 1:
                    SetAnim(A, "ready", t); SetAnim(B, "ready", t);
                    if (pt > 0.7) Next(s, t);
                    return true;
                case 2:
                {
                    double u = pt / 0.6;
                    A.Lift = Hop(u, 16); B.Lift = Hop(u, 16);
                    SetAnim(A, "highfive", t); SetAnim(B, "highfive", t);
                    if (!s.Flag && u > 0.45) { s.Flag = true; Sparkle((A.X + B.X) / 2, Ground - 16 * Math.Max(A.P, B.P) - 14, 6); }
                    if (u >= 1) { A.Lift = 0; B.Lift = 0; Next(s, t); }
                    return true;
                }
            }
            React(A, "laugh", 1.4, t); React(B, "laugh", 1.4, t);
            Say(B, Pick("Boa!", "Isso aí!", "Uhul!"), 1.5, t);
            return false;
        }

        bool Talk(Scene s, Creature A, Creature B, double pt, double t, double dt)
        {
            if (s.Phase == 0) { if (Meet(s, t, dt)) Next(s, t); return true; }
            if (s.Phase == 1)
            {
                if (s.Count >= s.Lines.Length) { Next(s, t); return true; }
                Creature who = s.Count % 2 == 0 ? A : B, other = who == A ? B : A;
                if (!s.Flag) { s.Flag = true; Say(who, s.Lines[s.Count], 2.2, t); }
                SetAnim(who, pt < 1.6 ? "talk" : "watch", t);
                SetAnim(other, "watch", t);
                if (pt > 2.5) { s.Count++; s.Flag = false; s.PhaseAt = t; }
                return true;
            }
            React(A, "laugh", 1.3, t); React(B, "laugh", 1.3, t);
            return false;
        }

        bool Chase(Scene s, Creature A, Creature B, double pt, double t, double dt)
        {
            if (s.Phase == 0)
            {
                Say(A, Pick("Pega-pega!", "Vou te pegar!"), 1.5, t);
                B.PendingText = "Ih!";
                B.PendingAt = t + 0.4;
                s.Dir = B.X >= A.X ? 1 : -1;
                Next(s, t);
                return true;
            }
            if (s.Phase == 1)
            {
                Creature ch = s.Count % 2 == 0 ? A : B, run = ch == A ? B : A;
                run.X += s.Dir * (run.Pup ? 46 : 52) * dt;
                if (run.X < 30) { run.X = 30; s.Dir = 1; }
                if (run.X > W - 30) { run.X = W - 30; s.Dir = -1; }
                run.Facing = s.Dir;
                SetAnim(run, "flee", t);
                if (pt < 0.6) { Face(ch, run); SetAnim(ch, "ready", t); }
                else Go(ch, run.X, ch.Pup ? 66 : 74, dt, t, "run");
                if (pt >= 0.6 && Math.Abs(ch.X - run.X) < Half(ch) + Half(run) - 2)
                {
                    s.Count++;
                    Sparkle((ch.X + run.X) / 2, Ground - 14 * run.P, 3);
                    Say(ch, Pick("Peguei!", "Tá com você!", "Ha!"), 1.1, t);
                    s.PhaseAt = t;
                    s.Dir = ch.X >= run.X ? 1 : -1;   // quem foi pego vira caçador; o outro foge
                    if (s.Count >= 4 || t - s.Start > 16) Next(s, t);
                }
                return true;
            }
            Say(A, Pick("Ufa! Cansei.", "Chega, chega!"), 1.6, t);
            React(A, "laugh", 1.4, t); React(B, "laugh", 1.4, t);
            return false;
        }

        bool BallGame(Scene s, Creature A, Creature B, double pt, double t, double dt)
        {
            if (s.Phase == 0)
            {
                if (Meet(s, t, dt)) { Next(s, t); Say(A, Pick("Bola!", "Olha a bola!", "Chuta pra cá!"), 1.4, t); }
                return true;
            }
            Creature k = s.Count % 2 == 0 ? A : B, r = k == A ? B : A;
            double from = k.X + k.Facing * (Half(k) + 3), to = r.X + r.Facing * (Half(r) + 1);
            if (s.Phase == 1)
            {
                // passe: chute do chão no primeiro, depois cabeçadas de um para o outro
                double u = Math.Min(1, pt / 1.0);
                double h0 = s.Count == 0 ? 0 : 13 * k.P, h1 = 13 * r.P;
                s.BallX = from + (to - from) * u;
                s.BallLift = h0 + (h1 - h0) * u + Hop(u, 28);
                SetAnim(k, u < 0.25 ? (s.Count == 0 ? "kick" : "head") : "watch", t);
                SetAnim(r, u > 0.6 ? "head" : "ready", t);
                r.Lift = u > 0.72 ? Hop((u - 0.72) / 0.28, 8) : 0;
                k.Lift = 0;
                if (u >= 1) { r.Lift = 0; s.Count++; s.PhaseAt = t; if (s.Count >= 6) Next(s, t); }
                return true;
            }
            if (s.Phase == 2)
            {
                // a última sobe demais e cai na cabeça do outro
                double u = Math.Min(1, pt / 1.1);
                double h0 = 13 * k.P, h1 = 15 * r.P;
                s.BallX = from + (r.X - from) * u;
                s.BallLift = h0 + (h1 - h0) * u + Hop(u, 56);
                SetAnim(k, u < 0.2 ? "head" : "watch", t);
                SetAnim(r, u < 0.9 ? "head" : "dizzy", t);
                if (u >= 1)
                {
                    if (!s.Flag) { s.Flag = true; Sparkle(r.X, Ground - 16 * r.P, 4); Say(r, "Ai!", 1.0, t); }
                    s.BallX = double.NaN;
                }
                if (pt > 2.2) Next(s, t);
                return true;
            }
            Say(k, "Foi mal!", 1.5, t);
            React(r, "dizzy", 1.2, t); React(k, "laugh", 1.4, t);
            return false;
        }

        bool Leapfrog(Scene s, Creature A, Creature B, double pt, double t, double dt)
        {
            Creature j = s.Count % 2 == 0 ? A : B, b = j == A ? B : A;
            switch (s.Phase)
            {
                case 0:
                    if (Meet(s, t, dt)) { Next(s, t); Say(A, Pick("Pula-sela!", "Abaixa aí!"), 1.4, t); }
                    return true;
                case 1:
                    Face(j, b);
                    b.Facing = j.Facing;   // a base olha para o lado do salto
                    SetAnim(b, "crouch", t); SetAnim(j, "ready", t);
                    if (pt > 0.6) { s.AX = j.X; s.BX = Clamp(b.X + j.Facing * s.Gap, 30, W - 30); Next(s, t); }
                    return true;
                case 2:
                {
                    double u = Math.Min(1, pt / 0.75);
                    j.X = s.AX + (s.BX - s.AX) * u;
                    j.Lift = Hop(u, 11 * b.P + 10);
                    SetAnim(j, "jump", t); SetAnim(b, "crouch", t);
                    if (u >= 1)
                    {
                        j.Lift = 0;
                        s.Count++;
                        double nextLand = j.X + j.Facing * s.Gap;   // o próximo salto (da base) cai depois de j
                        bool fits = nextLand > 30 && nextLand < W - 30;
                        s.Phase = s.Count >= 4 || !fits ? 3 : 1;
                        s.PhaseAt = t;
                    }
                    return true;
                }
            }
            React(A, "laugh", 1.4, t); React(B, "laugh", 1.4, t);
            Say(B, Pick("De novo!", "Hahaha!"), 1.4, t);
            return false;
        }

        bool Race(Scene s, Creature A, Creature B, double pt, double t, double dt)
        {
            double mid = W / 2;
            switch (s.Phase)
            {
                case 0:
                {
                    if (!s.Flag) { s.Flag = true; Say(A, Pick("Corrida até a bandeira!", "Aposto corrida!"), 1.8, t); }
                    bool a = Go(A, s.Dir > 0 ? 34 : W - 34, 50, dt, t, "walk");
                    bool b = Go(B, s.Dir > 0 ? W - 34 : 34, 50, dt, t, "walk");
                    if (a) { A.Facing = s.Dir; SetAnim(A, "ready", t); }
                    if (b) { B.Facing = -s.Dir; SetAnim(B, "ready", t); }
                    if (a && b) { s.Count = 0; Next(s, t); }
                    return true;
                }
                case 1:
                {
                    string[] cd = { "3", "2", "1", "Já!" };
                    int i = Math.Min(3, (int)(pt / 0.6));
                    if (i >= s.Count) { Say(A, cd[i], 0.55, t); s.Count = i + 1; }
                    SetAnim(A, "ready", t); SetAnim(B, "ready", t);
                    if (pt > 2.4) Next(s, t);
                    return true;
                }
                case 2:
                {
                    double ta = mid - s.Dir * (Half(A) + 3), tb = mid + s.Dir * (Half(B) + 3);
                    bool a = Go(A, ta, s.SpeedA, dt, t, "run");
                    bool b = Go(B, tb, s.SpeedB, dt, t, "run");
                    if (s.Winner == null && (a || b)) s.Winner = a ? A : B;
                    if (a) SetAnim(A, A == s.Winner ? "cheer" : "pout", t);
                    if (b) SetAnim(B, B == s.Winner ? "cheer" : "pout", t);
                    if (a && b)
                    {
                        Creature w = s.Winner, l = w == A ? B : A;
                        Say(w, Pick("Ganhei!", "Primeiro!"), 1.6, t);
                        l.PendingText = Pick("Quase!", "Revanche!", "Roubou!");
                        l.PendingAt = t + 0.9;
                        Next(s, t);
                    }
                    return true;
                }
                case 3:
                {
                    Creature w = s.Winner, l = w == A ? B : A;
                    SetAnim(w, "cheer", t); SetAnim(l, "pout", t);
                    if (pt > 2.2) Next(s, t);
                    return true;
                }
            }
            return false;
        }

        bool DanceOff(Scene s, Creature A, Creature B, double pt, double t, double dt)
        {
            switch (s.Phase)
            {
                case 0:
                    if (Meet(s, t, dt)) { Next(s, t); Say(A, Pick("Duelo de dança!", "Solta o som!"), 1.6, t); }
                    return true;
                case 1:
                    SetAnim(A, "dance", t); SetAnim(B, "watch", t);
                    if (pt > 2.4) { Next(s, t); Say(B, Pick("Minha vez!", "Olha essa!"), 1.4, t); }
                    return true;
                case 2:
                    SetAnim(B, "dance", t); SetAnim(A, "watch", t);
                    if (pt > 2.4) Next(s, t);
                    return true;
                case 3:
                    SetAnim(A, "dance", t); SetAnim(B, "dance", t);
                    if (pt > 2.6) Next(s, t);
                    return true;
            }
            Say(A, Pick("Empate!", "Arrasamos!"), 1.5, t);
            React(A, "cheer", 1.2, t); React(B, "cheer", 1.2, t);
            return false;
        }

        bool Share(Scene s, Creature A, Creature B, double pt, double t, double dt)
        {
            switch (s.Phase)
            {
                case 0:
                {
                    int sg = A.X <= B.X ? 1 : -1;
                    Face(B, A); SetAnim(B, "watch", t);
                    if (Go(A, B.X - sg * (Half(A) + Half(B) + 10), 40, dt, t, "walk"))
                    {
                        Face(A, B);
                        Next(s, t);
                        Say(A, Pick("Quer um biscoito?", "Trouxe lanche!"), 1.8, t);
                    }
                    return true;
                }
                case 1:
                    SetAnim(A, "give", t); SetAnim(B, "watch", t);
                    if (pt > 1.7) { Next(s, t); Say(B, Pick("Valeu!", "Que delícia!", "Oba, biscoito!"), 1.6, t); }
                    return true;
                case 2:
                    SetAnim(A, "eat", t); SetAnim(B, "eat", t);
                    if (pt > 2.6) Next(s, t);
                    return true;
            }
            React(B, "pet", 1.4, t); React(A, "laugh", 1.0, t);
            return false;
        }

        bool Tug(Scene s, Creature A, Creature B, double pt, double t, double dt)
        {
            Creature w = s.Winner, l = w == A ? B : A;
            switch (s.Phase)
            {
                case 0:
                    if (Meet(s, t, dt))
                    {
                        Next(s, t);
                        Say(A, Pick("Cabo de guerra!", "Segura essa!"), 1.4, t);
                        s.Dir = w.X >= l.X ? 1 : -1;
                        s.AX = A.X; s.BX = B.X;
                    }
                    return true;
                case 1:
                    SetAnim(A, "ready", t); SetAnim(B, "ready", t);
                    if (pt > 0.7) Next(s, t);
                    return true;
                case 2:
                {
                    // vai e vem, e aos poucos o vencedor leva a corda
                    double shift = Math.Sin(pt * 5) * 2 + pt * 4;
                    A.X = s.AX + s.Dir * shift; B.X = s.BX + s.Dir * shift;
                    SetAnim(A, "tug", t); SetAnim(B, "tug", t);
                    if (pt > 3.2) Next(s, t);
                    return true;
                }
                case 3:
                    if (!s.Flag)
                    {
                        s.Flag = true;
                        Say(w, Pick("Ganhei!", "Força!"), 1.4, t);
                        l.PendingText = "Eita!";
                        l.PendingAt = t + 0.6;
                    }
                    if (pt < 0.35) l.X += s.Dir * 70 * dt;
                    SetAnim(w, "cheer", t); SetAnim(l, pt < 0.35 ? "flee" : "dizzy", t);
                    if (pt > 1.9) Next(s, t);
                    return true;
            }
            return false;
        }

        double PickRideTarget(Creature c)
        {
            double x = c.X + (rng.NextDouble() < 0.5 ? -1 : 1) * (50 + rng.NextDouble() * 60);
            if (x < 40 || x > W - 40) x = c.X - (x - c.X);
            return Clamp(x, 40, W - 40);
        }

        // A monta em B, dá uma volta e desce.
        bool Ride(Scene s, Creature A, Creature B, double pt, double t, double dt)
        {
            switch (s.Phase)
            {
                case 0:
                {
                    int sg = A.X <= B.X ? 1 : -1;
                    Face(B, A); SetAnim(B, "watch", t);
                    if (Go(A, B.X - sg * (Half(A) + Half(B) + 4), 40, dt, t, "walk"))
                    {
                        Face(A, B);
                        Next(s, t);
                        s.AX = A.X;
                        Say(A, Pick("Upa, cavalinho!", "Me dá carona?"), 1.6, t);
                    }
                    return true;
                }
                case 1:
                {
                    double u = Math.Min(1, pt / 0.6);
                    A.X = s.AX + (B.X - s.AX) * u;
                    A.Lift = RideH(A, B) * u + Hop(u, 14);
                    A.NoShadow = u > 0.4;
                    SetAnim(A, "jump", t); SetAnim(B, "ready", t);
                    if (u >= 1) { A.Facing = B.Facing; Next(s, t); s.BX = PickRideTarget(B); }
                    return true;
                }
                case 2:
                {
                    if (Go(B, s.BX, B.Pup ? 26 : 32, dt, t, "carry")) { SetAnim(B, "carry", t); s.BX = PickRideTarget(B); }
                    A.X = B.X;
                    A.Facing = B.Facing;
                    A.Lift = RideH(A, B) + (F(t, 7, 2) == 0 ? B.P : 0);
                    SetAnim(A, "ride", t);
                    if (!s.Flag && pt > 0.8) { s.Flag = true; Say(A, Pick("Iuhuu!", "Mais rápido!", "Que vista!"), 1.6, t); }
                    if (pt > 4.8)
                    {
                        Next(s, t);
                        s.AX = A.X;
                        double d = Half(A) + Half(B) + 6, land = B.X + B.Facing * d;
                        if (land < 30 || land > W - 30) land = B.X - B.Facing * d;
                        s.BX = Clamp(land, 30, W - 30);
                    }
                    return true;
                }
                case 3:
                {
                    double u = Math.Min(1, pt / 0.6);
                    A.X = s.AX + (s.BX - s.AX) * u;
                    A.Lift = RideH(A, B) * (1 - u) + Hop(u, 12);
                    A.NoShadow = u < 0.6;
                    SetAnim(A, "jump", t); SetAnim(B, "watch", t);
                    if (u >= 1) { A.Lift = 0; A.NoShadow = false; Next(s, t); }
                    return true;
                }
            }
            Face(A, B); Face(B, A);
            React(A, "laugh", 1.3, t); React(B, "laugh", 1.3, t);
            return false;
        }

        bool Hug(Scene s, Creature A, Creature B, double pt, double t, double dt)
        {
            switch (s.Phase)
            {
                case 0:
                    if (Meet(s, t, dt)) { Next(s, t); Say(A, Pick("Vem cá!", "Abraço!"), 1.3, t); }
                    return true;
                case 1:
                    SetAnim(A, "hug", t); SetAnim(B, "hug", t);
                    if (!s.Flag && pt > 1.1) { s.Flag = true; Say(B, Pick("Amigos!", "<3", "Que fofo!"), 1.4, t); }
                    if (pt > 2.6) Next(s, t);
                    return true;
            }
            React(A, "pet", 1.2, t); React(B, "pet", 1.2, t);
            return false;
        }

        // Sozinho: corre atrás de uma borboleta, pula para pegar, e ela vai embora.
        bool Butterfly(Scene s, Creature A, double pt, double t, double dt)
        {
            double el = t - s.Start;
            if (s.Phase == 0)
            {
                s.BugX = Clamp(s.FX + Math.Sin(el * 0.8) * 110, 24, W - 24);
                s.BugY = 30 + Math.Sin(el * 2.6) * 12 + (el < 1 ? (1 - el) * 70 : 0);
                if (!s.Flag && el > 0.6) { s.Flag = true; Say(A, Pick("Uma borboleta!", "Ooh!"), 1.4, t); }
                if (s.JumpAt > 0)
                {
                    double u = (t - s.JumpAt) / 0.5;
                    A.Lift = Hop(u, 22);
                    if (u >= 1) { A.Lift = 0; s.JumpAt = 0; }
                }
                else if (Math.Abs(s.BugX - A.X) < Half(A) + 18 && t - s.LastJump > 1.3) { s.JumpAt = s.LastJump = t; }
                double target = s.BugX - Math.Sign(s.BugX - A.X) * (Half(A) + 2);
                bool there = Go(A, target, s.JumpAt > 0 ? 30 : 46, dt, t, "run");
                if (s.JumpAt > 0) { Face2(A, s.BugX); SetAnim(A, "jump", t); }
                else if (there) { Face2(A, s.BugX); SetAnim(A, "lookup", t); }
                if (el > 8 && s.JumpAt == 0)
                {
                    Next(s, t);
                    A.Lift = 0;
                    Say(A, Pick("Tchau, borboleta!", "Quase!"), 1.6, t);
                }
                return true;
            }
            if (s.Phase == 1)
            {
                s.BugY += 70 * dt;
                s.BugX += Math.Sin(t * 3) * 30 * dt;
                Face2(A, s.BugX);
                SetAnim(A, "wave", t);
                if (pt > 2.2) Next(s, t);
                return true;
            }
            React(A, "laugh", 1.0, t);
            return false;
        }

        static void Face2(Creature c, double x) { c.Facing = x >= c.X ? 1 : -1; }

        // Filhote de fora do evento: não segue o grande para o meio da brincadeira; fica onde
        // está e, se alguém do evento chegar perto, sai do caminho.
        double AvoidScene(Account a, Creature p, double target)
        {
            if (scene.Members().Contains(a.Main)) target = p.X;
            return Dodge(p, target);
        }

        // Quem está fora do evento assiste parado, virado para a brincadeira, e só anda para
        // sair do caminho.
        void Spectate(Creature c, double t, double dt)
        {
            double target = Dodge(c, c.X);
            if (Math.Abs(target - c.X) > 1) { Go(c, target, 44, dt, t, "walk"); return; }
            Face2(c, scene.Members().Average(m => m.X));
            SetAnim(c, "watch", t);
        }

        static readonly string[] Roaming = { "chase", "race", "ride", "leapfrog", "butterfly" };

        // Sai do espaço ocupado pelo evento, pelo lado mais perto. Nos eventos que andam pela
        // faixa toda (corrida, pega-pega...) não adianta fugir: fica onde está.
        double Dodge(Creature p, double target)
        {
            if (scene.Kind == "race")
            {
                // na corrida, só não fica parado em cima da linha de chegada
                double zone = Half(p) + 40;
                if (Math.Abs(target - W / 2) >= zone) return target;
                return target < W / 2 ? W / 2 - zone - 4 : W / 2 + zone + 4;
            }
            if (Roaming.Contains(scene.Kind)) return target;
            var ms = scene.Members();
            double lo = ms.Min(m => m.X - Half(m)) - Half(p) - 8, hi = ms.Max(m => m.X + Half(m)) + Half(p) + 8;
            if (target <= lo || target >= hi) return target;
            bool goLeft = target - lo < hi - target;
            if (goLeft && lo - 4 < 20) goLeft = false;
            if (!goLeft && hi + 4 > W - 20) goLeft = true;
            return Clamp(goLeft ? lo - 4 : hi + 4, 20, W - 20);
        }

        // Os objetos do evento que não pertencem a nenhum Clawd: bola, corda, bandeira, borboleta.
        static readonly string[] BallArt = { ".kkk.", "kwrwk", "kwwrk", "krwwk", ".kkk." };
        static readonly string[] Bug1 = { "PP.PP", "PPkPP", ".PkP." };
        static readonly string[] Bug2 = { "..k..", "PPkPP", "..k.." };
        static readonly string[] FlagArt = { "kwkwk", "wkwkw", "kwkwk", "wkwkw" };

        void DrawScene(DrawingContext dc, double t)
        {
            var s = scene;
            if (s == null) return;
            const int P = 2;
            if (s.Kind == "race")
            {
                double x = Math.Round(W / 2 / P) * P;
                dc.DrawRectangle(neutral['G'], null, new Rect(x, Ground - 40, P, 40));
                DrawArt(dc, FlagArt, x + P, Ground - 40, P);
            }
            if (s.Kind == "tug" && (s.Phase == 1 || s.Phase == 2))
            {
                double ax = s.A.X + s.A.Facing * 10.5 * s.A.P, ay = Ground - 9.5 * s.A.P;
                double bx = s.B.X + s.B.Facing * 10.5 * s.B.P, by = Ground - 9.5 * s.B.P;
                int n = (int)(Math.Abs(bx - ax) / P);
                for (int i = 0; i <= n; i++)
                {
                    double u = n == 0 ? 0 : (double)i / n;
                    double x = ax + (bx - ax) * u, y = ay + (by - ay) * u + Math.Sin(u * Math.PI) * 3;
                    dc.DrawRectangle(neutral['o'], null, new Rect(Math.Round(x / P) * P, Math.Round(y / P) * P, P, P));
                }
                double mx = (ax + bx) / 2, my = (ay + by) / 2 + 3;
                dc.DrawRectangle(neutral['r'], null, new Rect(Math.Round(mx / P) * P, Math.Round(my / P) * P, P, 3 * P));
            }
            if (!double.IsNaN(s.BallX))
            {
                int sh = Math.Max(2, 5 - (int)(s.BallLift / 12));
                dc.DrawRectangle(neutral['z'], null, new Rect(Math.Round(s.BallX / P) * P - sh, Ground - P, sh * 2, P));
                DrawArt(dc, BallArt, s.BallX - 5, Ground - s.BallLift - 10, P);
            }
            if (s.Kind == "butterfly" && !double.IsNaN(s.BugX))
                DrawArt(dc, F(t, 9, 2) == 0 ? Bug1 : Bug2, s.BugX - 5, Ground - s.BugY - 6, P);
        }

        void DrawArt(DrawingContext dc, string[] art, double x, double y, int P)
        {
            scratch.Clear();
            scratch.Stamp(art, 0, 0);
            Blit(dc, scratch, neutral, Math.Round(x / P) * P - Pix.OX * P, Math.Round(y / P) * P - Pix.OY * P, P);
        }

        // ------------------------------------------------------- partículas --

        void Emit(Creature c, double t)
        {
            if (t < c.NextEmit) return;
            int P = c.P;
            double head = Ground - c.Lift - 15 * P;
            double front = c.X + 12 * P * c.Facing;
            switch (c.Anim)
            {
                case "sleep":
                    Add(c.Pup ? "z1" : "z2", c.X + 7 * P * c.Facing, head, 7 * c.Facing, -12, 2.8, 'g', c);
                    c.NextEmit = t + 1.4;
                    break;
                case "dance":
                case "conduct":
                    Add("note", c.X + (rng.NextDouble() - 0.5) * 16 * P, head, (rng.NextDouble() - 0.5) * 14, -20, 1.8, 'y', c);
                    c.NextEmit = t + 0.7;
                    break;
                case "pet":
                    Add("heart", c.X + (rng.NextDouble() - 0.5) * 16 * P, head, (rng.NextDouble() - 0.5) * 16, -36, 1.3, 'P', c);
                    c.NextEmit = t + 0.25;
                    break;
                case "build":
                    if (((int)((t - c.AnimStart) * 3)) % 2 == 1)
                        for (int i = 0; i < 4; i++) Add("dot", front + 2 * P * c.Facing, Ground - 7 * P, (rng.NextDouble() - 0.5) * 130, -60 - rng.NextDouble() * 70, 0.45, 'y', c);
                    c.NextEmit = t + 0.33;
                    break;
                case "tool":
                case "plug":
                    for (int i = 0; i < 2; i++) Add("dot", front, Ground - 6 * P, (rng.NextDouble() - 0.5) * 110, -50 - rng.NextDouble() * 60, 0.45, 'y', c);
                    c.NextEmit = t + 0.4;
                    break;
                case "glob":
                    Add("paper", front, Ground - 7 * P, (rng.NextDouble() - 0.3) * 40 * c.Facing, -70, 1.0, 'p', c);
                    c.NextEmit = t + 0.6;
                    break;
                case "spell":
                    Add("plus", front + (rng.NextDouble() - 0.5) * 8 * P, Ground - 10 * P, (rng.NextDouble() - 0.5) * 12, -26, 1.2, rng.NextDouble() < 0.5 ? 'y' : 'P', c);
                    c.NextEmit = t + 0.3;
                    break;
                case "thinkhard":
                case "compact":
                    Add("steam", c.X + (rng.NextDouble() - 0.5) * 10 * P, head - 2 * P, (rng.NextDouble() - 0.5) * 10, -22, 1.4, 'm', c);
                    c.NextEmit = t + 0.45;
                    break;
                case "done":
                    Sparkle(c.X + (rng.NextDouble() - 0.5) * 20 * P, head + rng.NextDouble() * 6 * P, 1);
                    c.NextEmit = t + 0.25;
                    break;
                case "test":
                    Add("dot", front + 1 * P * c.Facing, Ground - 17 * P, (rng.NextDouble() - 0.5) * 8, -24, 1.0, 'w', c);
                    c.NextEmit = t + 0.35;
                    break;
                case "hug":
                    Add("heart", c.X + c.Facing * 10 * P, head, (rng.NextDouble() - 0.5) * 14, -30, 1.2, 'P', c);
                    c.NextEmit = t + 0.35;
                    break;
                case "eat":
                    Add("dot", c.X + c.Facing * 6 * P, Ground - c.Lift - 8 * P, (rng.NextDouble() - 0.5) * 50, -30, 0.6, 'o', c);
                    c.NextEmit = t + 0.3;
                    break;
                case "run":
                case "flee":
                    Add("puff", c.X - c.Facing * 8 * P, Ground - 2 * P, -c.Facing * 20, -8, 0.4, 'w', c);
                    c.NextEmit = t + 0.18;
                    break;
                case "tug":
                    if (rng.NextDouble() < 0.35) Add("dot", c.X, head + 3 * P, -c.Facing * 30, -40, 0.6, 'c', c);
                    c.NextEmit = t + 0.3;
                    break;
                default:
                    c.NextEmit = t + 0.2;
                    break;
            }
        }

        void Add(string kind, double x, double y, double vx, double vy, double life, char color, Creature c)
        {
            if (parts.Count > 260) return;
            var p = new Particle();
            p.Kind = kind; p.X = x; p.Y = y; p.VX = vx; p.VY = vy; p.Life = p.Max = life; p.Color = color;
            p.Acc = c != null ? c.Acc : null;
            p.P = c != null ? c.P : 2;
            parts.Add(p);
        }

        void Confetti(Creature c, int n)
        {
            char[] colors = { 'b', 'a', 'y', 'P', 'c', 'w', 'n', 'f' };
            for (int i = 0; i < n; i++)
                Add("dot", c.X, Ground - c.Lift - 14 * c.P, (rng.NextDouble() - 0.5) * 260, -150 - rng.NextDouble() * 170, 1.6 + rng.NextDouble() * 0.6, colors[rng.Next(colors.Length)], c);
        }

        void Puff(Creature c, int n)
        {
            for (int i = 0; i < n; i++)
            {
                double a = i * Math.PI * 2 / n;
                Add("puff", c.X + Math.Cos(a) * 4 * c.P, Ground - 5 * c.P + Math.Sin(a) * 2 * c.P, Math.Cos(a) * 40, Math.Sin(a) * 18 - 10, 0.6, 'w', c);
            }
        }

        void Sparkle(double x, double y, int n)
        {
            for (int i = 0; i < n; i++)
                Add("spark", x + (rng.NextDouble() - 0.5) * 30, y + (rng.NextDouble() - 0.5) * 16, 0, -6, 0.9, 'y', null);
        }

        void UpdateParticles(double dt)
        {
            for (int i = parts.Count - 1; i >= 0; i--)
            {
                var p = parts[i];
                p.Life -= dt;
                if (p.Life <= 0) { parts.RemoveAt(i); continue; }
                switch (p.Kind)
                {
                    case "dot": p.VY += 400 * dt; p.VX *= 1 - dt * 1.4; break;
                    case "paper": p.VY += 160 * dt; break;
                    case "heart": p.VX = Math.Sin((p.Max - p.Life) * 6) * 14; break;
                    case "note": p.VX = Math.Sin((p.Max - p.Life) * 5) * 12; break;
                    case "puff": p.VX *= 1 - dt * 3; p.VY *= 1 - dt * 3; break;
                }
                p.X += p.VX * dt;
                p.Y += p.VY * dt;
                if (p.Kind == "dot" && p.Y > Ground - 1) { p.Y = Ground - 1; p.VY = 0; p.VX = 0; }
            }
        }

        // ------------------------------------------------------------ mouse --

        Creature HitTest(Point pt)
        {
            foreach (var c in AllCreatures().Reverse())
                if (Inflate(c.Bounds, 3).Contains(pt)) return c;
            return null;
        }

        Point Pos(MouseEventArgs e)
        {
            if (!InHome) return e.GetPosition(view);
            Point p = e.GetPosition(homeView);
            return new Point(p.X, p.Y - HomeOffsetY);
        }

        double MaxLift { get { return Ground - 60 + HomeOffsetY; } }

        void OnDown(object sender, MouseButtonEventArgs e)
        {
            Point pt = Pos(e);
            var c = HitTest(pt);
            if (c == null)
            {
                if (InHome && pt.Y > Ground - 70) { CallTo(pt.X, T); e.Handled = true; }
                return;
            }
            if (e.ClickCount >= 2) { OpenClaude(c.Acc); e.Handled = true; return; }
            pressed = true;
            pressPt = pt;
            pressC = c;
            Surface.CaptureMouse();
            e.Handled = true;
        }

        void OnMove(object sender, MouseEventArgs e)
        {
            if (!pressed) return;
            Point pt = Pos(e);
            double t = T;
            if (drag == null)
            {
                if (Math.Abs(pt.X - pressPt.X) + Math.Abs(pt.Y - pressPt.Y) < 5) return;
                drag = pressC;
                drag.React = null;
                drag.Falling = false;
                dragOff = new Point(pt.X - drag.X, pt.Y - (Ground - drag.Lift));
                lastDragPt = pt;
                lastDragT = t;
                panel.Close2();
                Say(drag, drag.Pup ? "Ei!" : "Ei! Me põe no chão!", 2.2, t);
            }
            drag.X = Clamp(pt.X - dragOff.X, 24, W - 24);
            drag.Lift = Clamp(Ground - (pt.Y - dragOff.Y), 0, MaxLift);
            double vt = Math.Max(0.001, t - lastDragT);
            drag.Swing += (Clamp(-(pt.X - lastDragPt.X) / vt * 0.02, -2, 2) - drag.Swing) * 0.3;
            lastDragPt = pt;
            lastDragT = t;
        }

        void OnUp(object sender, MouseButtonEventArgs e)
        {
            if (!pressed) return;
            pressed = false;
            Surface.ReleaseMouseCapture();
            double t = T;
            if (drag != null)
            {
                drag.Falling = true;
                drag.VY = 0;
                drag.Idle = "stand";
                drag.WalkTarget = double.NaN;
                drag = null;
            }
            else if (pressC != null) Pet(pressC, t);
            e.Handled = true;
        }

        void Pet(Creature c, double t)
        {
            petCount = t - lastPet < 3 ? petCount + 1 : 1;
            lastPet = t;
            if (c.Falling) return;
            React(c, "pet", 1.8, t);
            if (petCount >= 5) { Say(c, "Tá bom, tá bom! Hahaha", 2.2, t); petCount = 0; }
            else Say(c, Pets[rng.Next(Pets.Length)], 1.6, t);
        }

        void OnRight(object sender, MouseButtonEventArgs e)
        {
            var c = HitTest(Pos(e));
            if (c == null) return;
            panel.Close2();
            var a = c.Acc;
            var menu = new ContextMenu();
            menu.Items.Add(Item("Abrir o Claude " + a.Name, true, delegate { OpenClaude(a); }));
            menu.Items.Add(Item("Fazer carinho", false, delegate { Pet(c, T); }));
            if (InHome)
            {
                menu.Items.Add(Item("Brincar agora", false, delegate { PlayNow(); }));
            }
            else
            {
                menu.Items.Add(Item("Esconder por 1 hora", false, delegate { a.HiddenUntil = T + 3600; a.Visible = false; a.Main = null; a.Pups.Clear(); }));
                menu.Items.Add(new Separator());
                menu.Items.Add(Item("Abrir o Clawd Companheiro", false, delegate { if (OpenAppRequested != null) OpenAppRequested(); }));
                string other = corner == "esquerdo" ? "direito" : "esquerdo";
                menu.Items.Add(Item("Ficar no canto " + other, false, delegate { SetCorner(other); }));
                menu.Items.Add(Item("Tirar da barra de tarefas", false, delegate { SetTaskbar(false); }));
                menu.Items.Add(Item("Sair", false, delegate { if (QuitRequested != null) QuitRequested(); else Application.Current.Shutdown(); }));
            }
            menu.PlacementTarget = Surface;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
            menu.IsOpen = true;
            e.Handled = true;
        }

        static MenuItem Item(string text, bool bold, RoutedEventHandler click)
        {
            var m = new MenuItem();
            m.Header = text;
            if (bold) m.FontWeight = FontWeights.Bold;
            m.Click += click;
            return m;
        }

        void SetCorner(string c)
        {
            corner = c;
            try { cfg.Corner = c; cfg.Save(); cfgStamp = Cfg.Stamp(); } catch { }
            Place();
            foreach (var a in accounts) if (a.Main != null) a.Main.X = W - a.Main.X;
            Homes();
        }

        // O app gravou a config: relê já, sem esperar a próxima leitura.
        public void ReloadConfig()
        {
            Safe(LoadConfig);
            Safe(Poll);
        }

        public void SetTaskbar(bool on)
        {
            try { cfg.Taskbar = on; cfg.Save(); cfgStamp = Cfg.Stamp(); } catch { }
            Safe(Poll);
        }

        void OpenClaude(Account a)
        {
            if (a.Main != null) { Say(a.Main, "Abrindo o Claude " + a.Name + "…", 2.2, T); React(a.Main, "greet", 1.4, T); }
            try { Profiles.Launch(a.Profile, a.Name); }
            catch (Exception e) { Program.Log("abrir " + a.Key + ": " + e.Message); }
        }

        // ------------------------------------------------------------ desenho --

        public void Render(DrawingContext dc)
        {
            double t = T;
            // quem está no alto (montado, pulando, arrastado) é desenhado por cima
            foreach (var c in AllCreatures().OrderBy(x => x.Lift > 0 ? 1 : 0).ToList()) DrawCreature(dc, c, t);
            DrawScene(dc, t);
            DrawParticles(dc);
            // Os grandes falam primeiro; um balão que bateria em outro espera a vez.
            var used = new List<Rect>();
            foreach (var c in AllCreatures().OrderBy(x => x.Pup)) DrawBubble(dc, c, t, used);
        }

        static void Blit(DrawingContext dc, Pix g, Dictionary<char, Brush> pal, double ox, double oy, double P)
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

        int PopRows(Creature c, double t)
        {
            // Nascendo: o Clawd sobe do chão em degraus; saindo, afunda.
            double k = Math.Min(1, (t - c.BornAt) / 0.4);
            if (c.Leaving) k = Math.Min(k, Math.Max(0, 1 - (t - c.LeaveT - 0.6) / 0.3));
            return (int)Math.Round((1 - k) * 16);
        }

        void DrawCreature(DrawingContext dc, Creature c, double t)
        {
            int P = c.P;
            int sink = PopRows(c, t);
            Pix g = c.Grid;
            g.Clear();
            g.Flip = c.Facing < 0;
            int liftRows;
            Compose(c, g, t, out liftRows);
            double lift = Math.Round(c.Lift / P) * P + liftRows * P;
            double gx = Math.Round(c.X), gy = Math.Round(Ground - lift);

            // sombra fixa no chão, menor quando ele sobe
            if (sink < 16 && !c.NoShadow)
            {
                int sw2 = Math.Max(3, 7 - (int)(lift / (4 * P)));
                dc.DrawRectangle(c.Acc.Pal['z'], null, new Rect(gx - sw2 * P, Ground - P, 2 * sw2 * P, P));
            }
            if (sink > 0)
            {
                dc.PushClip(new RectangleGeometry(new Rect(0, 0, W, Ground)));
                Blit(dc, g, c.Acc.Pal, gx - Pix.OX * P, gy - Pix.OY * P + sink * P, P);
                dc.Pop();
            }
            else Blit(dc, g, c.Acc.Pal, gx - Pix.OX * P, gy - Pix.OY * P, P);

            c.Bounds = new Rect(c.X - 11 * P, gy - 16 * P, 22 * P, 16 * P);
            c.TopY = gy + (g.MinRow - Pix.OY) * P;
        }

        void DrawParticles(DrawingContext dc)
        {
            foreach (var p in parts)
            {
                var pal = p.Acc != null ? p.Acc.Pal : neutral;
                double fade = Math.Min(1, p.Life / Math.Min(0.4, p.Max * 0.5));
                if (p.Kind == "puff" || p.Kind == "steam") fade = p.Life / p.Max * 0.8;
                if (fade < 0.15) continue;
                int P = p.P;
                scratch.Clear();
                switch (p.Kind)
                {
                    case "dot": scratch.Px(0, 0, p.Color); break;
                    case "paper": scratch.Stamp(Art.Paper, 0, 0); break;
                    case "heart": scratch.Stamp(Art.HeartS, -1, -1); break;
                    case "note": scratch.Stamp(Art.Note, -2, -2); break;
                    case "z1": scratch.Stamp(Art.Z1, -1, -1); break;
                    case "z2": scratch.Stamp(Art.Z2, -2, -2); break;
                    case "plus": scratch.Rect(0, -1, 1, 3, p.Color); scratch.Rect(-1, 0, 3, 1, p.Color); break;
                    case "spark": scratch.Stamp(((int)(p.Life * 8)) % 2 == 0 ? Art.Spark : Art.Plus, -2, -2); break;
                    case "puff": scratch.Rect(0, 0, 2, 2, 'w'); break;
                    case "steam": scratch.Rect(0, 0, 2, 2, p.Life / p.Max > 0.5 ? 'm' : 'i'); break;
                }
                dc.PushOpacity(fade);
                Blit(dc, scratch, pal, Math.Round(p.X / P) * P - Pix.OX * P, Math.Round(p.Y / P) * P - Pix.OY * P, P);
                dc.Pop();
            }
        }

        void DrawBubble(DrawingContext dc, Creature c, double t, List<Rect> used)
        {
            if (c.BubbleText == null || t > c.BubbleUntil || PopRows(c, t) > 4) return;
            double fs = c.Pup ? 10 : 11;
            var ft = new FormattedText(c.BubbleText, Words.PtBR, FlowDirection.LeftToRight, font, fs, BubbleInk, pixelsPerDip);
            ft.MaxTextWidth = c.Pup ? 110 : 170;
            const double S = 2;
            double w = Math.Ceiling((ft.Width + 14) / S) * S, h = Math.Ceiling((ft.Height + 8) / S) * S;
            double cx = Math.Round(Clamp(c.X, w / 2 + 4, W - w / 2 - 4) / S) * S;
            double bottom = Math.Round((c.TopY - 8) / S) * S;
            double top = Math.Max(2, bottom - h);
            double life = Math.Min(t - c.BubbleFrom, c.BubbleUntil - t);
            if (life < 0.08) return;
            double x0 = cx - w / 2;
            var box = new Rect(x0 - 2, top - 2, w + 4, h + 6);
            foreach (var r in used) if (r.IntersectsWith(box)) return;
            used.Add(box);
            Brush edge = c.Acc.Pal['b'];
            // caixa com cantos em degrau, como num jogo antigo
            dc.DrawRectangle(edge, null, new Rect(x0 + S, top, w - 2 * S, h));
            dc.DrawRectangle(edge, null, new Rect(x0, top + S, w, h - 2 * S));
            dc.DrawRectangle(BubbleFill, null, new Rect(x0 + S, top + S, w - 2 * S, h - 2 * S));
            double tx = Math.Round(Clamp(c.X, x0 + 3 * S, x0 + w - 4 * S) / S) * S;
            dc.DrawRectangle(edge, null, new Rect(tx, top + h, 2 * S, S));
            dc.DrawRectangle(edge, null, new Rect(tx, top + h + S, S, S));
            dc.DrawText(ft, new Point(Math.Round(cx - ft.Width / 2), Math.Round(top + (h - ft.Height) / 2)));
        }

        // ---------------------------------------------------- o Clawd em pixels --

        static int F(double t, double fps, int n) { return (int)Math.Floor(t * fps) % n; }

        // Monta o quadro do Clawd: pose do corpo e objetos da animação atual.
        void Compose(Creature c, Pix g, double t, out int liftRows)
        {
            var p = new Pose();
            double tl = t - c.AnimStart;
            string mood = c.Acc.Mood;
            p.Blink = t < c.BlinkUntil;
            p.LookX = c.LookX;
            p.LookY = c.LookY;
            p.Frame = F(t, 2, 2);
            p.ArmL = p.ArmR = F(t, 0.8, 2) == 0 ? Arm.Mid : Arm.Down;
            p.Eyes = mood == "triste" ? "sad" : mood == "sonolento" ? "sleepy" : "open";
            if (mood == "triste") p.ArmL = p.ArmR = Arm.Down;
            if (!double.IsNaN(c.Ctx) && c.Ctx >= 85) p.Puff = 1;
            bool hat = c.Acc.Cfg == null || c.Acc.Cfg.Hat;
            liftRows = 0;
            string anim = c.Anim;

            // ---- pose
            switch (anim)
            {
                case "walk":
                    p.Legs = Legs.Walk;
                    p.Frame = F(t, mood == "feliz" ? 10 : 7, 4);
                    p.ArmL = p.Frame < 2 ? Arm.Mid : Arm.Down;
                    p.ArmR = p.Frame < 2 ? Arm.Down : Arm.Mid;
                    if (mood == "feliz") { p.Mouth = "smile"; liftRows = p.Frame % 2; }
                    break;
                case "watch": if (mood != "triste") p.Mouth = "smile"; break;
                case "look": p.LookX = F(tl, 1.2, 2) == 0 ? 1 : -1; p.LookY = 0; break;
                case "stretch":
                    p.ArmL = p.ArmR = tl > 0.3 && tl < 1.9 ? Arm.High : Arm.Up;
                    p.Stretch = tl > 0.3 && tl < 1.9 ? 1 : 0;
                    p.Eyes = "closed";
                    p.Mouth = "o";
                    break;
                case "sit":
                    p.Legs = Legs.Sit; p.Dy = 2; p.Frame = F(t, 2.5, 2); p.ArmL = p.ArmR = Arm.Down; p.LookY = Math.Max(0, p.LookY);
                    break;
                case "book":
                    p.Legs = Legs.Sit; p.Dy = 2; p.Frame = 0; p.ArmL = Arm.Down; p.ArmR = Arm.Low; p.LenR = 3; p.Eyes = "focus"; p.LookX = 1; p.LookY = 1;
                    break;
                case "yawn":
                    p.Mouth = tl < 2 ? "open" : ""; p.Eyes = "closed"; p.ArmL = p.ArmR = tl < 1.8 ? Arm.Up : Arm.Mid;
                    break;
                case "hop":
                {
                    int[] seq = { 0, 2, 4, 5, 5, 4, 2, 0, 0 };
                    int k = F(tl, 16, seq.Length);
                    liftRows = seq[k];
                    if (k == 0 || k == 8) { p.Puff = Math.Max(p.Puff, 1); p.Dy = 1; }
                    p.ArmL = p.ArmR = liftRows > 2 ? Arm.Up : Arm.Mid;
                    p.Eyes = "happy"; p.Mouth = "smile";
                    break;
                }
                case "dance":
                {
                    int k = F(t, 4, 4);
                    p.ArmL = k == 0 || k == 1 ? Arm.High : Arm.Down;
                    p.ArmR = k == 2 || k == 3 ? Arm.High : Arm.Down;
                    p.ShakeX = k == 1 ? 1 : k == 3 ? -1 : 0;
                    liftRows = k % 2;
                    p.Eyes = "happy"; p.Mouth = "tongue";
                    break;
                }
                case "juggle":
                    p.ArmL = F(t, 4, 2) == 0 ? Arm.Up : Arm.Mid;
                    p.ArmR = F(t, 4, 2) == 1 ? Arm.Up : Arm.Mid;
                    p.LookY = -1; p.LookX = 0; p.Mouth = "smile";
                    break;
                case "nod":
                {
                    bool down = F(t, 0.7, 3) == 0;
                    p.Eyes = down ? "closed" : "sleepy"; p.Dy = down ? 1 : 0;
                    break;
                }
                case "wave":
                case "greet":
                    p.ArmR = F(t, 6, 2) == 0 ? Arm.Up : Arm.High;
                    p.Eyes = "happy"; p.Mouth = "smile";
                    break;
                case "surprise":
                    p.Eyes = "wide"; p.Mouth = "o"; p.ArmL = p.ArmR = Arm.Up; liftRows = tl < 0.25 ? 2 : 0;
                    break;
                case "pet":
                    p.Eyes = "heart"; p.Blush = true; p.Mouth = "smile"; p.ArmL = p.ArmR = Arm.Up;
                    p.ShakeX = tl < 1.2 ? (F(t, 10, 2) == 0 ? 1 : 0) : 0;
                    break;
                case "cheer":
                    p.ArmL = F(t, 5, 2) == 0 ? Arm.High : Arm.Up;
                    p.ArmR = F(t, 5, 2) == 1 ? Arm.High : Arm.Up;
                    liftRows = F(t, 5, 2) * 2; p.Eyes = "happy"; p.Mouth = "smile";
                    break;
                case "done":
                {
                    int[] seq = { 2, 5, 7, 8, 8, 7, 5, 2, 0, 0, 0, 0 };
                    int k = Math.Min(seq.Length - 1, (int)(tl * 14));
                    liftRows = seq[k];
                    if (k == 8) { p.Puff = 1; p.Dy = 1; }
                    p.ArmL = p.ArmR = Arm.High;
                    if (c.Trophy && tl > 0.9) { p.ArmL = Arm.Up; p.ArmR = Arm.High; }
                    p.Eyes = tl < 1.6 ? "star" : "happy"; p.Mouth = "smile"; p.Blush = true;
                    break;
                }
                case "error":
                    p.Eyes = "sad"; p.ArmL = p.ArmR = Arm.Down; p.Mouth = "wobble";
                    if (tl < 0.8) p.ShakeX = F(t, 14, 2) == 0 ? 1 : -1;
                    break;
                case "halt":
                    p.Eyes = "wide"; p.Mouth = "o"; p.ArmL = p.ArmR = Arm.High; liftRows = tl < 0.2 ? 1 : 0;
                    break;
                case "refuse":
                    p.Eyes = "angry"; p.Mouth = "flat"; p.Cross = true; p.ArmL = p.ArmR = Arm.Hidden;
                    p.ShakeX = tl < 1.4 ? (F(t, 5, 2) == 0 ? 1 : -1) : 0;
                    break;
                case "sleep":
                    p.Legs = Legs.Tuck; p.Dy = 4; p.Eyes = "closed"; p.Cap = true; p.ArmL = p.ArmR = Arm.Hidden;
                    p.Stretch = F(t, 0.6, 2) == 0 ? 0 : -1;
                    break;
                case "drag":
                    p.Legs = Legs.Dangle; p.Frame = F(t, 8, 2);
                    p.ArmL = F(t, 8, 2) == 0 ? Arm.Up : Arm.High; p.ArmR = F(t, 8, 2) == 0 ? Arm.High : Arm.Up;
                    p.Eyes = "wide"; p.Mouth = "o";
                    p.ShakeX = (int)Math.Round(c.Swing);
                    break;
                case "fall":
                    p.Legs = Legs.Dangle; p.ArmL = p.ArmR = Arm.High; p.Eyes = "wide"; p.Mouth = "o";
                    break;
                case "dizzy":
                    p.Eyes = "spiral"; p.Mouth = "wobble"; p.ArmL = p.ArmR = Arm.Down; p.ShakeX = F(t, 3, 2);
                    break;
                case "lowbat":
                    p.Eyes = "sleepy"; p.ArmL = p.ArmR = Arm.Down; p.Mouth = "flat";
                    break;

                // ---- eventos entre Clawds
                case "run":
                case "flee":
                    p.Legs = Legs.Walk; p.Frame = F(t, 14, 4);
                    p.ArmL = p.Frame < 2 ? Arm.Out : Arm.Mid; p.ArmR = p.Frame < 2 ? Arm.Mid : Arm.Out;
                    liftRows = p.Frame % 2;
                    p.LookX = 1; p.LookY = 0;
                    if (anim == "flee") { p.Eyes = "wide"; p.Mouth = "open"; } else { p.Eyes = "happy"; p.Mouth = "smile"; }
                    break;
                case "ready":
                    p.Dy = 1; p.Puff = Math.Max(p.Puff, 1); p.ArmL = p.ArmR = Arm.Down; p.Eyes = "focus"; p.Mouth = "flat"; p.LookX = 1; p.LookY = 0;
                    break;
                case "highfive":
                    p.ArmR = Arm.High; p.ArmL = Arm.Up; p.Legs = Legs.Dangle; p.Frame = 0; p.Eyes = "happy"; p.Mouth = "open";
                    break;
                case "jump":
                    p.Legs = Legs.Dangle; p.Frame = F(t, 8, 2); p.ArmL = p.ArmR = Arm.High; p.Eyes = "happy"; p.Mouth = "open";
                    break;
                case "laugh":
                    p.Eyes = "happy"; p.Mouth = "open"; p.Blush = true; p.ShakeX = F(t, 8, 2); liftRows = F(t, 6, 2);
                    p.ArmL = p.ArmR = F(t, 6, 2) == 0 ? Arm.Mid : Arm.Up;
                    break;
                case "kick":
                    p.Legs = Legs.Tap; p.Frame = 0; p.ArmL = Arm.Up; p.ArmR = Arm.Mid; p.Eyes = "focus"; p.Mouth = "flat"; p.LookX = 1;
                    break;
                case "head":
                    p.ArmL = p.ArmR = Arm.Up; p.LookX = 1; p.LookY = -1; p.Eyes = "wide"; p.Mouth = "o";
                    break;
                case "crouch":
                    p.Legs = Legs.Tuck; p.Dy = 4; p.Puff = Math.Max(p.Puff, 1); p.ArmL = p.ArmR = Arm.Hidden; p.Eyes = "closed"; p.Mouth = "flat";
                    break;
                case "hug":
                    p.ArmR = Arm.Out; p.LenR = 4; p.ArmL = Arm.Up; p.Eyes = "happy"; p.Mouth = "smile"; p.Blush = true;
                    break;
                case "give":
                    p.ArmR = Arm.Out; p.LenR = 3; p.Eyes = "happy"; p.Mouth = "smile"; p.LookX = 1;
                    break;
                case "eat":
                    p.ArmR = Arm.Low; p.LenR = 2; p.Eyes = "happy"; p.Blush = true; p.Mouth = F(t, 4, 2) == 0 ? "open" : "flat";
                    break;
                case "tug":
                    p.Legs = Legs.Walk; p.Frame = F(t, 6, 4); p.ArmR = Arm.Out; p.LenR = 3; p.ArmL = Arm.Mid;
                    p.ShakeX = -1; p.Eyes = "angry"; p.Mouth = "wobble"; p.LookX = 1;
                    break;
                case "ride":
                    p.Legs = Legs.Dangle; p.Frame = F(t, 4, 2);
                    p.ArmL = F(t, 3, 2) == 0 ? Arm.High : Arm.Up; p.ArmR = F(t, 3, 2) == 1 ? Arm.High : Arm.Up;
                    p.Eyes = "happy"; p.Mouth = "tongue";
                    break;
                case "carry":
                    p.Legs = Legs.Walk; p.Frame = F(t, 6, 4); p.ArmL = p.ArmR = Arm.Up; p.Eyes = "focus"; p.Mouth = "flat"; p.LookY = -1;
                    break;
                case "talk":
                    p.Mouth = F(t, 6, 2) == 0 ? "open" : "smile"; p.ArmR = F(t, 1.5, 2) == 0 ? Arm.Out : Arm.Mid; p.LenR = 2; p.LookX = 1; p.LookY = 0;
                    break;
                case "pout":
                    p.Eyes = "sad"; p.Mouth = "frown"; p.ArmL = p.ArmR = Arm.Down; p.LookY = 1;
                    break;
                case "lookup":
                    p.LookX = 1; p.LookY = -1; p.Mouth = "o"; p.ArmL = p.ArmR = Arm.Mid;
                    break;

                // ---- trabalho
                case "bash":
                case "ps":
                case "git":
                case "peek":
                    p.ArmR = Arm.Type; p.Frame = F(t, 9, 2); p.Eyes = "focus"; p.LookX = 1; p.LookY = 1; p.ArmL = Arm.Down;
                    if (anim == "peek") { p.ArmR = Arm.Out; p.LenR = 3; p.Eyes = "wide"; }
                    break;
                case "test":
                    p.ArmR = Arm.Up; p.Eyes = "focus"; p.LookX = 1; p.LookY = -1; p.Mouth = F(t, 1, 3) == 2 ? "o" : "";
                    break;
                case "install":
                {
                    double k = (tl % 1.8) / 1.8;
                    p.ArmL = p.ArmR = k > 0.55 && k < 0.75 ? Arm.Out : Arm.Up; p.LenR = p.LenL = 3;
                    p.LookX = 1; p.LookY = k < 0.55 ? -1 : 1; p.Eyes = "wide";
                    break;
                }
                case "build":
                    p.ArmR = F(tl, 3, 2) == 0 ? Arm.High : Arm.Out; p.LenR = 4; p.Eyes = "focus"; p.LookX = 1; p.LookY = 1; p.Mouth = "flat";
                    break;
                case "trash":
                {
                    double k = (tl % 1.2) / 1.2;
                    p.ArmR = k < 0.3 ? Arm.Up : k < 0.45 ? Arm.Out : Arm.Mid; p.LenR = 3; p.LookX = 1; p.LookY = k > 0.4 ? 1 : 0;
                    break;
                }
                case "stack":
                    p.ArmR = Arm.Up; p.ArmL = Arm.Up; p.LookX = 1; p.LookY = -1; p.Mouth = "flat";
                    break;
                case "net":
                    p.ArmR = Arm.Out; p.LenR = 3; p.LookX = 1; p.LookY = -1; p.Eyes = "wide";
                    break;
                case "rocket":
                {
                    double k = (tl % 2.6) / 2.6;
                    p.ArmL = p.ArmR = k > 0.35 ? Arm.High : Arm.Mid; p.LookX = 1; p.LookY = k > 0.35 ? -1 : 0;
                    p.Eyes = k > 0.35 ? "happy" : "wide"; p.Mouth = k > 0.35 ? "smile" : "o";
                    break;
                }
                case "read":
                    p.ArmR = Arm.Low; p.LenR = 3; p.ArmL = Arm.Low; p.LenL = 2; p.Eyes = "focus"; p.LookX = 1; p.LookY = 1;
                    break;
                case "grep":
                    p.ArmR = Arm.Out; p.LenR = 3 + F(t, 2.5, 3); p.Eyes = "focus"; p.LookX = 1; p.LookY = 1;
                    break;
                case "glob":
                    p.ArmR = Arm.Out; p.LenR = 3; p.Eyes = "wide"; p.LookX = 1; p.LookY = 1; p.Mouth = "o";
                    break;
                case "edit":
                case "write":
                case "paint":
                case "grid":
                    p.ArmR = Arm.Out; p.LenR = 2 + F(t, 3, 3); p.Eyes = "focus"; p.LookX = 1; p.LookY = 0; p.Mouth = "tongue";
                    if (anim == "write") p.Mouth = "flat";
                    break;
                case "search":
                case "fetch":
                    p.ArmR = Arm.Out; p.LenR = 3; p.Eyes = "wide"; p.LookX = 1; p.LookY = anim == "fetch" ? 1 : 0;
                    break;
                case "browser":
                    p.ArmR = Arm.Out; p.LenR = 2; p.Eyes = "focus"; p.LookX = 1;
                    break;
                case "camera":
                    p.ArmR = Arm.Up; p.ArmL = Arm.Up; p.LookX = 1; p.Eyes = F(tl, 0.7, 2) == 1 && (tl % 1.43) < 0.15 ? "closed" : "focus";
                    break;
                case "mouse":
                    p.ArmR = Arm.Out; p.LenR = 3 + F(t, 1.5, 2); p.Eyes = "focus"; p.LookX = 1;
                    break;
                case "agent":
                    p.ArmR = Arm.Up; p.LookX = 1; p.Mouth = F(t, 5, 2) == 0 ? "o" : "flat";
                    break;
                case "conduct":
                    p.ArmR = F(t, 3, 2) == 0 ? Arm.High : Arm.Up; p.ArmL = F(t, 3, 2) == 1 ? Arm.High : Arm.Up; p.Eyes = "happy"; p.Mouth = "smile";
                    break;
                case "plan":
                    p.ArmR = Arm.Low; p.LenR = 3; p.Eyes = "focus"; p.LookX = 1; p.LookY = 1;
                    break;
                case "spell":
                    p.ArmR = Arm.Low; p.LenR = 3; p.ArmL = Arm.Up; p.Eyes = "star"; p.Mouth = "o";
                    break;
                case "toolbox":
                    p.ArmR = Arm.Out; p.LenR = 3; p.Eyes = "wide"; p.LookX = 1; p.LookY = 1;
                    break;
                case "plug":
                    p.ArmR = Arm.Out; p.LenR = 2 + F(tl, 1, 2); p.Eyes = "focus"; p.LookX = 1; p.LookY = 1;
                    break;
                case "tool":
                    p.ArmR = F(t, 4, 2) == 0 ? Arm.Out : Arm.Low; p.LenR = 3; p.Eyes = "focus"; p.LookX = 1; p.LookY = 1; p.Mouth = "flat";
                    break;
                case "think":
                    p.ArmR = Arm.Up; p.LookX = 1; p.LookY = -1;
                    break;
                case "thinkhard":
                    p.ArmR = Arm.Up; p.ArmL = Arm.Up; p.LookX = 0; p.LookY = -1; p.Eyes = "focus"; p.Mouth = "wobble";
                    break;
                case "ask":
                case "askplan":
                    p.ArmR = anim == "ask" ? Arm.High : Arm.Out; p.LenR = 3; p.Legs = Legs.Tap; p.Frame = F(t, 3, 2);
                    p.LookX = 0; p.LookY = c.LookY; p.Mouth = "o";
                    break;
                case "askperm":
                    p.ArmR = F(t, 2, 2) == 0 ? Arm.High : Arm.Up; p.Legs = Legs.Tap; p.Frame = F(t, 3, 2); p.Eyes = "wide"; p.LookX = 0; p.Mouth = "o";
                    break;
                case "compact":
                {
                    int k = F(t, 2, 4);
                    p.ArmR = Arm.Out; p.LenR = k == 0 ? 3 : k == 1 ? 5 : k == 2 ? 6 : 4; p.ArmL = Arm.Mid;
                    p.Eyes = k == 2 ? "closed" : "focus"; p.Mouth = k == 2 ? "frown" : "flat"; p.LookX = 1;
                    break;
                }
            }

            // ---- corpo
            Geo geo = Body.GeoOf(p);
            Skin skin = c.Pup ? c.Acc.PupSkin : c.Acc.Skin;
            DrawBack(c, g, p, geo, t, tl);
            Body.DrawParts(g, skin, "tras", geo);
            Body.DrawLegs(g, p, geo, t);
            Body.DrawArm(g, p.ArmL, p.LenL, geo, -1, p.Frame);
            Body.DrawArm(g, p.ArmR, p.LenR, geo, 1, p.Frame);
            Body.DrawBody(g, p, geo);
            Body.DrawParts(g, skin, "corpo", geo);
            Body.DrawFace(g, p, geo);
            Body.DrawParts(g, skin, "frente", geo);
            if (p.Cap) Body.DrawCap(g, geo);
            else if (hat && !string.IsNullOrEmpty(c.Model) && anim != "spell") Body.DrawHat(g, c.Model, geo);
            if (p.Puff > 0 && !double.IsNaN(c.Ctx) && c.Ctx >= 85) g.Stamp(Art.Drop, geo.X1 - 1, geo.Top - 2);
            DrawFront(c, g, p, geo, t, tl);
            if (c.Agents > 0) DrawHelpers(g, (int)Math.Min(4, c.Agents), t);
        }

        // Ajudantes: um robozinho por subagente, saltitando atrás do Clawd.
        static void DrawHelpers(Pix g, int n, double t)
        {
            for (int i = 0; i < n; i++)
            {
                int x = -16 - i * 7, hop = F(t + i * 0.37, 5, 3) == 0 ? 1 : 0;
                g.Stamp(Art.Bot, x, -6 - hop);
            }
        }

        // Objetos atrás do corpo.
        void DrawBack(Creature c, Pix g, Pose p, Geo geo, double t, double tl)
        {
            switch (c.Anim)
            {
                case "juggle":
                {
                    char[] cols = { 'r', 'y', 'c' };
                    for (int i = 0; i < 3; i++)
                    {
                        double k = (t * 1.3 + i / 3.0) % 1;
                        int x = (int)Math.Round(-9 + 18 * k), y = geo.Top - 3 - (int)Math.Round(Math.Sin(k * Math.PI) * 9);
                        g.Rect(x, y, 2, 2, cols[i]);
                    }
                    break;
                }
            }
        }

        // Os objetos de cada animação, na frente do corpo (lado positivo de x).
        void DrawFront(Creature c, Pix g, Pose p, Geo geo, double t, double tl)
        {
            int f = F(t, 6, 6);
            switch (c.Anim)
            {
                case "bash": Laptop(g, t, 's', 'n', 'N'); break;
                case "ps": Laptop(g, t, 'U', 'i', 'c'); break;
                case "git":
                {
                    Laptop(g, t, 's', 'f', 'N');
                    int k = F(t, 2, 5);
                    g.Line(13, -15, 19, -15, 'G');
                    g.Line(15, -15, 17, -18, 'G');
                    g.Line(17, -18, 19, -18, 'G');
                    if (k >= 1) g.Rect(12, -16, 2, 2, 'f');
                    if (k >= 2) g.Rect(15, -16, 2, 2, 'f');
                    if (k >= 3) g.Rect(17, -19, 2, 2, 'n');
                    if (k >= 4) g.Rect(19, -16, 2, 2, 'f');
                    break;
                }
                case "peek":
                {
                    Laptop(g, t, 's', 'n', 'N');
                    int hx = geo.HandX, hy = geo.HandY;
                    g.Ring(hx + 3, hy - 1, 2, 'G');
                    g.Px(hx + 3, hy - 1, 'c'); g.Px(hx + 2, hy - 2, 'c');
                    g.Line(hx, hy + 1, hx + 1, hy, 'O');
                    break;
                }
                case "test":
                {
                    int hx = geo.HandX - 1, hy = geo.HandY - 2;
                    g.Rect(hx, hy - 5, 3, 1, 'k');
                    g.Rect(hx, hy - 4, 1, 4, 'k'); g.Rect(hx + 2, hy - 4, 1, 4, 'k');
                    g.Px(hx + 1, hy - 4, 'i'); g.Px(hx + 1, hy - 3, 'i');
                    g.Box(hx - 2, hy, 7, 5, 'n', 'k');
                    g.Rect(hx - 1, hy + 1, 5, 1, F(t, 4, 2) == 0 ? 'n' : 'N');
                    g.Px(hx, hy + 2, 'w');
                    if ((tl % 2.4) > 1.9) g.Stamp(Art.Check, hx + 5, hy - 9);
                    break;
                }
                case "install":
                {
                    double k = (tl % 1.8) / 1.8;
                    int y = k < 0.6 ? -26 + (int)(k / 0.6 * 20) : -6;
                    g.Box(11, y - 1, 9, 7, 'o', 'O');
                    g.Rect(15, y, 1, 5, 'y');
                    g.Rect(12, y + 1, 7, 1, 'O');
                    if (k < 0.6) { g.Rect(14, y - 6, 3, 3, 'n'); g.Rect(13, y - 3, 5, 1, 'n'); g.Px(15, y - 2, 'n'); }
                    else g.Stamp(Art.Check, 21, -10);
                    break;
                }
                case "build":
                {
                    // bigorna
                    g.Rect(11, -6, 11, 2, 'G'); g.Rect(10, -6, 1, 1, 'G'); g.Rect(13, -4, 7, 1, 'h'); g.Rect(14, -3, 5, 2, 'h');
                    g.Rect(12, -6, 9, 1, 'g');
                    int hx = geo.HandX, hy = geo.HandY;
                    bool up = p.ArmR == Arm.High;
                    if (up) { g.Rect(hx - 1, hy - 4, 1, 4, 'O'); g.Rect(hx - 3, hy - 6, 5, 3, 'G'); g.Rect(hx - 3, hy - 6, 5, 1, 'g'); }
                    else { g.Rect(hx, hy, 4, 1, 'O'); g.Rect(hx + 3, hy - 1, 3, 5, 'G'); g.Px(hx + 3, hy - 1, 'g'); g.Rect(16, -8, 1, 1, 'y'); }
                    if (!up) { g.Px(14, -9, 'y'); g.Px(19, -8, 'y'); g.Px(17, -10, 'w'); }
                    break;
                }
                case "trash":
                {
                    g.Rect(16, -9, 9, 1, 'G');
                    g.Rect(19, -10, 3, 1, 'G');
                    g.Rect(17, -8, 7, 7, 'g');
                    for (int i = 0; i < 3; i++) g.Rect(18 + i * 2, -7, 1, 5, 'G');
                    g.Rect(17, -1, 7, 1, 'G');
                    double k = (tl % 1.2) / 1.2;
                    if (k > 0.3 && k < 0.9)
                    {
                        double u = (k - 0.3) / 0.6;
                        int x = (int)Math.Round(geo.X1 + 3 + u * 10), y = geo.Top + 1 - (int)Math.Round(Math.Sin(u * Math.PI) * 7) + (int)(u * 4);
                        g.Rect(x, y, 2, 2, 'p'); g.Px(x + 1, y + 1, 'q');
                    }
                    else if (k <= 0.3) { g.Rect(geo.HandX - 1, geo.HandY - 2, 2, 2, 'p'); }
                    break;
                }
                case "stack":
                {
                    char[] cols = { 'u', 'n', 'f', 'r' };
                    char[] dark = { 'U', 'N', 'Y', 'R' };
                    int k = F(tl, 1.2, 5);
                    for (int i = 0; i < Math.Min(k, 4); i++)
                    {
                        int y = -4 - i * 4;
                        g.Rect(11, y, 10, 3, cols[i]);
                        g.Rect(11, y + 2, 10, 1, dark[i]);
                        for (int j = 0; j < 4; j++) g.Px(12 + j * 2, y + 1, dark[i]);
                    }
                    if (k < 4) { int y = geo.HandY - 3; g.Rect(geo.HandX - 2, y, 10, 3, cols[k]); g.Rect(geo.HandX - 2, y + 2, 10, 1, dark[k]); }
                    break;
                }
                case "net":
                {
                    g.Rect(17, -6, 1, 5, 'G'); g.Rect(15, -1, 5, 1, 'G');
                    g.Line(13, -10, 15, -6, 'g'); g.Line(15, -6, 20, -6, 'g'); g.Line(13, -10, 13, -10, 'g');
                    g.Rect(14, -9, 1, 3, 'g'); g.Rect(15, -7, 5, 1, 'i');
                    g.Px(17, -11, 'r');
                    int k = F(t, 3, 3);
                    for (int i = 0; i <= k; i++)
                    {
                        int r = 3 + i * 3;
                        g.Line(17 - r / 2, -12 - r, 17, -12 - r - 1, 'c');
                        g.Line(17, -12 - r - 1, 17 + r / 2, -12 - r, 'c');
                    }
                    break;
                }
                case "rocket":
                {
                    double k = (tl % 2.6) / 2.6;
                    int y = k < 0.35 ? 0 : -(int)Math.Round((k - 0.35) / 0.65 * 40);
                    int x = 16;
                    g.Px(x + 1, y - 13, 'r');
                    g.Rect(x, y - 12, 3, 1, 'r');
                    g.Rect(x, y - 11, 3, 7, 'w'); g.Rect(x + 2, y - 11, 1, 7, 'i');
                    g.Px(x + 1, y - 9, 'c');
                    g.Rect(x - 1, y - 5, 1, 3, 'r'); g.Rect(x + 3, y - 5, 1, 3, 'r');
                    g.Rect(x, y - 4, 3, 1, 'G');
                    int fl = F(t, 12, 2);
                    g.Rect(x, y - 3, 3, 1, 'y');
                    g.Px(x + 1, y - 2, 'f');
                    if (k >= 0.35 || fl == 0) { g.Px(x + 1, y - 1, fl == 0 ? 'r' : 'y'); g.Px(x, y - 2, 'f'); g.Px(x + 2, y - 2, 'f'); }
                    if (k >= 0.35) { g.Rect(x - 2, -2, 2, 2, 'm'); g.Rect(x + 3, -2, 2, 2, 'm'); }
                    break;
                }
                case "read": Book(g, geo, c.Detail, t); break;
                case "book": Book(g, geo, "doc", t); break;
                case "grep":
                {
                    for (int i = 0; i < 3; i++)
                    {
                        int y = -5 + i * 2;
                        g.Rect(11, y, 3 + (i * 5) % 7, 1, 'q');
                        g.Rect(16 + (i * 3) % 4, y, 4, 1, 'q');
                    }
                    int sx = 11 + F(t, 2.5, 3) * 3;
                    g.Rect(sx, -5 + F(t, 2.5, 3) % 3 * 2, 3, 1, 'y');
                    int hx = geo.HandX, hy = geo.HandY;
                    g.Line(hx, hy, hx + 1, hy + 1, 'O');
                    g.Ring(hx + 3, hy + 3, 2, 'G');
                    g.Px(hx + 3, hy + 3, 'c'); g.Px(hx + 2, hy + 2, 'c');
                    break;
                }
                case "glob":
                    g.Rect(12, -7, 4, 1, 'Y');
                    g.Rect(12, -6, 10, 6, 'y');
                    g.Rect(12, -6, 10, 1, 'Y');
                    g.Rect(12, -1, 10, 1, 'Y');
                    g.Rect(14 + F(t, 2, 2), -9, 3, 4, 'p');
                    break;
                case "edit":
                case "write":
                case "grid":
                case "paint":
                    Easel(g, geo, c.Anim, c.Detail, t, tl);
                    break;
                case "search":
                case "fetch":
                {
                    Globe(g, 15, -6, t);
                    if (c.Anim == "search")
                    {
                        int hx = geo.HandX, hy = geo.HandY;
                        g.Ring(hx + 2, hy - 1, 2, 'G'); g.Px(hx + 2, hy - 1, 'c'); g.Px(hx + 1, hy - 2, 'Z');
                        g.Line(hx - 1, hy + 1, hx, hy, 'O');
                    }
                    else
                    {
                        int k = F(t, 4, 4);
                        int y = -16 + k * 2;
                        g.Rect(14, y, 3, 2, 'n'); g.Rect(13, y + 2, 5, 1, 'n'); g.Px(15, y + 3, 'n');
                        if (k == 3) g.Rect(geo.HandX - 1, geo.HandY - 3, 3, 3, 'p');
                    }
                    break;
                }
                case "browser":
                {
                    int x = 11, y = geo.Top - 1;
                    g.Rect(x, y, 13, 11, 'k');
                    g.Rect(x + 1, y + 1, 11, 2, 'G');
                    g.Px(x + 2, y + 1, 'r'); g.Px(x + 4, y + 1, 'y'); g.Px(x + 6, y + 1, 'n');
                    g.Rect(x + 1, y + 3, 11, 7, 'w');
                    int load = F(t, 5, 12);
                    g.Rect(x + 1, y + 3, Math.Min(11, load), 1, 'c');
                    g.Rect(x + 2, y + 5, 6, 1, 'q'); g.Rect(x + 2, y + 7, 8, 1, 'q'); g.Rect(x + 9, y + 5, 2, 3, 'u');
                    break;
                }
                case "camera":
                {
                    int x = geo.X1 + 2, y = geo.Top - 6;
                    g.Rect(x + 2, y - 1, 3, 1, 'h');
                    g.Box(x, y, 9, 6, 'h', 'k');
                    g.Disc(x + 4, y + 3, 1, 'c');
                    g.Px(x + 7, y + 1, 'r');
                    if ((tl % 1.43) < 0.15) { g.Stamp(Art.Spark, x + 8, y - 5); g.Rect(x + 6, y - 1, 2, 1, 'w'); }
                    break;
                }
                case "mouse":
                {
                    double k = (tl % 2.0) / 2.0;
                    int x = 14 + (int)Math.Round(Math.Sin(k * Math.PI * 2) * 4), y = -14 + (int)Math.Round(Math.Cos(k * Math.PI * 2) * 3);
                    g.Stamp(Art.Cursor, x, y);
                    if (k > 0.45 && k < 0.6) { g.Ring(x, y, 3, 'y'); }
                    break;
                }
                case "agent":
                {
                    int hx = geo.HandX, hy = geo.HandY;
                    g.Rect(hx - 1, hy - 4, 1, 3, 'G');
                    g.Box(hx - 1, hy - 1, 4, 6, 'h', 'k');
                    g.Rect(hx, hy, 2, 1, 'g');
                    if (F(t, 4, 2) == 0) g.Px(hx, hy + 3, 'r');
                    int k = F(t, 4, 3);
                    for (int i = 0; i <= k; i++) { g.Px(hx + 1 + i * 2, hy - 5 - i, 'c'); g.Px(hx + 2 + i * 2, hy - 4 - i, 'c'); }
                    break;
                }
                case "conduct":
                {
                    int hx = geo.HandX, hy = geo.HandY;
                    g.Line(hx, hy, hx + 4, hy - 3 + (p.ArmR == Arm.High ? -2 : 2), 'w');
                    break;
                }
                case "plan": Clipboard(g, geo, t, true); break;
                case "askplan":
                    Clipboard(g, geo, t, false);
                    g.Stamp(Art.Question, geo.HandX + 1, geo.Top - 12);
                    break;
                case "spell":
                {
                    int x = geo.HandX - 3, y = geo.HandY - 3;
                    g.Rect(x, y, 11, 5, 'V');
                    g.Rect(x + 1, y, 4, 4, 'p'); g.Rect(x + 6, y, 4, 4, 'p');
                    g.Rect(x + 5, y, 1, 5, 'v');
                    g.Px(x + 2, y + 1, 'y'); g.Px(x + 8, y + 2, 'P');
                    break;
                }
                case "toolbox":
                {
                    bool open = F(tl, 1, 2) == 1;
                    g.Rect(12, -5, 11, 5, 'r'); g.Rect(12, -1, 11, 1, 'R');
                    g.Rect(16, -7, 3, 1, 'h');
                    if (open)
                    {
                        g.Rect(11, -9, 6, 1, 'R');
                        g.Rect(14, -9, 1, 3, 'g'); g.Rect(13, -10, 3, 1, 'g');
                        g.Rect(19, -10, 1, 4, 'O'); g.Rect(19, -12, 1, 2, 'g');
                    }
                    else g.Rect(12, -6, 11, 1, 'R');
                    break;
                }
                case "plug":
                {
                    g.Box(19, -8, 5, 7, 'i', 'G');
                    g.Px(21, -6, 'h'); g.Px(21, -4, 'h');
                    int hx = geo.HandX, hy = geo.HandY;
                    bool on = F(tl, 1, 2) == 1;
                    int px = on ? 17 : 14;
                    g.Line(hx, hy, px - 1, -5, 'G');
                    g.Rect(px - 1, -6, 2, 3, 'g');
                    g.Rect(px + 1, -6, 1, 1, 'y'); g.Rect(px + 1, -4, 1, 1, 'y');
                    if (on) g.Stamp(Art.Plus, 20, -12);
                    break;
                }
                case "tool":
                {
                    Gear(g, 16, -6, F(t, 6, 2));
                    int hx = geo.HandX, hy = geo.HandY;
                    g.Rect(hx, hy, 3, 1, 'g'); g.Rect(hx + 2, hy - 1, 1, 3, 'g'); g.Px(hx + 3, hy - 1, 'g'); g.Px(hx + 3, hy + 1, 'g');
                    break;
                }
                case "think":
                case "thinkhard":
                {
                    int x = geo.X1 - 1, y = geo.Top - 13;
                    g.Px(geo.X1 + 1, geo.Top - 2, 'q');
                    g.Rect(geo.X1 + 2, geo.Top - 5, 2, 2, 'q');
                    g.Stamp(Art.Thought, x, y);
                    double ph = tl % 5;
                    if (c.Anim == "think" && ph > 3.6) g.Stamp(Art.Bulb, x + 5, y);
                    else
                    {
                        int n = F(t, c.Anim == "thinkhard" ? 5 : 2.5, 4);
                        for (int i = 0; i < n; i++) g.Rect(x + 3 + i * 3, y + 3, 2, 2, c.Anim == "thinkhard" ? 'r' : 'G');
                    }
                    break;
                }
                case "ask":
                {
                    int hx = geo.HandX, hy = geo.HandY;
                    g.Rect(hx, hy - 4, 1, 6, 'O');
                    g.Box(hx - 4, hy - 13, 9, 10, 'p', 'O');
                    g.Stamp(Art.Question, hx - 2, hy - 12);
                    break;
                }
                case "askperm":
                    if (F(t, 2, 2) == 0) g.Stamp(Art.Bang, geo.X0 + 1, geo.Top - 9);
                    // cadeado
                    g.Ring(geo.X1 + 6, geo.Top - 4, 2, 'G');
                    g.Box(geo.X1 + 3, geo.Top - 3, 7, 5, 'y', 'Y');
                    g.Px(geo.X1 + 6, geo.Top - 1, 'k');
                    break;
                case "compact":
                {
                    int w = 13 - p.LenR;
                    int x = geo.X1 + 2 + p.LenR;
                    for (int i = 0; i < 5; i++)
                    {
                        int y = geo.Top + 1 + i * 2;
                        g.Rect(x, y, Math.Max(2, w), 1, 'p');
                        g.Rect(x, y + 1, Math.Max(2, w), 1, 'q');
                    }
                    break;
                }
                case "done":
                    if (c.Trophy && tl > 0.9) g.Stamp(Art.Trophy, geo.HandX - 3, geo.HandY - 8);
                    break;
                case "error":
                {
                    int x = -7, y = geo.Top - 12;
                    g.Stamp(Art.Rain, x, y);
                    for (int i = 0; i < 4; i++)
                    {
                        int ry = y + 6 + ((F(t, 10, 6) + i * 2) % 6);
                        g.Px(x + 2 + i * 3, ry, 'c');
                    }
                    g.Stamp(Art.Drop, geo.X1, geo.Top);
                    break;
                }
                case "halt": g.Stamp(Art.Stop, -5, geo.Top - 11); break;
                case "refuse": if (F(t, 2, 2) == 0) g.Stamp(Art.Cross, -4, geo.Top - 8); break;
                case "dizzy":
                    for (int i = 0; i < 3; i++)
                    {
                        double a = t * 4 + i * 2.094;
                        g.Stamp(Art.Plus, (int)Math.Round(Math.Cos(a) * 8) - 1, geo.Top - 4 + (int)Math.Round(Math.Sin(a) * 2));
                    }
                    break;
                case "surprise":
                    g.Stamp(Art.Bang, geo.X1 + 1, geo.Top - 9);
                    break;
                case "lowbat":
                    if (F(t, 1.5, 2) == 0) g.Stamp(Art.Battery, -4, geo.Top - 7);
                    break;
                case "give": g.Stamp(Art.Cookie, geo.HandX - 1, geo.HandY - 2); break;
                case "eat":
                {
                    int bites = (int)(tl / 0.8);
                    string[] ck = bites == 0 ? Art.Cookie : bites == 1 ? Art.Cookie2 : bites == 2 ? Art.Cookie3 : null;
                    if (ck != null) g.Stamp(ck, geo.HandX - 2, geo.HandY - 4);
                    break;
                }
            }
        }

        // Notebook no chão, com a tela rolando.
        static void Laptop(Pix g, double t, char screen, char line, char line2)
        {
            g.Rect(12, -11, 10, 8, 'h');
            g.Rect(13, -10, 8, 6, screen);
            int fr = F(t, 6, 6);
            for (int k = 0; k < 3; k++)
            {
                int row = -10 + ((k * 2 + fr) % 6);
                int len = 2 + ((k * 3 + (int)(t * 1.3)) % 5);
                g.Rect(14, row, Math.Min(6, len), 1, k == 0 ? line : line2);
            }
            if (F(t, 3, 2) == 0) g.Px(19, -5, line);
            g.Rect(10, -3, 13, 1, 'g');
            g.Rect(10, -2, 13, 1, 'G');
            for (int i = 0; i < 5; i++) g.Px(11 + i * 2 + (F(t, 9, 2)), -3, 'i');
        }

        static void Book(Pix g, Geo geo, string detail, double t)
        {
            int x = geo.HandX - 4, y = geo.HandY - 4;
            char cover = detail == "codigo" ? 'u' : detail == "dados" ? 'N' : detail == "pdf" ? 'r' : detail == "imagem" ? 'O' : 'a';
            if (detail == "imagem")
            {
                g.Rect(x, y, 10, 8, 'O');
                g.Rect(x + 1, y + 1, 8, 6, 'c');
                g.Rect(x + 6, y + 2, 2, 2, 'y');
                g.Line(x + 1, y + 6, x + 4, y + 3, 'N'); g.Line(x + 4, y + 3, x + 7, y + 6, 'N');
                g.Rect(x + 2, y + 5, 5, 2, 'n');
                return;
            }
            g.Rect(x - 1, y, 13, 7, cover);
            g.Rect(x, y, 5, 6, 'p');
            g.Rect(x + 6, y, 5, 6, 'p');
            g.Rect(x + 5, y, 1, 7, 'k');
            char ink = detail == "codigo" ? 'n' : detail == "dados" ? 'N' : detail == "pdf" ? 'r' : 'q';
            for (int i = 0; i < 3; i++)
            {
                g.Rect(x + 1, y + 1 + i * 2, 1 + (i * 2) % 4, 1, ink);
                g.Rect(x + 7, y + 1 + i * 2, 3 - i % 2, 1, detail == "codigo" ? 'u' : ink);
            }
            if (detail == "dados") { g.Rect(x + 3, y + 1, 1, 5, 'q'); g.Rect(x + 8, y + 1, 1, 5, 'q'); }
            if (F(t, 0.7, 3) == 0) g.Rect(x + 6, y - 1, 3, 6, 'i');
        }

        static void Easel(Pix g, Geo geo, string anim, string detail, double t, double tl)
        {
            // cavalete com a folha, e o lápis (ou pincel) na mão
            g.Line(13, -1, 15, -12, 'O');
            g.Line(23, -1, 21, -12, 'O');
            int x = 13, y = -14, w = 10, h = 10;
            g.Rect(x - 1, y - 1, w + 2, h + 2, 'q');
            g.Rect(x, y, w, h, anim == "grid" ? 'i' : 'p');
            double k = (tl % 4.8) / 1.2;
            int line = (int)k;
            int prog = (int)((k - line) * 7);
            char[] code = { 'n', 'u', 'f', 'v' };
            if (anim == "paint")
            {
                char[] paint = { 'r', 'y', 'c', 'n', 'P', 'u' };
                int dabs = (int)(tl * 3) % 12;
                for (int i = 0; i < dabs; i++) g.Rect(x + 1 + (i * 3) % 8, y + 1 + (i * 5) % 8, 2, 2, paint[i % paint.Length]);
            }
            else if (anim == "grid")
            {
                for (int i = 0; i < 3; i++) g.Rect(x, y + 3 + i * 3, w, 1, 'q');
                g.Rect(x + 4, y, 1, h, 'q');
                int filled = (int)(tl * 2) % 7;
                for (int i = 0; i < filled; i++) g.Rect(x + (i % 2 == 0 ? 1 : 6), y + 1 + (i / 2) * 3, 2, 1, 'u');
            }
            else
            {
                for (int i = 0; i < 4; i++)
                {
                    int len = i < line ? 7 - (i * 2) % 3 : i == line ? prog : 0;
                    char ink = detail == "codigo" ? code[i] : detail == "dados" ? 'N' : 'G';
                    int indent = detail == "codigo" && i % 2 == 1 ? 2 : 0;
                    if (len > 0) g.Rect(x + 1 + indent, y + 1 + i * 2, Math.Min(len, 8 - indent), 1, ink);
                }
                if (anim == "write")
                {
                    int sheets = (int)(tl / 1.5) % 4;
                    for (int i = 0; i < sheets; i++) { g.Rect(26, -2 - i * 2, 6, 1, 'p'); g.Rect(26, -1 - i * 2, 6, 1, 'q'); }
                }
            }
            // lápis/pincel saindo da mão até a folha
            int hx = geo.HandX, hy = geo.HandY;
            int tx = x + 1 + Math.Min(7, prog), ty = y + 1 + Math.Min(3, line) * 2;
            if (anim == "paint") { g.Line(hx, hy, tx, ty, 'o'); g.Rect(tx, ty, 2, 2, 'r'); }
            else { g.Line(hx, hy, tx, ty, 'y'); g.Px(tx, ty, 'x'); g.Px(hx, hy, 'P'); }
        }

        static void Globe(Pix g, int cx, int cy, double t)
        {
            g.Disc(cx, cy, 4, 'u');
            int sh = F(t, 4, 9);
            for (int j = -3; j <= 3; j++)
                for (int i = -4; i <= 4; i++)
                {
                    if (i * i + j * j > 16) continue;
                    int u = ((i + sh + 9) * 7 + j * 3) % 9;
                    if (u < 3) g.Px(cx + i, cy + j, 'n');
                }
            g.Ring(cx, cy, 5, 'U');
            g.Px(cx - 2, cy - 3, 'c');
            g.Rect(cx - 1, cy + 6, 3, 1, 'G');
        }

        static void Gear(Pix g, int cx, int cy, int frame)
        {
            g.Disc(cx, cy, 3, 'g');
            if (frame == 0)
            {
                g.Rect(cx - 1, cy - 5, 3, 2, 'g'); g.Rect(cx - 1, cy + 4, 3, 2, 'g');
                g.Rect(cx - 5, cy - 1, 2, 3, 'g'); g.Rect(cx + 4, cy - 1, 2, 3, 'g');
            }
            else
            {
                g.Rect(cx - 4, cy - 4, 2, 2, 'g'); g.Rect(cx + 3, cy - 4, 2, 2, 'g');
                g.Rect(cx - 4, cy + 3, 2, 2, 'g'); g.Rect(cx + 3, cy + 3, 2, 2, 'g');
            }
            g.Rect(cx - 1, cy - 1, 3, 3, 'G');
            g.Px(cx, cy, 'h');
        }

        static void Clipboard(Pix g, Geo geo, double t, bool ticking)
        {
            int x = geo.HandX - 3, y = geo.HandY - 8;
            g.Rect(x, y, 9, 11, 'o');
            g.Rect(x + 1, y + 2, 7, 8, 'p');
            g.Rect(x + 3, y - 1, 3, 2, 'G');
            int done = ticking ? F(t, 1.25, 4) : 1;
            for (int i = 0; i < 3; i++)
            {
                int yy = y + 3 + i * 2;
                g.Px(x + 2, yy, i < done ? 'n' : 'q');
                g.Rect(x + 4, yy, 3, 1, 'q');
            }
        }

        // -------------------------------------------------------------- demo --

        static readonly string[][] Cases =
        {
            new[] { "pensando", "", "" }, new[] { "trabalhando", "Bash", "comando" }, new[] { "trabalhando", "PowerShell", "comando" },
            new[] { "trabalhando", "Bash", "git" }, new[] { "trabalhando", "Bash", "teste" }, new[] { "trabalhando", "Bash", "instalar" },
            new[] { "trabalhando", "Bash", "compilar" }, new[] { "trabalhando", "Bash", "apagar" }, new[] { "trabalhando", "Bash", "docker" },
            new[] { "trabalhando", "Bash", "rede" }, new[] { "trabalhando", "Bash", "olhar" }, new[] { "trabalhando", "Bash", "rodar" },
            new[] { "trabalhando", "Read", "codigo" }, new[] { "trabalhando", "Read", "imagem" }, new[] { "trabalhando", "Read", "dados" },
            new[] { "trabalhando", "Grep", "" }, new[] { "trabalhando", "Glob", "" }, new[] { "trabalhando", "Edit", "codigo" },
            new[] { "trabalhando", "Edit", "imagem" }, new[] { "trabalhando", "Write", "doc" }, new[] { "trabalhando", "NotebookEdit", "notebook" },
            new[] { "trabalhando", "WebSearch", "" }, new[] { "trabalhando", "WebFetch", "" }, new[] { "trabalhando", "mcp__Claude_Browser__navigate", "" },
            new[] { "trabalhando", "mcp__computer-use__screenshot", "" }, new[] { "trabalhando", "mcp__computer-use__left_click", "" },
            new[] { "trabalhando", "Agent", "" }, new[] { "trabalhando", "Workflow", "" }, new[] { "trabalhando", "TodoWrite", "" },
            new[] { "trabalhando", "Skill", "" }, new[] { "trabalhando", "ToolSearch", "" }, new[] { "trabalhando", "mcp__github__create_issue", "" },
            new[] { "trabalhando", "Outra", "" }, new[] { "aguardando", "AskUserQuestion", "" }, new[] { "aguardando", "ExitPlanMode", "" },
            new[] { "aguardando", "Bash", "" }, new[] { "compactando", "", "" }, new[] { "feito", "", "" }, new[] { "erro", "", "" },
            new[] { "interrompido", "", "" }, new[] { "recusa", "", "" }, new[] { "ocioso", "", "" }, new[] { "dormindo", "", "" },
        };

        void DemoSessions(double t, double now, Dictionary<string, List<SessionView>> by)
        {
            if (accounts.Count == 0) return;
            int k = (int)(t / 3.5);
            string k0 = accounts[0].Key, k1 = accounts[accounts.Count > 1 ? 1 : 0].Key;
            if (shot != null)
            {
                // fotos do README: as sessões da cena
                foreach (var s in shot)
                {
                    s.UpdatedAt = now;
                    s.TurnStartedAt = now - 25000;
                    if (s.EndedAt > 0) s.EndedAt = now;
                    by[s.Account].Add(s);
                }
            }
            else
            {
                // --eventos: todo mundo livre, para os encontros acontecerem um atrás do outro
                string[] idle = { "ocioso", "", "" };
                by[k0].Add(DemoS("u1", k0, "Corrigir o login", festa ? idle : Cases[k % Cases.Length], now, 3, "claude-fable-5-2", 40));
                by[k0].Add(DemoS("u2", k0, "Revisar o relatório", festa ? idle : Cases[(k + 11) % Cases.Length], now, 2, "claude-opus-5-5", 90));
                by[k1].Add(DemoS("p1", k1, "Site novo", festa ? idle : Cases[(k + 21) % Cases.Length], now, 3, "claude-sonnet-5-5", 30));
                if (!festa && (int)(t / 30) % 2 == 0) by[k1].Add(DemoS("p2", k1, "Lista de compras", Cases[(k + 32) % Cases.Length], now, 1, "claude-haiku-5-5", 12));
            }
            foreach (var a in accounts)
            {
                var u = new Usage();
                u.UpdatedAt = now - 120000;
                u.Limits.Add(new Limit { Kind = "five_hour", Pct = a == accounts[0] ? 57 : 93, Resets = DateTime.Now.AddMinutes(134) });
                u.Limits.Add(new Limit { Kind = "seven_day", Pct = a == accounts[0] ? 64 : 41, Resets = DateTime.Now.AddDays(3).AddHours(5) });
                if (a == accounts[0]) u.Limits.Add(new Limit { Kind = "seven_day_fable", Pct = 38, Resets = DateTime.Now.AddDays(3).AddHours(5) });
                a.Usage = u;
                var m = new Dictionary<string, object>();
                m["tasksDone"] = a == accounts[0] ? 37 : 12;
                m["streak"] = a == accounts[0] ? 4 : 0;
                a.Memory = m;
            }
        }

        // ---------------------------------------------------- fotos do README --

        List<SessionView> shot;
        double fakeT = -1;
        int frames;

        static SessionView Fake(string acc, string id, int age, string state, string tool, string detail, string model, string label, int agents)
        {
            var s = new SessionView();
            s.Id = id; s.Account = acc; s.Label = label; s.State = state; s.Tool = tool; s.Detail = detail; s.Model = model;
            s.StartedAt = Data.NowMs() - age * 3600000.0;
            s.Turns = age * 6; s.Cost = age * 0.45; s.Ctx = 35; s.Agents = agents;
            if (state == "encerrada") s.EndedAt = 1;
            return s;
        }

        void Scene(params SessionView[] sessions)
        {
            shot = sessions.ToList();
            ReadData(fakeT);
        }

        // Anda o relógio de mentira, quadro a quadro.
        void Run(double secs)
        {
            for (double end = fakeT + secs; fakeT < end; fakeT += 1.0 / 30)
            {
                if (frames++ % 45 == 0) ReadData(fakeT);
                Frame();
            }
        }

        bool Until(Func<bool> ok, double max)
        {
            for (double end = fakeT + max; fakeT < end && !ok();) Run(0.1);
            return ok();
        }

        // A faixa dos Clawds sobre um fundo e uma barra de tarefas desenhados.
        void Snap(string file, Account withPanel)
        {
            const double S = 2, Sky = 150, Bar = 34;
            double extra = withPanel != null ? 120 : 0, w = W, h = extra + Sky + Bar;
            var dv = new DrawingVisual();
            RenderOptions.SetEdgeMode(dv, EdgeMode.Aliased);
            using (var dc = dv.RenderOpen())
            {
                dc.DrawRectangle(new LinearGradientBrush(Pal.C("#1c2a4e"), Pal.C("#2c6a82"), 90), null, new Rect(0, 0, w, h));
                dc.DrawRectangle(Pal.B("#14151b", 1), null, new Rect(0, h - Bar, w, Bar));
                for (int i = 0; i < 5; i++)
                    dc.DrawRectangle(Pal.B("#ffffff", i == 0 ? 0.55 : 0.2), null, new Rect(w / 2 - 72 + i * 30, h - Bar + 9, 16, 16));
                dc.PushTransform(new TranslateTransform(0, h - Bar - H));
                Render(dc);
                if (withPanel != null && withPanel.Main != null)
                {
                    panel.Build(withPanel, withPanel.Main.SessionId, Data.NowMs());
                    ImageSource img = panel.Snapshot(S);
                    double pw = panel.Width, ph = img.Height;
                    dc.DrawImage(img, new Rect(Math.Round(Clamp(withPanel.Main.X - pw / 2, 6, w - pw - 6)), Math.Round(withPanel.Main.TopY - 8 - ph), pw, ph));
                }
                dc.Pop();
            }
            var bmp = new RenderTargetBitmap((int)(w * S), (int)(h * S), 96 * S, 96 * S, PixelFormats.Pbgra32);
            bmp.Render(dv);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(bmp));
            using (var fs = File.Create(file)) enc.Save(fs);
        }

        // Para as fotos da Home: põe sessões de mentira e anda o relógio.
        public void PhotoSessions(params SessionView[] sessions)
        {
            if (fakeT < 0) fakeT = 0;
            if (scene != null) EndScene(fakeT, true);
            festa = false;
            nextEvent = fakeT + 999;
            Scene(sessions);
        }

        public void PhotoAdvance(double secs)
        {
            if (fakeT < 0) fakeT = 0;
            Run(secs);
        }

        public static SessionView PhotoSession(string acc, string id, int age, string state, string tool, string detail, string model, string label, int agents)
        {
            var s = Fake(acc, id, age, state, tool, detail, model, label, agents);
            s.UpdatedAt = Data.NowMs();
            s.TurnStartedAt = Data.NowMs() - 95000;
            return s;
        }

        // As cenas do README, com duas contas de mentira: cada uma num contexto.
        public void Photos(string dir)
        {
            Directory.CreateDirectory(dir);
            rng = new Random(11);
            fakeT = 0;
            string a = accounts[0].Key, b = accounts[1].Key;
            Func<string, string> at = n => System.IO.Path.Combine(dir, "clawds-" + n + ".png");

            Scene(Fake(a, "a1", 3, "ocioso", "", "", "claude-opus-5-5", "Corrigir o login", 0), Fake(b, "b1", 2, "ocioso", "", "", "claude-sonnet-5-5", "Site novo", 0));
            Run(3.4);
            Scene(Fake(a, "a1", 3, "trabalhando", "Bash", "git", "claude-opus-5-5", "Corrigir o login", 0), Fake(b, "b1", 2, "trabalhando", "Edit", "codigo", "claude-sonnet-5-5", "Site novo", 0));
            Run(1.3);
            Snap(at("trabalhando"), null);
            Scene(Fake(a, "a1", 3, "trabalhando", "Bash", "teste", "claude-opus-5-5", "Corrigir o login", 0), Fake(b, "b1", 2, "trabalhando", "WebSearch", "", "claude-sonnet-5-5", "Site novo", 0));
            Run(2.2);
            Snap(at("testes-web"), null);
            Scene(Fake(a, "a1", 3, "aguardando", "AskUserQuestion", "", "claude-opus-5-5", "Corrigir o login", 0), Fake(b, "b1", 2, "pensando", "", "", "claude-sonnet-5-5", "Site novo", 0));
            Run(1.2);
            Snap(at("esperando"), null);
            Scene(Fake(a, "a1", 3, "feito", "", "", "claude-opus-5-5", "Corrigir o login", 0), Fake(b, "b1", 2, "erro", "", "", "claude-sonnet-5-5", "Site novo", 0));
            Run(0.75);
            Snap(at("feito-erro"), null);
            Scene(Fake(a, "a1", 3, "trabalhando", "Agent", "", "claude-fable-5-2", "Corrigir o login", 2), Fake(a, "a2", 1, "trabalhando", "Read", "doc", "claude-haiku-5-5", "Revisar o relatório", 0),
                  Fake(b, "b1", 2, "trabalhando", "Bash", "compilar", "claude-sonnet-5-5", "Site novo", 0), Fake(b, "b2", 1, "pensando", "", "", "claude-haiku-5-5", "Lista de compras", 0));
            Run(3.4);
            Snap(at("filhotes"), null);
            Snap(at("painel"), accounts[0]);
            Scene(Fake(b, "b1", 2, "encerrada", "", "", "", "Site novo", 0));
            Run(6);
            Snap(at("descansando"), null);
            Scene(Fake(a, "a1", 3, "ocioso", "", "", "claude-opus-5-5", "Corrigir o login", 0), Fake(b, "b1", 2, "ocioso", "", "", "claude-sonnet-5-5", "Site novo", 0));
            Run(4);
            festa = true;
            festaIdx = Array.IndexOf(FestaKinds, "ball");
            nextEvent = fakeT;
            if (Until(() => scene != null && scene.Kind == "ball" && scene.Phase == 1 && scene.Count >= 2, 30)) { Run(0.45); Snap(at("bola"), null); }
            festaIdx = Array.IndexOf(FestaKinds, "ride");
            Until(() => scene == null, 30);
            if (Until(() => scene != null && scene.Kind == "ride" && scene.Phase == 2, 30)) { Run(1.6); Snap(at("cavalinho"), null); }
        }

        static SessionView DemoS(string id, string acc, string label, string[] st, double now, int age, string model, double ctx)
        {
            var s = new SessionView();
            s.Id = id; s.Account = acc; s.Label = label; s.State = st[0]; s.Tool = st[1]; s.Detail = st[2]; s.Model = model;
            s.StartedAt = now - age * 3600000.0; s.UpdatedAt = now; s.TurnStartedAt = now - (age == 3 ? 95000 : 20000);
            s.Turns = age * 7; s.Cost = age * 0.83; s.Ctx = ctx;
            s.Agents = st[1] == "Agent" ? 2 : st[1] == "Workflow" ? 4 : 0;
            return s;
        }
    }

    // ----------------------------------------------------- painel do mouse --

    // Painel curto: as sessões numa linha cada, os limites em barras e o custo do dia.
    sealed class InfoPanel : Window
    {
        readonly StackPanel stack = new StackPanel();
        readonly Border card = new Border();
        public Account Acc;
        public string FocusId;
        double anchorBottom;

        static readonly Brush Text = Pal.B("#f2f0f5", 1);
        static readonly Brush Dim = Pal.B("#a8a3b3", 1);
        static readonly Brush Track = Pal.B("#ffffff", 0.12);
        static readonly Brush RowHi = Pal.B("#ffffff", 0.08);
        static readonly FontFamily Mono = new FontFamily("Consolas");

        public InfoPanel()
        {
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            ShowActivated = false;
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.Height;
            Width = 270;
            Focusable = false;
            UseLayoutRounding = true;
            card.Background = Pal.B("#16141b", 0.97);
            card.BorderThickness = new Thickness(2);
            card.Padding = new Thickness(12, 9, 12, 10);
            card.Child = stack;
            Content = card;
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
            SourceInitialized += delegate { Native.MakeOverlay(this); };
            SizeChanged += delegate { Top = anchorBottom - ActualHeight; };
        }

        public void Open(Account a, string focus, double nowMs, double anchorX, double anchorBottomY)
        {
            Build(a, focus, nowMs);
            Rect wa = SystemParameters.WorkArea;
            Left = Math.Round(Math.Max(wa.Left + 2, Math.Min(wa.Right - Width - 2, anchorX - Width / 2)));
            anchorBottom = Math.Round(anchorBottomY);
            if (!IsVisible) Show();
            Top = anchorBottom - ActualHeight;
            Native.KeepOnTop(this);
        }

        // O painel num bitmap, para as fotos do README.
        public RenderTargetBitmap Snapshot(double scale)
        {
            card.Measure(new Size(Width, double.PositiveInfinity));
            card.Arrange(new Rect(0, 0, Width, card.DesiredSize.Height));
            card.UpdateLayout();
            var bmp = new RenderTargetBitmap((int)(Width * scale), (int)Math.Ceiling(card.DesiredSize.Height * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
            bmp.Render(card);
            return bmp;
        }

        public void Close2()
        {
            if (!IsVisible) return;
            Hide();
            Acc = null;
        }

        static TextBlock Tx(string s, double size, Brush fg, bool bold)
        {
            var t = new TextBlock();
            t.Text = s;
            t.FontFamily = Mono;
            t.FontSize = size;
            t.Foreground = fg;
            t.FontWeight = bold ? FontWeights.Bold : FontWeights.Normal;
            t.TextTrimming = TextTrimming.CharacterEllipsis;
            t.VerticalAlignment = VerticalAlignment.Center;
            return t;
        }

        static Brush StateColor(SessionView s, double now)
        {
            if (!s.IsActive(now)) return Pal.B("#5b5666", 1);
            switch (s.State)
            {
                case "trabalhando": return Pal.B("#fcb421", 1);
                case "pensando": case "compactando": return Pal.B("#5cc8ff", 1);
                case "aguardando": return Pal.B("#ff7eb6", 1);
                case "feito": return Pal.B("#5ee08a", 1);
                case "erro": case "recusa": case "interrompido": return Pal.B("#e5484d", 1);
                default: return Pal.B("#a8a3b3", 1);
            }
        }

        static Brush BarColor(double pct)
        {
            return pct >= 85 ? Pal.B("#e5484d", 1) : pct >= 60 ? Pal.B("#f5a524", 1) : Pal.B("#5ee08a", 1);
        }

        public void Build(Account a, string focus, double now)
        {
            Acc = a;
            FocusId = focus;
            card.BorderBrush = a.Ui;
            stack.Children.Clear();

            var head = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            var dot = new Rectangle { Width = 8, Height = 8, Fill = a.Ui, Margin = new Thickness(0, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center };
            head.Children.Add(dot);
            int active = a.Active.Count;
            var count = Tx(active == 0 ? "sem sessões" : active == 1 ? "1 ativa" : active + " ativas", 11, Dim, false);
            DockPanel.SetDock(count, Dock.Right);
            head.Children.Add(count);
            head.Children.Add(Tx("Claude " + a.Name, 13, Text, true));
            stack.Children.Add(head);

            // sessões: uma linha cada
            foreach (var s in a.Sessions.Take(5))
            {
                var row = new Grid { Margin = new Thickness(-4, 0, -4, 1) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                if (s.Id == focus)
                {
                    var bg = new Border { Background = RowHi };
                    Grid.SetColumnSpan(bg, 3);
                    row.Children.Add(bg);
                }
                var d = new Rectangle { Width = 6, Height = 6, Fill = StateColor(s, now), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                row.Children.Add(d);
                var label = Tx(s.Label, 11.5, s.IsActive(now) ? Text : Dim, false);
                label.Margin = new Thickness(0, 3, 8, 3);
                Grid.SetColumn(label, 1);
                row.Children.Add(label);
                var st = Tx(Words.Short(s, now), 11, Dim, false);
                st.Margin = new Thickness(0, 0, 4, 0);
                st.MaxWidth = 130;
                Grid.SetColumn(st, 2);
                row.Children.Add(st);
                stack.Children.Add(row);
            }
            if (a.Sessions.Count == 0) stack.Children.Add(Tx("nenhuma sessão hoje", 11, Dim, false));

            // limites: nome, barra, porcentagem e quando reinicia, numa linha
            var limits = a.Usage != null ? a.Usage.Limits : new List<Limit>();
            if (limits.Count > 0)
            {
                var grid = new Grid { Margin = new Thickness(0, 8, 0, 0) };
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(62) });
                for (int i = 0; i < limits.Count; i++)
                {
                    var l = limits[i];
                    grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(18) });
                    var name = Tx(Words.LimitName(l.Kind), 11, Text, false);
                    Grid.SetRow(name, i);
                    grid.Children.Add(name);
                    var bar = Bar(l.Pct, BarColor(l.Pct));
                    Grid.SetRow(bar, i); Grid.SetColumn(bar, 1);
                    grid.Children.Add(bar);
                    var pct = Tx((int)Math.Round(l.Pct) + "%", 11, Text, true);
                    pct.HorizontalAlignment = HorizontalAlignment.Right;
                    Grid.SetRow(pct, i); Grid.SetColumn(pct, 2);
                    grid.Children.Add(pct);
                    var rs = Tx(Words.Resets(l.Resets), 10.5, Dim, false);
                    rs.HorizontalAlignment = HorizontalAlignment.Right;
                    Grid.SetRow(rs, i); Grid.SetColumn(rs, 3);
                    grid.Children.Add(rs);
                }
                stack.Children.Add(grid);
            }
            else stack.Children.Add(Margined(Tx("limites: aparecem após a 1ª resposta", 10.5, Dim, false), 8));

            DateTime today = DateTime.Today;
            double todayMs = (today.ToUniversalTime() - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds;
            var costed = a.Sessions.Where(s => !double.IsNaN(s.Cost) && s.UpdatedAt >= todayMs).ToList();
            if (costed.Count > 0) stack.Children.Add(Margined(Tx("custo hoje  " + Words.Money(costed.Sum(s => s.Cost)), 11, Dim, false), 6));
        }

        static FrameworkElement Margined(FrameworkElement e, double top)
        {
            e.Margin = new Thickness(0, top, 0, 0);
            return e;
        }

        static FrameworkElement Bar(double pct, Brush fill)
        {
            pct = Math.Max(0, Math.Min(100, pct));
            var g = new Grid { Height = 6, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.001, pct), GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.001, 100 - pct), GridUnitType.Star) });
            var track = new Border { Background = Track };
            Grid.SetColumnSpan(track, 2);
            g.Children.Add(track);
            if (pct > 0.5) g.Children.Add(new Border { Background = fill });
            return g;
        }
    }
}
