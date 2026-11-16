// Instalador do Clawdesk: um .exe só, com o programa dentro.
//
// Um assistente em quatro passos (boas-vindas, opções, instalação e pronto). Copia o
// Clawdesk.exe para ~/.claude/clawd/app, tira os programas de antes da 1.2, cria os atalhos,
// registra em "Aplicativos instalados" e, se a pessoa quiser, instala ou atualiza o mod do
// Claude Code. Rodar de novo atualiza. As escolhas da pessoa (config.json, skins, histórico)
// só são apagadas se ela pedir, na desinstalação.
//
//   ClawdeskSetup.exe                  o assistente
//   ClawdeskSetup.exe /quiet           instala sem perguntar (é o que a atualização automática usa)
//   ClawdeskSetup.exe /quiet /abrir    idem, e abre a janela no fim
//   ClawdeskSetup.exe /uninstall       remove (com /quiet, sem perguntar)

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Microsoft.Win32;
using Clawdesk;

namespace ClawdeskSetup
{
    sealed class Options
    {
        public bool Startup = true, DesktopIcon, Mod = true, Open = true, Background;
    }

    static class Installer
    {
        const string RegKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Clawdesk";
        // o registro de quando o app se chamava Clawd Companheiro
        const string OldRegKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\ClawdCompanheiro";
        static string Self { get { return Assembly.GetExecutingAssembly().Location; } }
        public static string Exe { get { return System.IO.Path.Combine(AppPaths.AppDir, AppPaths.Exe); } }
        public static string Kept { get { return System.IO.Path.Combine(AppPaths.AppDir, "ClawdeskSetup.exe"); } }

        public static bool IsInstalled { get { return File.Exists(Exe) || AppPaths.OldExes.Any(e => File.Exists(System.IO.Path.Combine(AppPaths.AppDir, e))); } }

        public static string InstalledVersion
        {
            get
            {
                try
                {
                    foreach (string key in new[] { RegKey, OldRegKey })
                        using (var k = Registry.CurrentUser.OpenSubKey(key))
                            if (k != null && k.GetValue("DisplayVersion") is string) return (string)k.GetValue("DisplayVersion");
                    return null;
                }
                catch { return null; }
            }
        }

