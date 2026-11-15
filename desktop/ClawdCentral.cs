// Central dos Clawds: um painel para as contas do Claude Desktop nesta máquina.
//
//   - lista as instâncias (os perfis do Claude Profile Switcher), mostra quais estão abertas
//     e quantas sessões têm, e abre, fecha ou cria uma
//   - escolhe as cores do tema do Claude de cada conta (o mod clawd-companheiro aplica)
//   - escolhe as cores e a skin do Clawd de cada conta na área de trabalho
//
// Tudo fica em ~/.claude/clawd/config.json, que o mod e os Clawds leem sozinhos: mudou
// aqui, muda lá em alguns segundos. Compilar: build.ps1.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using IOPath = System.IO.Path;

namespace ClawdDesktop
{
    // ------------------------------------------------------------- desenhos --

    // Um Clawd parado, com a skin e as cores dadas.
    sealed class ClawdView : FrameworkElement
    {
        public Skin Skin;
        public Dictionary<char, Brush> Palette;
        public bool Blink;
        readonly int p;
        readonly Pix grid = new Pix();

        public ClawdView(int scale, Skin skin, Dictionary<char, Brush> palette)
        {
            p = scale;
            Skin = skin;
            Palette = palette;
            Width = 30 * scale;
            Height = 26 * scale;
            ClipToBounds = true;
            SnapsToDevicePixels = true;
            RenderOptions.SetEdgeMode(this, EdgeMode.Aliased);
        }

        protected override void OnRender(DrawingContext dc)
        {
            grid.Clear();
            Body.DrawStanding(grid, Skin, null, Blink);
            double cx = Math.Round(Width / 2);
            dc.DrawRectangle(Palette['z'], null, new Rect(cx - 7 * p, Height - 2 * p, 14 * p, p));
            Body.Blit(dc, grid, Palette, cx - Pix.OX * p, Height - p - Pix.OY * p, p);
        }
    }

    // Uma janelinha do Claude com as cores do tema, no modo claro ou escuro.
    sealed class ThemePreview : FrameworkElement
    {
        readonly AccountCfg a;
        readonly bool dark;

        public ThemePreview(AccountCfg account, bool darkMode)
        {
            a = account;
            dark = darkMode;
            Width = 236;
            Height = 138;
            SnapsToDevicePixels = true;
        }

        static void Bar(DrawingContext dc, Brush b, double x, double y, double w, double h)
        {
            dc.DrawRoundedRectangle(b, null, new Rect(x, y, w, h), h / 2, h / 2);
        }

        protected override void OnRender(DrawingContext dc)
        {
            var s = Tint.Surfaces(a.Surface, dark);
            Brush s0 = Pal.B(s["surface-0"], 1), s1 = Pal.B(s["surface-1"], 1), s2 = Pal.B(s["surface-2"], 1);
            Brush tp = Pal.B(s["text-primary"], 1), ts = Pal.B(s["text-secondary"], 1), tm = Pal.B(s["text-muted"], 1);
            Brush accent = Pal.B(a.Accent, 1), line = Pal.B(s["text-muted"], 0.25);
            double w = Width, h = Height;
            dc.PushClip(new RectangleGeometry(new Rect(0, 0, w, h), 8, 8));
            dc.DrawRectangle(dark ? s1 : s2, null, new Rect(0, 0, w, h));
            // barra lateral com as sessões
            dc.DrawRectangle(s0, null, new Rect(0, 0, 62, h));
            Bar(dc, tp, 10, 12, 30, 5);
            dc.DrawRoundedRectangle(Pal.B(a.Accent, 0.22), null, new Rect(6, 26, 50, 14), 4, 4);
            Bar(dc, accent, 11, 31, 26, 4);
            Bar(dc, tm, 11, 49, 38, 4);
            Bar(dc, tm, 11, 63, 30, 4);
            Bar(dc, tm, 11, 77, 34, 4);
            // conversa: a mensagem da pessoa e a resposta
            dc.DrawRoundedRectangle(dark ? s2 : s0, null, new Rect(124, 12, 100, 24), 8, 8);
            Bar(dc, ts, 134, 19, 78, 4);
            Bar(dc, ts, 134, 27, 50, 4);
            Bar(dc, tp, 74, 48, 130, 4);
            Bar(dc, tp, 74, 58, 144, 4);
            Bar(dc, tp, 74, 68, 96, 4);
            // os avisos do tema
            double dx = 74;
            foreach (string c in new[] { a.Success, a.Warning, a.Danger, a.Pro })
            {
                dc.DrawRoundedRectangle(Pal.B(c, 0.2), null, new Rect(dx, 82, 30, 12), 6, 6);
                dc.DrawEllipse(Pal.B(c, 1), null, new Point(dx + 8, 88), 3, 3);
                Bar(dc, Pal.B(c, 1), dx + 14, 86, 11, 4);
                dx += 36;
            }
            // a caixa de mensagem com o botão de enviar
            dc.DrawRoundedRectangle(dark ? s2 : Brushes.White, new Pen(line, 1), new Rect(72.5, 104.5, 153, 24), 8, 8);
            Bar(dc, tm, 82, 115, 60, 4);
            dc.DrawEllipse(accent, null, new Point(212, 116.5), 8, 8);
            dc.Pop();
            dc.DrawRoundedRectangle(null, new Pen(line, 1), new Rect(0.5, 0.5, w - 1, h - 1), 8, 8);
        }
    }

    // ---------------------------------------------------------------- estilo --

