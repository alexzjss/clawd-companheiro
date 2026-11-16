// O Clawdesk: um programa só, que fica rodando em segundo plano.
//
//   - os Clawds em cima da barra de tarefas (Stage, em ClawdDesktop.cs)
//   - o ícone na bandeja, os avisos do Windows e a busca de atualizações (ClawdServices.cs)
//   - o histórico das sessões (ClawdHistory.cs)
//   - a janela: Home, Estatísticas, Contas e Configurações
//
//   Clawdesk.exe              abre a janela (ou traz para a frente a que já está aberta)
//   Clawdesk.exe --fundo      só os Clawds e a bandeja, sem janela (é assim que abre com o Windows)
//   Clawdesk.exe --demo       os Clawds passam por todos os estados; --eventos, pelas brincadeiras
//   Clawdesk.exe --fotos dir  desenha as imagens do README, com contas de mentira
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

namespace Clawdesk
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
        bool updateNoticeShown, trayTipShown, migrated;

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
                    Tray.Notify(L.T("Versão ", "Version ") + Updater.Latest + L.T(" disponível", " available"), L.T("Clique para ver o que mudou e atualizar.", "Click to see what's new and update."), delegate { OpenWindow("configuracoes"); });
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
            ModMessage = L.T("Procurando o Claude Code…", "Looking for Claude Code…");
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
                    // o mod ainda com o nome de antes do Clawdesk: troca sozinho, uma vez
                    if (st.Legacy && !st.Installed && st.Claude != null && !migrated && !photo) { migrated = true; InstallMod(); }
                }));
            });
        }

        public void InstallMod()
        {
            if (ModBusy) return;
            ModBusy = true;
            var st = Mod;
            ModMessage = st != null && st.Installed ? L.T("Atualizando o mod…", "Updating the mod…") : L.T("Instalando o mod…", "Installing the mod…");
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
                    ModMessage = err == null ? L.T("Pronto. Reinicie o Claude para o mod carregar.", "Done. Restart Claude to load the mod.") : L.T("Não deu: ", "Didn't work: ") + err;
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
                        Tray.Notify(L.T("O Clawdesk continua aqui", "Clawdesk is still here"), L.T("Ele fica na bandeja, de olho nas sessões. Para fechar de vez, use Sair no menu do ícone.", "It stays in the tray, keeping an eye on your sessions. To close it for good, use Quit in the icon's menu."), null);
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

        public void ChangeLanguage(string choice)
        {
            if (Cfg.Lang == choice) return;
            Cfg.Lang = choice;
            L.Choice = choice;
            saveTimer.Stop();
            SaveNow();
            Restart();
        }

        // Fecha e abre de novo (para trocar o idioma de tudo de uma vez).
        public void Restart()
        {
            try
            {
                string exe = System.Reflection.Assembly.GetExecutingAssembly().Location;
                Process.Start(new ProcessStartInfo(exe, "--depois " + Process.GetCurrentProcess().Id) { UseShellExecute = true });
            }
            catch (Exception e) { Program.Log("reiniciar: " + e.Message); return; }
            Quit();
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
                Stage.PhotoSession(a, "a1", 3, "trabalhando", "Bash", "teste", "claude-opus-5-5", L.T("Corrigir o login", "Fix the login"), 0),
                Stage.PhotoSession(a, "a2", 1, "aguardando", "AskUserQuestion", "", "claude-fable-5-2", L.T("Revisar o relatório", "Review the report"), 0),
                Stage.PhotoSession(b, "b1", 2, "trabalhando", "Edit", "codigo", "claude-sonnet-5-5", L.T("Site novo", "New website"), 2),
                Stage.PhotoSession(b, "b2", 1, "pensando", "", "", "claude-haiku-5-5", L.T("Lista de compras", "Shopping list"), 0),
                Stage.PhotoSession(b, "b3", 1, "ocioso", "", "", "claude-sonnet-5-5", L.T("Ideias de nomes", "Name ideas"), 0),
                Stage.PhotoSession(a, "a3", 2, "feito", "", "", "claude-sonnet-5-5", L.T("Planilha de custos", "Cost spreadsheet"), 0),
            };
            w.Go("home");
            w.UpdateLayout();
            w.OnSessions(fake.ToList(), Data.NowMs());
            w.Home.Office.Advance(0.1);
            w.OnSessions(fake.ToList(), Data.NowMs());
            for (int i = 0; i < 120; i++) w.Home.Office.Advance(1.0 / 30);
            w.Home.Office.Select("a2");
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
            new[] { "home", "Home", Ui.IcHome, L.T("O escritório: cada conta é um setor, cada sessão um Clawd na sua mesa.", "The office: each account is a department, each session a Clawd at its desk.") },
            new[] { "estatisticas", L.T("Estatísticas", "Statistics"), Ui.IcChart, L.T("Quanto o Claude trabalhou, quanto custou e no quê.", "How much Claude worked, what it cost and on what.") },
            new[] { "contas", L.T("Contas", "Accounts"), Ui.IcPeople, L.T("As contas do Claude Desktop, o tema de cada uma e o Clawd.", "Your Claude Desktop accounts, each one's theme and Clawd.") },
            new[] { "configuracoes", L.T("Configurações", "Settings"), Ui.IcSettings, L.T("Clawds, avisos, mod do Claude Code e atualizações.", "Clawds, notifications, the Claude Code mod and updates.") },
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

            Home = new HomePage(delegate { return app.Cfg; }, photo);
            Home.Margin = new Thickness(18, 18, 18, 18);
            Home.Visibility = Visibility.Collapsed;
            stats = new StatsPage(app.Hist, delegate { return app.Cfg; });
            accounts = new AccountsPage(delegate { return app.Cfg; }, app.Changed, photo);
            settings = new SettingsPage(app);

            Resources.Add(typeof(System.Windows.Controls.Primitives.ScrollBar), Ui.ScrollBarStyle());
            var root = new Grid { Background = Ui.Bg };
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
            bt.Children.Add(Ui.T(AppPaths.Name, 16, Ui.Text, true));
            bt.Children.Add(Ui.T(L.T("versão ", "version ") + AppPaths.Version, 11.5, Ui.Faint, false));
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
            content.Children.Add(Home);   // a Home é o escritório inteiro: sem título e sem rolagem
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
                app.Stage.SetAway(false);
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

        public bool Busy { get { return accounts.Busy || settings.Busy; } }
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
                sp.Children.Add(FootCard(Ui.IcDownload, L.T("Versão ", "Version ") + app.Updater.Latest, L.T("Clique para atualizar", "Click to update"), Ui.Good, delegate { Go("configuracoes"); }));
            if (app.Mod != null && !app.Mod.Installed && !app.ModBusy)
                sp.Children.Add(FootCard(Ui.IcInfo, L.T("Falta o mod", "The mod is missing"), L.T("Sem ele, as sessões não aparecem", "Without it, sessions don't show up"), Ui.Warn, delegate { Go("configuracoes"); }));
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
                case "home": body.Content = null; break;
                case "estatisticas": stats.Refresh(); statsBuilt = DateTime.Now; body.Content = stats; break;
                case "contas": body.Content = accounts; break;
                default: settings.Refresh(); body.Content = settings; break;
            }
            scroll.ScrollToTop();
            Home.Visibility = page == "home" ? Visibility.Visible : Visibility.Collapsed;
            scroll.Visibility = page == "home" ? Visibility.Collapsed : Visibility.Visible;
            BuildNav();
            SyncHome();
        }

        // Enquanto a Home está à vista, os Clawds estão no escritório: somem da barra de tarefas.
        void SyncHome()
        {
            if (photo) return;
            app.Stage.SetAway(page == "home" && IsVisible && WindowState != WindowState.Minimized);
        }

        public void OnSessions(List<SessionView> all, double now)
        {
            Home.Update(all, now, app.Stage.Running);
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

        // Alguém digitando aqui (o Gist ou o token): não redesenha embaixo.
        public bool Busy
        {
            get
            {
                var f = Keyboard.FocusedElement as DependencyObject;
                return (f is TextBox || f is PasswordBox) && IsAncestorOf(f);
            }
        }

        public void Refresh()
        {
            if (Busy) return;
            var c = app.Cfg;
            var sp = new StackPanel();

            // ---- Clawds
            var clawds = new StackPanel();
            clawds.Children.Add(Ui.Setting(L.T("Clawds em cima da barra de tarefas", "Clawds above the taskbar"), L.T("Fora da Home, eles ficam no canto da tela. Com a Home aberta, vão trabalhar no escritório.", "Outside Home they sit in the corner of the screen. With Home open, they go work in the office."),
                Ui.Toggle(c.Taskbar, delegate(bool v) { c.Taskbar = v; Changed(); })));
            clawds.Children.Add(Ui.Divider());
            clawds.Children.Add(Ui.Setting(L.T("Canto da tela", "Screen corner"), null,
                Ui.Segmented(new[] { L.T("Esquerdo", "Left"), L.T("Direito", "Right") }, c.Corner == "esquerdo" ? 0 : 1, delegate(int i) { c.Corner = i == 0 ? "esquerdo" : "direito"; Changed(); Refresh(); })));
            clawds.Children.Add(Ui.Divider());
            clawds.Children.Add(Ui.Setting(L.T("Brincadeiras na barra de tarefas", "Games on the taskbar"), L.T("De tempo em tempo, os Clawds livres brincam juntos.", "Every now and then, idle Clawds play together."),
                Ui.Toggle(c.Events, delegate(bool v) { c.Events = v; Changed(); })));
            sp.Children.Add(Ui.Section("Clawds", null, clawds));

            // ---- avisos
            var n = c.Notify;
            var notif = new StackPanel();
            notif.Children.Add(Ui.Setting(L.T("Avisos do Windows", "Windows notifications"), L.T("Quando uma sessão precisa de você. Não avisa o que você já está vendo: a Home aberta ou o Claude da conta na frente.", "When a session needs you. It doesn't notify about what you're already looking at: Home open, or that account's Claude in front."),
                Ui.Toggle(n.On, delegate(bool v) { n.On = v; Changed(); Refresh(); })));
            if (n.On)
            {
                notif.Children.Add(Ui.Divider());
                notif.Children.Add(Ui.Setting(L.T("Esperando você", "Waiting for you"), L.T("Uma pergunta, um plano para aprovar ou um pedido de permissão.", "A question, a plan to approve or a permission request."), Ui.Toggle(n.Waiting, delegate(bool v) { n.Waiting = v; Changed(); })));
                notif.Children.Add(Ui.Divider());
                notif.Children.Add(Ui.Setting(L.T("Terminou", "Finished"), null, Ui.Toggle(n.Done, delegate(bool v) { n.Done = v; Changed(); Refresh(); })));
                if (n.Done)
                {
                    int[] secs = { 0, 30, 60, 300 };
                    int sel = Array.IndexOf(secs, n.MinSecs);
                    notif.Children.Add(Ui.Setting(L.T("Só tarefas que levaram mais de", "Only tasks that took longer than"), L.T("Respostas rápidas não precisam de aviso.", "Quick answers don't need a notification."),
                        Ui.Segmented(new[] { L.T("Qualquer", "Any"), "30 s", "1 min", "5 min" }, sel < 0 ? 1 : sel, delegate(int i) { n.MinSecs = secs[i]; Changed(); Refresh(); })));
                }
                notif.Children.Add(Ui.Divider());
                notif.Children.Add(Ui.Setting(L.T("Deu erro", "Error"), null, Ui.Toggle(n.Error, delegate(bool v) { n.Error = v; Changed(); })));
                notif.Children.Add(Ui.Divider());
                double now = Data.NowMs();
                bool quiet = n.QuietUntil > now;
                DateTime until = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMilliseconds(n.QuietUntil).ToLocalTime();
                double tomorrow = (DateTime.Today.AddDays(1).AddHours(8).ToUniversalTime() - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds;
                notif.Children.Add(Ui.Setting(L.T("Não perturbe", "Do not disturb"), quiet ? L.T("Ligado até ", "On until ") + until.ToString(until.Date == DateTime.Today ? "HH:mm" : "ddd HH:mm", L.Culture) + "." : L.T("Pausa os avisos por um tempo.", "Pauses notifications for a while."),
                    Ui.Segmented(new[] { L.T("Desligado", "Off"), L.T("1 hora", "1 hour"), L.T("Até amanhã", "Until tomorrow") }, !quiet ? 0 : Math.Abs(n.QuietUntil - tomorrow) < 1000 ? 2 : 1,
                        delegate(int i) { n.QuietUntil = i == 0 ? 0 : i == 1 ? Data.NowMs() + 3600000 : tomorrow; Changed(); Refresh(); })));
            }
            sp.Children.Add(Ui.Section(L.T("Avisos", "Notifications"), null, notif));

            // ---- mod
            sp.Children.Add(Ui.Section(L.T("Mod do Claude Code", "Claude Code mod"), L.T("É o mod que conta para o app o que cada sessão está fazendo, aplica o tema de cada conta e põe um Clawd dentro do Claude.", "The mod tells the app what each session is doing, applies each account's theme and puts a Clawd inside Claude."), BuildMod()));

            // ---- sessões de fora deste PC
            sp.Children.Add(Ui.Section(L.T("Nuvem e outros PCs", "Cloud and other PCs"), L.T("Sessões na nuvem do claude.ai (app, web, celular, tarefas agendadas) e em outros computadores também aparecem, com uma nuvenzinha. Elas mandam o estado para um Gist secreto do GitHub, e o app lê de lá a cada 15 segundos.", "Sessions in the claude.ai cloud (app, web, phone, scheduled tasks) and on other computers show up too, with a little cloud. They send their status to a secret GitHub Gist, and the app reads it every 15 seconds."), BuildRelay(c)));

            // ---- inicialização e atualizações
            var sys = new StackPanel();
            bool startup = AppPaths.StartsWithWindows;
            sys.Children.Add(Ui.Setting(L.T("Abrir com o Windows", "Start with Windows"), L.T("Começa em segundo plano, na bandeja, sem abrir a janela.", "Starts in the background, in the tray, without opening the window."),
                Ui.Toggle(startup, delegate(bool v)
                {
                    try { AppPaths.SetStartup(v, System.Reflection.Assembly.GetExecutingAssembly().Location); }
                    catch (Exception e) { MessageBox.Show(Window.GetWindow(this), e.Message, AppPaths.Name); }
                })));
            sys.Children.Add(Ui.Divider());
            sys.Children.Add(Ui.Setting(L.T("Procurar atualizações sozinho", "Check for updates automatically"), L.T("Uma vez ao abrir e depois a cada 12 horas, no GitHub do projeto.", "Once at startup and then every 12 hours, on the project's GitHub."),
                Ui.Toggle(c.CheckUpdates, delegate(bool v) { c.CheckUpdates = v; Changed(); })));
            sys.Children.Add(Ui.Divider());
            sys.Children.Add(BuildUpdate());
            sp.Children.Add(Ui.Section(L.T("Sistema", "System"), null, sys));

            // ---- idioma
            int lang = c.Lang == "pt" ? 1 : c.Lang == "en" ? 2 : 0;
            sp.Children.Add(Ui.Section(L.T("Idioma", "Language"), null, Ui.Setting(L.T("Idioma do app", "App language"),
                L.T("Automático segue o idioma do Windows. Trocar reinicia o Clawdesk.", "Automatic follows the Windows language. Changing it restarts Clawdesk."),
                Ui.Segmented(new[] { L.T("Automático", "Automatic"), "Português", "English" }, lang, delegate(int i) { app.ChangeLanguage(i == 1 ? "pt" : i == 2 ? "en" : "auto"); }))));

            // ---- dados
            var data = new WrapPanel();
            data.Children.Add(Ui.Button(Ui.IcFolder, L.T("Pasta de dados", "Data folder"), null, delegate { Open(Cfg.Dir); }));
            data.Children.Add(Ui.Button(Ui.IcFolder, L.T("Pasta de skins", "Skins folder"), null, delegate { Skin.WriteGuide(Cfg.SkinsDir); Open(Cfg.SkinsDir); }));
            data.Children.Add(Ui.Button(Ui.IcOpen, L.T("Página do projeto", "Project page"), null, delegate { Open("https://github.com/" + AppPaths.Repo); }));
            sp.Children.Add(Ui.Section(L.T("Dados e privacidade", "Data and privacy"), L.T("Tudo fica nesta máquina, em .claude\\clawd: as escolhas, o histórico e o estado das sessões (nunca os comandos, caminhos ou conteúdo). Nada é enviado para fora, além da consulta de versão nova no GitHub.", "Everything stays on this machine, in .claude\\clawd: your choices, the history and the session status (never commands, paths or content). Nothing is sent out, except the new-version check on GitHub."), data));

            Content = sp;
        }

        // Aceita o ID ou o endereço inteiro do Gist (gist.github.com/usuario/<id>).
        static string GistIdOf(string text)
        {
            string t = (text ?? "").Trim().TrimEnd('/');
            int slash = t.LastIndexOf('/');
            if (slash >= 0) t = t.Substring(slash + 1);
            int hash = t.IndexOf('#');
            if (hash >= 0) t = t.Substring(0, hash);
            return t;
        }

        static TextBlock Para(string text)
        {
            var t = Ui.T(text, 12.5, Ui.Dim, false);
            t.Margin = new Thickness(0, 0, 0, 8);
            return t;
        }

        static TextBox CodeBox(string text)
        {
            return new TextBox
            {
                Text = text, IsReadOnly = true, FontFamily = Ui.Mono, FontSize = 12.5, Padding = new Thickness(8, 5, 8, 5),
                Background = Ui.Soft, Foreground = Ui.Text, BorderBrush = Ui.Line, Margin = new Thickness(0, 0, 0, 8),
                HorizontalAlignment = HorizontalAlignment.Left, MinWidth = 320, TextWrapping = TextWrapping.Wrap,
            };
        }

        UIElement BuildRelay(Cfg c)
        {
            var sp = new StackPanel();
            sp.Children.Add(Para(L.T("1. Crie um Gist secreto com qualquer arquivo (por exemplo LEIA-ME.md) e cole aqui o endereço ou o ID dele.", "1. Create a secret Gist with any file (for example README.md) and paste its address or ID here.")));
            var idBox = new TextBox { Text = c.GistId, FontFamily = Ui.Mono, FontSize = 13, Padding = new Thickness(10, 7, 10, 7), Background = Ui.Soft, Foreground = Ui.Text, CaretBrush = Ui.Text, BorderBrush = Ui.Line, MinWidth = 320, MaxWidth = 460, HorizontalAlignment = HorizontalAlignment.Left };
            var idWarn = Ui.T("", 12, Ui.Warn, false);
            idWarn.Margin = new Thickness(0, 6, 0, 10);
            Action checkId = delegate
            {
                bool bad = c.GistId.Length > 0 && !c.HasRelay;
                idWarn.Text = bad ? L.T("Isso não parece o ID de um Gist (letras de a a f e números).", "That doesn't look like a Gist ID (letters a to f and digits).") : "";
                idWarn.Visibility = bad ? Visibility.Visible : Visibility.Collapsed;
            };
            idBox.TextChanged += delegate { c.GistId = GistIdOf(idBox.Text); checkId(); Changed(); };
            sp.Children.Add(idBox);
            sp.Children.Add(idWarn);
            checkId();
            sp.Children.Add(Para(L.T("2. Crie um token do GitHub só com a permissão \"gist\" e cole aqui. Ele fica em nuvem.json, nesta máquina, e serve para o app ler o Gist sem esbarrar no limite do GitHub.", "2. Create a GitHub token with only the \"gist\" permission and paste it here. It stays in nuvem.json, on this machine, and lets the app read the Gist without hitting GitHub's rate limit.")));
            var tok = new PasswordBox { Password = c.GistToken, FontFamily = Ui.Mono, FontSize = 13, Padding = new Thickness(10, 7, 10, 7), Background = Ui.Soft, Foreground = Ui.Text, CaretBrush = Ui.Text, BorderBrush = Ui.Line, MinWidth = 320, MaxWidth = 460, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 10) };
            tok.PasswordChanged += delegate { c.GistToken = tok.Password.Trim(); Changed(); };
            sp.Children.Add(tok);
            var links = new WrapPanel { Margin = new Thickness(0, 0, 0, 14) };
            links.Children.Add(Ui.Button(Ui.IcOpen, L.T("Criar o Gist", "Create the Gist"), null, delegate { Open("https://gist.github.com/"); }));
            links.Children.Add(Ui.Button(Ui.IcOpen, L.T("Criar o token", "Create the token"), null, delegate { Open("https://github.com/settings/tokens/new?scopes=gist&description=Clawdesk"); }));
            sp.Children.Add(links);
            sp.Children.Add(Para(L.T("Secreto não quer dizer trancado: quem tiver o endereço lê. Vão para lá o assunto de cada sessão, o nome da pasta, o estado, o modelo e o e-mail da conta, nunca comandos, caminhos ou conteúdo.", "Secret doesn't mean locked: anyone with the address can read it. It gets each session's topic, folder name, status, model and account email, never commands, paths or content.")));

            string id = c.HasRelay ? c.GistId : L.T("<id do gist>", "<gist id>");
            var cloud = Ui.T(L.T("Na nuvem", "In the cloud"), 13.5, Ui.Text, true);
            cloud.Margin = new Thickness(0, 10, 0, 6);
            sp.Children.Add(cloud);
            sp.Children.Add(Para(L.T("No ambiente da nuvem que as sessões usam (claude.ai/code, Configurações do ambiente): a variável abaixo, a rede com api.github.com liberado e um segredo de rede para api.github.com com o token (cabeçalho Authorization: Bearer), para o token não ficar dentro do container. O mod precisa estar ligado nessas sessões.", "In the cloud environment your sessions use (claude.ai/code, environment settings): the variable below, network access with api.github.com allowed, and a network secret for api.github.com with the token (Authorization: Bearer header), so the token doesn't live inside the container. The mod must be enabled in those sessions.")));
            sp.Children.Add(CodeBox("CLAWD_GIST_ID=" + id));
            var other = Ui.T(L.T("Em outro computador", "On another computer"), 13.5, Ui.Text, true);
            other.Margin = new Thickness(0, 10, 0, 6);
            sp.Children.Add(other);
            sp.Children.Add(Para(L.T("Num computador sem o app (Mac, Linux, outro Windows), com o mod instalado no Claude Code, defina antes de abrir o Claude:", "On a computer without the app (Mac, Linux, another Windows), with the mod installed in Claude Code, set before opening Claude:")));
            sp.Children.Add(CodeBox("CLAWD_GIST_ID=" + id + L.T("\nCLAWD_GITHUB_TOKEN=<o token>", "\nCLAWD_GITHUB_TOKEN=<the token>")));
            sp.Children.Add(Para(L.T("Num Windows que também tem o app, as sessões só aparecem nele; para mandar ao Gist mesmo assim, defina CLAWD_RELAY=1. CLAWD_RELAY=0 desliga o envio em qualquer máquina.", "On a Windows PC that also has the app, sessions only show up there; to send them to the Gist anyway, set CLAWD_RELAY=1. CLAWD_RELAY=0 turns sending off on any machine.")));
            return sp;
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
            if (app.ModBusy || st == null) { state = app.ModMessage ?? L.T("Verificando…", "Checking…"); color = Ui.Info; }
            else if (st.Claude == null) { state = L.T("Não achei o Claude Code neste computador.", "Claude Code was not found on this computer."); color = Ui.Warn; }
            else if (st.Installed) { state = L.T("Instalado", "Installed") + (st.Version.Length > 0 ? L.T(", versão ", ", version ") + st.Version : "") + "."; color = Ui.Good; }
            else { state = st.Error != null ? L.T("Não consegui conferir: ", "Couldn't check: ") + st.Error : L.T("Ainda não instalado.", "Not installed yet."); color = Ui.Warn; }
            var row = new DockPanel();
            var btns = new StackPanel { Orientation = Orientation.Horizontal };
            if (st != null && st.Claude != null && !app.ModBusy)
                btns.Children.Add(Ui.Button(Ui.IcDownload, st.Installed ? L.T("Atualizar o mod", "Update the mod") : L.T("Instalar o mod", "Install the mod"), st.Installed ? null : Ui.Accent, delegate { app.InstallMod(); }));
            var again = Ui.Button(Ui.IcSync, "", null, delegate { app.CheckMod(); });
            again.ToolTip = L.T("Conferir de novo", "Check again");
            again.Margin = new Thickness(0);
            if (!app.ModBusy) btns.Children.Add(again);
            DockPanel.SetDock(btns, Dock.Right);
            row.Children.Add(btns);
            row.Children.Add(Ui.Badge(state, color));
            sp.Children.Add(row);
            if (!app.ModBusy && app.ModMessage != null)
            {
                var m = Ui.T(app.ModMessage, 12.5, app.ModMessage.StartsWith(L.T("Pronto", "Done")) ? Ui.Good : Ui.Warn, false);
                m.Margin = new Thickness(0, 10, 0, 0);
                sp.Children.Add(m);
            }
            var hint = Ui.T(L.T("Também dá para instalar por dentro do Claude Code, com o comando abaixo (clique para copiar):", "You can also install it from inside Claude Code with the command below (click to copy):"), 12, Ui.Dim, false);
            hint.Margin = new Thickness(0, 14, 0, 6);
            sp.Children.Add(hint);
            var line = new Border { Background = Ui.Soft, CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 8, 12, 9), Cursor = Cursors.Hand, ToolTip = L.T("Copiar", "Copy"), HorizontalAlignment = HorizontalAlignment.Left };
            var lt = Ui.T(ModTool.ManualLine, 12, Ui.Text, false);
            lt.FontFamily = Ui.Mono;
            line.Child = lt;
            line.MouseLeftButtonUp += delegate
            {
                try
                {
                    Clipboard.SetText(ModTool.ManualLine);
                    var w = Window.GetWindow(this) as MainWindow;
                    if (w != null) w.Toast(L.T("Comando copiado.", "Command copied."));
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
            left.Children.Add(Ui.T(L.T("Versão ", "Version ") + AppPaths.Version, 13.5, Ui.Text, false));
            string note = u.Status.Length > 0 ? u.Status : u.LastCheck > DateTime.MinValue ? L.T("Última busca às ", "Last checked at ") + u.LastCheck.ToString("HH:mm") + "." : L.T("Ainda não procurei hoje.", "Haven't checked yet today.");
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
                btns.Children.Add(Ui.Button(Ui.IcDownload, L.T("Atualizar agora", "Update now"), Ui.Accent, delegate { u.Install(); }));
                if (u.Latest != app.Cfg.SkipVersion)
                    btns.Children.Add(Ui.Button(L.T("Pular esta versão", "Skip this version"), null, delegate { app.Cfg.SkipVersion = u.Latest; app.Changed(); app.SyncTray(); Refresh(); }));
            }
            else if (!u.Checking && !u.Installing) btns.Children.Add(Ui.Button(Ui.IcSync, L.T("Procurar agora", "Check now"), null, delegate { u.Check(true); }));
            Grid.SetColumn(btns, 1);
            g.Children.Add(btns);
            return g;
        }
    }

    // --------------------------------------------------------------- início --

    static class Program
    {
        static string logPath = Path.Combine(AppPaths.DataDir, "app.log");
        const string MutexName = "Local\\Clawdesk", OpenSignal = "Local\\Clawdesk.Abrir";

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
            L.LoadChoice();
            // --idioma pt|en força o idioma (as fotos do README saem em inglês assim)
            int li = Array.FindIndex(args, a => a.TrimStart('-', '/').Equals("idioma", StringComparison.OrdinalIgnoreCase));
            if (li >= 0 && li + 1 < args.Length) L.Choice = args[li + 1];
            // --depois <pid>: reinício (troca de idioma); espera a cópia anterior fechar
            int wi = Array.FindIndex(args, a => a.TrimStart('-', '/').Equals("depois", StringComparison.OrdinalIgnoreCase));
            if (wi >= 0 && wi + 1 < args.Length)
            {
                int pid;
                if (int.TryParse(args[wi + 1], out pid))
                {
                    try { Process.GetProcessById(pid).WaitForExit(15000); } catch { }
                }
            }
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
                    Log(demo ? "Clawdesk abriu (demo)" : "Clawdesk abriu " + AppPaths.Version);
                    app.Run();
                    Log("Clawdesk fechou");
                }
            }
        }
    }
}
