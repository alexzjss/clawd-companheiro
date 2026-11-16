// A página Contas: as instâncias do Claude Desktop nesta máquina (os perfis do Claude
// Profile Switcher), quais estão abertas e com quantas sessões, e de cada uma: os e-mails,
// o tema do Claude (o mod aplica) e o Clawd (cores e skins).
//
// Tudo fica em ~/.claude/clawd/config.json, que o mod e os Clawds leem sozinhos: mudou
// aqui, muda lá em alguns segundos.

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
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using IOPath = System.IO.Path;

namespace Clawdesk
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


    // ------------------------------------------------------------------ página --

    sealed class AccountsPage : ContentControl
    {
        // Temas prontos: nome, principal, fundo, aviso, sucesso, erro, pro.
        public static readonly string[][] Presets =
        {
            new[] { "Claude", "#d97757", "#c8643f", "#c9801a", "#2f8a5b", "#c23b32", "#7a5cd6" },
            new[] { L.T("Oceano", "Ocean"), "#1094ab", "#1094ab", "#fcb421", "#2f8a5b", "#c23b32", "#3f6db3" },
            new[] { L.T("Violeta", "Violet"), "#8b30e6", "#8b30e6", "#c9801a", "#3d8b5f", "#c23a4b", "#d03fe0" },
            new[] { L.T("Floresta", "Forest"), "#2f9e6b", "#2f9e6b", "#d9a21b", "#3d8b5f", "#c2412f", "#3f6db3" },
            new[] { L.T("Rosa", "Pink"), "#e0559a", "#e0559a", "#d9901b", "#3d8b5f", "#c23a4b", "#8a5cf6" },
            new[] { L.T("Âmbar", "Amber"), "#c9801a", "#c9801a", "#e0a020", "#3d8b5f", "#c2412f", "#3f6db3" },
            new[] { L.T("Vinho", "Wine"), "#a3324a", "#a3324a", "#c9801a", "#3d8b5f", "#d0453a", "#8a5cf6" },
            new[] { L.T("Céu", "Sky"), "#3f7fe0", "#3f7fe0", "#d9a21b", "#2f8a5b", "#c23b32", "#8a5cf6" },
            new[] { L.T("Grafite", "Graphite"), "#6b7280", "#7a7f8a", "#c9801a", "#3d8b5f", "#c23b32", "#3f6db3" },
        };

        readonly Func<Cfg> cfg;
        readonly Action changed;    // grava daqui a pouco
        readonly bool photo;
        List<Skin> skins;
        string selKey;
        int tab;                    // 0: geral, 1: tema, 2: Clawd
        Dictionary<string, int> running = new Dictionary<string, int>();
        Dictionary<string, int> active = new Dictionary<string, int>();
        string statusSig = "";
        Popup picker;
        readonly List<ClawdView> blinkers = new List<ClawdView>();
        readonly StackPanel root = new StackPanel();

        public AccountsPage(Func<Cfg> config, Action onChanged, bool photo)
        {
            cfg = config;
            changed = onChanged;
            this.photo = photo;
            Focusable = false;
            if (!photo) Skin.WriteGuide(Cfg.SkinsDir);
            skins = photo ? Skin.Builtin() : Skin.LoadAll(Cfg.SkinsDir);
            Content = root;
            var blink = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(3200) };
            blink.Tick += delegate { BlinkAll(); };
            blink.Start();
            Render();
        }

        AccountCfg Sel
        {
            get
            {
                var c = cfg();
                return c.Accounts.FirstOrDefault(a => a.Key == selKey) ?? c.Accounts.FirstOrDefault();
            }
        }

        public bool Busy { get { return Keyboard.FocusedElement is TextBox || (picker != null && picker.IsOpen); } }

        // O status das instâncias, a cada leitura; só redesenha se mudou.
        public void Status(Dictionary<string, int> run, List<SessionView> all, double now)
        {
            var act = new Dictionary<string, int>();
            foreach (var s in all.Where(x => x.IsActive(now))) act[s.Account] = (act.ContainsKey(s.Account) ? act[s.Account] : 0) + 1;
            string sig = string.Join(",", run.Keys.OrderBy(x => x).ToArray()) + "/" + string.Join(",", act.OrderBy(x => x.Key).Select(x => x.Key + x.Value).ToArray());
            running = run;
            active = act;
            if (sig == statusSig || Busy) return;
            statusSig = sig;
            Render();
        }

        // A config foi relida (mudou fora daqui): redesenha.
        public void Reloaded()
        {
            if (Busy) return;
            skins = photo ? Skin.Builtin() : Skin.LoadAll(Cfg.SkinsDir);
            Render();
        }

        public void Select(string key, int toTab)
        {
            selKey = key;
            tab = toTab;
            Render();
        }

        void Changed(bool redraw)
        {
            changed();
            if (redraw) Render();
        }

        bool IsRunning(AccountCfg a) { return running.ContainsKey(a.Profile); }
        int ActiveOf(AccountCfg a) { int n; return active.TryGetValue(a.Key, out n) ? n : 0; }

        string StatusText(AccountCfg a)
        {
            int n = ActiveOf(a);
            string sessions = n == 0 ? "" : n == 1 ? L.T(" · 1 sessão ativa", " · 1 active session") : " · " + n + L.T(" sessões ativas", " active sessions");
            return (IsRunning(a) ? L.T("aberto", "open") : L.T("fechado", "closed")) + sessions;
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

        void Render()
        {
            blinkers.Clear();
            root.Children.Clear();
            var c = cfg();
            var sel = Sel;
            if (sel != null) selKey = sel.Key;
            root.Children.Add(BuildSwitcher(c, sel));
            if (sel == null)
            {
                root.Children.Add(Ui.Section(L.T("Nenhuma conta do Claude encontrada", "No Claude account found"), L.T("Instale o Claude Desktop e entre com a sua conta; ela aparece aqui sozinha.", "Install Claude Desktop and sign in; your account shows up here on its own."), null));
                return;
            }
            root.Children.Add(BuildHeader(sel));
            var tabs = Ui.Segmented(new[] { L.T("Geral", "General"), L.T("Tema do Claude", "Claude theme"), "Clawd" }, tab, delegate(int i) { tab = i; Render(); });
            tabs.Margin = new Thickness(0, 0, 0, 16);
            root.Children.Add(tabs);
            if (tab == 0) BuildGeneral(c, sel);
            else if (tab == 1) root.Children.Add(Ui.Section(L.T("Tema do Claude", "Claude theme"), L.T("As cores do app nesta conta. Sessões abertas trocam em alguns segundos.", "The app colors for this account. Open sessions switch within a few seconds."), BuildTheme(sel)));
            else root.Children.Add(Ui.Section("Clawd", L.T("O mascote desta conta: na barra de tarefas e na Home.", "This account's mascot: on the taskbar and in Home."), BuildClawd(sel)));
        }

        // As contas lado a lado, cada uma com o seu Clawd.
        UIElement BuildSwitcher(Cfg c, AccountCfg sel)
        {
            var wrap = new WrapPanel { Margin = new Thickness(0, 0, 0, 18) };
            foreach (var acc in c.Accounts)
            {
                var a = acc;
                bool on = a == sel;
                var row = new StackPanel { Orientation = Orientation.Horizontal };
                var v = View(2, a, a.Skin);
                v.Opacity = a.ShowClawd ? 1 : 0.35;
                row.Children.Add(v);
                var txt = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 4, 0) };
                txt.Children.Add(Ui.T(a.Name, 14, Ui.Text, true));
                var st = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 0) };
                var dot = Ui.Dot(IsRunning(a) ? Ui.Good : Ui.Off, 7);
                dot.Margin = new Thickness(0, 1, 6, 0);
                st.Children.Add(dot);
                st.Children.Add(Ui.T(StatusText(a), 11.5, Ui.Dim, false));
                txt.Children.Add(st);
                row.Children.Add(txt);

                var card = new Border { CornerRadius = new CornerRadius(12), Padding = new Thickness(10, 8, 14, 8), Margin = new Thickness(0, 0, 10, 10), Cursor = Cursors.Hand, Child = row, MinWidth = 200 };
                card.Background = on ? Ui.CardHi : Ui.Card;
                card.BorderThickness = new Thickness(1.5);
                card.BorderBrush = on ? Pal.B(a.Accent, 1) : Ui.Line;
                card.MouseEnter += delegate { if (!on) card.Background = Ui.CardHi; };
                card.MouseLeave += delegate { if (!on) card.Background = Ui.Card; };
                card.MouseLeftButtonUp += delegate { selKey = a.Key; Render(); };
                wrap.Children.Add(card);
            }
            if (Profiles.HasSwitcher || photo)
            {
                var plus = new Border { CornerRadius = new CornerRadius(12), Padding = new Thickness(18, 8, 18, 8), Margin = new Thickness(0, 0, 10, 10), Cursor = Cursors.Hand, BorderThickness = new Thickness(1.5), BorderBrush = Ui.SoftHi, MinHeight = 64 };
                var pr = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                var ic = Ui.Icon(Ui.IcAdd, 13, Ui.Dim);
                ic.Margin = new Thickness(0, 1, 8, 0);
                pr.Children.Add(ic);
                pr.Children.Add(Ui.T(L.T("Nova conta", "New account"), 13, Ui.Dim, false));
                plus.Child = pr;
                plus.MouseEnter += delegate { plus.Background = Ui.Card; };
                plus.MouseLeave += delegate { plus.Background = Brushes.Transparent; };
                plus.MouseLeftButtonUp += delegate { AddAccount(); };
                plus.ToolTip = L.T("Cria um perfil novo do Claude Desktop pelo Claude Profile Switcher.", "Creates a new Claude Desktop profile through Claude Profile Switcher.");
                wrap.Children.Add(plus);
            }
            return wrap;
        }

        UIElement BuildHeader(AccountCfg a)
        {
            var head = new DockPanel { Margin = new Thickness(0, 0, 0, 14) };
            var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            actions.Children.Add(Ui.Button(Ui.IcOpen, IsRunning(a) ? L.T("Mostrar o Claude", "Show Claude") : L.T("Abrir o Claude", "Open Claude"), a.Accent, delegate { Try(delegate { Profiles.Launch(a.Profile, a.Name); }); }));
            if (IsRunning(a)) actions.Children.Add(Ui.Button(Ui.IcClose, L.T("Fechar", "Close"), null, delegate { CloseInstance(a); }));
            var folder = Ui.Button(Ui.IcFolder, L.T("Pasta", "Folder"), null, delegate { OpenFolder(a); });
            folder.Margin = new Thickness(0);
            folder.ToolTip = L.T("Abre a pasta de dados deste perfil no Explorador.", "Opens this profile's data folder in Explorer.");
            actions.Children.Add(folder);
            DockPanel.SetDock(actions, Dock.Right);
            head.Children.Add(actions);
            var name = new StackPanel();
            name.Children.Add(Ui.T("Claude " + a.Name, 22, Ui.Text, true));
            name.Children.Add(Ui.T(StatusText(a) + L.T("  ·  perfil ", "  ·  profile ") + a.Profile, 12.5, Ui.Dim, false));
            head.Children.Add(name);
            return head;
        }

        void BuildGeneral(Cfg c, AccountCfg a)
        {
            var who = new StackPanel();
            var box = new TextBox();
            box.Text = a.Emails;
            box.FontFamily = Ui.Mono;
            box.FontSize = 13;
            box.Padding = new Thickness(10, 7, 10, 7);
            box.Background = Ui.Soft;
            box.Foreground = Ui.Text;
            box.CaretBrush = Ui.Text;
            box.BorderBrush = Ui.Line;
            box.MaxWidth = 460;
            box.MinWidth = 320;
            box.HorizontalAlignment = HorizontalAlignment.Left;
            var warn = Ui.T("", 12, Ui.Warn, false);
            warn.Margin = new Thickness(0, 8, 0, 0);
            Action check = delegate
            {
                string w = EmailWarning(c, a);
                warn.Text = w ?? "";
                warn.Visibility = w == null ? Visibility.Collapsed : Visibility.Visible;
            };
            box.TextChanged += delegate { a.Emails = box.Text.Trim(); check(); Changed(false); };
            who.Children.Add(box);
            who.Children.Add(warn);
            check();
            root.Children.Add(Ui.Section(L.T("E-mails desta conta", "This account's emails"),
                L.T("É por aqui que cada sessão descobre de qual conta é. Escreva domínios (empresa.com) ou e-mails inteiros, separados por vírgula. Um * pega todos os outros.", "This is how each session finds out which account it belongs to. Enter domains (company.com) or full emails, separated by commas. A * catches everything else."), who));

            var more = new StackPanel();
            more.Children.Add(Ui.Setting(L.T("Clawd desta conta", "This account's Clawd"), L.T("Mostrar o mascote desta conta na barra de tarefas e na Home.", "Show this account's mascot on the taskbar and in Home."), Ui.Toggle(a.ShowClawd, delegate(bool v) { a.ShowClawd = v; Changed(true); })));
            more.Children.Add(Ui.Divider());
            more.Children.Add(Ui.Setting(L.T("Tema próprio no Claude", "Custom Claude theme"), a.ThemeOn ? L.T("Ligado. As cores ficam na aba Tema do Claude.", "On. Colors are in the Claude theme tab.") : L.T("Desligado: o app fica com as cores normais.", "Off: the app keeps its normal colors."), Ui.Toggle(a.ThemeOn, delegate(bool v) { a.ThemeOn = v; Changed(true); })));
            if (Profiles.HasSwitcher || photo)
            {
                more.Children.Add(Ui.Divider());
                var sw = Ui.Button(L.T("Abrir o Switcher…", "Open Switcher…"), null, delegate { Try(Profiles.OpenSwitcher); });
                sw.Margin = new Thickness(0);
                more.Children.Add(Ui.Setting(L.T("Nome, atalhos e pasta do perfil", "Profile name, shortcuts and folder"), L.T("Quem cuida disso é o Claude Profile Switcher: renomear ou apagar a conta é por lá.", "Claude Profile Switcher handles this: rename or delete the account there."), sw));
            }
            root.Children.Add(Ui.Section(null, null, more));
            if (!Profiles.HasSwitcher && !photo)
                root.Children.Add(Ui.Section(L.T("Mais de uma conta", "More than one account"), L.T("Para ter várias contas do Claude Desktop abertas ao mesmo tempo, instale o Claude Profile Switcher. Com ele, cada perfil vira uma conta aqui.", "To have several Claude Desktop accounts open at once, install Claude Profile Switcher. Each profile then becomes an account here."), null));
        }

        static string EmailWarning(Cfg c, AccountCfg a)
        {
            if (a.Emails.Trim().Length == 0) return L.T("Sem e-mail, nenhuma sessão é reconhecida como desta conta: ela fica sem tema e sem atividade no Clawd.", "Without an email, no session is recognized as this account's: it gets no theme and no Clawd activity.");
            if (a.Emails.Split(',', ';', ' ').Contains("*") && c.Accounts.Any(x => x != a && x.Emails.Split(',', ';', ' ').Contains("*")))
                return L.T("Outra conta também tem *. Só a primeira da lista fica com os e-mails que sobram.", "Another account also has *. Only the first one in the list gets the remaining emails.");
            return null;
        }

        // ---- tema

        void ApplyPreset(AccountCfg a, string[] p)
        {
            a.Accent = p[1]; a.Surface = p[2]; a.Warning = p[3]; a.Success = p[4]; a.Danger = p[5]; a.Pro = p[6];
            a.ThemeOn = true;
            Changed(true);
        }

        static bool Matches(AccountCfg a, string[] p)
        {
            return a.Accent == p[1] && a.Surface == p[2] && a.Warning == p[3] && a.Success == p[4] && a.Danger == p[5] && a.Pro == p[6];
        }

        UIElement PresetChip(AccountCfg a, string[] p)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            var d = Ui.Dot(Pal.B(p[1], 1), 12);
            d.Margin = new Thickness(0, 1, 7, 0);
            row.Children.Add(d);
            row.Children.Add(Ui.T(p[0], 12.5, Ui.Text, false));
            return Ui.Chip(row, a.ThemeOn && Matches(a, p), a.Accent, delegate { ApplyPreset(a, p); });
        }

        FrameworkElement ColorPick(string label, string tip, string value, Action<string> set)
        {
            return Ui.ColorCell(label, tip, value, set, delegate(Popup p) { picker = p; });
        }

        UIElement BuildTheme(AccountCfg a)
        {
            var sp = new StackPanel();
            sp.Children.Add(Ui.Check(L.T("Usar um tema próprio nesta conta", "Use a custom theme for this account"), a.ThemeOn, delegate(bool v) { a.ThemeOn = v; Changed(true); }));
            if (!a.ThemeOn)
            {
                sp.Children.Add(Ui.T(L.T("Esta conta fica com as cores normais do Claude.", "This account keeps Claude's normal colors."), 12.5, Ui.Dim, false));
                return sp;
            }

            var presets = new WrapPanel { Margin = new Thickness(0, 6, 0, 8) };
            foreach (var p in Presets) presets.Children.Add(PresetChip(a, p));
            sp.Children.Add(presets);

            var colors = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
            colors.Children.Add(ColorPick(L.T("Principal", "Accent"), L.T("Botões, links e destaques.", "Buttons, links and highlights."), a.Accent, delegate(string v) { a.Accent = v; Changed(true); }));
            colors.Children.Add(ColorPick(L.T("Fundo", "Background"), L.T("O tom dos fundos e dos textos.", "The tint of backgrounds and text."), a.Surface, delegate(string v) { a.Surface = v; Changed(true); }));
            colors.Children.Add(ColorPick(L.T("Sucesso", "Success"), null, a.Success, delegate(string v) { a.Success = v; Changed(true); }));
            colors.Children.Add(ColorPick(L.T("Aviso", "Warning"), null, a.Warning, delegate(string v) { a.Warning = v; Changed(true); }));
            colors.Children.Add(ColorPick(L.T("Erro", "Error"), null, a.Danger, delegate(string v) { a.Danger = v; Changed(true); }));
            colors.Children.Add(ColorPick("Pro", L.T("Selos e recursos extras.", "Badges and extra features."), a.Pro, delegate(string v) { a.Pro = v; Changed(true); }));
            sp.Children.Add(colors);

            var previews = new WrapPanel();
            foreach (bool dark in new[] { false, true })
            {
                var one = new StackPanel { Margin = new Thickness(0, 0, 16, 0) };
                one.Children.Add(new ThemePreview(a, dark));
                var cap = Ui.T(dark ? L.T("Escuro", "Dark") : L.T("Claro", "Light"), 11.5, Ui.Dim, false);
                cap.Margin = new Thickness(2, 5, 0, 0);
                one.Children.Add(cap);
                previews.Children.Add(one);
            }
            sp.Children.Add(previews);
            return sp;
        }

        // ---- Clawd

        UIElement BuildClawd(AccountCfg a)
        {
            var sp = new StackPanel();
            sp.Children.Add(Ui.Check(L.T("Mostrar o Clawd desta conta", "Show this account's Clawd"), a.ShowClawd, delegate(bool v) { a.ShowClawd = v; Changed(true); }));
            if (!a.ShowClawd) return sp;

            var top = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 14) };
            var stage = new Border();
            stage.Background = Pal.B(Tint.Surfaces(a.Body, true)["surface-1"], 1);
            stage.CornerRadius = new CornerRadius(12);
            stage.Padding = new Thickness(14, 10, 14, 8);
            var both = new StackPanel { Orientation = Orientation.Horizontal };
            var big = View(5, a, a.Skin);
            big.VerticalAlignment = VerticalAlignment.Bottom;
            both.Children.Add(big);
            var pup = View(3, a, string.IsNullOrEmpty(a.PupSkin) ? a.Skin : a.PupSkin);
            pup.VerticalAlignment = VerticalAlignment.Bottom;
            pup.ToolTip = L.T("Um filhote aparece para cada sessão extra aberta na conta.", "A pup appears for each extra session open in the account.");
            both.Children.Add(pup);
            stage.Child = both;
            top.Children.Add(stage);

            var right = new StackPanel { Margin = new Thickness(18, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            var colors = new WrapPanel();
            colors.Children.Add(ColorPick(L.T("Corpo", "Body"), L.T("Sombra, brilho e contorno saem desta cor.", "Shading, highlight and outline come from this color."), a.Body, delegate(string v) { a.Body = v; Changed(true); }));
            colors.Children.Add(ColorPick(L.T("Detalhe", "Detail"), L.T("Acessórios da skin e enfeites.", "Skin accessories and decorations."), a.Detail, delegate(string v) { a.Detail = v; Changed(true); }));
            right.Children.Add(colors);
            var same = Ui.Button(L.T("Usar a cor do tema", "Use the theme color"), null, delegate { a.Body = a.Accent; a.Detail = a.Warning; Changed(true); });
            same.Margin = new Thickness(0, 4, 0, 10);
            right.Children.Add(same);
            var hat = Ui.Check(L.T("Chapéu do modelo em uso", "Hat for the model in use"), a.Hat, delegate(bool v) { a.Hat = v; Changed(false); });
            hat.ToolTip = L.T("Mago para o Fable, coroa para o Opus, fones para o Sonnet e broto para o Haiku.", "Wizard hat for Fable, crown for Opus, headphones for Sonnet and a sprout for Haiku.");
            right.Children.Add(hat);
            top.Children.Add(right);
            sp.Children.Add(top);

            sp.Children.Add(Ui.T("Skin", 13.5, Ui.Text, true));
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
                if (skin.Custom) chip.ToolTip = L.T("Skin da pasta de skins (", "Skin from the skins folder (") + skin.Id + ".json)";
                grid.Children.Add(chip);
            }
            sp.Children.Add(grid);

            sp.Children.Add(Ui.T(L.T("Skin dos filhotes", "Pups' skin"), 13.5, Ui.Text, true));
            var pups = new WrapPanel { Margin = new Thickness(0, 8, 0, 6) };
            pups.Children.Add(Ui.Chip(Ui.T(L.T("Igual à do grande", "Same as the big one"), 12.5, Ui.Text, false), string.IsNullOrEmpty(a.PupSkin), a.Body, delegate { a.PupSkin = ""; Changed(true); }));
            foreach (var sk in skins)
            {
                var skin = sk;
                pups.Children.Add(Ui.Chip(Ui.T(skin.Name, 12.5, Ui.Text, false), a.PupSkin == skin.Id, a.Body, delegate { a.PupSkin = skin.Id; Changed(true); }));
            }
            sp.Children.Add(pups);

            var more = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
            more.Children.Add(Ui.Button(Ui.IcFolder, L.T("Pasta de skins", "Skins folder"), null, delegate { Try(delegate { Process.Start("explorer.exe", "\"" + Cfg.SkinsDir + "\""); }); }));
            more.Children.Add(Ui.Button(Ui.IcSync, L.T("Recarregar", "Reload"), null, delegate { skins = Skin.LoadAll(Cfg.SkinsDir); Render(); }));
            sp.Children.Add(more);
            var hint = Ui.T(L.T("Cada .json na pasta de skins vira uma skin; o LEIA-ME de lá explica o formato.", "Each .json in the skins folder becomes a skin; the README there explains the format."), 11.5, Ui.Dim, false);
            hint.Margin = new Thickness(0, 8, 0, 0);
            sp.Children.Add(hint);
            return sp;
        }

        // ------------------------------------------------------------- ações --

        void Try(Action f)
        {
            try { f(); }
            catch (Exception e) { MessageBox.Show(Window.GetWindow(this), e.Message, "Clawdesk", MessageBoxButton.OK, MessageBoxImage.Warning); }
        }

        void AddAccount()
        {
            if (MessageBox.Show(Window.GetWindow(this),
                    L.T("Vou criar um perfil novo e abrir o Claude nele para você entrar com a outra conta.\n\nEle aparece aqui em seguida, com o nome \"Account N\"; para trocar o nome, use o Switcher.", "I'll create a new profile and open Claude in it so you can sign in with the other account.\n\nIt shows up here right after, named \"Account N\"; to rename it, use the Switcher."),
                    L.T("Nova conta", "New account"), MessageBoxButton.OKCancel, MessageBoxImage.Information) != MessageBoxResult.OK) return;
            Try(Profiles.AddAccount);
        }

        void CloseInstance(AccountCfg a)
        {
            int pid;
            if (!running.TryGetValue(a.Profile, out pid)) return;
            int n = ActiveOf(a);
            string extra = n == 0 ? "" : "\n\n" + (n == 1 ? L.T("Há 1 sessão ativa nele, que será interrompida.", "It has 1 active session, which will be interrupted.") : L.T("Há ", "It has ") + n + L.T(" sessões ativas nele, que serão interrompidas.", " active sessions, which will be interrupted."));
            if (MessageBox.Show(Window.GetWindow(this), L.T("Fechar o Claude ", "Close Claude ") + a.Name + "?" + extra, L.T("Fechar", "Close"), MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
            Try(delegate
            {
                var psi = new ProcessStartInfo("taskkill.exe", "/PID " + pid + " /T /F");
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                Process.Start(psi);
            });
        }

        void OpenFolder(AccountCfg a)
        {
            var p = Profiles.List().FirstOrDefault(x => x.Id == a.Profile);
            if (p != null && Directory.Exists(p.Folder)) Try(delegate { Process.Start("explorer.exe", "\"" + p.Folder + "\""); });
        }
    }
}
