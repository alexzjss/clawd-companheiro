// A Home: o escritório dos Clawds ocupando a janela inteira (ClawdOffice.cs), com um resumo
// no alto e, ao clicar num Clawd ou numa mesa, um painel com os detalhes daquela sessão.
// Enquanto ela está à vista, os Clawds da barra de tarefas somem: estão aqui.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace ClawdDesktop
{
    sealed class HomePage : Grid
    {
        public readonly OfficeView Office;
        readonly Func<Cfg> cfg;
        readonly TextBlock summary = Ui.T("", 12.5, Ui.Text, false);
        readonly Border detail = new Border();
        readonly Border frame;
        readonly StackPanel detailBody = new StackPanel();
        List<SessionView> last = new List<SessionView>();
        double lastNow;

        public HomePage(Func<Cfg> config, bool photo)
        {
            cfg = config;
            Office = new OfficeView(config, photo);
            frame = new Border { CornerRadius = new CornerRadius(14), ClipToBounds = true, BorderBrush = Ui.Line, BorderThickness = new Thickness(1), Child = Office };
            Children.Add(frame);

            // o resumo, numa pílula no canto de cima
            var pill = new Border { Background = Pal.B("#15131a", 0.88), CornerRadius = new CornerRadius(14), Padding = new Thickness(12, 5, 14, 6), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(14, 0, 0, 14), IsHitTestVisible = false };
            summary.TextWrapping = TextWrapping.NoWrap;
            pill.Child = summary;
            Children.Add(pill);

            // os detalhes da sessão escolhida, à direita
            detail.Width = 300;
            detail.HorizontalAlignment = HorizontalAlignment.Right;
            detail.VerticalAlignment = VerticalAlignment.Top;
            detail.Margin = new Thickness(0, 0, 0, 0);
            detail.Background = Pal.B("#1b1922", 0.97);
            detail.BorderBrush = Ui.Line;
            detail.BorderThickness = new Thickness(1);
            detail.CornerRadius = new CornerRadius(12);
            detail.Padding = new Thickness(18, 14, 18, 16);
            detail.Child = detailBody;
            detail.Visibility = Visibility.Collapsed;
            Children.Add(detail);

            Office.SelectionChanged += delegate { BuildDetail(); };
            Office.OpenClaude += Open;
        }

        public void Update(List<SessionView> all, double now, Dictionary<string, int> running)
        {
            last = all;
            lastNow = now;
            Office.Update(all, now);
            Office.SetRunning(running);

            int working = all.Count(s => s.IsActive(now) && (s.State == "trabalhando" || s.State == "pensando" || s.State == "compactando"));
            int waiting = all.Count(s => s.IsActive(now) && s.State == "aguardando");
            int active = all.Count(s => s.IsActive(now));
            var bits = new List<string>();
            if (waiting > 0) bits.Add(waiting + " esperando você");
            if (working > 0) bits.Add(working + " trabalhando");
            if (active == 0) bits.Add("ninguém trabalhando agora: abra o Claude e comece uma conversa");
            else bits.Add(active == 1 ? "1 sessão ativa" : active + " sessões ativas");
            summary.Text = string.Join("  ·  ", bits.ToArray());
            BuildDetail();
        }

        void Open(AccountCfg a)
        {
            if (a == null) return;
            try { Profiles.Launch(a.Profile, a.Name); }
            catch (Exception e) { MessageBox.Show(Window.GetWindow(this), e.Message, AppPaths.Name, MessageBoxButton.OK, MessageBoxImage.Warning); }
        }

        void Row(string label, string value)
        {
            var g = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(92) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.Children.Add(Ui.T(label, 12, Ui.Faint, false));
            var v = Ui.T(value, 12.5, Ui.Text, false);
            Grid.SetColumn(v, 1);
            g.Children.Add(v);
            detailBody.Children.Add(g);
        }

        void BuildDetail()
        {
            string id = Office.Selected;
            var s = Office.SessionOf(id);
            if (s == null)
            {
                // sem sessão escolhida, o escritório ocupa tudo
                detail.Visibility = Visibility.Collapsed;
                frame.Margin = new Thickness(0);
                return;
            }
            var acc = Office.AccountOf(id);
            double now = lastNow;
            detailBody.Children.Clear();

            var top = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
            var close = Ui.Icon(Ui.IcClose, 11, Ui.Dim);
            close.Cursor = Cursors.Hand;
            close.ToolTip = "Fechar";
            close.MouseLeftButtonUp += delegate { Office.Select(null); };
            DockPanel.SetDock(close, Dock.Right);
            top.Children.Add(close);
            var dot = Ui.Dot(acc != null ? Pal.B(acc.Body, 1) : Ui.Off, 9);
            dot.Margin = new Thickness(0, 1, 8, 0);
            top.Children.Add(dot);
            top.Children.Add(Ui.T(acc != null ? acc.Name : "Visitante", 12.5, Ui.Dim, false));
            detailBody.Children.Add(top);

            detailBody.Children.Add(Ui.T(s.Label, 16, Ui.Text, true));
            var badge = Ui.Badge(Ui.StateName(s, now), Ui.StateColor(s, now));
            badge.HorizontalAlignment = HorizontalAlignment.Left;
            badge.Margin = new Thickness(0, 8, 0, 4);
            detailBody.Children.Add(badge);
            var what = Ui.T(Words.Short(s, now), 12.5, Ui.Dim, false);
            what.Margin = new Thickness(0, 0, 0, 12);
            detailBody.Children.Add(what);

            string model = Ui.ModelName(s.Model);
            if (model.Length > 0) Row("Modelo", model);
            if (s.Agents > 0) Row("Subagentes", ((int)s.Agents).ToString());
            if (s.Turns > 0) Row("Turnos", ((int)s.Turns).ToString());
            if (!double.IsNaN(s.Cost)) Row("Custo", Words.Money(s.Cost));
            if (s.StartedAt > 0) Row("Aberta", Words.Ago(now - s.StartedAt));
            if (!double.IsNaN(s.Ctx) && s.IsActive(now))
            {
                var g = new Grid { Margin = new Thickness(0, 2, 0, 6) };
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(92) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                g.Children.Add(Ui.T("Contexto", 12, Ui.Faint, false));
                var bar = Ui.Bar(s.Ctx, s.Ctx >= 85 ? Ui.Bad : s.Ctx >= 65 ? Ui.Warn : Pal.B(acc != null ? acc.Body : Ui.Accent, 1), 6);
                Grid.SetColumn(bar, 1);
                g.Children.Add(bar);
                var pct = Ui.T((int)Math.Round(s.Ctx) + "%", 12, Ui.Text, false);
                pct.Margin = new Thickness(8, 0, 0, 0);
                Grid.SetColumn(pct, 2);
                g.Children.Add(pct);
                detailBody.Children.Add(g);
            }
            if (acc != null)
            {
                var open = Ui.Button(Ui.IcOpen, "Abrir o Claude " + acc.Name, acc.Accent, delegate { Open(acc); });
                open.Margin = new Thickness(0, 12, 0, 0);
                detailBody.Children.Add(open);
            }
            detail.Visibility = Visibility.Visible;
            frame.Margin = new Thickness(0, 0, 314, 0);   // o escritório abre espaço para os detalhes
        }
    }
}
