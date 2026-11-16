// A Home: todas as sessões de todas as contas numa tela só, com os Clawds morando numa
// faixa no alto. Enquanto ela está aberta, os Clawds saem de cima da barra de tarefas e vêm
// para cá (o mesmo palco, desenhado aqui); fechou, minimizou ou trocou de página, eles voltam.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace ClawdDesktop
{
    // O chão e o céu da Home, e os Clawds por cima.
    sealed class HomeStageView : FrameworkElement
    {
        readonly Stage stage;
        public const double Floor = 18;

        public HomeStageView(Stage s)
        {
            stage = s;
            Height = 270;
            ClipToBounds = true;
            Focusable = false;
            RenderOptions.SetEdgeMode(this, EdgeMode.Aliased);
            SnapsToDevicePixels = true;
        }

        static Color Mix(Color a, Color b, double k)
        {
            k = Math.Max(0, Math.Min(1, k));
            return Color.FromRgb((byte)(a.R + (b.R - a.R) * k), (byte)(a.G + (b.G - a.G) * k), (byte)(a.B + (b.B - a.B) * k));
        }

        // 0 = noite, 1 = dia, com amanhecer e entardecer no meio.
        static double Daylight()
        {
            double h = DateTime.Now.Hour + DateTime.Now.Minute / 60.0;
            if (h < 5 || h >= 20) return 0;
            if (h < 7) return (h - 5) / 2;
            if (h >= 18) return 1 - (h - 18) / 2;
            return 1;
        }

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth, h = ActualHeight;
            if (w <= 0 || h <= 0) return;
            double day = Daylight();
            double dusk = 1 - Math.Abs(day - 0.5) * 2;   // 1 no meio da transição
            Color top = Mix(Pal.C("#0d1026"), Pal.C("#2a5f93"), day), bottom = Mix(Pal.C("#1d2350"), Pal.C("#8cc3df"), day);
            bottom = Mix(bottom, Pal.C("#e08a5f"), dusk * 0.6);
            dc.DrawRectangle(new LinearGradientBrush(top, bottom, 90), null, new Rect(0, 0, w, h));

            double t = DateTime.Now.TimeOfDay.TotalSeconds;
            const double P = 3;
            if (day < 0.6)
            {
                // estrelas que piscam
                var rng = new Random(7);
                Brush star = Pal.B("#ffffff", 0.75 * (1 - day / 0.6));
                for (int i = 0; i < (int)(w / 18); i++)
                {
                    double x = Math.Round(rng.NextDouble() * w / P) * P, y = Math.Round(rng.NextDouble() * (h - 120) / P) * P;
                    if ((int)(t * 1.3 + i * 7) % 11 == 0) continue;
                    dc.DrawRectangle(star, null, new Rect(x, y, P, P));
                }
                // lua
                double mx = Math.Round((w - 80) / P) * P;
                dc.DrawEllipse(Pal.B("#f4f1e0", 1 - day), null, new Point(mx, 40), 14, 14);
                dc.DrawEllipse(new SolidColorBrush(top), null, new Point(mx + 7, 36), 12, 12);
            }
            else
            {
                double sx = Math.Round((w - 80) / P) * P;
                dc.DrawEllipse(Pal.B("#ffe08a", 0.25), null, new Point(sx, 42), 26, 26);
                dc.DrawEllipse(Pal.B("#ffd23f", 1), null, new Point(sx, 42), 15, 15);
            }
            // nuvens em blocos, devagar
            Brush cloud = Pal.B("#ffffff", 0.10 + 0.25 * day);
            for (int i = 0; i < 3; i++)
            {
                double span = w + 160, x = ((t * (4 + i * 2) + i * 397) % span) - 120, y = 30 + i * 34;
                x = Math.Round(x / P) * P;
                dc.DrawRectangle(cloud, null, new Rect(x, y, 22 * P, 4 * P));
                dc.DrawRectangle(cloud, null, new Rect(x + 4 * P, y - 3 * P, 11 * P, 3 * P));
                dc.DrawRectangle(cloud, null, new Rect(x + 6 * P, y - 5 * P, 6 * P, 2 * P));
            }
            // morrinhos ao fundo
            Brush hill = new SolidColorBrush(Mix(Pal.C("#18243a"), Pal.C("#5b9a6e"), day));
            for (double x = 0; x < w; x += P)
            {
                double y = 22 + 14 * Math.Sin(x / 90) + 8 * Math.Sin(x / 37 + 1.3);
                y = Math.Round(y / P) * P;
                dc.DrawRectangle(hill, null, new Rect(x, h - Floor - y, P, y));
            }
            // o chão: grama com tufos
            Color grass = Mix(Pal.C("#1f3d2a"), Pal.C("#3f8a4c"), day), soil = Mix(Pal.C("#2a2119"), Pal.C("#6b4a2b"), day);
            dc.DrawRectangle(new SolidColorBrush(soil), null, new Rect(0, h - Floor, w, Floor));
            dc.DrawRectangle(new SolidColorBrush(grass), null, new Rect(0, h - Floor, w, 2 * P));
            Brush tuft = new SolidColorBrush(Mix(grass, Colors.White, 0.18));
            for (double x = 0; x < w; x += 13 * P)
            {
                dc.DrawRectangle(tuft, null, new Rect(x, h - Floor - P, P, P));
                dc.DrawRectangle(tuft, null, new Rect(x + 5 * P, h - Floor - P, P, P));
                dc.DrawRectangle(tuft, null, new Rect(x + 6 * P, h - Floor - 2 * P, P, P));
            }

            stage.RenderHome(dc);
        }
    }

    // O cartão de uma sessão: atualiza no lugar, para não piscar a cada leitura.
    sealed class SessionCard : Border
    {
        public string Id;
        readonly TextBlock title = Ui.Line1("", 14, Ui.Text, true), what = Ui.Line1("", 12.5, Ui.Dim, false), meta = Ui.Line1("", 11.5, Ui.Faint, false);
        readonly TextBlock accName = Ui.Line1("", 11.5, Ui.Dim, false);
        readonly Border accDot = Ui.Dot(Ui.Off, 8);
        readonly ContentControl badge = new ContentControl { Focusable = false };
        readonly ContentControl ctx = new ContentControl { Focusable = false };
        string sig = "";
        bool lit;
        public AccountCfg Acc;

        public SessionCard(string id, Action<string> ping)
        {
            Id = id;
            Width = 300;
            Margin = new Thickness(0, 0, 12, 12);
            Padding = new Thickness(16, 13, 16, 14);
            CornerRadius = new CornerRadius(12);
            Background = Ui.Card;
            BorderThickness = new Thickness(1);
            BorderBrush = Ui.Line;
            Cursor = Cursors.Hand;
            ToolTip = "Clique para o Clawd desta sessão dar um alô";

            var sp = new StackPanel();
            var top = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            DockPanel.SetDock(badge, Dock.Right);
            top.Children.Add(badge);
            accDot.Margin = new Thickness(0, 1, 7, 0);
            top.Children.Add(accDot);
            top.Children.Add(accName);
            sp.Children.Add(top);
            sp.Children.Add(title);
            what.Margin = new Thickness(0, 3, 0, 0);
            sp.Children.Add(what);
            ctx.Margin = new Thickness(0, 10, 0, 0);
            sp.Children.Add(ctx);
            meta.Margin = new Thickness(0, 8, 0, 0);
            sp.Children.Add(meta);
            Child = sp;

            MouseEnter += delegate { if (!lit) Background = Ui.CardHi; };
            MouseLeave += delegate { if (!lit) Background = Ui.Card; };
            MouseLeftButtonUp += delegate(object s, MouseButtonEventArgs e) { e.Handled = true; ping(Id); };
        }

        public void Light(bool on)
        {
            if (lit == on) return;
            lit = on;
            Background = on ? Ui.CardHi : Ui.Card;
            BorderBrush = on && Acc != null ? Pal.B(Acc.Body, 1) : Ui.Line;
        }

        public void Update(SessionView s, AccountCfg acc, double now)
        {
            Acc = acc;
            title.Text = s.Label;
            what.Text = Words.Short(s, now);
            string state = Ui.StateName(s, now);
            if (state + s.State != sig)
            {
                sig = state + s.State;
                badge.Content = Ui.Badge(state, Ui.StateColor(s, now));
            }
            accDot.Background = acc != null ? Pal.B(acc.Body, 1) : Ui.Off;
            accName.Text = acc != null ? acc.Name : "sem conta";
            if (!lit) BorderBrush = Ui.Line;

            var parts = new List<string>();
            string model = Ui.ModelName(s.Model);
            if (model.Length > 0) parts.Add(model);
            if (s.Agents > 0) parts.Add((int)s.Agents + (s.Agents == 1 ? " subagente" : " subagentes"));
            if (s.Turns > 0) parts.Add((int)s.Turns + (s.Turns == 1 ? " turno" : " turnos"));
            if (!double.IsNaN(s.Cost)) parts.Add(Words.Money(s.Cost));
            if (s.StartedAt > 0) parts.Add("aberta " + Words.Ago(now - s.StartedAt));
            meta.Text = string.Join("  ·  ", parts.ToArray());

            if (!double.IsNaN(s.Ctx) && s.IsActive(now))
            {
                var g = new Grid();
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var l = Ui.T("contexto", 11.5, Ui.Faint, false);
                l.Margin = new Thickness(0, 0, 10, 0);
                g.Children.Add(l);
                var bar = Ui.Bar(s.Ctx, s.Ctx >= 85 ? Ui.Bad : s.Ctx >= 65 ? Ui.Warn : Pal.B(acc != null ? acc.Body : Ui.Accent, 1), 5);
                Grid.SetColumn(bar, 1);
                g.Children.Add(bar);
                var p = Ui.T((int)Math.Round(s.Ctx) + "%", 11.5, Ui.Dim, false);
                p.Margin = new Thickness(10, 0, 0, 0);
                Grid.SetColumn(p, 2);
                g.Children.Add(p);
                ctx.Content = g;
                ctx.Visibility = Visibility.Visible;
            }
            else ctx.Visibility = Visibility.Collapsed;
            Opacity = s.IsActive(now) ? 1 : 0.62;
        }
    }

    sealed class HomePage : ContentControl
    {
        public readonly HomeStageView StageView;
        readonly Stage stage;
        readonly Func<Cfg> cfg;
        readonly TextBlock summary = Ui.T("", 13, Ui.Dim, false);
        readonly WrapPanel cards = new WrapPanel();
        readonly ContentControl filters = new ContentControl { Focusable = false };
        readonly ContentControl empty = new ContentControl { Focusable = false };
        readonly StackPanel limits = new StackPanel();
        readonly Dictionary<string, SessionCard> byId = new Dictionary<string, SessionCard>();
        List<SessionView> last = new List<SessionView>();
        double lastNow;
        int filter;       // 0: hoje, 1: ativas, 2: esperando você
        string account;   // null: todas
        string order = "", filterSig = "", limitSig = "";
        string lit;

        public HomePage(Stage s, Func<Cfg> config)
        {
            stage = s;
            cfg = config;
            Focusable = false;
            StageView = new HomeStageView(s);

            var sp = new StackPanel();
            var stageCard = new Border { CornerRadius = new CornerRadius(14), ClipToBounds = true, Margin = new Thickness(0, 0, 0, 10), BorderBrush = Ui.Line, BorderThickness = new Thickness(1) };
            var layer = new Grid();
            layer.Children.Add(StageView);
            var hint = Ui.T("Clique num Clawd: carinho · arraste: ele voa · clique no chão: eles vêm ver · duplo clique: abre o Claude", 11.5, Pal.B("#ffffff", 0.55), false);
            hint.VerticalAlignment = VerticalAlignment.Top;
            hint.Margin = new Thickness(14, 10, 14, 0);
            hint.IsHitTestVisible = false;
            layer.Children.Add(hint);
            stageCard.Child = new Border { CornerRadius = new CornerRadius(13), ClipToBounds = true, Child = layer };
            sp.Children.Add(stageCard);

            var bar = new DockPanel { Margin = new Thickness(2, 4, 0, 14) };
            var play = Ui.Button(Ui.IcPlay, "Brincar agora", null, delegate { stage.PlayNow(); });
            play.Margin = new Thickness(0);
            play.ToolTip = "Dois Clawds livres fazem uma brincadeira juntos.";
            DockPanel.SetDock(play, Dock.Right);
            bar.Children.Add(play);
            bar.Children.Add(summary);
            sp.Children.Add(bar);

            sp.Children.Add(filters);
            sp.Children.Add(empty);
            sp.Children.Add(cards);
            limits.Margin = new Thickness(0, 10, 0, 0);
            sp.Children.Add(limits);
            Content = sp;
            BuildFilters();
        }

        void BuildFilters()
        {
            var c = cfg();
            string sig = filter + "|" + account + "|" + string.Join(",", c.Accounts.Select(a => a.Key + a.Name + a.Body).ToArray());
            if (sig == filterSig) return;
            filterSig = sig;
            var row = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
            var seg = Ui.Segmented(new[] { "Hoje", "Ativas", "Esperando você" }, filter, delegate(int i) { filter = i; BuildFilters(); Update(last, lastNow); });
            seg.Margin = new Thickness(0, 0, 14, 6);
            row.Children.Add(seg);
            if (c.Accounts.Count > 1)
            {
                row.Children.Add(Ui.Chip(Ui.T("Todas as contas", 12.5, Ui.Text, false), account == null, Ui.Accent, delegate { account = null; BuildFilters(); Update(last, lastNow); }));
                foreach (var a0 in c.Accounts)
                {
                    var a = a0;
                    var r = new StackPanel { Orientation = Orientation.Horizontal };
                    var d = Ui.Dot(Pal.B(a.Body, 1), 9);
                    d.Margin = new Thickness(0, 1, 7, 0);
                    r.Children.Add(d);
                    r.Children.Add(Ui.T(a.Name, 12.5, Ui.Text, false));
                    row.Children.Add(Ui.Chip(r, account == a.Key, a.Body, delegate { account = a.Key; BuildFilters(); Update(last, lastNow); }));
                }
            }
            filters.Content = row;
        }

        static int Rank(SessionView s, double now)
        {
            if (!s.IsActive(now)) return 5;
            switch (s.State)
            {
                case "aguardando": return 0;
                case "erro": return 1;
                case "trabalhando": case "pensando": case "compactando": return 2;
                case "feito": return 3;
                default: return 4;
            }
        }

        public void Update(List<SessionView> all, double now)
        {
            last = all;
            lastNow = now;
            var c = cfg();
            BuildFilters();
            double todayMs = (DateTime.Today.ToUniversalTime() - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds;

            var today = all.Where(s => s.IsActive(now) || s.UpdatedAt >= todayMs).ToList();
            int working = today.Count(s => s.IsActive(now) && (s.State == "trabalhando" || s.State == "pensando" || s.State == "compactando"));
            int waiting = today.Count(s => s.IsActive(now) && s.State == "aguardando");
            int active = today.Count(s => s.IsActive(now));
            var bits = new List<string>();
            if (waiting > 0) bits.Add(waiting + (waiting == 1 ? " esperando você" : " esperando você"));
            if (working > 0) bits.Add(working + " trabalhando");
            bits.Add(active == 0 ? "nenhuma sessão ativa" : active == 1 ? "1 sessão ativa" : active + " sessões ativas");
            bits.Add(today.Count == 1 ? "1 sessão hoje" : today.Count + " sessões hoje");
            summary.Text = string.Join("  ·  ", bits.ToArray());

            var shown = today.Where(s => account == null || s.Account == account)
                .Where(s => filter == 0 || (filter == 1 ? s.IsActive(now) : s.IsActive(now) && s.State == "aguardando"))
                .OrderBy(s => Rank(s, now)).ThenByDescending(s => s.UpdatedAt).Take(30).ToList();

            foreach (var s in shown)
            {
                SessionCard card;
                if (!byId.TryGetValue(s.Id, out card)) { card = new SessionCard(s.Id, delegate(string id) { stage.Ping(id); }); byId[s.Id] = card; }
                card.Update(s, c.Accounts.FirstOrDefault(a => a.Key == s.Account), now);
                card.Light(s.Id == lit);
            }
            foreach (var id in byId.Keys.Where(k => !shown.Any(s => s.Id == k)).ToList()) byId.Remove(id);
            string sig = string.Join(",", shown.Select(s => s.Id).ToArray());
            if (sig != order)
            {
                order = sig;
                cards.Children.Clear();
                foreach (var s in shown) cards.Children.Add(byId[s.Id]);
            }

            if (shown.Count == 0)
            {
                string msg = filter == 2 ? "Ninguém esperando você agora." : filter == 1 ? "Nenhuma sessão ativa agora." : "Nenhuma sessão hoje.";
                var box = new StackPanel();
                box.Children.Add(Ui.T(msg, 15, Ui.Text, true));
                var n = Ui.T(filter == 0 ? "Abra o Claude e comece uma conversa: cada sessão aparece aqui, e o Clawd da conta acorda para trabalhar junto. Se nada aparecer, confira se o mod está instalado em Configurações." : "As outras sessões estão em \"Hoje\".", 12.5, Ui.Dim, false);
                n.Margin = new Thickness(0, 4, 0, 12);
                box.Children.Add(n);
                if (filter == 0)
                {
                    var btns = new WrapPanel();
                    foreach (var a0 in c.Accounts.Take(4))
                    {
                        var a = a0;
                        btns.Children.Add(Ui.Button(Ui.IcOpen, "Abrir o Claude " + a.Name, btns.Children.Count == 0 ? a.Accent : null, delegate { Open(a); }));
                    }
                    box.Children.Add(btns);
                }
                empty.Content = Ui.CardOf(box, new Thickness(22, 18, 22, 20));
                empty.Visibility = Visibility.Visible;
            }
            else empty.Visibility = Visibility.Collapsed;

            BuildLimits(c, all, now);
        }

        void Open(AccountCfg a)
        {
            try { Profiles.Launch(a.Profile, a.Name); }
            catch (Exception e) { MessageBox.Show(Window.GetWindow(this), e.Message, "Clawd Companheiro", MessageBoxButton.OK, MessageBoxImage.Warning); }
        }

        // Os limites de uso e o custo do dia de cada conta, lado a lado.
        void BuildLimits(Cfg c, List<SessionView> all, double now)
        {
            double todayMs = (DateTime.Today.ToUniversalTime() - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds;
            var items = new List<Tuple<AccountCfg, Usage, double>>();
            foreach (var a in c.Accounts)
            {
                if (account != null && a.Key != account) continue;
                var u = Data.UsageFrom(Data.Read(System.IO.Path.Combine(Cfg.Dir, "uso-" + a.Key + ".json")));
                double cost = all.Where(s => s.Account == a.Key && !double.IsNaN(s.Cost) && s.UpdatedAt >= todayMs).Sum(s => s.Cost);
                items.Add(Tuple.Create(a, u, cost));
            }
            string sig = string.Join("|", items.Select(x => x.Item1.Key + x.Item1.Name + x.Item1.Body + ":" + (x.Item2 != null ? string.Join(",", x.Item2.Limits.Select(l => l.Kind + (int)l.Pct + Words.Resets(l.Resets)).ToArray()) : "-") + ":" + x.Item3.ToString("0.00")).ToArray());
            if (sig == limitSig) return;
            limitSig = sig;
            limits.Children.Clear();
            if (items.Count == 0) return;
            limits.Children.Add(new Border { Child = Ui.T("Limites de uso", 15, Ui.Text, true), Margin = new Thickness(0, 0, 0, 10) });
            var wrap = new WrapPanel();
            foreach (var it in items)
            {
                var a = it.Item1;
                var sp = new StackPanel();
                var head = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
                var cost = Ui.T(it.Item3 > 0 ? "hoje " + Words.Money(it.Item3) : "", 12, Ui.Dim, false);
                DockPanel.SetDock(cost, Dock.Right);
                head.Children.Add(cost);
                var d = Ui.Dot(Pal.B(a.Body, 1), 9);
                d.Margin = new Thickness(0, 1, 8, 0);
                head.Children.Add(d);
                head.Children.Add(Ui.T(a.Name, 13.5, Ui.Text, true));
                sp.Children.Add(head);
                var lims = it.Item2 != null ? it.Item2.Limits : new List<Limit>();
                if (lims.Count == 0) sp.Children.Add(Ui.T("Aparecem depois da primeira resposta numa sessão desta conta.", 12, Ui.Dim, false));
                foreach (var l in lims)
                {
                    var g = new Grid { Margin = new Thickness(0, 0, 0, 7) };
                    g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
                    g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
                    g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
                    g.Children.Add(Ui.T(Words.LimitName(l.Kind), 12, Ui.Dim, false));
                    var b = Ui.Bar(l.Pct, Ui.LimitColor(l.Pct), 6);
                    Grid.SetColumn(b, 1);
                    g.Children.Add(b);
                    var p = Ui.T((int)Math.Round(l.Pct) + "%", 12, Ui.Text, true);
                    p.HorizontalAlignment = HorizontalAlignment.Right;
                    Grid.SetColumn(p, 2);
                    g.Children.Add(p);
                    var r = Ui.T(Words.Resets(l.Resets), 11.5, Ui.Faint, false);
                    r.HorizontalAlignment = HorizontalAlignment.Right;
                    r.ToolTip = "reinicia";
                    Grid.SetColumn(r, 3);
                    g.Children.Add(r);
                    sp.Children.Add(g);
                }
                var card = Ui.CardOf(sp, new Thickness(18, 14, 18, 10));
                card.Width = 360;
                card.Margin = new Thickness(0, 0, 12, 12);
                wrap.Children.Add(card);
            }
            limits.Children.Add(wrap);
        }

        // O Clawd sob o mouse acende o cartão da sessão dele.
        public void Highlight(string sessionId)
        {
            lit = sessionId;
            foreach (var kv in byId) kv.Value.Light(kv.Key == sessionId);
        }
    }
}
