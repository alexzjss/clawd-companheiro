// Os serviços de fundo do app: o ícone na bandeja, os avisos do Windows quando uma sessão
// termina, dá erro ou espera você, e a busca de versão nova no GitHub.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace Clawdesk
{
    // ------------------------------------------------------------- bandeja --

    sealed class Tray : IDisposable
    {
        readonly Forms.NotifyIcon icon = new Forms.NotifyIcon();
        readonly Forms.ToolStripMenuItem taskbar, quiet, update;
        Action balloonClick;

        public event Action<string> OpenPage;   // "home", "estatisticas", "contas", "configuracoes"
        public event Action ToggleTaskbar, ToggleQuiet, CheckUpdates, Quit;

        public Tray()
        {
            try { icon.Icon = System.Drawing.Icon.ExtractAssociatedIcon(System.Reflection.Assembly.GetExecutingAssembly().Location); }
            catch { icon.Icon = System.Drawing.SystemIcons.Application; }
            icon.Text = AppPaths.Name;
            var menu = new Forms.ContextMenuStrip();
            var open = new Forms.ToolStripMenuItem(L.T("Abrir a Home", "Open Home"), null, delegate { Fire("home"); });
            open.Font = new System.Drawing.Font(open.Font, System.Drawing.FontStyle.Bold);
            menu.Items.Add(open);
            menu.Items.Add(new Forms.ToolStripMenuItem(L.T("Estatísticas", "Statistics"), null, delegate { Fire("estatisticas"); }));
            menu.Items.Add(new Forms.ToolStripMenuItem(L.T("Contas", "Accounts"), null, delegate { Fire("contas"); }));
            menu.Items.Add(new Forms.ToolStripMenuItem(L.T("Configurações", "Settings"), null, delegate { Fire("configuracoes"); }));
            menu.Items.Add(new Forms.ToolStripSeparator());
            taskbar = new Forms.ToolStripMenuItem(L.T("Clawds na barra de tarefas", "Clawds on the taskbar"), null, delegate { if (ToggleTaskbar != null) ToggleTaskbar(); });
            menu.Items.Add(taskbar);
            quiet = new Forms.ToolStripMenuItem(L.T("Não perturbe por 1 hora", "Do not disturb for 1 hour"), null, delegate { if (ToggleQuiet != null) ToggleQuiet(); });
            menu.Items.Add(quiet);
            menu.Items.Add(new Forms.ToolStripSeparator());
            update = new Forms.ToolStripMenuItem(L.T("Procurar atualizações", "Check for updates"), null, delegate { if (CheckUpdates != null) CheckUpdates(); });
            menu.Items.Add(update);
            menu.Items.Add(new Forms.ToolStripMenuItem(L.T("Sair", "Quit"), null, delegate { if (Quit != null) Quit(); }));
            icon.ContextMenuStrip = menu;
            icon.MouseClick += delegate(object s, Forms.MouseEventArgs e) { if (e.Button == Forms.MouseButtons.Left) Fire("home"); };
            icon.BalloonTipClicked += delegate { if (balloonClick != null) balloonClick(); };
            icon.Visible = true;
        }

        void Fire(string page) { if (OpenPage != null) OpenPage(page); }

        public void Sync(Cfg cfg, string newVersion)
        {
            taskbar.Checked = cfg.Taskbar;
            bool q = cfg.Notify.QuietUntil > Data.NowMs();
            quiet.Checked = q;
            quiet.Text = q ? L.T("Não perturbe até ", "Do not disturb until ") + Local(cfg.Notify.QuietUntil).ToString("HH:mm") : L.T("Não perturbe por 1 hora", "Do not disturb for 1 hour");
            update.Text = newVersion != null ? L.T("Atualizar para a versão ", "Update to version ") + newVersion : L.T("Procurar atualizações", "Check for updates");
            icon.Text = newVersion != null ? AppPaths.Name + L.T(" (versão ", " (version ") + newVersion + L.T(" disponível)", " available)") : AppPaths.Name;
        }

        static DateTime Local(double ms) { return new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMilliseconds(ms).ToLocalTime(); }

        public void Notify(string title, string text, Action onClick)
        {
            balloonClick = onClick;
            if (title.Length > 60) title = title.Substring(0, 59) + "…";
            if (text.Length > 200) text = text.Substring(0, 199) + "…";
            icon.ShowBalloonTip(6000, title, text, Forms.ToolTipIcon.None);
        }

        public void Dispose()
        {
            icon.Visible = false;
            icon.Dispose();
        }
    }

    // -------------------------------------------------------------- avisos --

    // Olha cada sessão entre uma leitura e outra e avisa quando ela muda para "terminou",
    // "erro" ou "esperando você". Não avisa o que você já está vendo: a Home aberta ou o
    // Claude daquela conta na frente.
    sealed class Notifier
    {
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);

        readonly Dictionary<string, string> last = new Dictionary<string, string>();
        bool primed;
        readonly Func<Cfg> cfg;
        readonly Tray tray;
        readonly Func<bool> homeVisible;
        readonly Func<Dictionary<string, int>> running;

        public Notifier(Func<Cfg> config, Tray t, Func<bool> homeIsVisible, Func<Dictionary<string, int>> runningProfiles)
        {
            cfg = config;
            tray = t;
            homeVisible = homeIsVisible;
            running = runningProfiles;
        }

        bool ClaudeInFront(AccountCfg a)
        {
            try
            {
                uint pid;
                GetWindowThreadProcessId(GetForegroundWindow(), out pid);
                int main;
                if (a != null && running().TryGetValue(a.Profile, out main) && main == pid) return true;
                // com uma conta só, qualquer Claude na frente é ele
                if (cfg().Accounts.Count <= 1) return Process.GetProcessById((int)pid).ProcessName.Equals("Claude", StringComparison.OrdinalIgnoreCase);
            }
            catch { }
            return false;
        }

        public void Observe(List<SessionView> all, double now)
        {
            var c = cfg();
            var n = c.Notify;
            var events = new List<Tuple<SessionView, string>>();
            foreach (var s in all)
            {
                string old;
                bool had = last.TryGetValue(s.Id, out old);
                string cur = s.IsActive(now) ? s.State + "|" + s.Tool : "parada";
                last[s.Id] = cur;
                if (!primed || !had || old == cur || !s.IsActive(now)) continue;
                string oldState = old.Split('|')[0];
                if (s.State == oldState && s.State != "aguardando") continue;
                if (s.State == "feito" && n.Done && now - s.TurnStartedAt >= n.MinSecs * 1000.0) events.Add(Tuple.Create(s, "feito"));
                else if (s.State == "erro" && n.Error) events.Add(Tuple.Create(s, "erro"));
                else if (s.State == "aguardando" && n.Waiting) events.Add(Tuple.Create(s, "aguardando"));
            }
            foreach (var id in last.Keys.Where(k => !all.Any(s => s.Id == k)).ToList()) last.Remove(id);
            primed = true;
            if (events.Count == 0 || !n.On || n.QuietUntil > now || homeVisible()) return;

            // várias de uma vez: um aviso só
            var shown = events.Where(e => !ClaudeInFront(c.Accounts.FirstOrDefault(a => a.Key == e.Item1.Account))).ToList();
            if (shown.Count == 0) return;
            var first = shown[0];
            var acc = c.Accounts.FirstOrDefault(a => a.Key == first.Item1.Account);
            string where = acc != null ? "Claude " + acc.Name : "Claude";
            string title, text;
            if (shown.Count > 1)
            {
                title = shown.Count + L.T(" sessões precisam de você", " sessions need you");
                text = string.Join("\n", shown.Take(4).Select(e => Line(e.Item2) + ": " + e.Item1.Label).ToArray());
            }
            else
            {
                var s = first.Item1;
                switch (first.Item2)
                {
                    case "feito":
                        title = L.T("Pronto: ", "Done: ") + s.Label;
                        text = where + L.T(" · levou ", " · took ") + Words.Dur(now - s.TurnStartedAt);
                        break;
                    case "erro":
                        title = L.T("Deu erro: ", "Error: ") + s.Label;
                        text = where + L.T(" · a sessão parou com um erro.", " · the session stopped with an error.");
                        break;
                    default:
                        title = (s.Tool == "AskUserQuestion" ? L.T("Uma pergunta para você: ", "A question for you: ") : s.Tool == "ExitPlanMode" ? L.T("Plano para aprovar: ", "Plan to approve: ") : L.T("Pedido de permissão: ", "Permission request: ")) + s.Label;
                        text = where + L.T(" está esperando a sua resposta.", " is waiting for your answer.");
                        break;
                }
            }
            tray.Notify(title, text, delegate
            {
                if (acc == null) return;
                try { Profiles.Launch(acc.Profile, acc.Name); } catch (Exception e) { Program.Log("abrir: " + e.Message); }
            });
        }

        static string Line(string kind)
        {
            return kind == "feito" ? L.T("pronto", "done") : kind == "erro" ? L.T("erro", "error") : L.T("esperando", "waiting");
        }
    }

    // --------------------------------------------------------- atualização --

    // Procura a última release no GitHub e, se for mais nova, baixa o ClawdeskSetup.exe, confere
    // o SHA-256 publicado junto e roda o instalador em modo silencioso.
    sealed class Updater
    {
        readonly Dispatcher ui;
        public string Latest, Notes, SetupUrl, ShaUrl, PageUrl;
        public string Status = "";
        public bool Checking, Installing;
        public DateTime LastCheck = DateTime.MinValue;
        public event Action Changed;
        public event Action ReadyToQuit;

        public Updater(Dispatcher d) { ui = d; }

        public bool Available
        {
            get { return Latest != null && Newer(Latest, AppPaths.Version); }
        }

        public static bool Newer(string a, string b)
        {
            Version x, y;
            return Version.TryParse(Clean(a), out x) && Version.TryParse(Clean(b), out y) && x > y;
        }

        static string Clean(string v)
        {
            v = (v ?? "").Trim().TrimStart('v', 'V');
            int dash = v.IndexOf('-');
            if (dash > 0) v = v.Substring(0, dash);
            return v.Split('.').Length == 2 ? v + ".0" : v;
        }

        static WebClient Client()
        {
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;   // TLS 1.2
            var w = new WebClient();
            w.Headers[HttpRequestHeader.UserAgent] = "Clawdesk/" + AppPaths.Version;
            w.Encoding = Encoding.UTF8;
            return w;
        }

        void Done(string status)
        {
            ui.BeginInvoke(new Action(delegate
            {
                Status = status;
                Checking = false;
                if (Changed != null) Changed();
            }));
        }

        public void Check(bool manual)
        {
            if (Checking || Installing) return;
            Checking = true;
            Status = L.T("Procurando…", "Checking…");
            if (Changed != null) Changed();
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    string json;
                    using (var w = Client())
                    {
                        w.Headers[HttpRequestHeader.Accept] = "application/vnd.github+json";
                        json = w.DownloadString("https://api.github.com/repos/" + AppPaths.Repo + "/releases/latest");
                    }
                    var d = new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.DeserializeObject(json) as Dictionary<string, object>;
                    string tag = Json.Str(d, "tag_name", null);
                    string setup = null, sha = null;
                    foreach (object o in Json.Arr(d, "assets"))
                    {
                        var a = o as Dictionary<string, object>;
                        string name = Json.Str(a, "name", "");
                        if (name.Equals("ClawdeskSetup.exe", StringComparison.OrdinalIgnoreCase)) setup = Json.Str(a, "browser_download_url", null);
                        if (name.Equals("ClawdeskSetup.exe.sha256", StringComparison.OrdinalIgnoreCase)) sha = Json.Str(a, "browser_download_url", null);
                    }
                    string notes = Json.Str(d, "body", "");
                    string page = Json.Str(d, "html_url", "https://github.com/" + AppPaths.Repo + "/releases/latest");
                    ui.BeginInvoke(new Action(delegate
                    {
                        Latest = tag != null ? Clean(tag) : null;
                        SetupUrl = setup; ShaUrl = sha; Notes = notes; PageUrl = page;
                        LastCheck = DateTime.Now;
                    }));
                    Done(tag == null ? L.T("Não achei nenhuma versão publicada.", "No published version found.") : Newer(tag, AppPaths.Version) ? L.T("Versão ", "Version ") + Clean(tag) + L.T(" disponível.", " available.") : L.T("Você já tem a versão mais nova.", "You already have the latest version."));
                }
                catch (Exception e)
                {
                    Program.Log("atualização: " + e.Message);
                    Done(manual ? L.T("Não consegui falar com o GitHub: ", "Couldn't reach GitHub: ") + e.Message : "");
                }
            });
        }

        public void Install()
        {
            if (Installing || SetupUrl == null) return;
            Installing = true;
            Status = L.T("Baixando a versão ", "Downloading version ") + Latest + "…";
            if (Changed != null) Changed();
            string url = SetupUrl, shaUrl = ShaUrl, ver = Latest;
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    string dir = Path.Combine(Path.GetTempPath(), "Clawdesk");
                    Directory.CreateDirectory(dir);
                    string file = Path.Combine(dir, "ClawdeskSetup-" + ver + ".exe");
                    using (var w = Client()) w.DownloadFile(url, file);
                    if (shaUrl != null)
                    {
                        string expected;
                        using (var w = Client()) expected = w.DownloadString(shaUrl).Trim().Split(' ', '\t', '\n')[0].ToLowerInvariant();
                        string got;
                        using (var sha = SHA256.Create())
                        using (var fs = File.OpenRead(file)) got = BitConverter.ToString(sha.ComputeHash(fs)).Replace("-", "").ToLowerInvariant();
                        if (got != expected)
                        {
                            try { File.Delete(file); } catch { }
                            throw new InvalidOperationException(L.T("o arquivo baixado não bate com o SHA-256 publicado; tente de novo mais tarde.", "the downloaded file doesn't match the published SHA-256; try again later."));
                        }
                    }
                    Process.Start(new ProcessStartInfo(file, "/quiet /abrir") { UseShellExecute = true });
                    ui.BeginInvoke(new Action(delegate { Status = L.T("Instalando…", "Installing…"); if (ReadyToQuit != null) ReadyToQuit(); }));
                }
                catch (Exception e)
                {
                    Program.Log("atualizar: " + e);
                    ui.BeginInvoke(new Action(delegate
                    {
                        Installing = false;
                        Status = L.T("Não consegui atualizar: ", "Couldn't update: ") + e.Message;
                        if (Changed != null) Changed();
                    }));
                }
            });
        }
    }
}
