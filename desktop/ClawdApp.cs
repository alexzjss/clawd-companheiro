// O Clawd Companheiro: um programa só, que fica rodando em segundo plano.
//
//   - os Clawds em cima da barra de tarefas (Stage, em ClawdDesktop.cs)
//   - o ícone na bandeja, os avisos do Windows e a busca de atualizações (ClawdServices.cs)
//   - o histórico das sessões (ClawdHistory.cs)
//   - a janela: Home, Estatísticas, Contas e Configurações
//
//   Clawd.exe              abre a janela (ou traz para a frente a que já está aberta)
//   Clawd.exe --fundo      só os Clawds e a bandeja, sem janela (é assim que abre com o Windows)
//   Clawd.exe --demo       os Clawds passam por todos os estados; --eventos, pelas brincadeiras
//   Clawd.exe --fotos dir  desenha as imagens do README, com contas de mentira
//
// Compilar: build.ps1 (csc do .NET Framework 4, C# 5).

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace ClawdDesktop
{
    // ------------------------------------------------------------------- app --

    sealed class App
    {
        public Cfg Cfg;
        DateTime stamp;
        readonly DispatcherTimer saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        public readonly Stage Stage;
        public readonly History Hist;
        public Tray Tray;
        Notifier notifier;
        public readonly Updater Updater;
        MainWindow win;
        public List<SessionView> LastSessions = new List<SessionView>();
        public double LastNow;
        public ModStatus Mod;
        public bool ModBusy;
        public string ModMessage;
        public event Action ModChanged, ConfigReloaded;
        readonly bool photo;
        bool updateNoticeShown, trayTipShown;

        public App(bool demo, bool festa, bool photo)
        {
            this.photo = photo;
            Cfg = photo ? Cfg.Sample() : Cfg.Load();
            if (Cfg.IsNew && !photo) { try { Cfg.Save(); } catch { } }
            stamp = Cfg.Stamp();
            saveTimer.Tick += delegate { saveTimer.Stop(); SaveNow(); };
            Hist = photo ? History.Sample(Cfg) : History.Load();
            Updater = new Updater(Dispatcher.CurrentDispatcher);
            Stage = new Stage(demo, festa, photo);
            if (photo) return;

            Tray = new Tray();
            notifier = new Notifier(delegate { return Cfg; }, Tray, delegate { return win != null && win.HomeVisible; }, delegate { return Stage.Running; });
            Stage.SessionsRead += OnSessions;
            Stage.OpenAppRequested += delegate { OpenWindow("home"); };
            Stage.QuitRequested += Quit;
            Stage.HoverChanged += delegate(Creature c) { if (win != null) win.Home.Highlight(c != null ? c.SessionId : null); };
            Tray.OpenPage += OpenWindow;
            Tray.ToggleTaskbar += delegate { Cfg.Taskbar = !Cfg.Taskbar; SaveNow(); SyncTray(); Reloaded(); };
            Tray.ToggleQuiet += delegate { Cfg.Notify.QuietUntil = Cfg.Notify.QuietUntil > Data.NowMs() ? 0 : Data.NowMs() + 3600000; SaveNow(); SyncTray(); Reloaded(); };
            Tray.CheckUpdates += delegate
            {
                if (Updater.Available) { OpenWindow("configuracoes"); Updater.Install(); }
                else { OpenWindow("configuracoes"); Updater.Check(true); }
            };
            Tray.Quit += Quit;
            Updater.Changed += delegate
            {
                SyncTray();
                if (Updater.Available && !updateNoticeShown && Updater.Latest != Cfg.SkipVersion)
                {
                    updateNoticeShown = true;
                    Tray.Notify("Versão " + Updater.Latest + " disponível", "Clique para ver o que mudou e atualizar.", delegate { OpenWindow("configuracoes"); });
                }
            };
            Updater.ReadyToQuit += Quit;
            SyncTray();

            // procura versão nova pouco depois de abrir e depois a cada 12 horas
            var upd = new DispatcherTimer { Interval = TimeSpan.FromSeconds(25) };
            upd.Tick += delegate
            {
                upd.Interval = TimeSpan.FromHours(12);
                if (Cfg.CheckUpdates) Updater.Check(false);
            };
            upd.Start();
            CheckMod();
        }

        public void SyncTray()
        {
            if (Tray != null) Tray.Sync(Cfg, Updater.Available && Updater.Latest != Cfg.SkipVersion ? Updater.Latest : null);
        }

        // ---- config

        // Algo mudou na janela: grava logo depois (os Clawds e o mod leem o arquivo).
        public void Changed()
        {
            saveTimer.Stop();
            saveTimer.Start();
            SyncTray();
        }

        void SaveNow()
        {
            if (photo) return;
            try { Cfg.Save(); }
            catch (Exception e) { Program.Log("config: " + e.Message); }
            stamp = Cfg.Stamp();
            Stage.ReloadConfig();
        }

        void Reloaded()
        {
            if (ConfigReloaded != null) ConfigReloaded();
        }

        // O arquivo mudou fora daqui (o menu do Clawd, outra cópia, a mão): relê.
        void CheckStamp()
        {
            if (saveTimer.IsEnabled || Cfg.Stamp() == stamp) return;
            if (win != null && win.Busy) return;
            Cfg = Cfg.Load();
            stamp = Cfg.Stamp();
            SyncTray();
            Reloaded();
        }

        // ---- leituras

        void OnSessions(List<SessionView> all, double now)
        {
            LastSessions = all;
            LastNow = now;
            CheckStamp();
            Hist.Observe(all, now);
            notifier.Observe(all, now);
            if (win != null) win.OnSessions(all, now);
        }

        // ---- mod

        public void CheckMod()
        {
            if (ModBusy) return;
            ModBusy = true;
            ModMessage = "Procurando o Claude Code…";
            if (ModChanged != null) ModChanged();
            var d = Dispatcher.CurrentDispatcher;
            ThreadPool.QueueUserWorkItem(delegate
            {
                var st = ModTool.Status();
                d.BeginInvoke(new Action(delegate
                {
                    Mod = st;
                    ModBusy = false;
                    ModMessage = null;
                    if (ModChanged != null) ModChanged();
                }));
            });
        }

        public void InstallMod()
        {
            if (ModBusy) return;
            ModBusy = true;
            var st = Mod;
            ModMessage = st != null && st.Installed ? "Atualizando o mod…" : "Instalando o mod…";
            if (ModChanged != null) ModChanged();
            var d = Dispatcher.CurrentDispatcher;
            ThreadPool.QueueUserWorkItem(delegate
            {
                if (st == null) st = ModTool.Status();
                string err = ModTool.InstallOrUpdate(st);
                var after = ModTool.Status();
                d.BeginInvoke(new Action(delegate
                {
                    Mod = after;
                    ModBusy = false;
                    ModMessage = err == null ? "Pronto. Reinicie o Claude para o mod carregar." : "Não deu: " + err;
                    if (ModChanged != null) ModChanged();
                }));
            });
        }

        // ---- janela

        public void OpenWindow(string page)
        {
            if (win == null)
            {
                win = new MainWindow(this, false);
                win.Closed += delegate
                {
                    win = null;
                    if (!trayTipShown && Tray != null)
                    {
                        trayTipShown = true;
                        Tray.Notify("O Clawd Companheiro continua aqui", "Ele fica na bandeja, de olho nas sessões. Para fechar de vez, use Sair no menu do ícone.", null);
                    }
                };
                win.Show();
                if (LastSessions.Count > 0 || LastNow > 0) win.OnSessions(LastSessions, LastNow);
            }
            if (win.WindowState == WindowState.Minimized) win.WindowState = WindowState.Normal;
            win.Go(page);
            win.Activate();
            win.Topmost = true;
            win.Topmost = false;
            win.Focus();
        }

        public void Quit()
        {
            try { Hist.Save(true); } catch { }
            if (saveTimer.IsEnabled) { saveTimer.Stop(); SaveNow(); }
            if (Tray != null) Tray.Dispose();
            Application.Current.Shutdown();
        }

        // ---- fotos

        public void Photos(string dir)
        {
            Stage.Photos(dir);
            var w = new MainWindow(this, true);
            w.WindowStartupLocation = WindowStartupLocation.Manual;
            w.Left = -6000;
            w.Top = 0;
            w.ShowInTaskbar = false;
            w.ShowActivated = false;
            w.Show();
            string a = Cfg.Accounts[0].Key, b = Cfg.Accounts[1].Key;
            var fake = new[]
            {
                Stage.PhotoSession(a, "a1", 3, "trabalhando", "Bash", "teste", "claude-opus-5-5", "Corrigir o login", 0),
                Stage.PhotoSession(a, "a2", 1, "aguardando", "AskUserQuestion", "", "claude-fable-5-2", "Revisar o relatório", 0),
                Stage.PhotoSession(b, "b1", 2, "trabalhando", "Edit", "codigo", "claude-sonnet-5-5", "Site novo", 2),
                Stage.PhotoSession(b, "b2", 1, "pensando", "", "", "claude-haiku-5-5", "Lista de compras", 0),
            };
            Stage.PhotoSessions(fake);
            w.Go("home");
            w.UpdateLayout();
            Stage.AttachHome(w.Home.StageView);
            w.OnSessions(fake.ToList(), Data.NowMs());
            Stage.PhotoAdvance(3.2);
            w.Shoot(Path.Combine(dir, "app-home.png"));
            w.Go("estatisticas");
            w.Shoot(Path.Combine(dir, "app-estatisticas.png"));
            w.Go("contas");
            w.Shoot(Path.Combine(dir, "app-contas.png"));
            w.Go("configuracoes");
            w.Shoot(Path.Combine(dir, "app-configuracoes.png"));
            w.Close();
        }
    }

    // --------------------------------------------------------------- janela --

    sealed class MainWindow : Window
    {
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        readonly App app;
        readonly bool photo;
        public readonly HomePage Home;
        readonly StatsPage stats;
        readonly AccountsPage accounts;
        readonly SettingsPage settings;
        readonly ScrollViewer scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false };
        readonly StackPanel nav = new StackPanel();
        readonly ContentControl navFoot = new ContentControl { Focusable = false };
        readonly TextBlock title = Ui.T("", 26, Ui.Text, true), subtitle = Ui.T("", 13, Ui.Dim, false);
        readonly ContentControl body = new ContentControl { Focusable = false };
        readonly Grid overlay = new Grid { IsHitTestVisible = false };
        string page = "";
        DateTime statsBuilt = DateTime.MinValue;

        static readonly string[][] Pages =
        {
            new[] { "home", "Home", Ui.IcHome, "Todas as sessões e os Clawds, juntos." },
            new[] { "estatisticas", "Estatísticas", Ui.IcChart, "Quanto o Claude trabalhou, quanto custou e no quê." },
            new[] { "contas", "Contas", Ui.IcPeople, "As contas do Claude Desktop, o tema de cada uma e o Clawd." },
            new[] { "configuracoes", "Configurações", Ui.IcSettings, "Clawds, avisos, mod do Claude Code e atualizações." },
        };

        public MainWindow(App a, bool photo)
        {
            app = a;
            this.photo = photo;
            Title = AppPaths.Name;
            Width = 1200;
            Height = 820;
            MinWidth = 940;
            MinHeight = 600;
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

            Home = new HomePage(app.Stage, delegate { return app.Cfg; });
            stats = new StatsPage(app.Hist, delegate { return app.Cfg; });
            accounts = new AccountsPage(delegate { return app.Cfg; }, app.Changed, photo);
            settings = new SettingsPage(app);

            var root = new Grid();
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(232) });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // navegação
            var side = new DockPanel { Background = Ui.Side, LastChildFill = true };
            var brand = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(16, 18, 16, 22) };
            var first = app.Cfg.Accounts.FirstOrDefault();
            var logo = new ClawdView(2, Skin.Builtin()[0], Tint.Palette(first != null ? first.Body : Ui.Accent, first != null ? first.Detail : "#ffd23f"));
            logo.Margin = new Thickness(-6, 0, 2, 0);
            brand.Children.Add(logo);
            var bt = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            bt.Children.Add(Ui.T("Clawd", 16, Ui.Text, true));
            bt.Children.Add(Ui.T("Companheiro · " + AppPaths.Version, 11.5, Ui.Faint, false));
            brand.Children.Add(bt);
            DockPanel.SetDock(brand, Dock.Top);
            side.Children.Add(brand);
            navFoot.Margin = new Thickness(12, 0, 12, 14);
            DockPanel.SetDock(navFoot, Dock.Bottom);
            side.Children.Add(navFoot);
            nav.Margin = new Thickness(10, 0, 10, 0);
            side.Children.Add(nav);
            root.Children.Add(side);

            // conteúdo
            var main = new StackPanel { Margin = new Thickness(34, 26, 34, 30), MaxWidth = 1280 };
            var head = new StackPanel { Margin = new Thickness(0, 0, 0, 20) };
            head.Children.Add(title);
            subtitle.Margin = new Thickness(0, 4, 0, 0);
            head.Children.Add(subtitle);
            main.Children.Add(head);
            main.Children.Add(body);
            scroll.Content = main;
            var content = new Grid();
            content.Children.Add(scroll);
            content.Children.Add(overlay);
            Grid.SetColumn(content, 1);
            root.Children.Add(content);
            Content = root;

            app.ModChanged += OnModChanged;
            app.ConfigReloaded += OnReloaded;
            app.Updater.Changed += OnUpdaterChanged;
            app.Hist.Changed += OnHistChanged;
            StateChanged += delegate { SyncHome(); };
            IsVisibleChanged += delegate { SyncHome(); };
            Closed += delegate
            {
                app.ModChanged -= OnModChanged;
                app.ConfigReloaded -= OnReloaded;
                app.Updater.Changed -= OnUpdaterChanged;
                app.Hist.Changed -= OnHistChanged;
                app.Stage.DetachHome();
            };
            PreviewKeyDown += delegate(object s, KeyEventArgs e)
            {
                if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
                int i = e.Key == Key.D1 ? 0 : e.Key == Key.D2 ? 1 : e.Key == Key.D3 ? 2 : e.Key == Key.D4 ? 3 : -1;
                if (i >= 0) { Go(Pages[i][0]); e.Handled = true; }
            };
            BuildFoot();
        }

        void OnModChanged() { settings.Refresh(); BuildFoot(); }
        void OnUpdaterChanged() { settings.Refresh(); BuildFoot(); }
        void OnHistChanged()
        {
            // estatísticas abertas: redesenha de vez em quando, não a cada leitura
            if (page == "estatisticas" && (DateTime.Now - statsBuilt).TotalSeconds > 30) { statsBuilt = DateTime.Now; stats.Refresh(); }
        }

        void OnReloaded()
        {
            accounts.Reloaded();
            settings.Refresh();
        }

        public bool Busy { get { return accounts.Busy; } }
        public bool HomeVisible { get { return page == "home" && IsVisible && WindowState != WindowState.Minimized && IsActive; } }

        void BuildNav()
        {
            nav.Children.Clear();
            foreach (var p0 in Pages)
            {
                var p = p0;
                bool on = p[0] == page;
                var row = new StackPanel { Orientation = Orientation.Horizontal };
                var ic = Ui.Icon(p[2], 15, on ? Ui.Text : Ui.Dim);
                ic.Margin = new Thickness(0, 1, 12, 0);
                row.Children.Add(ic);
                row.Children.Add(Ui.T(p[1], 13.5, on ? Ui.Text : Ui.Dim, on));
                var item = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 9, 12, 10), Margin = new Thickness(0, 0, 0, 3), Cursor = Cursors.Hand, Child = row };
                item.Background = on ? Ui.CardHi : Brushes.Transparent;
                item.BorderThickness = new Thickness(3, 0, 0, 0);
                item.BorderBrush = on ? Pal.B(Ui.Accent, 1) : Brushes.Transparent;
                item.ToolTip = p[3] + "  (Ctrl+" + (Array.IndexOf(Pages, p) + 1) + ")";
                item.MouseEnter += delegate { if (!on) item.Background = Ui.Card; };
                item.MouseLeave += delegate { if (!on) item.Background = Brushes.Transparent; };
                item.MouseLeftButtonUp += delegate { Go(p[0]); };
                nav.Children.Add(item);
            }
        }

        // O rodapé da navegação: versão nova e mod faltando, quando for o caso.
        void BuildFoot()
        {
            var sp = new StackPanel();
            if (app.Updater.Available && app.Updater.Latest != app.Cfg.SkipVersion)
                sp.Children.Add(FootCard(Ui.IcDownload, "Versão " + app.Updater.Latest, "Clique para atualizar", Ui.Good, delegate { Go("configuracoes"); }));
            if (app.Mod != null && !app.Mod.Installed && !app.ModBusy)
                sp.Children.Add(FootCard(Ui.IcInfo, "Falta o mod", "Sem ele, as sessões não aparecem", Ui.Warn, delegate { Go("configuracoes"); }));
            navFoot.Content = sp;
        }

        static Border FootCard(string icon, string title, string note, Brush color, Action click)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            var ic = Ui.Icon(icon, 14, color);
            ic.Margin = new Thickness(0, 2, 10, 0);
            ic.VerticalAlignment = VerticalAlignment.Top;
            row.Children.Add(ic);
            var t = new StackPanel();
            t.Children.Add(Ui.T(title, 12.5, Ui.Text, true));
            t.Children.Add(Ui.T(note, 11.5, Ui.Dim, false));
            row.Children.Add(t);
            var b = new Border { Background = Ui.Card, BorderBrush = Ui.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(12, 9, 12, 10), Margin = new Thickness(0, 8, 0, 0), Cursor = Cursors.Hand, Child = row };
            b.MouseLeftButtonUp += delegate { click(); };
            return b;
        }

        public void Go(string p)
        {
            var info = Pages.FirstOrDefault(x => x[0] == p) ?? Pages[0];
            if (page == info[0]) return;
            page = info[0];
            title.Text = info[1];
            subtitle.Text = info[3];
            switch (page)
            {
                case "home": body.Content = Home; break;
                case "estatisticas": stats.Refresh(); statsBuilt = DateTime.Now; body.Content = stats; break;
                case "contas": body.Content = accounts; break;
                default: settings.Refresh(); body.Content = settings; break;
            }
            scroll.ScrollToTop();
            BuildNav();
            SyncHome();
        }

        // Os Clawds moram na Home enquanto ela está à vista.
        void SyncHome()
        {
            bool want = page == "home" && IsVisible && WindowState != WindowState.Minimized;
            if (!want) { app.Stage.DetachHome(); return; }
            // espera a Home ter tamanho
            Dispatcher.BeginInvoke(new Action(delegate
            {
                if (page == "home" && IsVisible && WindowState != WindowState.Minimized) app.Stage.AttachHome(Home.StageView);
            }), DispatcherPriority.Loaded);
        }

        public void OnSessions(List<SessionView> all, double now)
        {
            Home.Update(all, now);
            accounts.Status(app.Stage.Running, all, now);
        }

        public void Toast(string text)
        {
            var t = Ui.Toast(text, Ui.Good);
            overlay.Children.Add(t);
            var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(500)) { BeginTime = TimeSpan.FromSeconds(2.4) };
            fade.Completed += delegate { overlay.Children.Remove(t); };
            t.BeginAnimation(OpacityProperty, fade);
        }

        // ---- fotos

        public void Shoot(string file)
        {
            var root = (FrameworkElement)Content;
            root.UpdateLayout();
            Dispatcher.Invoke(new Action(delegate { }), DispatcherPriority.ContextIdle);
            var bmp = new RenderTargetBitmap((int)(root.ActualWidth * 1.5), (int)(root.ActualHeight * 1.5), 144, 144, PixelFormats.Pbgra32);
            bmp.Render(root);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(bmp));
            using (var fs = File.Create(file)) enc.Save(fs);
        }
    }

    // ------------------------------------------------------- configurações --

    sealed class SettingsPage : ContentControl
    {
        readonly App app;

        public SettingsPage(App a)
        {
            app = a;
            Focusable = false;
            Refresh();
        }

        void Changed() { app.Changed(); }

        public void Refresh()
        {
            var c = app.Cfg;
            var sp = new StackPanel();

            // ---- Clawds
            var clawds = new StackPanel();
            clawds.Children.Add(Ui.Setting("Clawds em cima da barra de tarefas", "Fora da Home, eles ficam no canto da tela. Com a Home aberta, moram lá dentro.",
                Ui.Toggle(c.Taskbar, delegate(bool v) { c.Taskbar = v; Changed(); })));
            clawds.Children.Add(Ui.Divider());
            clawds.Children.Add(Ui.Setting("Canto da tela", null,
                Ui.Segmented(new[] { "Esquerdo", "Direito" }, c.Corner == "esquerdo" ? 0 : 1, delegate(int i) { c.Corner = i == 0 ? "esquerdo" : "direito"; Changed(); Refresh(); })));
            clawds.Children.Add(Ui.Divider());
            clawds.Children.Add(Ui.Setting("Brincadeiras na barra de tarefas", "De tempo em tempo, os Clawds livres brincam juntos. Na Home eles sempre brincam.",
                Ui.Toggle(c.Events, delegate(bool v) { c.Events = v; Changed(); })));
            sp.Children.Add(Ui.Section("Clawds", null, clawds));

            // ---- avisos
            var n = c.Notify;
            var notif = new StackPanel();
            notif.Children.Add(Ui.Setting("Avisos do Windows", "Quando uma sessão precisa de você. Não avisa o que você já está vendo: a Home aberta ou o Claude da conta na frente.",
                Ui.Toggle(n.On, delegate(bool v) { n.On = v; Changed(); Refresh(); })));
            if (n.On)
            {
                notif.Children.Add(Ui.Divider());
                notif.Children.Add(Ui.Setting("Esperando você", "Uma pergunta, um plano para aprovar ou um pedido de permissão.", Ui.Toggle(n.Waiting, delegate(bool v) { n.Waiting = v; Changed(); })));
                notif.Children.Add(Ui.Divider());
                notif.Children.Add(Ui.Setting("Terminou", null, Ui.Toggle(n.Done, delegate(bool v) { n.Done = v; Changed(); Refresh(); })));
                if (n.Done)
                {
                    int[] secs = { 0, 30, 60, 300 };
                    int sel = Array.IndexOf(secs, n.MinSecs);
                    notif.Children.Add(Ui.Setting("Só tarefas que levaram mais de", "Respostas rápidas não precisam de aviso.",
                        Ui.Segmented(new[] { "Qualquer", "30 s", "1 min", "5 min" }, sel < 0 ? 1 : sel, delegate(int i) { n.MinSecs = secs[i]; Changed(); Refresh(); })));
                }
                notif.Children.Add(Ui.Divider());
                notif.Children.Add(Ui.Setting("Deu erro", null, Ui.Toggle(n.Error, delegate(bool v) { n.Error = v; Changed(); })));
                notif.Children.Add(Ui.Divider());
                double now = Data.NowMs();
                bool quiet = n.QuietUntil > now;
                DateTime until = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMilliseconds(n.QuietUntil).ToLocalTime();
                double tomorrow = (DateTime.Today.AddDays(1).AddHours(8).ToUniversalTime() - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds;
                notif.Children.Add(Ui.Setting("Não perturbe", quiet ? "Ligado até " + until.ToString(until.Date == DateTime.Today ? "HH:mm" : "ddd HH:mm", Words.PtBR) + "." : "Pausa os avisos por um tempo.",
                    Ui.Segmented(new[] { "Desligado", "1 hora", "Até amanhã" }, !quiet ? 0 : Math.Abs(n.QuietUntil - tomorrow) < 1000 ? 2 : 1,
                        delegate(int i) { n.QuietUntil = i == 0 ? 0 : i == 1 ? Data.NowMs() + 3600000 : tomorrow; Changed(); Refresh(); })));
            }
            sp.Children.Add(Ui.Section("Avisos", null, notif));

            // ---- mod
            sp.Children.Add(Ui.Section("Mod do Claude Code", "É o mod que conta para o app o que cada sessão está fazendo, aplica o tema de cada conta e põe um Clawd dentro do Claude.", BuildMod()));

            // ---- inicialização e atualizações
            var sys = new StackPanel();
            bool startup = AppPaths.StartsWithWindows;
            sys.Children.Add(Ui.Setting("Abrir com o Windows", "Começa em segundo plano, na bandeja, sem abrir a janela.",
                Ui.Toggle(startup, delegate(bool v)
                {
                    try { AppPaths.SetStartup(v, System.Reflection.Assembly.GetExecutingAssembly().Location); }
                    catch (Exception e) { MessageBox.Show(Window.GetWindow(this), e.Message, AppPaths.Name); }
                })));
            sys.Children.Add(Ui.Divider());
            sys.Children.Add(Ui.Setting("Procurar atualizações sozinho", "Uma vez ao abrir e depois a cada 12 horas, no GitHub do projeto.",
                Ui.Toggle(c.CheckUpdates, delegate(bool v) { c.CheckUpdates = v; Changed(); })));
            sys.Children.Add(Ui.Divider());
            sys.Children.Add(BuildUpdate());
            sp.Children.Add(Ui.Section("Sistema", null, sys));

            // ---- dados
            var data = new WrapPanel();
            data.Children.Add(Ui.Button(Ui.IcFolder, "Pasta de dados", null, delegate { Open(Cfg.Dir); }));
            data.Children.Add(Ui.Button(Ui.IcFolder, "Pasta de skins", null, delegate { Skin.WriteGuide(Cfg.SkinsDir); Open(Cfg.SkinsDir); }));
            data.Children.Add(Ui.Button(Ui.IcOpen, "Página do projeto", null, delegate { Open("https://github.com/" + AppPaths.Repo); }));
            sp.Children.Add(Ui.Section("Dados e privacidade", "Tudo fica nesta máquina, em .claude\\clawd: as escolhas, o histórico e o estado das sessões (nunca os comandos, caminhos ou conteúdo). Nada é enviado para fora, além da consulta de versão nova no GitHub.", data));

            Content = sp;
        }

        static void Open(string what)
        {
            try { Process.Start(new ProcessStartInfo(what) { UseShellExecute = true }); } catch { }
        }

        UIElement BuildMod()
        {
            var sp = new StackPanel();
            var st = app.Mod;
            string state;
            Brush color;
            if (app.ModBusy || st == null) { state = app.ModMessage ?? "Verificando…"; color = Ui.Info; }
            else if (st.Claude == null) { state = "Não achei o Claude Code neste computador."; color = Ui.Warn; }
            else if (st.Installed) { state = "Instalado" + (st.Version.Length > 0 ? ", versão " + st.Version : "") + "."; color = Ui.Good; }
            else { state = st.Error != null ? "Não consegui conferir: " + st.Error : "Ainda não instalado."; color = Ui.Warn; }
            var row = new DockPanel();
            var btns = new StackPanel { Orientation = Orientation.Horizontal };
            if (st != null && st.Claude != null && !app.ModBusy)
                btns.Children.Add(Ui.Button(Ui.IcDownload, st.Installed ? "Atualizar o mod" : "Instalar o mod", st.Installed ? null : Ui.Accent, delegate { app.InstallMod(); }));
            var again = Ui.Button(Ui.IcSync, "", null, delegate { app.CheckMod(); });
            again.ToolTip = "Conferir de novo";
            again.Margin = new Thickness(0);
            if (!app.ModBusy) btns.Children.Add(again);
            DockPanel.SetDock(btns, Dock.Right);
            row.Children.Add(btns);
            row.Children.Add(Ui.Badge(state, color));
            sp.Children.Add(row);
            if (!app.ModBusy && app.ModMessage != null)
            {
                var m = Ui.T(app.ModMessage, 12.5, app.ModMessage.StartsWith("Pronto") ? Ui.Good : Ui.Warn, false);
                m.Margin = new Thickness(0, 10, 0, 0);
                sp.Children.Add(m);
            }
            var hint = Ui.T("Também dá para instalar por dentro do Claude Code, com o comando abaixo (clique para copiar):", 12, Ui.Dim, false);
            hint.Margin = new Thickness(0, 14, 0, 6);
            sp.Children.Add(hint);
            var line = new Border { Background = Ui.Soft, CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 8, 12, 9), Cursor = Cursors.Hand, ToolTip = "Copiar", HorizontalAlignment = HorizontalAlignment.Left };
            var lt = Ui.T(ModTool.ManualLine, 12, Ui.Text, false);
            lt.FontFamily = Ui.Mono;
            line.Child = lt;
            line.MouseLeftButtonUp += delegate
            {
                try
                {
                    Clipboard.SetText(ModTool.ManualLine);
                    var w = Window.GetWindow(this) as MainWindow;
                    if (w != null) w.Toast("Comando copiado.");
                }
                catch { }
            };
            sp.Children.Add(line);
            return sp;
        }

        UIElement BuildUpdate()
        {
            var u = app.Updater;
            var g = new Grid { Margin = new Thickness(0, 10, 0, 4) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            left.Children.Add(Ui.T("Versão " + AppPaths.Version, 13.5, Ui.Text, false));
            string note = u.Status.Length > 0 ? u.Status : u.LastCheck > DateTime.MinValue ? "Última busca às " + u.LastCheck.ToString("HH:mm") + "." : "Ainda não procurei hoje.";
            var nt = Ui.T(note, 12, u.Available ? Ui.Good : Ui.Dim, false);
            nt.Margin = new Thickness(0, 2, 0, 0);
            left.Children.Add(nt);
            if (u.Available && !string.IsNullOrEmpty(u.Notes))
            {
                string notes = u.Notes.Length > 600 ? u.Notes.Substring(0, 600) + "…" : u.Notes;
                var box = new Border { Background = Ui.Soft, CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 9, 12, 10), Margin = new Thickness(0, 10, 20, 0), Child = Ui.T(notes, 12, Ui.Dim, false) };
                left.Children.Add(box);
            }
            g.Children.Add(left);
            var btns = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top };
            if (u.Available && !u.Installing)
            {
                btns.Children.Add(Ui.Button(Ui.IcDownload, "Atualizar agora", Ui.Accent, delegate { u.Install(); }));
                if (u.Latest != app.Cfg.SkipVersion)
                    btns.Children.Add(Ui.Button("Pular esta versão", null, delegate { app.Cfg.SkipVersion = u.Latest; app.Changed(); app.SyncTray(); Refresh(); }));
            }
            else if (!u.Checking && !u.Installing) btns.Children.Add(Ui.Button(Ui.IcSync, "Procurar agora", null, delegate { u.Check(true); }));
            Grid.SetColumn(btns, 1);
            g.Children.Add(btns);
            return g;
        }
    }

    // --------------------------------------------------------------- início --

    static class Program
    {
        static string logPath = Path.Combine(AppPaths.DataDir, "app.log");
        const string MutexName = "Local\\ClawdCompanheiro", OpenSignal = "Local\\ClawdCompanheiro.Abrir";

        public static void Log(string text)
        {
            try
            {
                var fi = new FileInfo(logPath);
                if (fi.Exists && fi.Length > 2 * 1024 * 1024) fi.Delete();
                File.AppendAllText(logPath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss ") + text + Environment.NewLine, Encoding.UTF8);
            }
            catch { }
        }

        [STAThread]
        static void Main(string[] args)
        {
            Directory.CreateDirectory(AppPaths.DataDir);
            Func<string, bool> has = name => args.Any(a => a.TrimStart('-', '/').Equals(name, StringComparison.OrdinalIgnoreCase));
            bool festa = has("eventos");
            bool demo = festa || has("demo");
            bool background = has("fundo");

            int fi = Array.FindIndex(args, a => a.TrimStart('-', '/').Equals("fotos", StringComparison.OrdinalIgnoreCase));
            if (fi >= 0)
            {
                // desenha as imagens do README em arquivos, sem mexer na tela
                string dir = fi + 1 < args.Length ? args[fi + 1] : "docs";
                Directory.CreateDirectory(dir);
                var shots = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                shots.Startup += delegate
                {
                    try { new App(true, false, true).Photos(dir); }
                    catch (Exception e)
                    {
                        // sem console (é winexe): o erro vai para um arquivo, e o código de saída avisa
                        try { File.WriteAllText(Path.Combine(dir, "erro-fotos.txt"), e.ToString()); } catch { }
                        Environment.ExitCode = 3;
                    }
                    shots.Shutdown();
                };
                shots.Run();
                return;
            }

            bool created;
            using (var mutex = new Mutex(true, demo ? MutexName + ".Demo" : MutexName, out created))
            {
                if (!created)
                {
                    // já está rodando: pede para a outra cópia abrir a janela
                    if (!background)
                    {
                        try { using (var ev = EventWaitHandle.OpenExisting(OpenSignal)) ev.Set(); } catch { }
                    }
                    return;
                }
                using (var signal = new EventWaitHandle(false, EventResetMode.AutoReset, demo ? OpenSignal + ".Demo" : OpenSignal))
                {
                    var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                    app.DispatcherUnhandledException += (s, e) => { Log("erro: " + e.Exception); e.Handled = true; };
                    App clawd = null;
                    app.Startup += delegate
                    {
                        clawd = new App(demo, festa, false);
                        if (!background) clawd.OpenWindow("home");
                        var d = Dispatcher.CurrentDispatcher;
                        new Thread(delegate()
                        {
                            while (true)
                            {
                                signal.WaitOne();
                                d.BeginInvoke(new Action(delegate { clawd.OpenWindow("home"); }));
                            }
                        }) { IsBackground = true, Name = "abrir" }.Start();
                    };
                    Log(demo ? "Clawd Companheiro abriu (demo)" : "Clawd Companheiro abriu " + AppPaths.Version);
                    app.Run();
                    Log("Clawd Companheiro fechou");
                }
            }
        }
    }
}
