// Instalador do Clawd Companheiro: um .exe só, com os dois programas embutidos.
//
// Copia ClawdDesktop.exe e ClawdCentral.exe para ~/.claude/clawd/app, cria os atalhos no
// menu Iniciar (e na inicialização do Windows, se a pessoa quiser), registra o programa em
// "Aplicativos instalados" e abre a Central. Rodar de novo atualiza; /uninstall remove.
// As escolhas da pessoa (config.json, skins) nunca são apagadas.
//
//   ClawdSetup.exe               janela do instalador
//   ClawdSetup.exe /quiet        instala sem perguntar
//   ClawdSetup.exe /uninstall    remove (com /quiet, sem perguntar)

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;

[assembly: AssemblyTitle("Instalador do Clawd Companheiro")]
[assembly: AssemblyProduct("Clawd Companheiro")]
[assembly: AssemblyVersion("1.1.0.0")]
[assembly: AssemblyFileVersion("1.1.0.0")]

namespace ClawdSetup
{
    static class Installer
    {
        public const string Name = "Clawd Companheiro";
        public const string ModLine = "/plugin install clawd-companheiro --marketplace alexzjss/clawd-companheiro";
        const string RegKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\ClawdCompanheiro";
        static readonly string[] Programs = { "ClawdDesktop.exe", "ClawdCentral.exe" };

