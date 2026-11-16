// O escritório da Home: a janela inteira vista de cima, em pixel art. Cada conta é um setor
// (uma sala com o carpete na cor dela e uma placa com o nome, os limites e o custo do dia),
// cada sessão é um Clawd com a sua mesa, e os subagentes são robozinhos em volta da mesa.
//
//   trabalhando  sentado, digitando; a tela mostra o tipo de trabalho (terminal, código, web...)
//   pensando     sentado, com uma nuvem de pensamento
//   esperando    em pé ao lado da mesa, acenando, com um balão de pergunta
//   terminou     comemora na mesa; deu erro: a tela fica vermelha
//   livre        vai para a copa (café, bebedouro, estante) ou passeia pela sala
//   dormindo     cochila no sofá da copa
//   parou        sai pela porta; a mesa fica vazia uns minutos e depois some
//
// Mouse: passar mostra quem é; clique faz carinho e mostra os detalhes; duplo clique abre o
// Claude da conta. A simulação é própria (não é o palco da barra de tarefas).

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace ClawdDesktop
{
    sealed class OfficeSector
    {
        public string Key, Name;
        public AccountCfg Acc;
        public Dictionary<char, Brush> Pal;
        public Skin Skin, PupSkin;
        public Brush Carpet, CarpetAlt, Stripe;
        public Rect Room, Plaque;
        public Point DoorIn, DoorOut;
        public List<Rect> Cells = new List<Rect>();
        public Usage Usage;
        public double CostToday;
        public bool Running;
        public int Active, Overflow;
    }

    sealed class Desk
    {
        public string Id;
        public OfficeSector Sector;
        public int Slot;
        public SessionView S;
        public Worker W;
        public Rect Cell;
        public bool Shown;
        public bool Big;   // a sessão mais antiga ativa da conta: o Clawd grande, como na barra
    }

    sealed class Worker
    {
        public Desk Desk;
        public double X, Y;
        public readonly List<Point> Path = new List<Point>();
        public string Mode = "seat";      // seat, stand, free, sleep, leave
        public string Goal = "";          // onde ele está ou para onde vai: seat, stand, spot, room, exit
        public int Spot = -1;
        public int Facing = 1;
        public double NextBlink, BlinkUntil, HeartUntil, NextWander, FreeAt, PartyUntil;
        public bool Gone;
        public Rect Bounds;
        public readonly Pix Grid = new Pix();
    }

    sealed class LoungeSpot
    {
        public Point P;
        public string Kind;   // coffee, water, sofa, shelf, plant, window
        public int Facing;
        public Worker By;
    }

    sealed class OfficeView : FrameworkElement
    {
        const int P = 2;                       // tamanho do pixel dos Clawds
        const double CellW = 124, CellH = 108, Wall = 8, PlaqueH = 54;
        static readonly Brush Outside = Pal.B("#0c0b10", 1), WallTop = Pal.B("#3b3648", 1), WallFace = Pal.B("#26222f", 1), WallEdge = Pal.B("#4a4459", 1);
        static readonly Brush WoodTop = Pal.B("#b07a43", 1), WoodFace = Pal.B("#7a4f26", 1), WoodEdge = Pal.B("#5c3a1b", 1);
        static readonly Brush Metal = Pal.B("#5d6672", 1), MetalLight = Pal.B("#9aa4b1", 1), Dark = Pal.B("#15131a", 1), Ink = Pal.B("#17141c", 1);
        static readonly Brush PlaqueBg = Pal.B("#15131a", 0.92), Track = Pal.B("#ffffff", 0.12), Shadow = Pal.B("#000000", 0.25);
        static readonly Brush TextMain = Pal.B("#f3f1f7", 1), TextDim = Pal.B("#b3adc0", 1), TextFaint = Pal.B("#7d7790", 1);
        static readonly Brush ChairSeat = Pal.B("#2b313b", 1), ChairBack = Pal.B("#3d4552", 1);
        static readonly Brush Leaf = Pal.B("#3f8a4c", 1), LeafDark = Pal.B("#2f6b3a", 1), Pot = Pal.B("#a65a3a", 1);
        static readonly Brush Sofa = Pal.B("#5b4a8a", 1), SofaDark = Pal.B("#46386d", 1), Rug = Pal.B("#3a2f52", 0.85);
        static readonly Brush BubbleFill = Pal.B("#ffffff", 0.97), BubbleInk = Pal.B("#1b1b22", 1);
        static readonly Dictionary<char, Brush> Neutral = Pal.Make("#888888", "#aaaaaa", "#666666", "#222222", "#ffd23f");

        readonly Func<Cfg> cfg;
        readonly DispatcherTimer timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) };
        readonly Random rng = new Random();
        readonly Dictionary<string, Desk> desks = new Dictionary<string, Desk>();
        readonly List<OfficeSector> sectors = new List<OfficeSector>();
        readonly List<LoungeSpot> spots = new List<LoungeSpot>();
        readonly Dictionary<string, FormattedText> texts = new Dictionary<string, FormattedText>();
        readonly Pix scratch = new Pix();
        List<Skin> skins;
        Rect lounge;
        Point exit;
        double t, lastFrame = -1, nowMs;
        bool primed, layoutDirty = true, geoDirty = true;
        Size laidFor;
        string sectorSig = "";
        double ppd = 1;
        readonly Typeface font = new Typeface(new FontFamily("Segoe UI Variable Text, Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        readonly Typeface fontBold = new Typeface(new FontFamily("Segoe UI Variable Text, Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
        readonly Typeface mono = new Typeface(new FontFamily("Cascadia Mono, Consolas"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
        readonly List<Tuple<double, double, double, double, string, char>> confetti = new List<Tuple<double, double, double, double, string, char>>();
        readonly List<double[]> hearts = new List<double[]>();   // x, y, vida

        public string Hover, Selected;
        public event Action<string> SelectionChanged;
        public event Action<AccountCfg> OpenClaude;

        public OfficeView(Func<Cfg> config, bool photo)
        {
            cfg = config;
            skins = photo ? Skin.Builtin() : Skin.LoadAll(Cfg.SkinsDir);
            ClipToBounds = true;
            Focusable = false;
            SnapsToDevicePixels = true;
            RenderOptions.SetEdgeMode(this, EdgeMode.Aliased);
            timer.Tick += delegate { Step(); };
            IsVisibleChanged += delegate { if (IsVisible) { lastFrame = -1; timer.Start(); } else timer.Stop(); };
            SizeChanged += delegate { geoDirty = true; };
            Loaded += delegate { ppd = VisualTreeHelper.GetDpi(this).PixelsPerDip; };
            MouseMove += OnMouseMove;
            MouseLeave += delegate { Hover = null; Cursor = null; };
            MouseLeftButtonDown += OnDown;
        }

        public void ReloadSkins() { skins = Skin.LoadAll(Cfg.SkinsDir); sectorSig = ""; }

        static double Clock() { return (DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds; }

        // ------------------------------------------------------------- dados --

        static bool Busy(SessionView s)
        {
            switch (s.State)
            {
                case "trabalhando": case "pensando": case "compactando": case "feito": case "erro": case "recusa": case "interrompido": return true;
                default: return false;
            }
        }

        static string ModeFor(SessionView s, double now)
        {
            if (!s.IsActive(now)) return "leave";
            if (s.State == "aguardando") return "stand";
            if (s.State == "dormindo") return "sleep";
            return Busy(s) ? "seat" : "free";
        }

        public void Update(List<SessionView> all, double now)
        {
            nowMs = now;
            var c = cfg();
            double todayMs = (DateTime.Today.ToUniversalTime() - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds;

            // os setores: um por conta, e "Visitantes" para sessões de conta desconhecida
            bool visitors = all.Any(s => s.IsActive(now) && !c.Accounts.Any(a => a.Key == s.Account));
            string sig = string.Join("|", c.Accounts.Select(a => a.Key + a.Name + a.Body + a.Detail + a.Skin + a.PupSkin + a.Hat).ToArray()) + visitors;
            if (sig != sectorSig)
            {
                sectorSig = sig;
                sectors.Clear();
                foreach (var a in c.Accounts) sectors.Add(NewSector(a.Key, a.Name, a, a.Body, a.Detail));
                if (visitors) sectors.Add(NewSector("", "Visitantes", null, "#7a7f8a", "#ffd23f"));
                geoDirty = true;
                foreach (var d in desks.Values) { var sec = SectorOf(d.Sector.Key); if (sec != null) d.Sector = sec; }
            }
            foreach (var sec in sectors)
            {
                var mine = all.Where(s => sec.Acc != null ? s.Account == sec.Key : !c.Accounts.Any(a => a.Key == s.Account)).ToList();
                sec.Active = mine.Count(s => s.IsActive(now));
                sec.CostToday = mine.Where(s => !double.IsNaN(s.Cost) && s.UpdatedAt >= todayMs).Sum(s => s.Cost);
                if (sec.Acc != null) sec.Usage = Data.UsageFrom(Data.Read(System.IO.Path.Combine(Cfg.Dir, "uso-" + sec.Key + ".json")));
            }

            // as mesas: ativas, e as paradas há pouco (a mesa fica vazia uns minutos)
            var wanted = all.Where(s => s.IsActive(now) || now - Math.Max(s.UpdatedAt, s.EndedAt) < 10 * 60000).ToList();
            foreach (var s in wanted)
            {
                var sec = SectorOf(s.Account);
                if (sec == null) continue;
                Desk d;
                if (!desks.TryGetValue(s.Id, out d))
                {
                    if (!s.IsActive(now)) continue;   // nunca vista e já parada: não vale uma mesa
                    d = new Desk { Id = s.Id, Sector = sec, Slot = FreeSlot(sec) };
                    desks[s.Id] = d;
                    d.W = new Worker { Desk = d, NextBlink = t + rng.NextDouble() * 3 };
                    layoutDirty = true;
                    if (primed) Arrive(d.W);   // chegou agora: entra pela porta da copa
                }
                if (d.Sector != sec) { d.Sector = sec; d.Slot = FreeSlot(sec); layoutDirty = true; }
                d.S = s;
            }
            foreach (var id in desks.Keys.Where(k => !wanted.Any(s => s.Id == k)).ToList())
            {
                var d = desks[id];
                if (d.W != null && !d.W.Gone) { if (d.W.Mode != "leave") Want(d.W, "leave"); continue; }
                desks.Remove(id);
                layoutDirty = true;
            }
            foreach (var sec in sectors)
            {
                var oldest = desks.Values.Where(d => d.Sector == sec && d.S != null && d.S.IsActive(now)).OrderBy(d => d.S.StartedAt).FirstOrDefault();
                foreach (var d in desks.Values.Where(d => d.Sector == sec)) d.Big = d == oldest;
            }
            if (layoutDirty || geoDirty) Layout();

            foreach (var d in desks.Values)
            {
                if (d.W == null || d.S == null) continue;
                string mode = ModeFor(d.S, now);
                if (!primed) Place(d.W, mode);
                else if (mode != d.W.Mode) Want(d.W, mode);
                if (d.S.State == "feito" && d.W.PartyUntil < t - 20) { d.W.PartyUntil = t + 3; Confetti(d.W.X, d.W.Y - 28, d.Sector); }
            }
            primed = true;
            if (Selected != null && !desks.ContainsKey(Selected)) Select(null);
        }

        OfficeSector NewSector(string key, string name, AccountCfg a, string body, string detail)
        {
            var sec = new OfficeSector { Key = key, Name = name, Acc = a };
            sec.Pal = Tint.Palette(body, detail);
            sec.Skin = Skin.Find(skins, a != null ? a.Skin : "robo");
            sec.PupSkin = a != null && !string.IsNullOrEmpty(a.PupSkin) ? Skin.Find(skins, a.PupSkin) : sec.Skin;
            var floor = Tint.Surfaces(body, true);
            sec.Carpet = Pal.B(floor["surface-1"], 1);
            sec.CarpetAlt = Pal.B(floor["surface-2"], 1);
            sec.Stripe = Pal.B(body, 1);
            return sec;
        }

        OfficeSector SectorOf(string account)
        {
            var sec = sectors.FirstOrDefault(x => x.Acc != null && x.Key == account);
            return sec ?? sectors.FirstOrDefault(x => x.Acc == null);
        }

        int FreeSlot(OfficeSector sec)
        {
            for (int i = 0; ; i++) if (!desks.Values.Any(d => d.Sector == sec && d.Slot == i)) return i;
        }

        public IEnumerable<SessionView> Sessions { get { return desks.Values.Where(d => d.S != null).Select(d => d.S); } }
        public SessionView SessionOf(string id) { Desk d; return id != null && desks.TryGetValue(id, out d) ? d.S : null; }
        public AccountCfg AccountOf(string id) { Desk d; return id != null && desks.TryGetValue(id, out d) ? d.Sector.Acc : null; }

        // ------------------------------------------------------------ planta --

        void Layout()
        {
            double w = ActualWidth, h = ActualHeight;
            bool geo = geoDirty || laidFor.Width != w || laidFor.Height != h;
            layoutDirty = false;
            geoDirty = false;
            laidFor = new Size(w, h);
            if (w < 200 || h < 200) return;
            const double M = 14;
            double lh = Math.Max(120, Math.Min(170, h * 0.24));
            lounge = new Rect(M, h - M - lh, w - 2 * M, lh);
            exit = new Point(lounge.Right + 30, lounge.Top + lh * 0.55);

            int n = Math.Max(1, sectors.Count);
            int cols = n <= 3 ? n : (int)Math.Ceiling(n / 2.0);
            int rows = (int)Math.Ceiling(n / (double)cols);
            double areaTop = M, areaH = lounge.Top - Wall - areaTop;
            double rw = (w - 2 * M - (cols - 1) * Wall) / cols, rh = (areaH - (rows - 1) * Wall) / rows;
            for (int i = 0; i < sectors.Count; i++)
            {
                var sec = sectors[i];
                int cx = i % cols, cy = i / cols;
                sec.Room = new Rect(M + cx * (rw + Wall), areaTop + cy * (rh + Wall), rw, rh);
                sec.Plaque = new Rect(sec.Room.X + 12, sec.Room.Y + 10, sec.Room.Width - 24, PlaqueH - 14);
                // a porta: no meio da parede de baixo (salas da última fileira dão para a copa)
                sec.DoorIn = new Point(sec.Room.X + rw / 2, sec.Room.Bottom - 12);
                sec.DoorOut = new Point(sec.Room.X + rw / 2, lounge.Top + 16);
                // as mesas, em grade, abaixo da placa
                sec.Cells.Clear();
                double gx = sec.Room.X + 8, gy = sec.Room.Y + PlaqueH, gw = rw - 16, gh = rh - PlaqueH - 26;
                int cc = Math.Max(1, (int)(gw / CellW)), rr = Math.Max(1, (int)(gh / CellH));
                double ox = gx + (gw - cc * CellW) / 2;
                for (int r = 0; r < rr; r++)
                    for (int k = 0; k < cc; k++) sec.Cells.Add(new Rect(ox + k * CellW, gy + r * CellH, CellW, CellH));
                int used = desks.Values.Count(d => d.Sector == sec);
                sec.Overflow = Math.Max(0, used - sec.Cells.Count);
            }
            var moved = new List<Desk>();
            foreach (var d in desks.Values)
            {
                bool was = d.Shown;
                Rect old = d.Cell;
                d.Shown = d.Slot < d.Sector.Cells.Count;
                if (d.Shown) d.Cell = d.Sector.Cells[d.Slot];
                if (was != d.Shown || old != d.Cell) moved.Add(d);
            }

            // a copa: café, bebedouro, estante, sofá, plantas e janela
            spots.Clear();
            double ly = lounge.Top, lb = lounge.Bottom;
            AddSpot(lounge.X + 46, ly + 70, "coffee", 1);
            AddSpot(lounge.X + 80, ly + 74, "coffee", -1);
            AddSpot(lounge.X + 132, ly + 66, "water", 1);
            double sx = lounge.X + lounge.Width / 2 - 75;
            for (int i = 0; i < 3; i++) AddSpot(sx + 30 + i * 45, lb - 30, "sofa", i == 0 ? 1 : -1);
            AddSpot(lounge.Right - 100, ly + 62, "shelf", 1);
            AddSpot(lounge.Right - 150, ly + 66, "shelf", -1);
            AddSpot(lounge.X + lounge.Width * 0.30, lb - 40, "window", 1);
            AddSpot(lounge.X + lounge.Width * 0.72, lb - 40, "window", -1);

            // a planta mudou de tamanho: todo mundo vai direto para o seu lugar; se só uma mesa
            // mudou, só quem é dela (quem está chegando continua o caminho)
            foreach (var d in geo ? desks.Values.ToList() : moved)
            {
                var wk = d.W;
                if (wk == null || wk.Gone || wk.Mode == "") continue;
                if (!geo && wk.Path.Count > 0) continue;
                wk.Spot = -1;
                Place(wk, wk.Mode);
            }
            if (geo) foreach (var sp in spots) sp.By = null;
            if (geo) foreach (var d in desks.Values) if (d.W != null && d.W.Spot >= 0 && d.W.Spot < spots.Count) spots[d.W.Spot].By = d.W;
        }

        void AddSpot(double x, double y, string kind, int facing)
        {
            spots.Add(new LoungeSpot { P = new Point(Math.Round(x), Math.Round(y)), Kind = kind, Facing = facing });
        }

        Point Seat(Desk d) { return new Point(d.Cell.X + 44, d.Cell.Y + 64); }
        Point StandAt(Desk d) { return new Point(d.Cell.X + 14, d.Cell.Y + 92); }
        Point Aisle(Desk d) { return new Point(d.Cell.X + 44, d.Cell.Bottom - 6); }

        // ---------------------------------------------------------- movimento --

        // Põe direto no lugar, sem andar (primeira vez, ou a planta mudou).
        void Place(Worker w, string mode)
        {
            w.Mode = mode;
            w.Path.Clear();
            var d = w.Desk;
            if (!d.Shown) { w.Gone = mode == "leave"; w.X = d.Sector.DoorIn.X; w.Y = d.Sector.DoorIn.Y; w.Goal = "room"; return; }
            switch (mode)
            {
                case "seat": Set(w, Seat(d)); w.Goal = "seat"; break;
                case "stand": Set(w, StandAt(d)); w.Goal = "stand"; break;
                case "leave": w.Gone = true; w.Goal = "exit"; break;
                default:
                {
                    int i = FreeSpot(w, mode == "sleep" ? "sofa" : null);
                    if (i >= 0) { Set(w, spots[i].P); w.Goal = "spot"; Take(w, i); }
                    else { Set(w, Aisle(d)); w.Goal = "room"; }
                    w.NextWander = t + 6 + rng.NextDouble() * 12;
                    break;
                }
            }
        }

        static void Set(Worker w, Point p) { w.X = p.X; w.Y = p.Y; }

        void Take(Worker w, int i)
        {
            if (w.Spot >= 0 && w.Spot < spots.Count && spots[w.Spot].By == w) spots[w.Spot].By = null;
            w.Spot = i;
            if (i >= 0) spots[i].By = w;
        }

        int FreeSpot(Worker w, string kind)
        {
            var free = Enumerable.Range(0, spots.Count).Where(i => (spots[i].By == null || spots[i].By == w) && (kind == null || spots[i].Kind == kind)).ToList();
            if (free.Count == 0) return -1;
            return free[rng.Next(free.Count)];
        }

        bool InRoom(Worker w)
        {
            var r = w.Desk.Sector.Room;
            return w.Y < lounge.Top - 2 && w.X >= r.X - 2 && w.X <= r.Right + 2;
        }

        // O caminho até um ponto: sai da mesa pelo corredor, passa pela porta se trocar de ambiente.
        void Go(Worker w, Point target, bool targetInRoom)
        {
            w.Path.Clear();
            var d = w.Desk;
            bool here = InRoom(w);
            if (here && (w.Goal == "seat" || w.Goal == "stand") && d.Shown) w.Path.Add(Aisle(d));
            if (here && !targetInRoom) { w.Path.Add(d.Sector.DoorIn); w.Path.Add(d.Sector.DoorOut); }
            else if (!here && targetInRoom) { w.Path.Add(d.Sector.DoorOut); w.Path.Add(d.Sector.DoorIn); }
            if (targetInRoom && d.Shown && (target == Seat(d) || target == StandAt(d))) w.Path.Add(Aisle(d));
            w.Path.Add(target);
        }

        void Want(Worker w, string mode)
        {
            var d = w.Desk;
            w.Mode = mode;
            Take(w, -1);
            if (!d.Shown) { Place(w, mode); return; }
            switch (mode)
            {
                case "seat":
                    w.Goal = "seat";
                    Go(w, Seat(d), true);
                    break;
                case "stand":
                    w.Goal = "stand";
                    Go(w, StandAt(d), true);
                    break;
                case "leave":
                    w.Goal = "exit";
                    Go(w, new Point(exit.X, exit.Y), false);
                    break;
                case "sleep":
                {
                    int i = FreeSpot(w, "sofa");
                    if (i < 0) i = FreeSpot(w, null);
                    if (i >= 0) { Take(w, i); w.Goal = "spot"; Go(w, spots[i].P, false); }
                    break;
                }
                default:
                    // acabou de ficar livre: termina o que estava fazendo e só depois levanta
                    w.FreeAt = t + 2 + rng.NextDouble() * 3;
                    w.NextWander = w.FreeAt;
                    break;
            }
        }

        // Chegou agora: aparece na saída da copa e vai até a mesa (o Want logo depois traça o caminho).
        void Arrive(Worker w)
        {
            w.X = exit.X; w.Y = exit.Y;
            w.Goal = "";
            w.Mode = "";
        }

        void Wander(Worker w)
        {
            var d = w.Desk;
            if (rng.NextDouble() < 0.6)
            {
                int i = FreeSpot(w, null);
                if (i >= 0) { Take(w, i); w.Goal = "spot"; Go(w, spots[i].P, false); }
            }
            else if (d.Shown)
            {
                Take(w, -1);
                var r = d.Sector.Room;
                var p = new Point(Math.Round(r.X + 30 + rng.NextDouble() * (r.Width - 60)), Math.Round(d.Cell.Bottom - 6));
                w.Goal = "room";
                Go(w, p, true);
            }
            w.NextWander = t + 10 + rng.NextDouble() * 14;
        }

        void Step()
        {
            double now = Clock();
            double dt = lastFrame < 0 ? 0.033 : Math.Min(0.1, now - lastFrame);
            lastFrame = now;
            Advance(dt);
            InvalidateVisual();
        }

        // Anda o tempo do escritório (também usado pelas fotos, com passos fixos).
        public void Advance(double dt)
        {
            t += dt;
            if (layoutDirty || geoDirty || laidFor.Width != ActualWidth || laidFor.Height != ActualHeight) Layout();
            foreach (var d in desks.Values.ToList())
            {
                var w = d.W;
                if (w == null || w.Gone) continue;
                if (t > w.NextBlink) { w.BlinkUntil = t + 0.12; w.NextBlink = t + 2.5 + rng.NextDouble() * 3.5; }
                if (w.Mode == "free" && w.Path.Count == 0 && t >= w.NextWander && t >= w.FreeAt) Wander(w);
                if (w.Path.Count > 0)
                {
                    var p = w.Path[0];
                    double dx = p.X - w.X, dy = p.Y - w.Y, dist = Math.Sqrt(dx * dx + dy * dy), sp = 74 * dt;
                    if (Math.Abs(dx) > 0.5) w.Facing = dx > 0 ? 1 : -1;
                    if (dist <= sp) { w.X = p.X; w.Y = p.Y; w.Path.RemoveAt(0); }
                    else { w.X += dx / dist * sp; w.Y += dy / dist * sp; }
                    if (w.Path.Count == 0 && w.Goal == "exit") w.Gone = true;
                }
            }
            for (int i = confetti.Count - 1; i >= 0; i--)
            {
                var c = confetti[i];
                double life = c.Item3 - dt;
                if (life <= 0) { confetti.RemoveAt(i); continue; }
                confetti[i] = Tuple.Create(c.Item1 + c.Item4 * dt, c.Item2 + (40 - life * 50) * dt, life, c.Item4, c.Item5, c.Item6);
            }
            for (int i = hearts.Count - 1; i >= 0; i--)
            {
                hearts[i][1] -= 26 * dt;
                hearts[i][2] -= dt;
                if (hearts[i][2] <= 0) hearts.RemoveAt(i);
            }
        }

        void Confetti(double x, double y, OfficeSector sec)
        {
            char[] colors = { 'y', 'P', 'c', 'n', 'a' };
            for (int i = 0; i < 18; i++)
                confetti.Add(Tuple.Create(x + (rng.NextDouble() - 0.5) * 30, y - rng.NextDouble() * 10, 1.0 + rng.NextDouble() * 0.6, (rng.NextDouble() - 0.5) * 60, sec.Key, colors[rng.Next(colors.Length)]));
        }

        // ------------------------------------------------------------- mouse --

        Desk HitTest(Point pt)
        {
            foreach (var d in desks.Values.OrderByDescending(x => x.W != null ? x.W.Y : 0))
                if (d.W != null && !d.W.Gone && Inflate(d.W.Bounds, 3).Contains(pt)) return d;
            foreach (var d in desks.Values)
                if (d.Shown && new Rect(d.Cell.X + 10, d.Cell.Y + 26, d.Cell.Width - 20, 58).Contains(pt)) return d;
            return null;
        }

        static Rect Inflate(Rect r, double k) { if (r.IsEmpty) return r; r.Inflate(k, k); return r; }

        void OnMouseMove(object sender, MouseEventArgs e)
        {
            var d = HitTest(e.GetPosition(this));
            Hover = d != null ? d.Id : null;
            Cursor = d != null ? Cursors.Hand : null;
        }

        void OnDown(object sender, MouseButtonEventArgs e)
        {
            var d = HitTest(e.GetPosition(this));
            if (d == null) { Select(null); return; }
            if (e.ClickCount >= 2) { if (OpenClaude != null && d.Sector.Acc != null) OpenClaude(d.Sector.Acc); return; }
            if (d.W != null && !d.W.Gone && d.W.Bounds.Contains(e.GetPosition(this)))
            {
                d.W.HeartUntil = t + 1.4;
                hearts.Add(new[] { d.W.X + (rng.NextDouble() - 0.5) * 10, d.W.Y - 34, 1.2 });
            }
            Select(d.Id);
        }

        public void Select(string id)
        {
            if (Selected == id) return;
            Selected = id;
            if (SelectionChanged != null) SelectionChanged(id);
        }

        // ------------------------------------------------------------ desenho --

        FormattedText Txt(string s, double size, Brush b, bool bold, double maxW)
        {
            string key = s + "|" + size + "|" + bold + "|" + b.GetHashCode() + "|" + (int)maxW;
            FormattedText ft;
            if (texts.TryGetValue(key, out ft)) return ft;
            if (texts.Count > 600) texts.Clear();
            ft = new FormattedText(s, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, bold ? fontBold : font, size, b, ppd);
            if (maxW > 0 && ft.Width > maxW)
            {
                ft.MaxTextWidth = Math.Max(10, maxW);
                ft.MaxLineCount = 1;
                ft.Trimming = TextTrimming.CharacterEllipsis;
            }
            texts[key] = ft;
            return ft;
        }

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth, h = ActualHeight;
            dc.DrawRectangle(Outside, null, new Rect(0, 0, w, h));
            if (w < 200 || h < 200) return;
            if (layoutDirty || geoDirty || laidFor.Width != w || laidFor.Height != h) Layout();

            DrawLounge(dc);
            foreach (var sec in sectors) DrawRoom(dc, sec);

            // mesas e quem está nelas, de cima para baixo
            foreach (var d in desks.Values.Where(x => x.Shown).OrderBy(x => x.Cell.Y).ThenBy(x => x.Cell.X)) DrawDesk(dc, d);
            // quem anda por aí, ordenado pela altura (quem está mais embaixo fica na frente)
            foreach (var d in desks.Values.Where(x => x.W != null && !x.W.Gone && !AtDesk(x.W) && (x.Shown || x.W.Path.Count > 0)).OrderBy(x => x.W.Y)) DrawWorker(dc, d.W);
            foreach (var d in desks.Values.Where(x => x.Shown)) DrawHelpers(dc, d);

            DrawParticles(dc);
            foreach (var d in desks.Values.Where(x => x.W != null && !x.W.Gone && x.Shown)) DrawBubble(dc, d);
            if (Hover != null) DrawHoverCard(dc);
            if (sectors.Count == 0)
            {
                var ft = Txt("Nenhuma conta do Claude encontrada ainda.", 15, TextDim, false, 0);
                dc.DrawText(ft, new Point((w - ft.Width) / 2, h / 2 - 10));
            }
        }

        static bool AtDesk(Worker w)
        {
            return w.Path.Count == 0 && w.Goal == "seat" && w.Desk.Shown;
        }

        void Px(DrawingContext dc, Brush b, double x, double y, double ww, double hh)
        {
            dc.DrawRectangle(b, null, new Rect(Math.Round(x), Math.Round(y), ww, hh));
        }

        void DrawRoom(DrawingContext dc, OfficeSector sec)
        {
            var r = sec.Room;
            // carpete em quadrados
            dc.DrawRectangle(sec.Carpet, null, r);
            for (double y = r.Y; y < r.Bottom; y += 32)
                for (double x = r.X + ((int)((y - r.Y) / 32) % 2) * 32; x < r.Right; x += 64)
                    dc.DrawRectangle(sec.CarpetAlt, null, Rect.Intersect(new Rect(x, y, 32, 32), r));
            // paredes (a de cima com a face à mostra) e a porta
            Px(dc, WallFace, r.X - Wall, r.Y - Wall, r.Width + 2 * Wall, Wall + 6);
            Px(dc, WallTop, r.X - Wall, r.Y - Wall, r.Width + 2 * Wall, 4);
            Px(dc, WallTop, r.X - Wall, r.Y, Wall, r.Height + Wall);
            Px(dc, WallTop, r.Right, r.Y, Wall, r.Height + Wall);
            double door = sec.DoorIn.X;
            Px(dc, WallTop, r.X - Wall, r.Bottom, door - 22 - (r.X - Wall), Wall);
            Px(dc, WallTop, door + 22, r.Bottom, r.Right + Wall - (door + 22), Wall);
            Px(dc, WallEdge, door - 24, r.Bottom, 2, Wall);
            Px(dc, WallEdge, door + 22, r.Bottom, 2, Wall);
            // uma faixa na cor da conta no alto da sala
            Px(dc, sec.Stripe, r.X, r.Y + 6, r.Width, 3);
            // plantas nos cantos de baixo
            DrawPlant(dc, r.X + 14, r.Bottom - 10);
            DrawPlant(dc, r.Right - 14, r.Bottom - 10);
            DrawPlaque(dc, sec);
        }

        void DrawPlaque(DrawingContext dc, OfficeSector sec)
        {
            var pr = sec.Plaque;
            dc.DrawRoundedRectangle(PlaqueBg, new Pen(Pal.B("#ffffff", 0.06), 1), pr, 8, 8);
            dc.DrawRoundedRectangle(sec.Stripe, null, new Rect(pr.X + 8, pr.Y + 9, 4, pr.Height - 18), 2, 2);
            var limits = sec.Usage != null ? sec.Usage.Limits.Where(l => l.Kind == "five_hour" || l.Kind == "seven_day").ToList() : new List<Limit>();
            double right = pr.Right - 10;
            // os limites, à direita
            double lw = limits.Count > 0 && pr.Width > 360 ? 128 : 0;
            if (lw > 0)
            {
                for (int i = 0; i < limits.Count; i++)
                {
                    var l = limits[i];
                    double y = pr.Y + 8 + i * 13;
                    var name = Txt(l.Kind == "five_hour" ? "5h" : "sem.", 10, TextFaint, false, 0);
                    dc.DrawText(name, new Point(right - lw, y - 1));
                    double bx = right - lw + 30, bw = lw - 66;
                    dc.DrawRoundedRectangle(Track, null, new Rect(bx, y + 4, bw, 5), 2.5, 2.5);
                    dc.DrawRoundedRectangle(Ui.LimitColor(l.Pct), null, new Rect(bx, y + 4, Math.Max(2, bw * Math.Min(100, l.Pct) / 100), 5), 2.5, 2.5);
                    var pct = Txt((int)Math.Round(l.Pct) + "%", 10, TextDim, true, 0);
                    dc.DrawText(pct, new Point(right - pct.Width, y - 1));
                }
            }
            double textW = pr.Width - 30 - (lw > 0 ? lw + 12 : 0);
            dc.DrawText(Txt(sec.Name, 13.5, TextMain, true, textW), new Point(pr.X + 20, pr.Y + 4));
            var bits = new List<string>();
            if (sec.Acc != null) bits.Add(sec.Running ? "aberto" : "fechado");
            bits.Add(sec.Active == 0 ? "ninguém trabalhando" : sec.Active == 1 ? "1 sessão" : sec.Active + " sessões");
            if (sec.CostToday > 0) bits.Add("hoje " + Words.Money(sec.CostToday));
            if (sec.Overflow > 0) bits.Add("+" + sec.Overflow + " sem mesa");
            dc.DrawText(Txt(string.Join(" · ", bits.ToArray()), 11, TextDim, false, textW), new Point(pr.X + 20, pr.Y + 22));
        }

        public void SetRunning(Dictionary<string, int> running)
        {
            foreach (var sec in sectors) sec.Running = sec.Acc != null && running.ContainsKey(sec.Acc.Profile);
        }

        void DrawPlant(DrawingContext dc, double x, double y)
        {
            Px(dc, Pot, x - 6, y - 8, 12, 8);
            Px(dc, Pal.B("#7d3f27", 1), x - 6, y - 8, 12, 2);
            Px(dc, LeafDark, x - 9, y - 20, 18, 12);
            Px(dc, Leaf, x - 7, y - 24, 14, 12);
            Px(dc, Leaf, x - 2, y - 28, 4, 4);
        }

        void DrawLounge(DrawingContext dc)
        {
            var l = lounge;
            // piso de madeira em tábuas
            dc.DrawRectangle(Pal.B("#3a2c22", 1), null, l);
            Brush plank = Pal.B("#33271e", 1);
            for (double y = l.Y; y < l.Bottom; y += 12)
            {
                Px(dc, plank, l.X, y + 11, l.Width, 1);
                double off = ((int)((y - l.Y) / 12) % 3) * 40;
                for (double x = l.X + off; x < l.Right; x += 120) Px(dc, plank, x, y, 1, 11);
            }
            // paredes da copa
            Px(dc, WallTop, l.X - Wall, l.Bottom, l.Width + 2 * Wall, Wall);
            Px(dc, WallTop, l.X - Wall, l.Y - Wall, Wall, l.Height + 2 * Wall);
            Px(dc, WallTop, l.Right, l.Y - Wall, Wall, l.Height * 0.55 - 18 + Wall);
            Px(dc, WallTop, l.Right, l.Y + l.Height * 0.55 + 18, Wall, l.Height * 0.45 - 18 + Wall);
            var title = Txt("Copa", 11, TextFaint, true, 0);
            dc.DrawText(title, new Point(l.X + 10, l.Y + 6));

            // café e bebedouro
            double cx = l.X + 50, cy = l.Y + 22;
            Px(dc, Pal.B("#4a4f58", 1), cx, cy, 30, 34);
            Px(dc, Dark, cx + 6, cy + 6, 18, 10);
            Px(dc, Pal.B("#e5484d", 1), cx + 22, cy + 22, 3, 3);
            Px(dc, Pal.B("#f6f1e4", 1), cx + 11, cy + 26, 8, 6);
            Px(dc, Pal.B("#7a4f26", 1), cx + 12, cy + 26, 6, 2);
            double wx = l.X + 116, wy = l.Y + 16;
            Px(dc, Pal.B("#7fd6ff", 0.8), wx + 4, wy, 18, 18);
            Px(dc, Pal.B("#c9ced6", 1), wx, wy + 18, 26, 22);
            Px(dc, Pal.B("#2f7fd8", 1), wx + 10, wy + 26, 6, 3);
            // estante
            double bx = l.Right - 170, by = l.Y + 14;
            Px(dc, WoodFace, bx, by, 96, 36);
            Px(dc, WoodEdge, bx, by + 17, 96, 2);
            string[] books = { "#e5484d", "#2f7fd8", "#ffd23f", "#5ee08a", "#8a5cf6", "#ff9f43", "#7fd6ff" };
            for (int i = 0; i < 12; i++) Px(dc, Pal.B(books[i % books.Length], 1), bx + 4 + i * 7.5, by + 3 + (i % 3 == 0 ? 2 : 0), 5, 13 - (i % 3 == 0 ? 2 : 0));
            for (int i = 0; i < 9; i++) Px(dc, Pal.B(books[(i + 3) % books.Length], 1), bx + 6 + i * 9, by + 21, 6, 13);
            // tapete e sofá
            double sx = l.X + l.Width / 2 - 75, sy = l.Bottom - 46;
            dc.DrawRoundedRectangle(Rug, null, new Rect(sx + 26, l.Y + 34, 98, 40), 8, 8);
            Px(dc, Pal.B("#7a4f26", 1), sx + 40, l.Y + 44, 70, 24);
            Px(dc, WoodTop, sx + 40, l.Y + 44, 70, 4);
            Px(dc, Pal.B("#f6f1e4", 1), sx + 52, l.Y + 52, 8, 6);
            Px(dc, Pal.B("#2f7fd8", 1), sx + 86, l.Y + 50, 12, 9);
            Px(dc, SofaDark, sx, sy + 16, 150, 18);
            Px(dc, Sofa, sx + 6, sy + 4, 138, 14);
            Px(dc, SofaDark, sx - 6, sy + 2, 12, 30);
            Px(dc, SofaDark, sx + 144, sy + 2, 12, 30);
            // janela na parede de baixo
            for (int i = 0; i < 2; i++)
            {
                double jx = l.X + l.Width * (i == 0 ? 0.30 : 0.72) - 30;
                Px(dc, Pal.B("#7fd6ff", 0.55), jx, l.Bottom - 2, 60, Wall - 2);
                Px(dc, Pal.B("#ffffff", 0.5), jx + 29, l.Bottom - 2, 2, Wall - 2);
            }
            DrawPlant(dc, l.X + 18, l.Bottom - 8);
            DrawPlant(dc, l.Right - 40, l.Bottom - 8);
            // saída
            var sai = Txt("saída", 10, TextFaint, false, 0);
            dc.DrawText(sai, new Point(l.Right - sai.Width - 12, l.Y + l.Height * 0.55 - 6));
        }

        // A mesa: cadeira, o Clawd sentado (se estiver), o tampo, o monitor e o nome da sessão.
        void DrawDesk(DrawingContext dc, Desk d)
        {
            var c = d.Cell;
            double x = c.X, y = c.Y;
            bool here = d.W != null && !d.W.Gone && AtDesk(d.W);
            bool active = d.S != null && d.S.IsActive(nowMs);
            if (d.Id == Selected || d.Id == Hover)
                dc.DrawRoundedRectangle(Pal.B("#ffffff", d.Id == Selected ? 0.10 : 0.05), new Pen(d.Id == Selected ? d.Sector.Stripe : Pal.B("#ffffff", 0.15), d.Id == Selected ? 2 : 1), new Rect(x + 4, y + 18, c.Width - 8, c.Height - 22), 8, 8);
            // cadeira (encosto atrás de quem senta)
            Px(dc, ChairBack, x + 32, y + 36, 24, 10);
            Px(dc, ChairSeat, x + 30, y + 46, 28, 8);
            if (here) DrawWorker(dc, d.W);
            // tampo
            Px(dc, WoodEdge, x + 12, y + 54, 96, 26);
            Px(dc, WoodFace, x + 12, y + 62, 96, 16);
            Px(dc, WoodTop, x + 12, y + 52, 96, 10);
            Px(dc, WoodEdge, x + 16, y + 78, 4, 6);
            Px(dc, WoodEdge, x + 100, y + 78, 4, 6);
            // teclado e caneca
            Px(dc, Pal.B("#c9ced6", 1), x + 36, y + 54, 26, 5);
            Px(dc, Pal.B("#9aa4b1", 1), x + 36, y + 58, 26, 1);
            Px(dc, Pal.B("#f6f1e4", 1), x + 18, y + 53, 7, 7);
            Px(dc, d.Sector.Stripe, x + 18, y + 53, 7, 2);
            // monitor
            Px(dc, Metal, x + 83, y + 46, 6, 8);
            Px(dc, Metal, x + 77, y + 52, 18, 3);
            Px(dc, Ink, x + 68, y + 24, 36, 24);
            DrawScreen(dc, d, new Rect(x + 70, y + 26, 32, 20), active);
            // nome da sessão
            if (d.S != null)
            {
                var ft = Txt(d.S.Label, 10.5, active ? TextDim : TextFaint, false, c.Width - 12);
                dc.DrawText(ft, new Point(Math.Round(x + (c.Width - ft.Width) / 2), y + 86));
            }
        }

        // A tela do computador mostra o tipo de trabalho, animada.
        void DrawScreen(DrawingContext dc, Desk d, Rect r, bool active)
        {
            var s = d.S;
            if (s == null || !active || (d.W != null && (d.W.Gone || d.W.Goal == "exit")))
            {
                dc.DrawRectangle(Pal.B("#0e0d12", 1), null, r);
                return;
            }
            string kind = s.State == "trabalhando" ? Words.WorkAnim(s.Tool, s.Detail) : s.State;
            int f = (int)(t * 6);
            switch (kind)
            {
                case "bash": case "ps": case "git": case "test": case "install": case "build": case "trash": case "stack": case "net": case "peek": case "rocket":
                {
                    dc.DrawRectangle(Pal.B("#0d1117", 1), null, r);
                    Brush g = Pal.B(kind == "git" ? "#ff9f43" : kind == "test" ? "#5ee08a" : "#7ee787", 1), gray = Pal.B("#6e7681", 1);
                    for (int i = 0; i < 5; i++)
                    {
                        int k = (f + i) % 7;
                        Px(dc, i == 4 ? g : gray, r.X + 2, r.Y + 2 + i * 4, 4 + (k * 5 + i * 3) % 22, 2);
                    }
                    if (f % 2 == 0) Px(dc, g, r.X + 2 + (f * 3) % 20, r.Y + 18, 3, 2);
                    break;
                }
                case "edit": case "write": case "read": case "grep": case "glob": case "grid": case "paint":
                {
                    dc.DrawRectangle(Pal.B("#1e1b2e", 1), null, r);
                    string[] cs = { "#c792ea", "#ffcb6b", "#82aaff", "#c3e88d", "#89ddff" };
                    for (int i = 0; i < 5; i++)
                    {
                        int k = (i + (kind == "read" ? f / 2 : 0)) % 5;
                        Px(dc, Pal.B(cs[k], 1), r.X + 2 + (i % 3) * 3, r.Y + 2 + i * 4, 6 + (k * 7 + i * 5) % 18, 2);
                    }
                    if (kind == "edit" || kind == "write") { if (f % 2 == 0) Px(dc, Brushes.White, r.X + 26, r.Y + 2 + (f / 2 % 5) * 4, 1, 3); }
                    break;
                }
                case "search": case "fetch": case "browser": case "camera": case "mouse":
                    dc.DrawRectangle(Pal.B("#f4f1ea", 1), null, r);
                    Px(dc, Pal.B("#2f7fd8", 1), r.X, r.Y, r.Width, 4);
                    Px(dc, Pal.B("#c9ced6", 1), r.X + 3, r.Y + 7, 18, 3);
                    for (int i = 0; i < 3; i++) Px(dc, Pal.B("#9aa4b1", 1), r.X + 3, r.Y + 12 + i * 3, 10 + ((f + i * 4) % 14), 1);
                    break;
                case "agent": case "conduct":
                    dc.DrawRectangle(Pal.B("#1d2333", 1), null, r);
                    for (int i = 0; i < 3; i++)
                    {
                        double bx = r.X + 3 + i * 10, by = r.Y + 6 + ((f + i) % 2);
                        Px(dc, Pal.B("#9aa4b1", 1), bx, by, 7, 6);
                        Px(dc, Pal.B("#ffd23f", 1), bx + 3, by - 2, 1, 2);
                        Px(dc, Pal.B("#7fd6ff", 1), bx + 1, by + 2, 2, 1);
                        Px(dc, Pal.B("#7fd6ff", 1), bx + 4, by + 2, 2, 1);
                    }
                    break;
                case "plan":
                    dc.DrawRectangle(Pal.B("#f4f1ea", 1), null, r);
                    for (int i = 0; i < 4; i++)
                    {
                        bool done = i < (f / 4) % 5;
                        Px(dc, done ? Pal.B("#2f8a5b", 1) : Pal.B("#9aa4b1", 1), r.X + 3, r.Y + 3 + i * 4, 3, 3);
                        Px(dc, Pal.B("#6e7681", 1), r.X + 8, r.Y + 4 + i * 4, 14, 1);
                    }
                    break;
                case "pensando": case "compactando":
                    dc.DrawRectangle(Pal.B("#16213a", 1), null, r);
                    if (f % 4 < 2) Px(dc, Pal.B("#5cc8ff", 1), r.X + 4, r.Y + 8, 2, 5);
                    break;
                case "aguardando":
                    dc.DrawRectangle(Pal.B("#3a1830", 1), null, r);
                    if (f % 4 < 3) DrawArtAt(dc, Art.Question, r.X + r.Width / 2 - 5, r.Y + 3, 2, 'P');
                    break;
                case "erro": case "recusa": case "interrompido":
                    dc.DrawRectangle(Pal.B("#3a1215", 1), null, r);
                    if (f % 3 < 2) DrawArtAt(dc, Art.Bang, r.X + r.Width / 2 - 2, r.Y + 4, 2, 'r');
                    break;
                case "feito":
                    dc.DrawRectangle(Pal.B("#10301f", 1), null, r);
                    DrawArtAt(dc, Art.Check, r.X + r.Width / 2 - 6, r.Y + 5, 2, 'n');
                    break;
                default:
                    dc.DrawRectangle(Pal.B("#22324a", 1), null, r);
                    for (int i = 0; i < 3; i++) if ((f + i) % 3 == 0) Px(dc, Pal.B("#ffd23f", 1), r.X + 5 + i * 9, r.Y + 6 + (i % 2) * 6, 2, 2);
                    break;
            }
        }

        // Desenha uma arte de letras numa cor só (a letra da arte vira a cor dada).
        void DrawArtAt(DrawingContext dc, string[] art, double x, double y, double p, char color)
        {
            Brush b = Neutral.ContainsKey(color) ? Neutral[color] : Brushes.White;
            for (int r = 0; r < art.Length; r++)
                for (int c = 0; c < art[r].Length; c++)
                    if (art[r][c] != '.') dc.DrawRectangle(art[r][c] == 'w' ? Brushes.White : b, null, new Rect(Math.Round(x + c * p), Math.Round(y + r * p), p, p));
        }

        void DrawWorker(DrawingContext dc, Worker w)
        {
            var d = w.Desk;
            var s = d.S;
            int scale = d.Big ? P + 1 : P;
            bool walking = w.Path.Count > 0;
            bool seated = !walking && w.Goal == "seat";
            bool onSofa = !walking && w.Goal == "spot" && w.Spot >= 0 && w.Spot < spots.Count && spots[w.Spot].Kind == "sofa";
            var pose = new Pose();
            pose.Blink = t < w.BlinkUntil;
            pose.Frame = (int)(t * 8);
            pose.Mouth = "smile";
            int facing = w.Facing;
            if (walking) { pose.Legs = Legs.Walk; pose.Dy = pose.Frame % 2 == 0 ? 0 : -1; }
            else if (seated)
            {
                pose.Legs = Legs.Sit;
                facing = 1;   // de frente para o monitor, à direita
                string st = s != null ? s.State : "";
                if (st == "trabalhando") { pose.ArmR = pose.Frame % 2 == 0 ? Arm.Type : Arm.Low; pose.ArmL = Arm.Low; pose.Eyes = "focus"; pose.Mouth = "flat"; pose.LookX = 1; }
                else if (st == "pensando" || st == "compactando") { pose.ArmR = Arm.Up; pose.LookY = -1; pose.Mouth = "flat"; }
                else if (st == "feito") { pose.ArmL = Arm.High; pose.ArmR = Arm.High; pose.Mouth = "open"; pose.Dy = pose.Frame % 4 < 2 ? -1 : 0; }
                else if (st == "erro" || st == "recusa" || st == "interrompido") { pose.Mouth = "frown"; pose.ShakeX = pose.Frame % 4 == 0 ? 1 : 0; }
            }
            else if (!walking && w.Goal == "stand")
            {
                facing = 1;
                pose.ArmR = pose.Frame % 4 < 2 ? Arm.High : Arm.Up;
                pose.Mouth = "open";
                pose.Eyes = "wide";
            }
            else if (onSofa)
            {
                pose.Legs = Legs.Sit;
                facing = spots[w.Spot].Facing;
                if (w.Mode == "sleep") { pose.Eyes = "closed"; pose.Mouth = "flat"; }
            }
            else if (!walking && w.Goal == "spot" && w.Spot >= 0)
            {
                facing = spots[w.Spot].Facing;
                string kind = spots[w.Spot].Kind;
                if (kind == "coffee" || kind == "water") pose.ArmR = Arm.Mid;
                if (kind == "shelf") { pose.ArmR = Arm.Up; pose.LookY = -1; }
            }
            if (t < w.HeartUntil) { pose.Blush = true; pose.Eyes = "closed"; pose.Mouth = "smile"; }
            if (s != null && !s.IsActive(nowMs)) { pose.Mouth = "flat"; }

            var g = w.Grid;
            g.Clear();
            g.Flip = facing < 0;
            var skin = d.Big ? d.Sector.Skin : d.Sector.PupSkin;
            string hat = d.Sector.Acc != null && d.Sector.Acc.Hat && s != null ? s.Model : null;
            Body.DrawPose(g, skin, hat, pose, t);
            double gx = Math.Round(w.X), gy = Math.Round(w.Y);
            // sombra
            if (!seated) dc.DrawRectangle(Shadow, null, new Rect(gx - 7 * scale, gy - scale, 14 * scale, scale + 1));
            Body.Blit(dc, g, d.Sector.Pal, gx - Pix.OX * scale, gy - Pix.OY * scale, scale);
            w.Bounds = new Rect(gx - 10 * scale, gy - 15 * scale, 20 * scale, 15 * scale);

            // a xícara de café na mão
            if (!walking && w.Goal == "spot" && w.Spot >= 0 && spots[w.Spot].Kind == "coffee")
            {
                double cx = gx + facing * 11 * scale, cy = gy - 9 * scale;
                Px(dc, Pal.B("#f6f1e4", 1), cx - 3, cy, 6, 6);
                if ((int)(t * 2) % 2 == 0) Px(dc, Pal.B("#ffffff", 0.5), cx - 1, cy - 5, 2, 3);
            }
            // Zzz no sofá
            if (onSofa && w.Mode == "sleep")
            {
                double k = (t * 0.8) % 1;
                DrawArtAt(dc, Art.Z1, gx + 10, gy - 34 - k * 14, 2, 'g');
            }
        }

        // Os subagentes: robozinhos em volta da mesa.
        void DrawHelpers(DrawingContext dc, Desk d)
        {
            if (d.S == null || !d.S.IsActive(nowMs) || d.S.Agents <= 0) return;
            int n = Math.Min(4, (int)d.S.Agents);
            double[][] pos = { new[] { 6.0, 70.0 }, new[] { 114.0, 72.0 }, new[] { 10.0, 40.0 }, new[] { 112.0, 34.0 } };
            for (int i = 0; i < n; i++)
            {
                double bob = Math.Round(Math.Sin(t * 5 + i * 1.7) * 2);
                double x = d.Cell.X + pos[i][0] - 6, y = d.Cell.Y + pos[i][1] - 12 + bob;
                dc.DrawRectangle(Shadow, null, new Rect(x + 1, d.Cell.Y + pos[i][1] + 1, 10, 2));
                scratch.Clear();
                scratch.Stamp(Art.Bot, -3, -6);
                Body.Blit(dc, scratch, d.Sector.Pal, x - (Pix.OX - 3) * 2, y - (Pix.OY - 6) * 2, 2);
                if ((int)(t * 4 + i) % 6 == 0) Px(dc, Pal.B("#ffd23f", 1), x + 5, y - 4, 2, 2);
            }
        }

        void DrawParticles(DrawingContext dc)
        {
            foreach (var c in confetti)
            {
                var sec = sectors.FirstOrDefault(x => x.Key == c.Item5);
                Brush b;
                if (sec == null || !sec.Pal.TryGetValue(c.Item6, out b)) b = Brushes.White;
                Px(dc, b, c.Item1, c.Item2, 3, 3);
            }
            foreach (var h in hearts) DrawArtAt(dc, Art.HeartS, h[0], h[1], 3, 'P');
        }

        // Balões em cima de quem precisa dizer algo: pergunta, pensando, erro, pronto.
        void DrawBubble(DrawingContext dc, Desk d)
        {
            var w = d.W;
            if (w.Path.Count > 0 || d.S == null || !d.S.IsActive(nowMs)) return;
            string st = d.S.State;
            string text = null;
            Brush edge = null;
            if (w.Goal == "stand" && st == "aguardando")
            {
                text = d.S.Tool == "AskUserQuestion" ? "Uma pergunta!" : d.S.Tool == "ExitPlanMode" ? "Aprova o plano?" : "Posso?";
                edge = Ui.Ask;
            }
            else if (w.Goal == "seat" && (st == "pensando" || st == "compactando")) text = new string('.', 1 + (int)(t * 2.5) % 3).PadRight(3);
            else if (w.Goal == "seat" && st == "erro") { text = "Ops!"; edge = Ui.Bad; }
            else if (w.Goal == "seat" && st == "feito") { text = "Pronto!"; edge = Ui.Good; }
            if (text == null) return;
            var ft = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, mono, 11, BubbleInk, ppd);
            double bw = Math.Round(ft.Width + 12), bh = 18;
            double bx = Math.Round(w.X - bw / 2 + 8), by = Math.Round(w.Bounds.Top - bh - 6);
            if (w.Goal == "stand") by -= 6;
            Brush line = edge ?? d.Sector.Stripe;
            dc.DrawRectangle(line, null, new Rect(bx - 2, by - 2, bw + 4, bh + 4));
            dc.DrawRectangle(BubbleFill, null, new Rect(bx, by, bw, bh));
            dc.DrawRectangle(line, null, new Rect(bx + 6, by + bh + 2, 4, 3));
            dc.DrawText(ft, new Point(bx + 6, by + 2));
        }

        // Ao passar o mouse: um cartãozinho com a sessão.
        void DrawHoverCard(DrawingContext dc)
        {
            Desk d;
            if (!desks.TryGetValue(Hover, out d) || d.S == null) return;
            var s = d.S;
            var title = Txt(s.Label, 12.5, TextMain, true, 240);
            var line = Txt(Words.Short(s, nowMs), 11, TextDim, false, 240);
            string model = Ui.ModelName(s.Model);
            var extra = Txt((model.Length > 0 ? model + " · " : "") + d.Sector.Name + (!double.IsNaN(s.Cost) ? " · " + Words.Money(s.Cost) : ""), 10.5, TextFaint, false, 240);
            double w = Math.Max(title.Width, Math.Max(line.Width, extra.Width)) + 24, h = 62;
            Point anchor = d.W != null && !d.W.Gone && !d.W.Bounds.IsEmpty ? new Point(d.W.Bounds.Right + 8, d.W.Bounds.Top - 4) : new Point(d.Cell.Right - 10, d.Cell.Y + 10);
            double x = Math.Min(ActualWidth - w - 6, anchor.X), y = Math.Max(6, Math.Min(ActualHeight - h - 6, anchor.Y));
            dc.DrawRoundedRectangle(Pal.B("#1b1922", 0.97), new Pen(d.Sector.Stripe, 1.5), new Rect(x, y, w, h), 8, 8);
            dc.DrawText(title, new Point(x + 12, y + 8));
            dc.DrawText(line, new Point(x + 12, y + 27));
            dc.DrawText(extra, new Point(x + 12, y + 43));
        }
    }
}