        // "Windows 11 com .NET Framework 4.8": o Environment.OSVersion mente para quem não tem
        // manifesto, então a versão vem do registro.
        public static string SystemName
        {
            get
            {
                string win = "Windows", net = ".NET Framework 4";
                try
                {
                    using (var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                    {
                        int build;
                        if (k != null && int.TryParse(k.GetValue("CurrentBuildNumber") as string, out build)) win = build >= 22000 ? "Windows 11" : build >= 10240 ? "Windows 10" : "Windows";
                    }
                    using (var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full"))
                    {
                        object r = k != null ? k.GetValue("Release") : null;
                        if (r is int) net = (int)r >= 533320 ? ".NET Framework 4.8.1" : (int)r >= 528040 ? ".NET Framework 4.8" : (int)r >= 461808 ? ".NET Framework 4.7.2" : ".NET Framework 4";
                    }
                }
                catch { }
                return win + " com " + net;
            }
        }

        // O Claude Desktop, da loja ou do instalador comum.
        public static bool HasClaudeDesktop
        {
            get
            {
                string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                return Directory.Exists(System.IO.Path.Combine(local, "AnthropicClaude")) || Directory.Exists(System.IO.Path.Combine(roaming, "Claude"))
                    || Directory.Exists(System.IO.Path.Combine(local, "Packages")) && Directory.GetDirectories(System.IO.Path.Combine(local, "Packages"), "Claude_*").Length > 0;
            }
        }

        public static void StopPrograms()
        {
            foreach (string name in new[] { "Clawdesk" }.Concat(AppPaths.OldProcesses))
                foreach (var p in Process.GetProcessesByName(name))
                {
                    try { p.Kill(); p.WaitForExit(4000); } catch { }
                }
            Thread.Sleep(300);
        }

        public static void CopyProgram()
        {
            Directory.CreateDirectory(AppPaths.AppDir);
            using (Stream src = Assembly.GetExecutingAssembly().GetManifestResourceStream(AppPaths.Exe))
            {
                if (src == null) throw new InvalidOperationException("Este instalador veio sem o " + AppPaths.Exe + ".");
                // o arquivo pode estar preso por um instante depois de fechar o programa
                for (int i = 0; ; i++)
                {
                    try
                    {
                        using (var dst = File.Create(Exe)) src.CopyTo(dst);
                        break;
                    }
                    catch (IOException) { if (i >= 8) throw; Thread.Sleep(500); src.Position = 0; }
                }
            }
            // uma cópia do instalador fica junto, para o Windows conseguir desinstalar
            if (!string.Equals(System.IO.Path.GetFullPath(Self), Kept, StringComparison.OrdinalIgnoreCase)) File.Copy(Self, Kept, true);
        }

        public static void RemoveOld()
        {
            foreach (string exe in AppPaths.OldExes)
            {
                try { File.Delete(System.IO.Path.Combine(AppPaths.AppDir, exe)); } catch { }
            }
            foreach (string lnk in AppPaths.OldLnks)
            {
                try { File.Delete(System.IO.Path.Combine(AppPaths.Menu, lnk)); } catch { }
            }
            try { File.Delete(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Clawd Companheiro.lnk")); } catch { }
            try { Registry.CurrentUser.DeleteSubKey(OldRegKey, false); } catch { }
        }

        public static void Shortcuts(Options o)
        {
            AppPaths.Shortcut(AppPaths.MenuLnk, Exe, "", "Os Clawds e as sessões do Claude");
            AppPaths.SetStartup(o.Startup, Exe);
            if (o.DesktopIcon) AppPaths.Shortcut(AppPaths.DesktopLnk, Exe, "", "Os Clawds e as sessões do Claude");
        }

        public static void Register()
        {
            using (var k = Registry.CurrentUser.CreateSubKey(RegKey))
            {
                k.SetValue("DisplayName", AppPaths.Name);
                k.SetValue("DisplayVersion", AppPaths.Version);
                k.SetValue("Publisher", "alexzjss");
                k.SetValue("DisplayIcon", Exe);
                k.SetValue("InstallLocation", AppPaths.AppDir);
                k.SetValue("UninstallString", "\"" + Kept + "\" /uninstall");
                k.SetValue("QuietUninstallString", "\"" + Kept + "\" /uninstall /quiet");
                k.SetValue("URLInfoAbout", "https://github.com/" + AppPaths.Repo);
                k.SetValue("URLUpdateInfo", "https://github.com/" + AppPaths.Repo + "/releases");
                k.SetValue("NoModify", 1, RegistryValueKind.DWord);
                k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                k.SetValue("EstimatedSize", 1200, RegistryValueKind.DWord);
            }
        }

        public static void Launch(bool window)
        {
            Process.Start(new ProcessStartInfo(Exe, window ? "" : "--fundo") { UseShellExecute = true });
        }

        // Tudo de uma vez, sem janela.
        public static void Quiet(bool open)
        {
            bool fresh = !IsInstalled;
            var o = new Options { Startup = fresh || AppPaths.StartsWithWindows, DesktopIcon = File.Exists(AppPaths.DesktopLnk) };
            StopPrograms();
            CopyProgram();
            RemoveOld();
            Shortcuts(o);
            Register();
            // mod já instalado: atualiza junto, para os dois andarem na mesma versão
            try
            {
                var st = ModTool.Status();
                if (st.Installed || st.Legacy) ModTool.InstallOrUpdate(st);
            }
            catch { }
            Launch(open);
        }

        public static void Uninstall(bool wipeData)
        {
            StopPrograms();
            foreach (string lnk in new[] { AppPaths.MenuLnk, AppPaths.StartupLnk, AppPaths.DesktopLnk })
            {
                try { File.Delete(lnk); } catch { }
            }
            RemoveOld();
            try { File.Delete(Exe); } catch { }
            try { Registry.CurrentUser.DeleteSubKey(RegKey, false); } catch { }
            if (wipeData)
            {
                foreach (string f in new[] { "config.json", "historico.json", "app.log", "desktop.log", "central.log" })
                {
                    try { File.Delete(System.IO.Path.Combine(AppPaths.DataDir, f)); } catch { }
                }
                foreach (string d in new[] { "sessions", "skins" })
                {
                    try { Directory.Delete(System.IO.Path.Combine(AppPaths.DataDir, d), true); } catch { }
                }
                try
                {
                    foreach (string f in Directory.GetFiles(AppPaths.DataDir, "*.json")) File.Delete(f);
                }
                catch { }
            }
            // a cópia do instalador se apaga sozinha depois de fechar
            if (File.Exists(Kept))
            {
                var psi = new ProcessStartInfo("cmd.exe", "/c ping -n 3 127.0.0.1 >nul & del /q \"" + Kept + "\" & rmdir \"" + AppPaths.AppDir + "\"");
                psi.CreateNoWindow = true;
                psi.UseShellExecute = false;
                try { Process.Start(psi); } catch { }
            }
        }
    }

    // ---------------------------------------------------------------- janela --

    sealed class SetupWindow : Window
    {
        static Brush B(string hex) { var b = (Brush)new BrushConverter().ConvertFromString(hex); b.Freeze(); return b; }
        static readonly Brush Bg = B("#0f0e13"), Card = B("#1b1922"), Soft = B("#2a2734"), SoftHi = B("#353142"), Line = B("#2e2a39");
        static readonly Brush Text = B("#f3f1f7"), Dim = B("#a7a1b4"), Faint = B("#6f6a7c"), Good = B("#4ade80"), Warn = B("#f5a524"), Bad = B("#ef5350"), Accent = B("#d97757");
        static readonly FontFamily Icons = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets");

        readonly Grid page = new Grid();
        readonly StackPanel dots = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
        readonly StackPanel buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        readonly Options opt = new Options();
        readonly bool installed;
        readonly string oldVersion;
        string claude;
        bool working;

        static TextBlock T(string s, double size, Brush color, bool bold)
        {
            return new TextBlock { Text = s, FontSize = size, Foreground = color, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        }

        static TextBlock Ic(string glyph, double size, Brush color)
        {
            return new TextBlock { Text = glyph, FontFamily = Icons, FontSize = size, Foreground = color, VerticalAlignment = VerticalAlignment.Center };
        }

        static Border Button(string text, bool primary, Action click)
        {
            Brush bg = primary ? Accent : Soft, hi = primary ? B("#e48a6b") : SoftHi;
            var b = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(18, 8, 18, 9), Margin = new Thickness(8, 0, 0, 0), Background = bg, Cursor = Cursors.Hand };
            b.Child = new TextBlock { Text = text, FontSize = 13, FontWeight = primary ? FontWeights.SemiBold : FontWeights.Normal, Foreground = primary ? Brushes.White : Text };
            b.MouseEnter += delegate { b.Background = hi; };
            b.MouseLeave += delegate { b.Background = bg; };
            b.MouseLeftButtonUp += delegate { click(); };
            return b;
        }

        // Uma opção com interruptor, título e explicação.
        static FrameworkElement Option(string title, string note, bool value, bool enabled, Action<bool> set)
        {
            bool v = value;
            var knob = new Ellipse { Width = 14, Height = 14 };
            var track = new Border { Width = 40, Height = 22, CornerRadius = new CornerRadius(11), Padding = new Thickness(4, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center, Child = knob, Cursor = enabled ? Cursors.Hand : Cursors.Arrow };
            Action paint = delegate
            {
                track.Background = v ? Accent : SoftHi;
                knob.HorizontalAlignment = v ? HorizontalAlignment.Right : HorizontalAlignment.Left;
                knob.Fill = v ? Brushes.White : Dim;
            };
            paint();
            var g = new Grid { Margin = new Thickness(0, 0, 0, 14), Opacity = enabled ? 1 : 0.5 };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var left = new StackPanel { Margin = new Thickness(0, 0, 16, 0) };
            left.Children.Add(T(title, 13.5, Text, false));
            if (note != null) { var n = T(note, 12, Dim, false); n.Margin = new Thickness(0, 2, 0, 0); left.Children.Add(n); }
            g.Children.Add(left);
            Grid.SetColumn(track, 1);
            g.Children.Add(track);
            if (enabled)
            {
                g.Background = Brushes.Transparent;
                g.Cursor = Cursors.Hand;
                g.MouseLeftButtonUp += delegate { v = !v; paint(); set(v); };
            }
            return g;
        }

        public SetupWindow()
        {
            installed = Installer.IsInstalled;
            oldVersion = Installer.InstalledVersion;
            Title = (installed ? "Atualizar o " : "Instalar o ") + AppPaths.Name;
            Width = 560;
            Height = 520;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = Bg;
            FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
            UseLayoutRounding = true;
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);

            var root = new DockPanel();
            var foot = new Grid { Margin = new Thickness(30, 0, 30, 24) };
            foot.Children.Add(dots);
            foot.Children.Add(buttons);
            DockPanel.SetDock(foot, Dock.Bottom);
            root.Children.Add(foot);
            page.Margin = new Thickness(30, 28, 30, 16);
            root.Children.Add(page);
            Content = root;
            Closing += delegate(object s, System.ComponentModel.CancelEventArgs e) { if (working) e.Cancel = true; };

            claude = ModTool.FindClaude();
            opt.Startup = !installed || AppPaths.StartsWithWindows;
            opt.DesktopIcon = File.Exists(AppPaths.DesktopLnk);
            opt.Mod = claude != null;
            Welcome();
        }

        void Step(int i)
        {
            dots.Children.Clear();
            for (int k = 0; k < 4; k++)
                dots.Children.Add(new Border { Width = k == i ? 20 : 7, Height = 7, CornerRadius = new CornerRadius(4), Margin = new Thickness(0, 0, 6, 0), Background = k == i ? Accent : SoftHi });
            page.Children.Clear();
            buttons.Children.Clear();
        }

        static FrameworkElement Check(string glyph, Brush color, string text)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            var ic = Ic(glyph, 13, color);
            ic.Margin = new Thickness(0, 1, 10, 0);
            row.Children.Add(ic);
            row.Children.Add(T(text, 12.5, Dim, false));
            return row;
        }

        void Welcome()
        {
            Step(0);
            var sp = new StackPanel();
            sp.Children.Add(T(AppPaths.Name, 26, Text, true));
            string ver = installed && oldVersion != null && oldVersion != AppPaths.Version ? "atualizar da versão " + oldVersion + " para a " + AppPaths.Version
                : installed ? "versão " + AppPaths.Version + " (reinstalar)" : "versão " + AppPaths.Version;
            var v = T(ver, 13, Faint, false);
            v.Margin = new Thickness(0, 2, 0, 18);
            sp.Children.Add(v);
            sp.Children.Add(T("Um mascote em pixel art para cada conta do Claude, em cima da barra de tarefas, e uma janela com todas as sessões, estatísticas, temas e avisos quando uma sessão termina ou espera você.", 13.5, Text, false));
            var checks = new StackPanel { Margin = new Thickness(0, 22, 0, 0) };
            checks.Children.Add(Check("\uE73E", Good, Installer.SystemName));
            checks.Children.Add(Installer.HasClaudeDesktop ? Check("\uE73E", Good, "Claude Desktop encontrado") : Check("\uE7BA", Warn, "Não achei o Claude Desktop. Instale-o depois, em claude.ai/download"));
            checks.Children.Add(claude != null ? Check("\uE73E", Good, "Claude Code encontrado: dá para instalar o mod junto") : Check("\uE946", Faint, "Claude Code não encontrado: o mod pode ser instalado depois, pelo app"));
            if (installed) checks.Children.Add(Check("\uE946", Faint, "Suas contas, cores, skins e histórico continuam como estão."));
            sp.Children.Add(checks);
            page.Children.Add(sp);
            if (installed) buttons.Children.Add(Button("Remover…", false, Uninstall));
            buttons.Children.Add(Button("Cancelar", false, Close));
            buttons.Children.Add(Button("Continuar", true, Choose));
        }

        void Choose()
        {
            Step(1);
            var sp = new StackPanel();
            sp.Children.Add(T("Opções", 22, Text, true));
            var sub = T("Dá para mudar tudo isso depois, nas Configurações do app.", 12.5, Dim, false);
            sub.Margin = new Thickness(0, 4, 0, 22);
            sp.Children.Add(sub);
            sp.Children.Add(Option("Abrir com o Windows", "Começa na bandeja, sem janela, de olho nas sessões.", opt.Startup, true, delegate(bool x) { opt.Startup = x; }));
            sp.Children.Add(Option("Atalho na área de trabalho", null, opt.DesktopIcon, true, delegate(bool x) { opt.DesktopIcon = x; }));
            sp.Children.Add(Option(claude != null ? "Instalar ou atualizar o mod do Claude Code" : "Instalar o mod do Claude Code",
                claude != null ? "É ele que conta ao app o que cada sessão faz." : "Precisa do Claude Code instalado. Dá para fazer depois, pelo app.", opt.Mod, claude != null, delegate(bool x) { opt.Mod = x; }));
            sp.Children.Add(Option("Abrir o Clawdesk no fim", null, opt.Open, true, delegate(bool x) { opt.Open = x; }));
            page.Children.Add(sp);
            buttons.Children.Add(Button("Voltar", false, Welcome));
            buttons.Children.Add(Button(installed ? "Atualizar" : "Instalar", true, Install));
        }

        // ---- instalação

        sealed class Row
        {
            public TextBlock Icon, Text;
        }

        Row AddRow(StackPanel list, string text)
        {
            var g = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
            var r = new Row { Icon = Ic("\u25CB", 13, Faint), Text = T(text, 13.5, Dim, false) };
            r.Icon.Width = 22;
            g.Children.Add(r.Icon);
            g.Children.Add(r.Text);
            list.Children.Add(g);
            return r;
        }

        void Mark(Row r, int state, string extra)
        {
            Dispatcher.Invoke(new Action(delegate
            {
                r.Icon.Text = state == 0 ? "\u25CF" : state == 1 ? "\uE73E" : state == 2 ? "\uE7BA" : "\uE711";
                r.Icon.Foreground = state == 0 ? Accent : state == 1 ? Good : state == 2 ? Warn : Bad;
                r.Text.Foreground = state == 0 ? Text : Dim;
                if (extra != null) r.Text.Text += " — " + extra;
            }));
        }

        void Install()
        {
            Step(2);
            var sp = new StackPanel();
            sp.Children.Add(T(installed ? "Atualizando…" : "Instalando…", 22, Text, true));
            var list = new StackPanel { Margin = new Thickness(0, 22, 0, 0) };
            sp.Children.Add(list);
            page.Children.Add(sp);
            var steps = new List<Tuple<Row, Action>>();
            steps.Add(Tuple.Create(AddRow(list, "Fechar o Clawd que estiver aberto"), (Action)Installer.StopPrograms));
            steps.Add(Tuple.Create(AddRow(list, "Copiar o programa"), (Action)Installer.CopyProgram));
            steps.Add(Tuple.Create(AddRow(list, "Tirar os programas antigos"), (Action)Installer.RemoveOld));
            steps.Add(Tuple.Create(AddRow(list, "Criar os atalhos"), (Action)delegate { Installer.Shortcuts(opt); }));
            steps.Add(Tuple.Create(AddRow(list, "Registrar em Aplicativos instalados"), (Action)Installer.Register));
            Row mod = opt.Mod && claude != null ? AddRow(list, "Instalar o mod do Claude Code") : null;
            working = true;
            var o = opt;
            ThreadPool.QueueUserWorkItem(delegate
            {
                string fail = null, modFail = null;
                foreach (var s in steps)
                {
                    Mark(s.Item1, 0, null);
                    try { s.Item2(); Mark(s.Item1, 1, null); }
                    catch (Exception e) { fail = e.Message; Mark(s.Item1, 3, e.Message); break; }
                }
                if (fail == null && mod != null)
                {
                    Mark(mod, 0, null);
                    try
                    {
                        modFail = ModTool.InstallOrUpdate(ModTool.Status());
                        Mark(mod, modFail == null ? 1 : 2, modFail == null ? null : "não deu");
                    }
                    catch (Exception e) { modFail = e.Message; Mark(mod, 2, "não deu"); }
                }
                if (fail == null && o.Open) { try { Installer.Launch(true); } catch { } }
                else if (fail == null) { try { Installer.Launch(false); } catch { } }
                Dispatcher.BeginInvoke(new Action(delegate { working = false; Finish(fail, modFail, mod != null); }));
            });
        }

        void Finish(string fail, string modFail, bool triedMod)
        {
            Step(3);
            var sp = new StackPanel();
            if (fail != null)
            {
                sp.Children.Add(T("Não consegui terminar", 22, Text, true));
                var e = T(fail, 13, Bad, false);
                e.Margin = new Thickness(0, 10, 0, 0);
                sp.Children.Add(e);
                var n = T("Feche o Clawdesk se ele estiver aberto e tente de novo. Se continuar, abra uma issue no GitHub do projeto com essa mensagem.", 12.5, Dim, false);
                n.Margin = new Thickness(0, 10, 0, 0);
                sp.Children.Add(n);
                page.Children.Add(sp);
                buttons.Children.Add(Button("Fechar", false, Close));
                buttons.Children.Add(Button("Tentar de novo", true, Install));
                return;
            }
            sp.Children.Add(T(installed ? "Atualizado!" : "Instalado!", 26, Text, true));
            var msg = T(opt.Open ? "O Clawdesk já abriu. Ele fica na bandeja do Windows, perto do relógio; feche a janela à vontade." : "O Clawdesk está rodando na bandeja do Windows, perto do relógio.", 13.5, Text, false);
            msg.Margin = new Thickness(0, 10, 0, 0);
            sp.Children.Add(msg);
            if (triedMod && modFail == null)
            {
                var m = T("O mod foi instalado. Reinicie o Claude para ele carregar.", 13, Good, false);
                m.Margin = new Thickness(0, 14, 0, 0);
                sp.Children.Add(m);
            }
            else
            {
                var m = T((triedMod ? "Não consegui instalar o mod (" + modFail + "). " : "Falta o mod. ") + "Pelo app, em Configurações, ou dentro do Claude Code com o comando abaixo (clique para copiar):", 12.5, triedMod ? Warn : Dim, false);
                m.Margin = new Thickness(0, 16, 0, 6);
                sp.Children.Add(m);
                var line = new Border { Background = Soft, CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 8, 12, 9), Cursor = Cursors.Hand, ToolTip = "Clique para copiar" };
                var lt = new TextBlock { Text = ModTool.ManualLine, FontFamily = new FontFamily("Cascadia Mono, Consolas"), FontSize = 12, Foreground = Text, TextWrapping = TextWrapping.Wrap };
                line.Child = lt;
                line.MouseLeftButtonUp += delegate { try { Clipboard.SetText(ModTool.ManualLine); lt.Text = "Copiado!"; } catch { } };
                sp.Children.Add(line);
            }
            var tip = T("O Windows pode avisar que o instalador não é assinado. Para atualizar das próximas vezes, o próprio app avisa e faz tudo sozinho.", 12, Faint, false);
            tip.Margin = new Thickness(0, 18, 0, 0);
            sp.Children.Add(tip);
            page.Children.Add(sp);
            if (!opt.Open) buttons.Children.Add(Button("Abrir agora", false, delegate { try { Installer.Launch(true); } catch { } Close(); }));
            buttons.Children.Add(Button("Concluir", true, Close));
        }

        // ---- fotos (as telas do assistente em arquivos, para conferir o visual no CI)

        void Shoot(string file)
        {
            var root = (FrameworkElement)Content;
            root.UpdateLayout();
            Dispatcher.Invoke(new Action(delegate { }), System.Windows.Threading.DispatcherPriority.ContextIdle);
            var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap((int)(root.ActualWidth * 1.5), (int)(root.ActualHeight * 1.5), 144, 144, PixelFormats.Pbgra32);
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen()) dc.DrawRectangle(Bg, null, new Rect(0, 0, root.ActualWidth, root.ActualHeight));
            bmp.Render(dv);
            bmp.Render(root);
            var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
            enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
            using (var fs = File.Create(file)) enc.Save(fs);
        }

        public void Photos(string dir)
        {
            Shoot(System.IO.Path.Combine(dir, "setup-1-inicio.png"));
            Choose();
            Shoot(System.IO.Path.Combine(dir, "setup-2-opcoes.png"));
            Finish(null, "exemplo de erro", true);
            Shoot(System.IO.Path.Combine(dir, "setup-4-pronto.png"));
        }

        // ---- remover

        void Uninstall()
        {
            Step(1);
            bool wipe = false;
            var sp = new StackPanel();
            sp.Children.Add(T("Remover o " + AppPaths.Name, 22, Text, true));
            var n = T("Tira o programa, os atalhos e o registro em Aplicativos instalados. O mod do Claude Code fica; para tirar, use /plugin no Claude Code.", 13, Dim, false);
            n.Margin = new Thickness(0, 6, 0, 22);
            sp.Children.Add(n);
            sp.Children.Add(Option("Apagar também as minhas escolhas", "Contas, cores, skins próprias e o histórico das estatísticas, em .claude\\clawd.", false, true, delegate(bool x) { wipe = x; }));
            page.Children.Add(sp);
            buttons.Children.Add(Button("Voltar", false, Welcome));
            buttons.Children.Add(Button("Remover", true, delegate
            {
                try
                {
                    Installer.Uninstall(wipe);
                    Step(3);
                    page.Children.Add(T("Removido. Até a próxima!", 22, Text, true));
                    buttons.Children.Add(Button("Fechar", true, Close));
                }
                catch (Exception e) { MessageBox.Show(this, e.Message, AppPaths.Name, MessageBoxButton.OK, MessageBoxImage.Warning); }
            }));
        }
    }