        public static string AppDir { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "clawd", "app"); } }
        static string Menu { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft", "Windows", "Start Menu", "Programs"); } }
        static string Self { get { return Assembly.GetExecutingAssembly().Location; } }
        public static string Version { get { return Assembly.GetExecutingAssembly().GetName().Version.ToString(3); } }
        public static bool IsInstalled { get { return File.Exists(Path.Combine(AppDir, "ClawdCentral.exe")); } }
        public static bool StartsWithWindows { get { return File.Exists(Path.Combine(Menu, "Startup", "Clawd.lnk")); } }

        static void StopPrograms()
        {
            foreach (string exe in Programs)
                foreach (var p in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(exe)))
                {
                    try { p.Kill(); p.WaitForExit(3000); } catch { }
                }
        }

        static void Shortcut(string lnk, string exe, string text)
        {
            dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
            dynamic s = shell.CreateShortcut(lnk);
            s.TargetPath = Path.Combine(AppDir, exe);
            s.WorkingDirectory = AppDir;
            s.Description = text;
            s.Save();
        }

        public static void Install(bool startup, bool open)
        {
            StopPrograms();
            Directory.CreateDirectory(AppDir);
            var asm = Assembly.GetExecutingAssembly();
            foreach (string exe in Programs)
            {
                using (Stream src = asm.GetManifestResourceStream(exe))
                {
                    if (src == null) throw new InvalidOperationException("Este instalador veio sem o " + exe + ".");
                    using (var dst = File.Create(Path.Combine(AppDir, exe))) src.CopyTo(dst);
                }
            }
            // uma cópia do instalador fica junto, para o Windows conseguir desinstalar
            string kept = Path.Combine(AppDir, "ClawdSetup.exe");
            if (!string.Equals(Path.GetFullPath(Self), kept, StringComparison.OrdinalIgnoreCase)) File.Copy(Self, kept, true);

            Shortcut(Path.Combine(Menu, "Clawd.lnk"), "ClawdDesktop.exe", "Os Clawds na área de trabalho");
            Shortcut(Path.Combine(Menu, "Central dos Clawds.lnk"), "ClawdCentral.exe", "Contas do Claude, temas e Clawds");
            string boot = Path.Combine(Menu, "Startup", "Clawd.lnk");
            if (startup) Shortcut(boot, "ClawdDesktop.exe", "Os Clawds na área de trabalho");
            else if (File.Exists(boot)) File.Delete(boot);

            using (var k = Registry.CurrentUser.CreateSubKey(RegKey))
            {
                k.SetValue("DisplayName", Name);
                k.SetValue("DisplayVersion", Version);
                k.SetValue("Publisher", "alexzjss");
                k.SetValue("DisplayIcon", Path.Combine(AppDir, "ClawdCentral.exe"));
                k.SetValue("InstallLocation", AppDir);
                k.SetValue("UninstallString", "\"" + kept + "\" /uninstall");
                k.SetValue("QuietUninstallString", "\"" + kept + "\" /uninstall /quiet");
                k.SetValue("URLInfoAbout", "https://github.com/alexzjss/clawd-companheiro");
                k.SetValue("NoModify", 1, RegistryValueKind.DWord);
                k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                k.SetValue("EstimatedSize", 1024, RegistryValueKind.DWord);
            }

            Process.Start(new ProcessStartInfo(Path.Combine(AppDir, "ClawdDesktop.exe")) { UseShellExecute = true });
            if (open) Process.Start(new ProcessStartInfo(Path.Combine(AppDir, "ClawdCentral.exe")) { UseShellExecute = true });
        }

        public static void Uninstall()
        {
            StopPrograms();
            foreach (string lnk in new[] { Path.Combine(Menu, "Clawd.lnk"), Path.Combine(Menu, "Central dos Clawds.lnk"), Path.Combine(Menu, "Startup", "Clawd.lnk") })
            {
                try { File.Delete(lnk); } catch { }
            }
            foreach (string exe in Programs)
            {
                try { File.Delete(Path.Combine(AppDir, exe)); } catch { }
            }
            try { Registry.CurrentUser.DeleteSubKey(RegKey, false); } catch { }
            // a cópia do instalador se apaga sozinha depois de fechar
            string kept = Path.Combine(AppDir, "ClawdSetup.exe");
            if (File.Exists(kept))
            {
                var psi = new ProcessStartInfo("cmd.exe", "/c ping -n 3 127.0.0.1 >nul & del /q \"" + kept + "\"");
                psi.CreateNoWindow = true;
                psi.UseShellExecute = false;
                try { Process.Start(psi); } catch { }
            }
        }
    }

    sealed class SetupWindow : Window
    {
        static Brush B(string hex) { var b = (Brush)new BrushConverter().ConvertFromString(hex); b.Freeze(); return b; }
        static readonly Brush Bg = B("#121016"), Soft = B("#2a2534"), Text = B("#f2f0f5"), Dim = B("#a9a3b6"), Good = B("#5ee08a");

        readonly TextBlock status = new TextBlock();
        readonly CheckBox startup = new CheckBox();

        static TextBlock T(string s, double size, Brush color, bool bold)
        {
            return new TextBlock { Text = s, FontSize = size, Foreground = color, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, TextWrapping = TextWrapping.Wrap };
        }

        static Border Button(string text, bool primary, Action click)
        {
            var b = new Border { CornerRadius = new CornerRadius(6), Padding = new Thickness(16, 8, 16, 9), Margin = new Thickness(0, 0, 8, 0), Background = primary ? Brushes.White : Soft, Cursor = Cursors.Hand };
            b.Child = new TextBlock { Text = text, FontSize = 13, FontWeight = primary ? FontWeights.SemiBold : FontWeights.Normal, Foreground = primary ? Bg : Text };
            b.MouseEnter += delegate { b.Opacity = 0.85; };
            b.MouseLeave += delegate { b.Opacity = 1; };
            b.MouseLeftButtonUp += delegate { click(); };
            return b;
        }

        public SetupWindow()
        {
            bool installed = Installer.IsInstalled;
            Title = "Instalar o " + Installer.Name;
            Width = 500;
            SizeToContent = SizeToContent.Height;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = Bg;
            FontFamily = new FontFamily("Segoe UI");
            UseLayoutRounding = true;

            var sp = new StackPanel { Margin = new Thickness(28, 24, 28, 24) };
            sp.Children.Add(T(Installer.Name, 22, Text, true));
            var ver = T("versão " + Installer.Version, 12, Dim, false);
            ver.Margin = new Thickness(0, 2, 0, 14);
            sp.Children.Add(ver);
            sp.Children.Add(T("Põe um Clawd por conta do Claude em cima da barra de tarefas e instala a Central dos Clawds, onde você escolhe contas, temas, cores e skins.", 13, Text, false));

            startup.Content = T("Abrir os Clawds junto com o Windows", 13, Text, false);
            startup.IsChecked = !installed || Installer.StartsWithWindows;
            startup.Margin = new Thickness(0, 16, 0, 0);
            startup.VerticalContentAlignment = VerticalAlignment.Center;
            sp.Children.Add(startup);

            var mod = T("Falta só o mod, que mostra o Clawd dentro do Claude e conta aos Clawds o que cada sessão faz. No Claude Code, rode:", 12.5, Dim, false);
            mod.Margin = new Thickness(0, 18, 0, 6);
            sp.Children.Add(mod);
            var line = new Border { Background = Soft, CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 7, 10, 8), Cursor = Cursors.Hand, ToolTip = "Clique para copiar" };
            line.Child = new TextBlock { Text = Installer.ModLine, FontFamily = new FontFamily("Consolas"), FontSize = 12, Foreground = Text, TextWrapping = TextWrapping.Wrap };
            line.MouseLeftButtonUp += delegate
            {
                try { Clipboard.SetText(Installer.ModLine); Say("Comando copiado.", true); } catch { }
            };
            sp.Children.Add(line);

            status.FontSize = 12.5;
            status.TextWrapping = TextWrapping.Wrap;
            status.Margin = new Thickness(0, 14, 0, 0);
            status.Visibility = Visibility.Collapsed;
            sp.Children.Add(status);

            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 18, 0, 0) };
            row.Children.Add(Button(installed ? "Atualizar" : "Instalar", true, DoInstall));
            if (installed) row.Children.Add(Button("Remover", false, DoUninstall));
            row.Children.Add(Button("Fechar", false, Close));
            sp.Children.Add(row);
            Content = sp;
        }

        void Say(string text, bool ok)
        {
            status.Text = text;
            status.Foreground = ok ? Good : B("#f5a524");
            status.Visibility = Visibility.Visible;
        }

        void DoInstall()
        {
            try
            {
                Installer.Install(startup.IsChecked == true, true);
                Say("Instalado. Os Clawds já estão na barra de tarefas e a Central abriu; pode fechar esta janela.", true);
            }
            catch (Exception e) { Say("Não consegui instalar: " + e.Message, false); }
        }

        void DoUninstall()
        {
            if (MessageBox.Show(this, "Remover o " + Installer.Name + "?\n\nSuas cores, contas e skins ficam guardadas em .claude\\clawd.", Title, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            Installer.Uninstall();
            Say("Removido.", true);
        }
    }

    static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            Func<string, bool> has = name => args.Any(a => a.TrimStart('-', '/').Equals(name, StringComparison.OrdinalIgnoreCase));
            bool quiet = has("quiet") || has("silent");
            bool created;
            using (var mutex = new Mutex(true, "Local\\ClawdSetup", out created))
            {
                if (!created) return 1;
                try
                {
                    if (has("uninstall"))
                    {
                        if (!quiet && MessageBox.Show("Remover o " + Installer.Name + "?\n\nSuas cores, contas e skins ficam guardadas em .claude\\clawd.", Installer.Name, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return 1;
                        Installer.Uninstall();
                        if (!quiet) MessageBox.Show("Removido.", Installer.Name);
                        return 0;
                    }
                    if (quiet)
                    {
                        Installer.Install(!Installer.IsInstalled || Installer.StartsWithWindows, false);
                        return 0;
                    }
                    new Application().Run(new SetupWindow());
                    return 0;
                }
                catch (Exception e)
                {
                    if (!quiet) MessageBox.Show(e.Message, Installer.Name, MessageBoxButton.OK, MessageBoxImage.Error);
                    return 2;
                }
            }
        }
    }
}