    static class Ui
    {
        public static readonly Brush Bg = Pal.B("#121016", 1), Side = Pal.B("#18151e", 1), Card = Pal.B("#1f1b27", 1);
        public static readonly Brush Soft = Pal.B("#2a2534", 1), Line = Pal.B("#352f42", 1);
        public static readonly Brush Text = Pal.B("#f2f0f5", 1), Dim = Pal.B("#a9a3b6", 1), Good = Pal.B("#5ee08a", 1), Off = Pal.B("#6b657a", 1);
        public static readonly FontFamily Font = new FontFamily("Segoe UI"), Mono = new FontFamily("Consolas");

        public static TextBlock T(string text, double size, Brush color, bool bold)
        {
            var t = new TextBlock();
            t.Text = text;
            t.FontFamily = Font;
            t.FontSize = size;
            t.Foreground = color;
            t.FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal;
            t.TextWrapping = TextWrapping.Wrap;
            t.VerticalAlignment = VerticalAlignment.Center;
            return t;
        }

        // Preto ou branco, o que der para ler em cima daquela cor.
        public static Brush On(string hex)
        {
            Color c = Tint.Parse(hex, "#888888");
            return 0.299 * c.R + 0.587 * c.G + 0.114 * c.B > 150 ? Pal.B("#16131c", 1) : Brushes.White;
        }

        public static Border Button(string text, string accent, Action click)
        {
            var b = new Border();
            b.CornerRadius = new CornerRadius(6);
            b.Padding = new Thickness(12, 6, 12, 7);
            b.Margin = new Thickness(0, 0, 8, 0);
            b.HorizontalAlignment = HorizontalAlignment.Left;
            b.Background = accent != null ? Pal.B(accent, 1) : Soft;
            b.Cursor = Cursors.Hand;
            b.VerticalAlignment = VerticalAlignment.Center;
            var t = T(text, 12.5, accent != null ? On(accent) : Text, accent != null);
            t.TextWrapping = TextWrapping.NoWrap;
            b.Child = t;
            b.MouseEnter += delegate { b.Opacity = 0.82; };
            b.MouseLeave += delegate { b.Opacity = 1; };
            b.MouseLeftButtonUp += delegate(object s, MouseButtonEventArgs e) { e.Handled = true; click(); };
            return b;
        }

        // Um botão de escolha: fica com a borda da cor da conta quando é o escolhido.
        public static Border Chip(UIElement content, bool selected, string accent, Action click)
        {
            var b = new Border();
            b.CornerRadius = new CornerRadius(7);
            b.Padding = new Thickness(9, 5, 9, 6);
            b.Margin = new Thickness(0, 0, 6, 6);
            b.Background = selected ? Pal.B(accent, 0.2) : Soft;
            b.BorderThickness = new Thickness(2);
            b.BorderBrush = selected ? Pal.B(accent, 1) : Brushes.Transparent;
            b.Cursor = Cursors.Hand;
            b.Child = content;
            b.MouseEnter += delegate { if (!selected) b.BorderBrush = Line; };
            b.MouseLeave += delegate { if (!selected) b.BorderBrush = Brushes.Transparent; };
            b.MouseLeftButtonUp += delegate(object s, MouseButtonEventArgs e) { e.Handled = true; click(); };
            return b;
        }

        public static Border Section(string title, string note, UIElement body)
        {
            var sp = new StackPanel();
            sp.Children.Add(T(title, 15, Text, true));
            if (note != null)
            {
                var n = T(note, 12, Dim, false);
                n.Margin = new Thickness(0, 3, 0, 0);
                sp.Children.Add(n);
            }
            var holder = new Border { Margin = new Thickness(0, 12, 0, 0), Child = body };
            sp.Children.Add(holder);
            var card = new Border();
            card.Background = Card;
            card.CornerRadius = new CornerRadius(10);
            card.Padding = new Thickness(18, 15, 18, 16);
            card.Margin = new Thickness(0, 0, 0, 14);
            card.Child = sp;
            return card;
        }

        public static CheckBox Check(string text, bool value, Action<bool> changed)
        {
            var c = new CheckBox();
            c.Content = T(text, 13, Text, false);
            c.IsChecked = value;
            c.Margin = new Thickness(0, 2, 0, 6);
            c.Cursor = Cursors.Hand;
            c.VerticalContentAlignment = VerticalAlignment.Center;
            c.Click += delegate { changed(c.IsChecked == true); };
            return c;
        }

        public static FrameworkElement Gap(double h) { return new Border { Height = h }; }
    }

    // ---------------------------------------------------------------- janela --

    sealed class Central : Window
    {
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        // Temas prontos: nome, principal, fundo, aviso, sucesso, erro, pro.
        static readonly string[][] Presets =
        {
            new[] { "Claude", "#d97757", "#c8643f", "#c9801a", "#2f8a5b", "#c23b32", "#7a5cd6" },
            new[] { "Oceano", "#1094ab", "#1094ab", "#fcb421", "#2f8a5b", "#c23b32", "#3f6db3" },
            new[] { "Violeta", "#8b30e6", "#8b30e6", "#c9801a", "#3d8b5f", "#c23a4b", "#d03fe0" },
            new[] { "Floresta", "#2f9e6b", "#2f9e6b", "#d9a21b", "#3d8b5f", "#c2412f", "#3f6db3" },
            new[] { "Rosa", "#e0559a", "#e0559a", "#d9901b", "#3d8b5f", "#c23a4b", "#8a5cf6" },
            new[] { "Âmbar", "#c9801a", "#c9801a", "#e0a020", "#3d8b5f", "#c2412f", "#3f6db3" },
            new[] { "Vinho", "#a3324a", "#a3324a", "#c9801a", "#3d8b5f", "#d0453a", "#8a5cf6" },
            new[] { "Céu", "#3f7fe0", "#3f7fe0", "#d9a21b", "#2f8a5b", "#c23b32", "#8a5cf6" },
            new[] { "Grafite", "#6b7280", "#7a7f8a", "#c9801a", "#3d8b5f", "#c23b32", "#3f6db3" },
        };