    static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            Func<string, bool> has = name => args.Any(a => a.TrimStart('-', '/').Equals(name, StringComparison.OrdinalIgnoreCase));
            bool quiet = has("quiet") || has("silent");
            int fi = Array.FindIndex(args, a => a.TrimStart('-', '/').Equals("fotos", StringComparison.OrdinalIgnoreCase));
            if (fi >= 0)
            {
                string dir = fi + 1 < args.Length ? args[fi + 1] : ".";
                Directory.CreateDirectory(dir);
                var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                int code = 0;
                app.Startup += delegate
                {
                    try
                    {
                        var w = new SetupWindow { WindowStartupLocation = WindowStartupLocation.Manual, Left = -4000, Top = 0, ShowInTaskbar = false, ShowActivated = false };
                        w.Show();
                        w.Photos(dir);
                        w.Close();
                    }
                    catch (Exception e) { try { File.WriteAllText(System.IO.Path.Combine(dir, "erro-setup.txt"), e.ToString()); } catch { } code = 3; }
                    app.Shutdown();
                };
                app.Run();
                return code;
            }
            bool created;
            using (var mutex = new Mutex(true, "Local\\ClawdeskSetup", out created))
            {
                if (!created) return 1;
                try
                {
                    if (has("uninstall"))
                    {
                        if (!quiet && MessageBox.Show("Remover o " + AppPaths.Name + "?\n\nSuas cores, contas, skins e histórico ficam guardados em .claude\\clawd.", AppPaths.Name, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return 1;
                        Installer.Uninstall(false);
                        if (!quiet) MessageBox.Show("Removido.", AppPaths.Name);
                        return 0;
                    }
                    if (quiet)
                    {
                        Installer.Quiet(has("abrir"));
                        return 0;
                    }
                    new Application().Run(new SetupWindow());
                    return 0;
                }
                catch (Exception e)
                {
                    if (!quiet) MessageBox.Show(e.Message, AppPaths.Name, MessageBoxButton.OK, MessageBoxImage.Error);
                    return 2;
                }
            }
        }
    }
}
