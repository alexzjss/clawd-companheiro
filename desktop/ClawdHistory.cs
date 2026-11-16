// O histórico das sessões e a página de estatísticas.
//
// O mod grava em sessions/<id>.json só o estado atual de cada sessão (e o app apaga os
// arquivos com mais de 7 dias). Para ter números de semanas e meses, o app olha as sessões a
// cada leitura e soma o que mudou desde a última vez (custo, tempo trabalhando, tarefas,
// ferramentas) no dia e na conta, em ~/.claude/clawd/historico.json. Nada sai da máquina.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Clawdesk
{
    sealed class AccDay
    {
        public double Cost, BusySec;
        public int Sessions, Done, Failed;
        public Dictionary<string, double> Tools = new Dictionary<string, double>();
        public double[] Hours = new double[24];   // segundos trabalhando em cada hora do dia
    }

    // O que foi visto da sessão na última leitura, para saber quanto mudou.
    sealed class Seen
    {
        public double Cost = double.NaN, BusyMs = double.NaN, Done, Failed, LastSeen;
        public string Day = "", State = "";
        public Dictionary<string, double> Tools = new Dictionary<string, double>();
    }

    sealed class History
    {
        public readonly Dictionary<string, Dictionary<string, AccDay>> Days = new Dictionary<string, Dictionary<string, AccDay>>();
        readonly Dictionary<string, Seen> seen = new Dictionary<string, Seen>();
        bool dirty;
        DateTime lastSave = DateTime.MinValue;
        double lastNow;

        public static string FilePath { get { return Path.Combine(Cfg.Dir, "historico.json"); } }
        public event Action Changed;

        static JavaScriptSerializer Ser() { return new JavaScriptSerializer { MaxJsonLength = int.MaxValue }; }
        static string Key(DateTime d) { return d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture); }

        public AccDay Get(string day, string acc, bool create)
        {
            Dictionary<string, AccDay> m;
            if (!Days.TryGetValue(day, out m)) { if (!create) return null; m = Days[day] = new Dictionary<string, AccDay>(); }
            AccDay a;
            if (!m.TryGetValue(acc, out a)) { if (!create) return null; a = m[acc] = new AccDay(); }
            return a;
        }

        // ------------------------------------------------------------ leitura --

        static double D(Dictionary<string, object> d, string k)
        {
            object v;
            if (d == null || !d.TryGetValue(k, out v) || v == null) return 0;
            try { return Convert.ToDouble(v, CultureInfo.InvariantCulture); } catch { return 0; }
        }

        static Dictionary<string, double> Map(object o)
        {
            var r = new Dictionary<string, double>();
            var d = o as Dictionary<string, object>;
            if (d != null) foreach (var kv in d) r[kv.Key] = D(d, kv.Key);
            return r;
        }

        public static History Load()
        {
            var h = new History();
            try
            {
                if (!File.Exists(FilePath)) return h;
                var root = Ser().DeserializeObject(File.ReadAllText(FilePath, Encoding.UTF8)) as Dictionary<string, object>;
                if (root == null) return h;
                var days = Json.Obj(root, "dias");
                if (days != null)
                    foreach (var day in days)
                    {
                        var accs = day.Value as Dictionary<string, object>;
                        if (accs == null) continue;
                        foreach (var acc in accs)
                        {
                            var x = acc.Value as Dictionary<string, object>;
                            if (x == null) continue;
                            var a = h.Get(day.Key, acc.Key, true);
                            a.Cost = D(x, "custo");
                            a.BusySec = D(x, "ocupado");
                            a.Sessions = (int)D(x, "sessoes");
                            a.Done = (int)D(x, "feitas");
                            a.Failed = (int)D(x, "erros");
                            object t;
                            if (x.TryGetValue("ferramentas", out t)) a.Tools = Map(t);
                            object hr;
                            if (x.TryGetValue("horas", out hr) && hr is object[])
                            {
                                var arr = (object[])hr;
                                for (int i = 0; i < 24 && i < arr.Length; i++) { try { a.Hours[i] = Convert.ToDouble(arr[i], CultureInfo.InvariantCulture); } catch { } }
                            }
                        }
                    }
                var sn = Json.Obj(root, "vistas");
                if (sn != null)
                    foreach (var kv in sn)
                    {
                        var x = kv.Value as Dictionary<string, object>;
                        if (x == null) continue;
                        var v = new Seen();
                        v.Cost = x.ContainsKey("custo") ? D(x, "custo") : double.NaN;
                        v.BusyMs = x.ContainsKey("ocupadoMs") ? D(x, "ocupadoMs") : double.NaN;
                        v.Done = D(x, "feitas");
                        v.Failed = D(x, "erros");
                        v.LastSeen = D(x, "visto");
                        v.Day = Json.Str(x, "dia", "");
                        v.State = Json.Str(x, "estado", "");
                        object t;
                        if (x.TryGetValue("ferramentas", out t)) v.Tools = Map(t);
                        h.seen[kv.Key] = v;
                    }
            }
            catch (Exception e) { Program.Log("histórico: " + e.Message); }
            return h;
        }

        public void Save(bool force)
        {
            if (!dirty || (!force && (DateTime.UtcNow - lastSave).TotalSeconds < 20)) return;
            dirty = false;
            lastSave = DateTime.UtcNow;
            try
            {
                var days = new Dictionary<string, object>();
                foreach (var day in Days)
                {
                    var accs = new Dictionary<string, object>();
                    foreach (var acc in day.Value)
                    {
                        var a = acc.Value;
                        var x = new Dictionary<string, object>();
                        x["custo"] = Math.Round(a.Cost, 4);
                        x["ocupado"] = Math.Round(a.BusySec);
                        x["sessoes"] = a.Sessions;
                        x["feitas"] = a.Done;
                        x["erros"] = a.Failed;
                        x["ferramentas"] = a.Tools.ToDictionary(k => k.Key, k => (object)k.Value);
                        x["horas"] = a.Hours.Select(v => (object)Math.Round(v)).ToArray();
                        accs[acc.Key] = x;
                    }
                    days[day.Key] = accs;
                }
                var sn = new Dictionary<string, object>();
                foreach (var kv in seen)
                {
                    var v = kv.Value;
                    var x = new Dictionary<string, object>();
                    if (!double.IsNaN(v.Cost)) x["custo"] = v.Cost;
                    if (!double.IsNaN(v.BusyMs)) x["ocupadoMs"] = v.BusyMs;
                    x["feitas"] = v.Done;
                    x["erros"] = v.Failed;
                    x["visto"] = v.LastSeen;
                    x["dia"] = v.Day;
                    x["estado"] = v.State;
                    x["ferramentas"] = v.Tools.ToDictionary(k => k.Key, k => (object)k.Value);
                    sn[kv.Key] = x;
                }
                var root = new Dictionary<string, object>();
                root["versao"] = 1;
                root["dias"] = days;
                root["vistas"] = sn;
                Directory.CreateDirectory(Cfg.Dir);
                string tmp = FilePath + ".tmp";
                File.WriteAllText(tmp, Ser().Serialize(root), new UTF8Encoding(false));
                if (File.Exists(FilePath)) File.Replace(tmp, FilePath, null);
                else File.Move(tmp, FilePath);
            }
            catch (Exception e) { Program.Log("histórico: " + e.Message); }
        }

        // ------------------------------------------------------------- soma --

        static bool Busy(string state) { return state == "trabalhando" || state == "pensando" || state == "compactando"; }

        // Soma no dia de hoje o que mudou em cada sessão desde a última leitura.
        public void Observe(List<SessionView> all, double now)
        {
            double dt = lastNow > 0 ? Math.Max(0, Math.Min(10, (now - lastNow) / 1000)) : 0;
            lastNow = now;
            DateTime local = DateTime.Now;
            string today = Key(local);
            int hour = local.Hour;
            DateTime midnight = local.Date;
            double todayMs = (midnight.ToUniversalTime() - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds;
            bool changed = false;

            foreach (var s in all)
            {
                if (string.IsNullOrEmpty(s.Account)) continue;
                Seen v;
                bool fresh = !seen.TryGetValue(s.Id, out v);
                if (fresh)
                {
                    v = new Seen();
                    // uma sessão de antes de hoje entra com o que já tinha: só o que vier depois conta
                    if (s.StartedAt < todayMs)
                    {
                        v.Cost = s.Cost; v.BusyMs = s.BusyMs;
                        v.Done = double.IsNaN(s.Done) ? 0 : s.Done; v.Failed = double.IsNaN(s.Failed) ? 0 : s.Failed;
                        if (s.Tools != null) v.Tools = new Dictionary<string, double>(s.Tools);
                        v.State = s.State;
                        v.Day = s.IsActive(now) ? "" : today;   // parada: não conta como sessão de hoje
                    }
                    seen[s.Id] = v;
                }
                v.LastSeen = now;
                bool active = s.IsActive(now);
                if (!active && s.UpdatedAt < todayMs && !fresh) continue;
                var a = Get(today, s.Account, true);

                if (v.Day != today && (active || s.UpdatedAt >= todayMs)) { a.Sessions++; v.Day = today; changed = true; }

                if (!double.IsNaN(s.Cost))
                {
                    double base0 = double.IsNaN(v.Cost) ? 0 : v.Cost;
                    if (s.Cost > base0 + 1e-9) { a.Cost += s.Cost - base0; changed = true; }
                    v.Cost = s.Cost;
                }
                if (!double.IsNaN(s.BusyMs))
                {
                    double base0 = double.IsNaN(v.BusyMs) ? 0 : v.BusyMs;
                    if (s.BusyMs > base0) { double sec = (s.BusyMs - base0) / 1000; a.BusySec += sec; a.Hours[hour] += sec; changed = true; }
                    v.BusyMs = s.BusyMs;
                }
                else if (active && Busy(s.State) && dt > 0)
                {
                    // mod antigo, sem busyMs: conta o tempo visto trabalhando
                    a.BusySec += dt; a.Hours[hour] += dt; changed = true;
                }

                if (!double.IsNaN(s.Done))
                {
                    if (s.Done > v.Done) { a.Done += (int)(s.Done - v.Done); changed = true; }
                    if (!double.IsNaN(s.Failed) && s.Failed > v.Failed) { a.Failed += (int)(s.Failed - v.Failed); changed = true; }
                    v.Done = s.Done;
                    v.Failed = double.IsNaN(s.Failed) ? v.Failed : s.Failed;
                }
                else if (s.State != v.State && !fresh)
                {
                    if (s.State == "feito") { a.Done++; changed = true; }
                    else if (s.State == "erro") { a.Failed++; changed = true; }
                }
                v.State = s.State;

                if (s.Tools != null)
                    foreach (var kv in s.Tools)
                    {
                        double old;
                        v.Tools.TryGetValue(kv.Key, out old);
                        if (kv.Value > old)
                        {
                            double prev;
                            a.Tools.TryGetValue(kv.Key, out prev);
                            a.Tools[kv.Key] = prev + (kv.Value - old);
                            changed = true;
                        }
                        v.Tools[kv.Key] = kv.Value;
                    }
            }

            // esquece sessões que sumiram e dias muito antigos
            foreach (var id in seen.Where(x => now - x.Value.LastSeen > 10 * 86400000.0).Select(x => x.Key).ToList()) { seen.Remove(id); changed = true; }
            string oldest = Key(local.AddDays(-400));
            foreach (var d in Days.Keys.Where(k => string.CompareOrdinal(k, oldest) < 0).ToList()) Days.Remove(d);

            if (changed)
            {
                dirty = true;
                if (Changed != null) Changed();
            }
            Save(false);
        }

        // ------------------------------------------------------------ consultas --

        public static IEnumerable<string> LastDays(int n)
        {
            for (int i = n - 1; i >= 0; i--) yield return Key(DateTime.Today.AddDays(-i));
        }

        public static IEnumerable<string> DaysBefore(int n)
        {
            for (int i = 2 * n - 1; i >= n; i--) yield return Key(DateTime.Today.AddDays(-i));
        }

        public AccDay Sum(IEnumerable<string> days, string account)
        {
            var r = new AccDay();
            foreach (string d in days)
            {
                Dictionary<string, AccDay> m;
                if (!Days.TryGetValue(d, out m)) continue;
                foreach (var kv in m)
                {
                    if (account != null && kv.Key != account) continue;
                    var a = kv.Value;
                    r.Cost += a.Cost; r.BusySec += a.BusySec; r.Sessions += a.Sessions; r.Done += a.Done; r.Failed += a.Failed;
                    foreach (var t in a.Tools) { double p; r.Tools.TryGetValue(t.Key, out p); r.Tools[t.Key] = p + t.Value; }
                    for (int i = 0; i < 24; i++) r.Hours[i] += a.Hours[i];
                }
            }
            return r;
        }

        public bool Empty { get { return Days.Count == 0; } }

        // Dados de mentira, para a foto da página.
        public static History Sample(Cfg cfg)
        {
            var h = new History();
            var rng = new Random(5);
            string[] tools = { "Bash:git", "Bash:teste", "Edit:codigo", "Read:codigo", "Read:doc", "Grep", "WebSearch", "Agent", "Bash:compilar", "Write:codigo", "mcp__github__create_pull_request" };
            for (int i = 0; i < 90; i++)
            {
                string day = Key(DateTime.Today.AddDays(-i));
                foreach (var acc in cfg.Accounts)
                {
                    if (rng.NextDouble() < 0.25) continue;
                    var a = h.Get(day, acc.Key, true);
                    a.Sessions = 1 + rng.Next(5);
                    a.Cost = Math.Round(rng.NextDouble() * 6, 2);
                    a.BusySec = rng.Next(600, 4 * 3600);
                    a.Done = rng.Next(2, 18);
                    a.Failed = rng.Next(0, 3);
                    foreach (string t in tools) a.Tools[t] = rng.Next(0, 40);
                    for (int k = 8; k < 20; k++) a.Hours[k] = rng.Next(0, 600) * (k == 12 ? 0.3 : 1);
                }
            }
            return h;
        }
    }

    // ------------------------------------------------------------ estatísticas --

    sealed class StatsPage : ContentControl
    {
        readonly History hist;
        readonly Func<Cfg> cfg;
        int range = 1;   // 0: 7 dias, 1: 30 dias, 2: 90 dias
        string account;  // null: todas
        static readonly int[] Ranges = { 7, 30, 90 };

        public StatsPage(History h, Func<Cfg> config)
        {
            hist = h;
            cfg = config;
            Focusable = false;
            Build();
        }

        public void Refresh() { Build(); }

        static readonly Dictionary<string, string> Category = new Dictionary<string, string>
        {
            { "bash", "Terminal" }, { "ps", "PowerShell" }, { "git", "Git" }, { "test", "Testes" }, { "install", "Instalar pacotes" },
            { "build", "Compilar" }, { "trash", "Apagar arquivos" }, { "stack", "Docker e contêineres" }, { "net", "Rede" },
            { "peek", "Olhar arquivos" }, { "rocket", "Rodar programas" }, { "read", "Ler arquivos" }, { "grep", "Buscar no código" },
            { "glob", "Achar arquivos" }, { "edit", "Editar arquivos" }, { "paint", "Imagens" }, { "write", "Criar arquivos" },
            { "grid", "Notebooks" }, { "search", "Pesquisar na web" }, { "fetch", "Abrir páginas" }, { "browser", "Navegador" },
            { "camera", "Capturas de tela" }, { "mouse", "Usar o computador" }, { "agent", "Subagentes" }, { "conduct", "Workflows" },
            { "plan", "Plano e tarefas" }, { "spell", "Skills" }, { "toolbox", "Procurar ferramentas" }, { "plug", "Conectores (MCP)" },
            { "tool", "Outras ferramentas" },
        };

        public static string ToolCategory(string key)
        {
            int i = key.IndexOf(':');
            string tool = i >= 0 ? key.Substring(0, i) : key, detail = i >= 0 ? key.Substring(i + 1) : "";
            string anim = Words.WorkAnim(tool, detail);
            string name;
            return Category.TryGetValue(anim, out name) ? name : "Outras ferramentas";
        }

        public static string Hours(double sec)
        {
            int min = (int)Math.Round(sec / 60);
            if (min < 60) return min + " min";
            int h = min / 60, m = min % 60;
            return m == 0 ? h + " h" : h + " h " + m.ToString("00");
        }

        static string Pct(double now, double before)
        {
            if (before <= 0.0001) return now > 0 ? "novo no período" : "";
            double p = (now - before) / before * 100;
            if (Math.Abs(p) < 1) return "igual ao período anterior";
            return (p > 0 ? "+" : "−") + Math.Abs(Math.Round(p)) + "% que o período anterior";
        }

        void Build()
        {
            var c = cfg();
            int n = Ranges[range];
            var days = History.LastDays(n).ToList();
            var cur = hist.Sum(days, account);
            var prev = hist.Sum(History.DaysBefore(n), account);

            var sp = new StackPanel();

            // filtros numa linha, acima de tudo
            var filters = new WrapPanel { Margin = new Thickness(0, 0, 0, 16) };
            var seg = Ui.Segmented(new[] { "7 dias", "30 dias", "90 dias" }, range, delegate(int i) { range = i; Build(); });
            seg.Margin = new Thickness(0, 0, 14, 6);
            filters.Children.Add(seg);
            filters.Children.Add(Ui.Chip(Ui.T("Todas as contas", 12.5, Ui.Text, false), account == null, Ui.Accent, delegate { account = null; Build(); }));
            foreach (var a0 in c.Accounts)
            {
                var a = a0;
                var row = new StackPanel { Orientation = Orientation.Horizontal };
                var d = Ui.Dot(Pal.B(a.Body, 1), 9);
                d.Margin = new Thickness(0, 1, 7, 0);
                row.Children.Add(d);
                row.Children.Add(Ui.T(a.Name, 12.5, Ui.Text, false));
                filters.Children.Add(Ui.Chip(row, account == a.Key, a.Body, delegate { account = a.Key; Build(); }));
            }
            sp.Children.Add(filters);

            if (hist.Empty)
            {
                sp.Children.Add(Ui.Section("Ainda sem histórico",
                    "As estatísticas começam a contar a partir de agora: custo, tempo trabalhando, tarefas e ferramentas de cada sessão, por dia e por conta. Use o Claude normalmente e volte aqui mais tarde.", null));
                Content = sp;
                return;
            }

            // os números do período
            var tiles = new UniformGrid4();
            tiles.Add(Tile("Custo", Words.Money(cur.Cost), Pct(cur.Cost, prev.Cost)));
            tiles.Add(Tile("Tempo trabalhando", Hours(cur.BusySec), Pct(cur.BusySec, prev.BusySec)));
            tiles.Add(Tile("Sessões", cur.Sessions.ToString(), Pct(cur.Sessions, prev.Sessions)));
            int total = cur.Done + cur.Failed;
            tiles.Add(Tile("Tarefas concluídas", cur.Done.ToString(), total > 0 ? Math.Round(100.0 * cur.Done / total) + "% sem erro · " + cur.Failed + (cur.Failed == 1 ? " erro" : " erros") : ""));
            sp.Children.Add(tiles.Panel);

            // custo e tempo por dia: dois gráficos, um eixo cada
            var costs = days.Select(d => hist.Sum(new[] { d }, account).Cost).ToList();
            var busy = days.Select(d => hist.Sum(new[] { d }, account).BusySec / 3600).ToList();
            var labels = days.Select(d => DateTime.ParseExact(d, "yyyy-MM-dd", CultureInfo.InvariantCulture).ToString(n <= 7 ? "ddd dd" : "dd/MM", Words.PtBR)).ToList();
            string color = account != null ? c.Accounts.First(a => a.Key == account).Body : Ui.Accent;
            sp.Children.Add(Ui.Section("Custo por dia", null, new BarChart(costs, labels, delegate(double v) { return Words.Money(v); }, color, 160)));
            sp.Children.Add(Ui.Section("Tempo trabalhando por dia", null, new BarChart(busy, labels, delegate(double v) { return Hours(v * 3600); }, color, 140)));

            // ferramentas e horários lado a lado
            var two = new Grid();
            two.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            two.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
            two.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var tools = cur.Tools.GroupBy(t => ToolCategory(t.Key)).Select(g => new KeyValuePair<string, double>(g.Key, g.Sum(x => x.Value)))
                .Where(x => x.Value > 0).OrderByDescending(x => x.Value).Take(10).ToList();
            UIElement toolBody = tools.Count == 0
                ? (UIElement)Ui.T("Ainda sem dados. A contagem de ferramentas vem do mod; atualize-o se ele for de antes da versão 1.2.", 12.5, Ui.Dim, false)
                : Ranking(tools, color);
            var tsec = Ui.Section("Ferramentas mais usadas", "Chamadas no período, por tipo.", toolBody);
            two.Children.Add(tsec);
            var hourLabels = Enumerable.Range(0, 24).Select(h => h.ToString("00") + "h").ToList();
            var hsec = Ui.Section("Horários", "Em que horas do dia o Claude mais trabalhou.", new BarChart(cur.Hours.Select(x => x / 3600).ToList(), hourLabels, delegate(double v) { return Hours(v * 3600); }, color, 150));
            Grid.SetColumn(hsec, 2);
            two.Children.Add(hsec);
            sp.Children.Add(two);

            // por conta (também é a versão em tabela dos números)
            if (account == null && c.Accounts.Count > 0) sp.Children.Add(Ui.Section("Por conta", null, AccountTable(c, days)));

            Content = sp;
        }

        sealed class UniformGrid4
        {
            public readonly System.Windows.Controls.Primitives.UniformGrid Panel = new System.Windows.Controls.Primitives.UniformGrid { Columns = 4, Margin = new Thickness(-7, 0, -7, 0) };
            public void Add(UIElement e) { Panel.Children.Add(e); }
        }

        static UIElement Tile(string label, string value, string note)
        {
            var sp = new StackPanel();
            sp.Children.Add(Ui.T(label, 12.5, Ui.Dim, false));
            var v = Ui.T(value, 26, Ui.Text, true);
            v.Margin = new Thickness(0, 4, 0, 2);
            sp.Children.Add(v);
            if (note.Length > 0) sp.Children.Add(Ui.T(note, 11.5, Ui.Faint, false));
            var card = Ui.CardOf(sp, new Thickness(18, 14, 18, 16));
            card.Margin = new Thickness(7, 0, 7, 14);
            return card;
        }

        static UIElement Ranking(List<KeyValuePair<string, double>> rows, string color)
        {
            double max = rows.Max(r => r.Value);
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(52) });
            for (int i = 0; i < rows.Count; i++)
            {
                g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(26) });
                var name = Ui.Line1(rows[i].Key, 12.5, Ui.Text, false);
                Grid.SetRow(name, i);
                g.Children.Add(name);
                var bar = Ui.Bar(rows[i].Value / max * 100, Pal.B(color, 1), 8);
                bar.Margin = new Thickness(0, 0, 10, 0);
                Grid.SetRow(bar, i); Grid.SetColumn(bar, 1);
                g.Children.Add(bar);
                var v = Ui.T(((int)rows[i].Value).ToString("N0", Words.PtBR), 12.5, Ui.Dim, false);
                v.HorizontalAlignment = HorizontalAlignment.Right;
                Grid.SetRow(v, i); Grid.SetColumn(v, 2);
                g.Children.Add(v);
            }
            return g;
        }

        UIElement AccountTable(Cfg c, List<string> days)
        {
            var g = new Grid();
            string[] heads = { "Conta", "Custo", "Tempo", "Sessões", "Tarefas", "Erros" };
            for (int i = 0; i < heads.Length; i++) g.ColumnDefinitions.Add(new ColumnDefinition { Width = i == 0 ? new GridLength(2, GridUnitType.Star) : new GridLength(1, GridUnitType.Star) });
            g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(28) });
            for (int i = 0; i < heads.Length; i++)
            {
                var h = Ui.T(heads[i], 12, Ui.Faint, false);
                if (i > 0) h.HorizontalAlignment = HorizontalAlignment.Right;
                Grid.SetColumn(h, i);
                g.Children.Add(h);
            }
            int r = 1;
            foreach (var a in c.Accounts)
            {
                var s = hist.Sum(days, a.Key);
                g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(32) });
                var line = new Border { Height = 1, Background = Ui.Line, VerticalAlignment = VerticalAlignment.Top };
                Grid.SetRow(line, r); Grid.SetColumnSpan(line, heads.Length);
                g.Children.Add(line);
                var name = new StackPanel { Orientation = Orientation.Horizontal };
                var d = Ui.Dot(Pal.B(a.Body, 1), 9);
                d.Margin = new Thickness(0, 1, 8, 0);
                name.Children.Add(d);
                name.Children.Add(Ui.T(a.Name, 13, Ui.Text, false));
                Grid.SetRow(name, r);
                g.Children.Add(name);
                string[] vals = { Words.Money(s.Cost), Hours(s.BusySec), s.Sessions.ToString(), s.Done.ToString(), s.Failed.ToString() };
                for (int i = 0; i < vals.Length; i++)
                {
                    var v = Ui.T(vals[i], 13, Ui.Text, false);
                    v.HorizontalAlignment = HorizontalAlignment.Right;
                    Grid.SetRow(v, r); Grid.SetColumn(v, i + 1);
                    g.Children.Add(v);
                }
                r++;
            }
            return g;
        }
    }

    // Barras verticais de uma série, com o valor no tooltip de cada uma e a área de passar o
    // mouse do tamanho da coluna inteira.
    sealed class BarChart : Grid
    {
        public BarChart(List<double> values, List<string> labels, Func<double, string> fmt, string color, double height)
        {
            double max = Math.Max(1e-9, values.Count > 0 ? values.Max() : 0);
            RowDefinitions.Add(new RowDefinition { Height = new GridLength(height) });
            RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // eixo discreto: só o máximo e o zero
            var axis = new Grid { Margin = new Thickness(0, 0, 8, 0) };
            var top = Ui.T(values.Count > 0 && values.Max() > 0 ? fmt(values.Max()) : "", 11, Ui.Faint, false);
            top.VerticalAlignment = VerticalAlignment.Top;
            top.Margin = new Thickness(0, -7, 0, 0);
            axis.Children.Add(top);
            var zero = Ui.T("0", 11, Ui.Faint, false);
            zero.VerticalAlignment = VerticalAlignment.Bottom;
            zero.HorizontalAlignment = HorizontalAlignment.Right;
            zero.Margin = new Thickness(0, 0, 0, -7);
            axis.Children.Add(zero);
            Children.Add(axis);

            var plot = new Grid();
            plot.Children.Add(new Border { Height = 1, Background = Ui.Line, VerticalAlignment = VerticalAlignment.Top });
            plot.Children.Add(new Border { Height = 1, Background = Ui.SoftHi, VerticalAlignment = VerticalAlignment.Bottom });
            var cols = new System.Windows.Controls.Primitives.UniformGrid { Rows = 1, Columns = Math.Max(1, values.Count) };
            Brush fill = Pal.B(color, 1), hi = Pal.B(color, 0.7);
            for (int i = 0; i < values.Count; i++)
            {
                double v = values[i];
                var cell = new Grid { Background = Brushes.Transparent };
                double h = v <= 0 ? 0 : Math.Max(3, v / max * (height - 2));
                var bar = new Border { Height = h, VerticalAlignment = VerticalAlignment.Bottom, Background = fill, CornerRadius = new CornerRadius(Math.Min(4, h / 2), Math.Min(4, h / 2), 0, 0), Margin = new Thickness(values.Count > 40 ? 1 : 2, 0, values.Count > 40 ? 1 : 2, 0), MaxWidth = 34 };
                cell.Children.Add(bar);
                cell.ToolTip = labels[i] + ": " + fmt(v);
                ToolTipService.SetInitialShowDelay(cell, 80);
                cell.MouseEnter += delegate { bar.Background = hi; };
                cell.MouseLeave += delegate { bar.Background = fill; };
                cols.Children.Add(cell);
            }
            plot.Children.Add(cols);
            Grid.SetColumn(plot, 1);
            Children.Add(plot);

            // rótulos de baixo: alguns, para não encavalar
            var under = new System.Windows.Controls.Primitives.UniformGrid { Rows = 1, Columns = Math.Max(1, values.Count), Margin = new Thickness(0, 6, 0, 0) };
            int step = values.Count <= 8 ? 1 : values.Count <= 24 ? 3 : values.Count <= 31 ? 5 : 15;
            for (int i = 0; i < values.Count; i++)
            {
                bool show = i % step == 0 || i == values.Count - 1 && values.Count <= 8;
                var t = Ui.T(show ? labels[i] : "", 10.5, Ui.Faint, false);
                t.TextWrapping = TextWrapping.NoWrap;
                t.HorizontalAlignment = HorizontalAlignment.Left;
                under.Children.Add(new Canvas { Height = 16, Children = { t } });
            }
            Grid.SetRow(under, 1);
            Grid.SetColumn(under, 1);
            Children.Add(under);
        }
    }
}