        // Os temas feitos à mão dentro do mod, para o botão "Original" de cada conta.
        static readonly Dictionary<string, string[]> Originals = new Dictionary<string, string[]>
        {
            { "usp", new[] { "Original USP", "#1094ab", "#1094ab", "#fcb421", "#2f8a5b", "#c23b32", "#3f6db3" } },
            { "pessoal", new[] { "Original Pessoal", "#8b30e6", "#8b30e6", "#c9801a", "#3d8b5f", "#c23a4b", "#d03fe0" } },
        };

        Cfg cfg;
        List<Skin> skins;
        AccountCfg sel;
        Dictionary<string, int> running = new Dictionary<string, int>();
        Dictionary<string, int> active = new Dictionary<string, int>();
        bool overlayOn;
        string statusSig = "", profileSig = "";
        DateTime stamp;
        Popup picker;
        bool busy;

        readonly StackPanel accountList = new StackPanel();
        readonly StackPanel globalBox = new StackPanel();
        readonly ScrollViewer detail = new ScrollViewer();
        readonly DispatcherTimer saveTimer = new DispatcherTimer();
        readonly List<ClawdView> blinkers = new List<ClawdView>();
        readonly string appDir = IOPath.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);

        public Central()
        {
            Title = "Central dos Clawds";
            Width = 1040;
            Height = 740;
            MinWidth = 860;
            MinHeight = 520;
            Background = Ui.Bg;
            FontFamily = Ui.Font;
            UseLayoutRounding = true;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
            SourceInitialized += delegate
            {
                int on = 1;
                try { DwmSetWindowAttribute(new WindowInteropHelper(this).Handle, 20, ref on, 4); } catch { }
            };

            Skin.WriteGuide(Cfg.SkinsDir);
            skins = Skin.LoadAll(Cfg.SkinsDir);
            cfg = Cfg.Load();
            profileSig = ProfileSig();
            sel = cfg.Accounts.FirstOrDefault();
            // Grava já: é este arquivo que liga o mod e os Clawds às escolhas daqui.
            SaveNow();

            var root = new Grid();
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(270) });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var side = new DockPanel { Background = Ui.Side, LastChildFill = true };
            var head = new StackPanel { Margin = new Thickness(18, 18, 18, 10) };
            head.Children.Add(Ui.T("Central dos Clawds", 18, Ui.Text, true));
            var sub = Ui.T("Contas do Claude, temas e Clawds", 12, Ui.Dim, false);
            sub.Margin = new Thickness(0, 2, 0, 0);
            head.Children.Add(sub);
            DockPanel.SetDock(head, Dock.Top);
            side.Children.Add(head);
            globalBox.Margin = new Thickness(18, 10, 18, 16);
            DockPanel.SetDock(globalBox, Dock.Bottom);
            side.Children.Add(globalBox);
            var listScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = accountList };
            accountList.Margin = new Thickness(10, 4, 10, 4);
            side.Children.Add(listScroll);
            root.Children.Add(side);

            detail.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            Grid.SetColumn(detail, 1);
            root.Children.Add(detail);
            Content = root;

            saveTimer.Interval = TimeSpan.FromMilliseconds(300);
            saveTimer.Tick += delegate { saveTimer.Stop(); SaveNow(); };

            var poll = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(2500) };
            poll.Tick += delegate { RefreshStatus(); };
            poll.Start();
            var blink = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(3200) };
            blink.Tick += delegate { BlinkAll(); };
            blink.Start();

            RenderAll();
            RefreshStatus();
            Closing += delegate { if (saveTimer.IsEnabled) SaveNow(); };
        }

        // ------------------------------------------------------------ estado --

        static string ProfileSig() { return string.Join("|", Profiles.List().Select(p => p.Id + "=" + p.Label).ToArray()); }

        void SaveNow()
        {
            try { cfg.Save(); }
            catch (Exception e) { MessageBox.Show(this, "Não consegui gravar a config:\n" + e.Message, Title); }
            stamp = Cfg.Stamp();
        }

        // Algo mudou: grava logo depois e redesenha.
        void Changed(bool redraw)
        {
            saveTimer.Stop();
            saveTimer.Start();
            if (redraw) RenderAll();
        }

        void Reload()
        {
            string key = sel != null ? sel.Key : null;
            int before = cfg.Accounts.Count;
            cfg = Cfg.Load();
            skins = Skin.LoadAll(Cfg.SkinsDir);
            sel = cfg.Accounts.FirstOrDefault(a => a.Key == key) ?? cfg.Accounts.LastOrDefault();
            // conta nova (ou apagada) no Switcher: a config acompanha
            if (cfg.Accounts.Count != before) SaveNow(); else stamp = Cfg.Stamp();
        }

        void RefreshStatus()
        {
            if (busy) return;
            busy = true;
            ThreadPool.QueueUserWorkItem(delegate
            {
                var run = new Dictionary<string, int>();
                var act = new Dictionary<string, int>();
                bool overlay = false;
                string sig = "";
                try
                {
                    run = Profiles.Running();
                    act = CountSessions();
                    overlay = Process.GetProcessesByName("ClawdDesktop").Length > 0;
                    sig = ProfileSig();
                }
                catch { }
                Dispatcher.BeginInvoke(new Action(delegate { busy = false; ApplyStatus(run, act, overlay, sig); }));
            });
        }

        static Dictionary<string, int> CountSessions()
        {
            var n = new Dictionary<string, int>();
            string dir = IOPath.Combine(Cfg.Dir, "sessions");
            if (!Directory.Exists(dir)) return n;
            double now = (DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds;
            foreach (string f in Directory.GetFiles(dir, "*.json"))
            {
                if (File.GetLastWriteTimeUtc(f) < DateTime.UtcNow.AddMinutes(-5)) continue;
                var d = Json.Read(f);
                if (d == null || d.ContainsKey("endedAt")) continue;
                double updated;
                try { updated = Convert.ToDouble(d["updatedAt"], System.Globalization.CultureInfo.InvariantCulture); } catch { continue; }
                if (now - updated > 180000) continue;
                string acc = Json.Str(d, "account", "");
                n[acc] = (n.ContainsKey(acc) ? n[acc] : 0) + 1;
            }
            return n;
        }

        void ApplyStatus(Dictionary<string, int> run, Dictionary<string, int> act, bool overlay, string sig)
        {
            running = run;
            active = act;
            overlayOn = overlay;
            bool reload = false;
            if (sig.Length > 0 && sig != profileSig) { profileSig = sig; reload = true; }
            if (!saveTimer.IsEnabled && Cfg.Stamp() != stamp) reload = true;
            string s = string.Join(",", run.Keys.OrderBy(x => x).ToArray()) + "/" +
                       string.Join(",", act.OrderBy(x => x.Key).Select(x => x.Key + x.Value).ToArray()) + "/" + overlay;
            bool typing = Keyboard.FocusedElement is TextBox;
            bool picking = picker != null && picker.IsOpen;
            if (typing || picking) return;   // não mexe na tela embaixo de quem está escolhendo
            if (reload) Reload();
            if (reload || s != statusSig) { statusSig = s; RenderAll(); }
        }

        bool IsRunning(AccountCfg a) { return running.ContainsKey(a.Profile); }
        int ActiveOf(AccountCfg a) { int n; return active.TryGetValue(a.Key, out n) ? n : 0; }

        string StatusText(AccountCfg a)
        {
            int n = ActiveOf(a);
            string sessions = n == 0 ? "" : n == 1 ? " · 1 sessão ativa" : " · " + n + " sessões ativas";
            return (IsRunning(a) ? "aberto" : "fechado") + sessions;
        }

        void BlinkAll()
        {
            foreach (var v in blinkers) { v.Blink = true; v.InvalidateVisual(); }
            var back = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(140) };
            back.Tick += delegate
            {
                back.Stop();
                foreach (var v in blinkers) { v.Blink = false; v.InvalidateVisual(); }
            };
            back.Start();
        }

        ClawdView View(int scale, AccountCfg a, string skinId)
        {
            var v = new ClawdView(scale, Skin.Find(skins, skinId), Tint.Palette(a.Body, a.Detail));
            blinkers.Add(v);
            return v;
        }

        // ------------------------------------------------------------ desenho --

        void RenderAll()
        {
            blinkers.Clear();
            BuildSidebar();
            detail.Content = sel != null ? BuildDetail(sel) : Ui.T("Nenhuma conta do Claude encontrada.", 14, Ui.Dim, false);
        }

        void BuildSidebar()
        {
            accountList.Children.Clear();
            foreach (var acc in cfg.Accounts)
            {
                var a = acc;
                bool on = a == sel;
                var row = new Grid();
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                var v = View(2, a, a.Skin);
                v.Opacity = a.ShowClawd ? 1 : 0.35;
                row.Children.Add(v);
                var txt = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
                txt.Children.Add(Ui.T(a.Name, 14, Ui.Text, true));
                var st = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 0) };
                st.Children.Add(new Border { Width = 7, Height = 7, CornerRadius = new CornerRadius(4), Background = IsRunning(a) ? Ui.Good : Ui.Off, Margin = new Thickness(0, 1, 6, 0), VerticalAlignment = VerticalAlignment.Center });
                st.Children.Add(Ui.T(StatusText(a), 11.5, Ui.Dim, false));
                txt.Children.Add(st);
                Grid.SetColumn(txt, 1);
                row.Children.Add(txt);

                var card = new Border();
                card.CornerRadius = new CornerRadius(9);
                card.Padding = new Thickness(8, 8, 10, 8);
                card.Margin = new Thickness(0, 0, 0, 6);
                card.Background = on ? Ui.Card : Brushes.Transparent;
                card.BorderThickness = new Thickness(3, 0, 0, 0);
                card.BorderBrush = on ? Pal.B(a.Accent, 1) : Brushes.Transparent;
                card.Cursor = Cursors.Hand;
                card.Child = row;
                card.MouseEnter += delegate { if (a != sel) card.Background = Ui.Soft; };
                card.MouseLeave += delegate { if (a != sel) card.Background = Brushes.Transparent; };
                card.MouseLeftButtonUp += delegate { sel = a; RenderAll(); detail.ScrollToTop(); };
                accountList.Children.Add(card);
            }

            var add = new WrapPanel { Margin = new Thickness(2, 6, 0, 0) };
            if (Profiles.HasSwitcher)
            {
                var plus = Ui.Button("+ Nova conta", null, AddAccount);
                plus.Margin = new Thickness(0, 0, 8, 6);
                add.Children.Add(plus);
                var sw = Ui.Button("Renomear ou apagar…", null, delegate { Try(Profiles.OpenSwitcher); });
                sw.ToolTip = "Abre o Claude Profile Switcher, que cuida dos nomes, atalhos e pastas dos perfis.";
                add.Children.Add(sw);
            }
            else
            {
                add.Children.Add(Ui.T("Para ter mais de uma conta, instale o Claude Profile Switcher.", 12, Ui.Dim, false));
            }
            accountList.Children.Add(add);

            // os Clawds da área de trabalho, para todas as contas
            globalBox.Children.Clear();
            globalBox.Children.Add(new Border { Height = 1, Background = Ui.Line, Margin = new Thickness(0, 0, 0, 12) });
            var top = new DockPanel();
            var toggle = Ui.Button(overlayOn ? "Desligar" : "Ligar", overlayOn ? null : "#5ee08a", ToggleOverlay);
            toggle.Margin = new Thickness(0);
            DockPanel.SetDock(toggle, Dock.Right);
            top.Children.Add(toggle);
            var title = new StackPanel();
            title.Children.Add(Ui.T("Clawds no desktop", 13.5, Ui.Text, true));
            title.Children.Add(Ui.T(overlayOn ? "ligados" : "desligados", 11.5, overlayOn ? Ui.Good : Ui.Dim, false));
            top.Children.Add(title);
            globalBox.Children.Add(top);
            var corner = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
            var cl = Ui.T("Canto", 12.5, Ui.Dim, false);
            cl.Margin = new Thickness(0, 0, 10, 6);
            corner.Children.Add(cl);
            foreach (string c in new[] { "esquerdo", "direito" })
            {
                string side = c;
                corner.Children.Add(Ui.Chip(Ui.T(side, 12.5, Ui.Text, false), cfg.Corner == side, "#a9a3b6", delegate { cfg.Corner = side; Changed(true); }));
            }
            globalBox.Children.Add(corner);
            globalBox.Children.Add(Ui.Check("Brincadeiras entre eles de tempo em tempo", cfg.Events, delegate(bool v) { cfg.Events = v; Changed(false); }));
        }

        UIElement BuildDetail(AccountCfg a)
        {
            var sp = new StackPanel { Margin = new Thickness(26, 22, 26, 26), MaxWidth = 760, HorizontalAlignment = HorizontalAlignment.Left };

            // ---- a instância
            var head = new DockPanel { Margin = new Thickness(0, 0, 0, 16) };
            var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            actions.Children.Add(Ui.Button(IsRunning(a) ? "Mostrar o Claude" : "Abrir o Claude", a.Accent, delegate { Try(delegate { Profiles.Launch(a.Profile, a.Name); }); }));
            if (IsRunning(a)) actions.Children.Add(Ui.Button("Fechar", null, delegate { CloseInstance(a); }));
            var folder = Ui.Button("Pasta", null, delegate { OpenFolder(a); });
            folder.Margin = new Thickness(0);
            folder.ToolTip = "Abre a pasta de dados deste perfil no Explorador.";
            actions.Children.Add(folder);
            DockPanel.SetDock(actions, Dock.Right);
            head.Children.Add(actions);
            var name = new StackPanel();
            name.Children.Add(Ui.T("Claude " + a.Name, 24, Ui.Text, true));
            var st = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 0) };
            st.Children.Add(new Border { Width = 8, Height = 8, CornerRadius = new CornerRadius(4), Background = IsRunning(a) ? Ui.Good : Ui.Off, Margin = new Thickness(0, 1, 7, 0), VerticalAlignment = VerticalAlignment.Center });
            st.Children.Add(Ui.T(StatusText(a) + "  ·  perfil " + a.Profile, 12.5, Ui.Dim, false));
            name.Children.Add(st);
            head.Children.Add(name);
            sp.Children.Add(head);

            // ---- de quem é a conta
            var who = new StackPanel();
            var box = new TextBox();
            box.Text = a.Emails;
            box.FontFamily = Ui.Mono;
            box.FontSize = 13;
            box.Padding = new Thickness(8, 6, 8, 6);
            box.Background = Ui.Soft;
            box.Foreground = Ui.Text;
            box.CaretBrush = Ui.Text;
            box.BorderBrush = Ui.Line;
            box.MaxWidth = 420;
            box.HorizontalAlignment = HorizontalAlignment.Left;
            box.MinWidth = 320;
            var warn = Ui.T("", 12, Pal.B("#f5a524", 1), false);
            warn.Margin = new Thickness(0, 8, 0, 0);
            Action check = delegate
            {
                string w = EmailWarning(a);
                warn.Text = w ?? "";
                warn.Visibility = w == null ? Visibility.Collapsed : Visibility.Visible;
            };
            box.TextChanged += delegate { a.Emails = box.Text.Trim(); check(); Changed(false); };
            box.LostKeyboardFocus += delegate { RefreshStatus(); };
            who.Children.Add(box);
            who.Children.Add(warn);
            check();
            sp.Children.Add(Ui.Section("E-mails desta conta",
                "É por aqui que cada sessão descobre de qual conta é. Escreva domínios (usp.br) ou e-mails inteiros, separados por vírgula. Um * pega todos os outros.", who));

            sp.Children.Add(Ui.Section("Tema do Claude", "As cores do app nesta conta. Sessões abertas trocam em alguns segundos.", BuildTheme(a)));
            sp.Children.Add(Ui.Section("Clawd na área de trabalho", "O mascote desta conta em cima da barra de tarefas.", BuildClawd(a)));
            return sp;
        }

        string EmailWarning(AccountCfg a)
        {
            if (a.Emails.Trim().Length == 0) return "Sem e-mail, nenhuma sessão é reconhecida como desta conta: ela fica sem tema e sem atividade no Clawd.";
            if (a.Emails.Split(',', ';', ' ').Contains("*") && cfg.Accounts.Any(x => x != a && x.Emails.Split(',', ';', ' ').Contains("*")))
                return "Outra conta também tem *. Só a primeira da lista fica com os e-mails que sobram.";
            return null;
        }

        // ---- tema

        void ApplyPreset(AccountCfg a, string[] p, bool original)
        {
            a.Accent = p[1]; a.Surface = p[2]; a.Warning = p[3]; a.Success = p[4]; a.Danger = p[5]; a.Pro = p[6];
            a.ThemeDefault = original;
            a.ThemeOn = true;
            Changed(true);
        }

        static bool Matches(AccountCfg a, string[] p)
        {
            return a.Accent == p[1] && a.Surface == p[2] && a.Warning == p[3] && a.Success == p[4] && a.Danger == p[5] && a.Pro == p[6];
        }

        UIElement PresetChip(AccountCfg a, string[] p, bool original)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(new Border { Width = 12, Height = 12, CornerRadius = new CornerRadius(6), Background = Pal.B(p[1], 1), Margin = new Thickness(0, 1, 6, 0), VerticalAlignment = VerticalAlignment.Center });
            row.Children.Add(Ui.T(p[0], 12.5, Ui.Text, false));
            bool on = a.ThemeOn && Matches(a, p) && a.ThemeDefault == original;
            return Ui.Chip(row, on, a.Accent, delegate { ApplyPreset(a, p, original); });
        }

        UIElement BuildTheme(AccountCfg a)
        {
            var sp = new StackPanel();
            sp.Children.Add(Ui.Check("Usar um tema próprio nesta conta", a.ThemeOn, delegate(bool v) { a.ThemeOn = v; Changed(true); }));
            if (!a.ThemeOn)
            {
                sp.Children.Add(Ui.T("Esta conta fica com as cores normais do Claude.", 12.5, Ui.Dim, false));
                return sp;
            }

            var presets = new WrapPanel { Margin = new Thickness(0, 6, 0, 8) };
            string[] orig;
            if (Originals.TryGetValue(a.Key, out orig)) presets.Children.Add(PresetChip(a, orig, true));
            foreach (var p in Presets) presets.Children.Add(PresetChip(a, p, false));
            sp.Children.Add(presets);

            var colors = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
            colors.Children.Add(ColorCell("Principal", "Botões, links e destaques.", a.Accent, delegate(string v) { a.Accent = v; a.ThemeDefault = false; Changed(true); }));
            colors.Children.Add(ColorCell("Fundo", "O tom dos fundos e dos textos.", a.Surface, delegate(string v) { a.Surface = v; a.ThemeDefault = false; Changed(true); }));
            colors.Children.Add(ColorCell("Sucesso", null, a.Success, delegate(string v) { a.Success = v; a.ThemeDefault = false; Changed(true); }));
            colors.Children.Add(ColorCell("Aviso", null, a.Warning, delegate(string v) { a.Warning = v; a.ThemeDefault = false; Changed(true); }));
            colors.Children.Add(ColorCell("Erro", null, a.Danger, delegate(string v) { a.Danger = v; a.ThemeDefault = false; Changed(true); }));
            colors.Children.Add(ColorCell("Pro", "Selos e recursos extras.", a.Pro, delegate(string v) { a.Pro = v; a.ThemeDefault = false; Changed(true); }));
            sp.Children.Add(colors);

            var previews = new WrapPanel();
            foreach (bool dark in new[] { false, true })
            {
                var one = new StackPanel { Margin = new Thickness(0, 0, 16, 0) };
                one.Children.Add(new ThemePreview(a, dark));
                var cap = Ui.T(dark ? "Escuro" : "Claro", 11.5, Ui.Dim, false);
                cap.Margin = new Thickness(2, 5, 0, 0);
                one.Children.Add(cap);
                previews.Children.Add(one);
            }
            sp.Children.Add(previews);
            if (a.ThemeDefault && Originals.ContainsKey(a.Key))
            {
                var n = Ui.T("Este é o tema original da conta, ajustado à mão; a prévia é aproximada.", 11.5, Ui.Dim, false);
                n.Margin = new Thickness(0, 10, 0, 0);
                sp.Children.Add(n);
            }
            return sp;
        }

        // ---- Clawd

        UIElement BuildClawd(AccountCfg a)
        {
            var sp = new StackPanel();
            sp.Children.Add(Ui.Check("Mostrar o Clawd desta conta", a.ShowClawd, delegate(bool v) { a.ShowClawd = v; Changed(true); }));
            if (!a.ShowClawd) return sp;

            var top = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 14) };
            var stage = new Border();
            stage.Background = Pal.B(Tint.Surfaces(a.Body, true)["surface-1"], 1);
            stage.CornerRadius = new CornerRadius(10);
            stage.Padding = new Thickness(14, 10, 14, 8);
            var both = new StackPanel { Orientation = Orientation.Horizontal };
            var big = View(5, a, a.Skin);
            big.VerticalAlignment = VerticalAlignment.Bottom;
            both.Children.Add(big);
            var pup = View(3, a, string.IsNullOrEmpty(a.PupSkin) ? a.Skin : a.PupSkin);
            pup.VerticalAlignment = VerticalAlignment.Bottom;
            pup.ToolTip = "Um filhote aparece para cada sessão extra aberta na conta.";
            both.Children.Add(pup);
            stage.Child = both;
            top.Children.Add(stage);

            var right = new StackPanel { Margin = new Thickness(18, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            var colors = new WrapPanel();
            colors.Children.Add(ColorCell("Corpo", "Sombra, brilho e contorno saem desta cor.", a.Body, delegate(string v) { a.Body = v; Changed(true); }));
            colors.Children.Add(ColorCell("Detalhe", "Acessórios da skin e enfeites.", a.Detail, delegate(string v) { a.Detail = v; Changed(true); }));
            right.Children.Add(colors);
            var same = Ui.Button("Usar a cor do tema", null, delegate { a.Body = a.Accent; a.Detail = a.Warning; Changed(true); });
            same.HorizontalAlignment = HorizontalAlignment.Left;
            same.Margin = new Thickness(0, 4, 0, 10);
            right.Children.Add(same);
            var hat = Ui.Check("Chapéu do modelo em uso", a.Hat, delegate(bool v) { a.Hat = v; Changed(false); });
            hat.ToolTip = "Mago para o Fable, coroa para o Opus, fones para o Sonnet e broto para o Haiku.";
            right.Children.Add(hat);
            top.Children.Add(right);
            sp.Children.Add(top);

            sp.Children.Add(Ui.T("Skin", 13, Ui.Text, true));
            var grid = new WrapPanel { Margin = new Thickness(0, 8, 0, 8) };
            foreach (var sk in skins)
            {
                var skin = sk;
                var cell = new StackPanel();
                cell.Children.Add(View(2, a, skin.Id));
                var cap = Ui.T(skin.Name, 11.5, Ui.Text, false);
                cap.HorizontalAlignment = HorizontalAlignment.Center;
                cap.TextWrapping = TextWrapping.NoWrap;
                cap.Margin = new Thickness(0, 2, 0, 0);
                cell.Children.Add(cap);
                var chip = Ui.Chip(cell, a.Skin == skin.Id, a.Body, delegate { a.Skin = skin.Id; Changed(true); });
                chip.Padding = new Thickness(5, 4, 5, 5);
                if (skin.Custom) chip.ToolTip = "Skin da pasta de skins (" + skin.Id + ".json)";
                grid.Children.Add(chip);
            }
            sp.Children.Add(grid);

            sp.Children.Add(Ui.T("Skin dos filhotes", 13, Ui.Text, true));
            var pups = new WrapPanel { Margin = new Thickness(0, 8, 0, 6) };
            pups.Children.Add(Ui.Chip(Ui.T("Igual à do grande", 12.5, Ui.Text, false), string.IsNullOrEmpty(a.PupSkin), a.Body, delegate { a.PupSkin = ""; Changed(true); }));
            foreach (var sk in skins)
            {
                var skin = sk;
                pups.Children.Add(Ui.Chip(Ui.T(skin.Name, 12.5, Ui.Text, false), a.PupSkin == skin.Id, a.Body, delegate { a.PupSkin = skin.Id; Changed(true); }));
            }
            sp.Children.Add(pups);

            var more = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
            more.Children.Add(Ui.Button("Pasta de skins", null, delegate { Try(delegate { Process.Start("explorer.exe", "\"" + Cfg.SkinsDir + "\""); }); }));
            more.Children.Add(Ui.Button("Recarregar", null, delegate { skins = Skin.LoadAll(Cfg.SkinsDir); RenderAll(); }));
            var hint = Ui.T("Cada .json na pasta vira uma skin; o LEIA-ME de lá explica o formato.", 11.5, Ui.Dim, false);
            hint.MaxWidth = 360;
            more.Children.Add(hint);
            sp.Children.Add(more);
            return sp;
        }

        // ---- cores

        FrameworkElement ColorCell(string label, string tip, string value, Action<string> set)
        {
            var cell = new StackPanel { Margin = new Thickness(0, 0, 8, 8), Width = 92 };
            var l = Ui.T(label, 12, Ui.Dim, false);
            l.Margin = new Thickness(1, 0, 0, 4);
            cell.Children.Add(l);
            var sw = new Border();
            sw.Height = 30;
            sw.CornerRadius = new CornerRadius(6);
            sw.Background = Pal.B(value, 1);
            sw.BorderBrush = Ui.Line;
            sw.BorderThickness = new Thickness(1);
            sw.Cursor = Cursors.Hand;
            var hex = Ui.T(value, 12, Ui.On(value), false);
            hex.FontFamily = Ui.Mono;
            hex.HorizontalAlignment = HorizontalAlignment.Center;
            sw.Child = hex;
            if (tip != null) sw.ToolTip = tip;
            sw.MouseLeftButtonUp += delegate(object s, MouseButtonEventArgs e) { e.Handled = true; OpenPicker(sw, value, set); };
            cell.Children.Add(sw);
            return cell;
        }

        static string NormHex(string text)
        {
            string t = (text ?? "").Trim().TrimStart('#').ToLowerInvariant();
            if (t.Length == 3) t = "" + t[0] + t[0] + t[1] + t[1] + t[2] + t[2];
            t = "#" + t;
            return Tint.IsHex(t) ? t : null;
        }

        static IEnumerable<string> PickerColors()
        {
            double[] hues = { 0, 14, 28, 42, 55, 90, 140, 165, 189, 210, 232, 268, 295, 330 };
            foreach (double l in new[] { 0.36, 0.5, 0.66 })
                foreach (double h in hues) yield return Tint.Hsl(h, l > 0.6 ? 0.8 : 0.72, l);
            foreach (double l in new[] { 0.08, 0.22, 0.38, 0.55, 0.72, 0.86, 0.96 }) yield return Tint.Hsl(250, 0.06, l);
        }

        void OpenPicker(FrameworkElement target, string current, Action<string> set)
        {
            var pop = new Popup { PlacementTarget = target, Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true, VerticalOffset = 4 };
            picker = pop;
            var box = new StackPanel { Width = 7 * 28 };
            var wrap = new WrapPanel();
            foreach (string c in PickerColors())
            {
                string hex = c;
                var cell = new Border { Width = 24, Height = 24, Margin = new Thickness(2), CornerRadius = new CornerRadius(5), Background = Pal.B(hex, 1), Cursor = Cursors.Hand, ToolTip = hex };
                if (hex == current) { cell.BorderBrush = Brushes.White; cell.BorderThickness = new Thickness(2); }
                cell.MouseLeftButtonUp += delegate(object s, MouseButtonEventArgs e) { e.Handled = true; pop.IsOpen = false; set(hex); };
                wrap.Children.Add(cell);
            }
            box.Children.Add(wrap);

            var row = new DockPanel { Margin = new Thickness(2, 8, 2, 0) };
            var tb = new TextBox { Text = current, FontFamily = Ui.Mono, FontSize = 13, Width = 68, Padding = new Thickness(5, 4, 5, 4), Background = Ui.Soft, Foreground = Ui.Text, CaretBrush = Ui.Text, BorderBrush = Ui.Line, MaxLength = 7 };
            Action apply = delegate
            {
                string v = NormHex(tb.Text);
                if (v == null) { tb.BorderBrush = Pal.B("#e5484d", 1); return; }
                pop.IsOpen = false;
                set(v);
            };
            tb.KeyDown += delegate(object s, KeyEventArgs e) { if (e.Key == Key.Enter) { e.Handled = true; apply(); } };
            row.Children.Add(tb);
            var more = Ui.Button("Mais…", null, delegate
            {
                pop.IsOpen = false;
                var dlg = new System.Windows.Forms.ColorDialog();
                dlg.FullOpen = true;
                Color c = Tint.Parse(current, "#888888");
                dlg.Color = System.Drawing.Color.FromArgb(c.R, c.G, c.B);
                if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK) set(Tint.Hex(Color.FromRgb(dlg.Color.R, dlg.Color.G, dlg.Color.B)));
            });
            more.Margin = new Thickness(0);
            more.ToolTip = "Abre o seletor de cores do Windows.";
            DockPanel.SetDock(more, Dock.Right);
            row.Children.Add(more);
            var ok = Ui.Button("OK", null, apply);
            ok.Margin = new Thickness(6, 0, 6, 0);
            ok.HorizontalAlignment = HorizontalAlignment.Left;
            row.Children.Add(ok);
            box.Children.Add(row);

            pop.Child = new Border { Background = Ui.Card, BorderBrush = Ui.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(9), Padding = new Thickness(9), Child = box };
            pop.IsOpen = true;
        }

        // ------------------------------------------------------------- ações --

        void Try(Action f)
        {
            try { f(); }
            catch (Exception e) { MessageBox.Show(this, e.Message, Title, MessageBoxButton.OK, MessageBoxImage.Warning); }
        }

        void AddAccount()
        {
            if (MessageBox.Show(this,
                    "Vou criar um perfil novo e abrir o Claude nele para você entrar com a outra conta.\n\nEle aparece aqui em seguida, com o nome \"Account N\"; para trocar o nome, use \"Renomear ou apagar…\".",
                    Title, MessageBoxButton.OKCancel, MessageBoxImage.Information) != MessageBoxResult.OK) return;
            Try(Profiles.AddAccount);
        }

        void CloseInstance(AccountCfg a)
        {
            int pid;
            if (!running.TryGetValue(a.Profile, out pid)) return;
            int n = ActiveOf(a);
            string extra = n == 0 ? "" : "\n\n" + (n == 1 ? "Há 1 sessão ativa nele, que será interrompida." : "Há " + n + " sessões ativas nele, que serão interrompidas.");
            if (MessageBox.Show(this, "Fechar o Claude " + a.Name + "?" + extra, Title, MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
            Try(delegate
            {
                var psi = new ProcessStartInfo("taskkill.exe", "/PID " + pid + " /T /F");
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                Process.Start(psi);
            });
            RefreshStatus();
        }

        void OpenFolder(AccountCfg a)
        {
            var p = Profiles.List().FirstOrDefault(x => x.Id == a.Profile);
            if (p != null && Directory.Exists(p.Folder)) Try(delegate { Process.Start("explorer.exe", "\"" + p.Folder + "\""); });
        }

        void ToggleOverlay()
        {
            Try(delegate
            {
                if (overlayOn)
                {
                    foreach (var p in Process.GetProcessesByName("ClawdDesktop")) { try { p.Kill(); } catch { } }
                }
                else Process.Start(new ProcessStartInfo(IOPath.Combine(appDir, "ClawdDesktop.exe")) { UseShellExecute = true });
            });
            overlayOn = !overlayOn;
            statusSig = "";
            RenderAll();
        }
    }

    static class CentralProgram
    {
        [STAThread]
        static void Main()
        {
            bool created;
            using (var mutex = new Mutex(true, "Local\\ClawdCentral", out created))
            {
                if (!created) return;
                var app = new Application();
                app.DispatcherUnhandledException += delegate(object s, DispatcherUnhandledExceptionEventArgs e)
                {
                    try { File.AppendAllText(IOPath.Combine(Cfg.Dir, "central.log"), DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss ") + e.Exception + Environment.NewLine); } catch { }
                    e.Handled = true;
                };
                app.Run(new Central());
            }
        }
    }
}
