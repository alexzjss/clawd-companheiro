// O que o app e o instalador usam igual: onde as coisas ficam, os atalhos do Windows e o
// mod do Claude Code (achar o `claude`, ver se o mod está instalado, instalar ou atualizar).
// Só System: o instalador compila este arquivo junto com o ClawdSetup.cs.

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace ClawdDesktop
{
    static class AppPaths
    {
        public const string Name = "Clawd Companheiro";
        public const string Repo = "alexzjss/clawd-companheiro";
        public const string Exe = "Clawd.exe";
        // os programas de antes da 1.2, que o app único substituiu
        public static readonly string[] OldExes = { "ClawdDesktop.exe", "ClawdCentral.exe" };

        static string Profile { get { return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile); } }
        public static string DataDir { get { return Path.Combine(Profile, ".claude", "clawd"); } }
        public static string AppDir { get { return Path.Combine(DataDir, "app"); } }
        public static string Menu { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft", "Windows", "Start Menu", "Programs"); } }
        public static string MenuLnk { get { return Path.Combine(Menu, Name + ".lnk"); } }
        public static string StartupLnk { get { return Path.Combine(Menu, "Startup", Name + ".lnk"); } }
        public static string DesktopLnk { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), Name + ".lnk"); } }
        // atalhos de antes da 1.2
        public static readonly string[] OldLnks = { "Clawd.lnk", "Central dos Clawds.lnk", Path.Combine("Startup", "Clawd.lnk") };

        public static string Version
        {
            get
            {
                var a = Assembly.GetExecutingAssembly().GetName().Version;
                return a.Major + "." + a.Minor + "." + Math.Max(0, a.Build);
            }
        }

        public static bool StartsWithWindows { get { return File.Exists(StartupLnk) || File.Exists(Path.Combine(Menu, "Startup", "Clawd.lnk")); } }

        public static void Shortcut(string lnk, string target, string args, string text)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(lnk));
            dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
            dynamic s = shell.CreateShortcut(lnk);
            s.TargetPath = target;
            s.Arguments = args ?? "";
            s.WorkingDirectory = Path.GetDirectoryName(target);
            s.Description = text;
            s.IconLocation = target + ",0";
            s.Save();
        }

        // Liga ou desliga a abertura junto com o Windows (em segundo plano, sem janela).
        public static void SetStartup(bool on, string exe)
        {
            string old = Path.Combine(Menu, "Startup", "Clawd.lnk");
            if (File.Exists(old)) File.Delete(old);
            if (on) Shortcut(StartupLnk, exe, "--fundo", "Os Clawds e os avisos das sessões do Claude");
            else if (File.Exists(StartupLnk)) File.Delete(StartupLnk);
        }
    }

    sealed class ModStatus
    {
        public string Claude;      // caminho do claude, ou null se não achou
        public bool Installed;
        public string Version = "";
        public string Error;
    }

    // O mod do Claude Code, pelo comando `claude plugin`.
    static class ModTool
    {
        public const string Plugin = "clawd-companheiro";
        public const string Market = "clawd-companheiro";
        public const string ManualLine = "/plugin install clawd-companheiro --marketplace alexzjss/clawd-companheiro";

        // Onde o instalador do Claude Code costuma deixar o `claude`.
        public static string FindClaude()
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var places = new[]
            {
                Path.Combine(home, ".local", "bin", "claude.exe"),
                Path.Combine(appData, "npm", "claude.cmd"),
                Path.Combine(local, "Programs", "claude", "claude.exe"),
                Path.Combine(local, "Programs", "claude-code", "claude.exe"),
            };
            foreach (string p in places) if (File.Exists(p)) return p;
            foreach (string dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';'))
            {
                if (dir.Trim().Length == 0) continue;
                foreach (string name in new[] { "claude.exe", "claude.cmd" })
                {
                    try
                    {
                        string p = Path.Combine(dir.Trim().Trim('"'), name);
                        // o app do Claude Desktop também tem um Claude.exe; esse não serve
                        if (File.Exists(p) && p.IndexOf("AnthropicClaude", StringComparison.OrdinalIgnoreCase) < 0 && p.IndexOf("WindowsApps", StringComparison.OrdinalIgnoreCase) < 0) return p;
                    }
                    catch { }
                }
            }
            return null;
        }

        // Roda o claude escondido e devolve o código de saída e o que ele escreveu.
        public static int Run(string claude, string args, int timeoutMs, out string output)
        {
            var psi = claude.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase)
                ? new ProcessStartInfo("cmd.exe", "/d /s /c \"\"" + claude + "\" " + args + "\"")
                : new ProcessStartInfo(claude, args);
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.RedirectStandardInput = true;
            psi.StandardOutputEncoding = Encoding.UTF8;
            psi.StandardErrorEncoding = Encoding.UTF8;
            var sb = new StringBuilder();
            using (var p = new Process { StartInfo = psi })
            {
                p.OutputDataReceived += delegate(object s, DataReceivedEventArgs e) { if (e.Data != null) lock (sb) sb.AppendLine(e.Data); };
                p.ErrorDataReceived += delegate(object s, DataReceivedEventArgs e) { if (e.Data != null) lock (sb) sb.AppendLine(e.Data); };
                p.Start();
                p.StandardInput.Close();
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
                if (!p.WaitForExit(timeoutMs))
                {
                    try { p.Kill(); } catch { }
                    lock (sb) output = sb.ToString() + "\n(o claude demorou demais e foi parado)";
                    return -1;
                }
                p.WaitForExit();
                lock (sb) output = sb.ToString();
                return p.ExitCode;
            }
        }

        public static ModStatus Status()
        {
            var st = new ModStatus();
            try
            {
                st.Claude = FindClaude();
                if (st.Claude == null) return st;
                string text;
                int code = Run(st.Claude, "plugin list --json", 60000, out text);
                if (code != 0) { st.Error = Last(text); return st; }
                st.Installed = text.IndexOf("\"" + Plugin, StringComparison.OrdinalIgnoreCase) >= 0 || text.IndexOf(Plugin + "@", StringComparison.OrdinalIgnoreCase) >= 0;
                var m = Regex.Match(text, Regex.Escape(Plugin) + "[^{}]*?\"version\"\\s*:\\s*\"([^\"]+)\"", RegexOptions.Singleline);
                if (m.Success) st.Version = m.Groups[1].Value;
            }
            catch (Exception e) { st.Error = e.Message; }
            return st;
        }

        // Instala o mod (ou atualiza, se já tiver). Devolve null se deu certo, ou o erro.
        public static string InstallOrUpdate(ModStatus st)
        {
            if (st == null || st.Claude == null) return "Não achei o Claude Code (o comando claude) neste computador.";
            string text;
            int code;
            if (st.Installed)
            {
                Run(st.Claude, "plugin marketplace update " + Market, 120000, out text);
                code = Run(st.Claude, "plugin update " + Plugin + "@" + Market, 180000, out text);
            }
            else code = Run(st.Claude, "plugin install " + Plugin + " --marketplace " + AppPaths.Repo + " -y", 180000, out text);
            return code == 0 ? null : Last(text);
        }

        static string Last(string text)
        {
            var lines = (text ?? "").Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
            return lines.Count == 0 ? "o claude terminou com erro, sem mensagem" : string.Join(" ", lines.Skip(Math.Max(0, lines.Count - 3)).ToArray());
        }
    }
}
