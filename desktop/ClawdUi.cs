// O visual do app: cores, textos e os controles feitos à mão (botões, interruptores,
// cartões, abas, barras), todos no mesmo estilo escuro. C# 5, sem XAML.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace ClawdDesktop
{
    static class Ui
    {
        // superfícies, da mais funda para a mais alta
        public static readonly Brush Bg = Pal.B("#0f0e13", 1), Side = Pal.B("#15131a", 1), Card = Pal.B("#1b1922", 1);
        public static readonly Brush CardHi = Pal.B("#23202c", 1), Soft = Pal.B("#2a2734", 1), SoftHi = Pal.B("#353142", 1), Line = Pal.B("#2e2a39", 1);
        // textos
        public static readonly Brush Text = Pal.B("#f3f1f7", 1), Dim = Pal.B("#a7a1b4", 1), Faint = Pal.B("#6f6a7c", 1);
        // estados (sempre junto de um texto, nunca só a cor)
        public static readonly Brush Good = Pal.B("#4ade80", 1), Warn = Pal.B("#f5a524", 1), Bad = Pal.B("#ef5350", 1), Info = Pal.B("#5cc8ff", 1), Ask = Pal.B("#ff7eb6", 1), Off = Pal.B("#5b5666", 1);
        public const string Accent = "#d97757";
        public static readonly FontFamily Font = new FontFamily("Segoe UI Variable Text, Segoe UI"), Mono = new FontFamily("Cascadia Mono, Consolas");
        public static readonly FontFamily Icons = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets");

        // ícones (Segoe MDL2 / Fluent)
        public const string IcHome = "\uE80F", IcChart = "\uE9D2", IcPeople = "\uE716", IcSettings = "\uE713", IcPlay = "\uE768";
        public const string IcOpen = "\uE8A7", IcFolder = "\uE838", IcSync = "\uE895", IcBell = "\uE7E7", IcCheck = "\uE73E";
        public const string IcInfo = "\uE946", IcClose = "\uE711", IcCopy = "\uE8C8", IcAdd = "\uE710", IcDownload = "\uE896", IcHand = "\uE76B";

        public static TextBlock T(string text, double size, Brush color, bool bold)
        {
            var t = new TextBlock();
            t.Text = text;
            t.FontFamily = Font;
            t.FontSize = size;
            t.Foreground = color;
            t.FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal;
            t.TextWrapping = TextWrapping.Wrap;
            t.VerticalAlignment = VerticalAlignment.Center;
            return t;
        }

        public static TextBlock Line1(string text, double size, Brush color, bool bold)
        {
            var t = T(text, size, color, bold);
            t.TextWrapping = TextWrapping.NoWrap;
            t.TextTrimming = TextTrimming.CharacterEllipsis;
            return t;
        }

        public static TextBlock Icon(string glyph, double size, Brush color)
        {
            var t = new TextBlock { Text = glyph, FontFamily = Icons, FontSize = size, Foreground = color, VerticalAlignment = VerticalAlignment.Center };
            return t;
        }

        // Preto ou branco, o que der para ler em cima daquela cor.
        public static Brush On(string hex)
        {
            Color c = Tint.Parse(hex, "#888888");
            return 0.299 * c.R + 0.587 * c.G + 0.114 * c.B > 150 ? Pal.B("#16131c", 1) : Brushes.White;
        }

        static Brush Lighter(Brush b)
        {
            var s = b as SolidColorBrush;
            if (s == null) return b;
            Color c = s.Color;
            return new SolidColorBrush(Color.FromArgb(c.A, (byte)Math.Min(255, c.R + 18), (byte)Math.Min(255, c.G + 18), (byte)Math.Min(255, c.B + 18)));
        }

        // Botão: com accent é o principal da tela; sem, é o secundário.
        public static Border Button(string text, string accent, Action click)
        {
            return Button(null, text, accent, click);
        }

        public static Border Button(string icon, string text, string accent, Action click)
        {
            var b = new Border();
            b.CornerRadius = new CornerRadius(8);
            b.Padding = new Thickness(13, 7, 13, 8);
            b.Margin = new Thickness(0, 0, 8, 0);
            b.HorizontalAlignment = HorizontalAlignment.Left;
            b.VerticalAlignment = VerticalAlignment.Center;
            Brush bg = accent != null ? Pal.B(accent, 1) : Soft, hi = Lighter(bg);
            Brush fg = accent != null ? On(accent) : Text;
            b.Background = bg;
            b.Cursor = Cursors.Hand;
            b.Focusable = true;
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            if (icon != null)
            {
                var ic = Icon(icon, 12.5, fg);
                ic.Margin = new Thickness(0, 1, text.Length > 0 ? 7 : 0, 0);
                row.Children.Add(ic);
            }
            if (text.Length > 0)
            {
                var t = T(text, 12.5, fg, accent != null);
                t.TextWrapping = TextWrapping.NoWrap;
                row.Children.Add(t);
            }
            b.Child = row;
            b.MouseEnter += delegate { b.Background = hi; };
            b.MouseLeave += delegate { b.Background = bg; };
            b.MouseLeftButtonUp += delegate(object s, MouseButtonEventArgs e) { e.Handled = true; click(); };
            b.KeyDown += delegate(object s, KeyEventArgs e) { if (e.Key == Key.Enter || e.Key == Key.Space) { e.Handled = true; click(); } };
            return b;
        }

        // Um botão de escolha: fica com a borda da cor dada quando é o escolhido.
        public static Border Chip(UIElement content, bool selected, string accent, Action click)
        {
            var b = new Border();
            b.CornerRadius = new CornerRadius(8);
            b.Padding = new Thickness(10, 5, 10, 6);
            b.Margin = new Thickness(0, 0, 6, 6);
            b.Background = selected ? Pal.B(accent, 0.18) : Soft;
            b.BorderThickness = new Thickness(1.5);
            b.BorderBrush = selected ? Pal.B(accent, 1) : Brushes.Transparent;
            b.Cursor = Cursors.Hand;
            b.Child = content;
            b.MouseEnter += delegate { if (!selected) b.BorderBrush = SoftHi; };
            b.MouseLeave += delegate { if (!selected) b.BorderBrush = Brushes.Transparent; };
            b.MouseLeftButtonUp += delegate(object s, MouseButtonEventArgs e) { e.Handled = true; click(); };
            return b;
        }

        // Abas em linha (só uma escolhida).
        public static Border Segmented(string[] labels, int selected, Action<int> pick)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            for (int i = 0; i < labels.Length; i++)
            {
                int k = i;
                bool on = i == selected;
                var seg = new Border { CornerRadius = new CornerRadius(6), Padding = new Thickness(12, 5, 12, 6), Cursor = Cursors.Hand };
                seg.Background = on ? SoftHi : Brushes.Transparent;
                var t = T(labels[i], 12.5, on ? Text : Dim, on);
                t.TextWrapping = TextWrapping.NoWrap;
                seg.Child = t;
                seg.MouseEnter += delegate { if (!on) t.Foreground = Text; };
                seg.MouseLeave += delegate { if (!on) t.Foreground = Dim; };
                seg.MouseLeftButtonUp += delegate(object s, MouseButtonEventArgs e) { e.Handled = true; if (!on) pick(k); };
                row.Children.Add(seg);
            }
            return new Border { Background = Soft, CornerRadius = new CornerRadius(8), Padding = new Thickness(3), Child = row, HorizontalAlignment = HorizontalAlignment.Left };
        }

        // Interruptor liga/desliga.
        public static Border Toggle(bool value, Action<bool> set)
        {
            bool v = value;
            var knob = new Ellipse { Width = 14, Height = 14, Fill = Brushes.White };
            var track = new Border { Width = 40, Height = 22, CornerRadius = new CornerRadius(11), Cursor = Cursors.Hand, Padding = new Thickness(4, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center };
            track.Child = knob;
            Action paint = delegate
            {
                track.Background = v ? Pal.B(Accent, 1) : SoftHi;
                knob.HorizontalAlignment = v ? HorizontalAlignment.Right : HorizontalAlignment.Left;
                knob.Fill = v ? Brushes.White : Dim;
            };
            paint();
            track.MouseLeftButtonUp += delegate(object s, MouseButtonEventArgs e) { e.Handled = true; v = !v; paint(); set(v); };
            return track;
        }

        // Uma linha de configuração: título e explicação à esquerda, o controle à direita.
        public static FrameworkElement Setting(string title, string note, UIElement control)
        {
            var g = new Grid { Margin = new Thickness(0, 10, 0, 10) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var left = new StackPanel { Margin = new Thickness(0, 0, 20, 0), VerticalAlignment = VerticalAlignment.Center };
            left.Children.Add(T(title, 13.5, Text, false));
            if (note != null)
            {
                var n = T(note, 12, Dim, false);
                n.Margin = new Thickness(0, 2, 0, 0);
                left.Children.Add(n);
            }
            g.Children.Add(left);
            if (control != null)
            {
                Grid.SetColumn(control, 1);
                g.Children.Add(control);
            }
            return g;
        }

        public static Border Divider()
        {
            return new Border { Height = 1, Background = Line };
        }

        // Cartão com título e explicação opcionais.
        public static Border Section(string title, string note, UIElement body)
        {
            var sp = new StackPanel();
            if (title != null) sp.Children.Add(T(title, 15, Text, true));
            if (note != null)
            {
                var n = T(note, 12.5, Dim, false);
                n.Margin = new Thickness(0, 3, 0, 0);
                sp.Children.Add(n);
            }
            var holder = new ContentControl { Content = body, Margin = new Thickness(0, title != null || note != null ? 14 : 0, 0, 0), Focusable = false };
            sp.Children.Add(holder);
            return CardOf(sp, new Thickness(22, 18, 22, 20));
        }

        public static Border CardOf(UIElement child, Thickness pad)
        {
            return new Border { Background = Card, CornerRadius = new CornerRadius(12), Padding = pad, Margin = new Thickness(0, 0, 0, 14), Child = child, BorderBrush = Line, BorderThickness = new Thickness(1) };
        }

        public static FrameworkElement Check(string text, bool value, Action<bool> set)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 8) };
            var tg = Toggle(value, set);
            tg.Margin = new Thickness(0, 0, 10, 0);
            row.Children.Add(tg);
            row.Children.Add(T(text, 13, Text, false));
            return row;
        }

        public static Border Dot(Brush b, double size)
        {
            return new Border { Width = size, Height = size, CornerRadius = new CornerRadius(size / 2), Background = b, VerticalAlignment = VerticalAlignment.Center };
        }

        // Etiqueta pequena: um ponto colorido e um texto (o texto diz o estado; a cor só ajuda).
        public static Border Badge(string text, Brush color)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            var d = Dot(color, 7);
            d.Margin = new Thickness(0, 1, 6, 0);
            row.Children.Add(d);
            var t = T(text, 11.5, Text, false);
            t.TextWrapping = TextWrapping.NoWrap;
            row.Children.Add(t);
            return new Border { Background = Pal.B("#ffffff", 0.06), CornerRadius = new CornerRadius(10), Padding = new Thickness(8, 2, 9, 3), Child = row, VerticalAlignment = VerticalAlignment.Center };
        }

        // Barra de progresso fina.
        public static FrameworkElement Bar(double pct, Brush fill, double height)
        {
            pct = Math.Max(0, Math.Min(100, pct));
            var g = new Grid { Height = height, VerticalAlignment = VerticalAlignment.Center };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.001, pct), GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.001, 100 - pct), GridUnitType.Star) });
            var track = new Border { Background = Pal.B("#ffffff", 0.09), CornerRadius = new CornerRadius(height / 2) };
            Grid.SetColumnSpan(track, 2);
            g.Children.Add(track);
            if (pct > 0.5) g.Children.Add(new Border { Background = fill, CornerRadius = new CornerRadius(height / 2) });
            return g;
        }

        public static Brush LimitColor(double pct)
        {
            return pct >= 85 ? Bad : pct >= 60 ? Warn : Good;
        }

        // A cor e o nome do estado de uma sessão.
        public static Brush StateColor(SessionView s, double now)
        {
            if (!s.IsActive(now)) return Off;
            switch (s.State)
            {
                case "trabalhando": return Warn;
                case "pensando": case "compactando": return Info;
                case "aguardando": return Ask;
                case "feito": return Good;
                case "erro": case "recusa": case "interrompido": return Bad;
                default: return Dim;
            }
        }

        public static string StateName(SessionView s, double now)
        {
            if (!s.IsActive(now)) return s.EndedAt > 0 ? "encerrada" : "parada";
            switch (s.State)
            {
                case "trabalhando": return "trabalhando";
                case "pensando": return "pensando";
                case "compactando": return "resumindo";
                case "aguardando": return "esperando você";
                case "feito": return "terminou";
                case "erro": return "erro";
                case "recusa": return "recusou";
                case "interrompido": return "interrompida";
                case "dormindo": return "dormindo";
                default: return "livre";
            }
        }

        public static string ModelName(string model)
        {
            string m = (model ?? "").ToLowerInvariant();
            if (m.Contains("fable")) return "Fable";
            if (m.Contains("opus")) return "Opus";
            if (m.Contains("sonnet")) return "Sonnet";
            if (m.Contains("haiku")) return "Haiku";
            return "";
        }

        // ---- seletor de cores

        static string NormHex(string text)
        {
            string t = (text ?? "").Trim().TrimStart('#').ToLowerInvariant();
            if (t.Length == 3) t = "" + t[0] + t[0] + t[1] + t[1] + t[2] + t[2];
            t = "#" + t;
            return Tint.IsHex(t) ? t : null;
        }

        static IEnumerable<string> PickerColors()
        {
            double[] hues = { 0, 14, 28, 42, 55, 90, 140, 165, 189, 210, 232, 268, 295, 330 };
            foreach (double l in new[] { 0.36, 0.5, 0.66 })
                foreach (double h in hues) yield return Tint.Hsl(h, l > 0.6 ? 0.8 : 0.72, l);
            foreach (double l in new[] { 0.08, 0.22, 0.38, 0.55, 0.72, 0.86, 0.96 }) yield return Tint.Hsl(250, 0.06, l);
        }

        public static Popup OpenPicker(FrameworkElement target, string current, Action<string> set)
        {
            var pop = new Popup { PlacementTarget = target, Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true, VerticalOffset = 4 };
            var box = new StackPanel { Width = 7 * 28 };
            var wrap = new WrapPanel();
            foreach (string c in PickerColors())
            {
                string hex = c;
                var cell = new Border { Width = 24, Height = 24, Margin = new Thickness(2), CornerRadius = new CornerRadius(5), Background = Pal.B(hex, 1), Cursor = Cursors.Hand, ToolTip = hex };
                if (hex == current) { cell.BorderBrush = Brushes.White; cell.BorderThickness = new Thickness(2); }
                cell.MouseLeftButtonUp += delegate(object s, MouseButtonEventArgs e) { e.Handled = true; pop.IsOpen = false; set(hex); };
                wrap.Children.Add(cell);
            }
            box.Children.Add(wrap);

            var row = new DockPanel { Margin = new Thickness(2, 8, 2, 0) };
            var tb = new TextBox { Text = current, FontFamily = Mono, FontSize = 13, Width = 72, Padding = new Thickness(5, 4, 5, 4), Background = Soft, Foreground = Text, CaretBrush = Text, BorderBrush = Line, MaxLength = 7 };
            Action apply = delegate
            {
                string v = NormHex(tb.Text);
                if (v == null) { tb.BorderBrush = Bad; return; }
                pop.IsOpen = false;
                set(v);
            };
            tb.KeyDown += delegate(object s, KeyEventArgs e) { if (e.Key == Key.Enter) { e.Handled = true; apply(); } };
            row.Children.Add(tb);
            var more = Button("Mais…", null, delegate
            {
                pop.IsOpen = false;
                var dlg = new System.Windows.Forms.ColorDialog();
                dlg.FullOpen = true;
                Color c = Tint.Parse(current, "#888888");
                dlg.Color = System.Drawing.Color.FromArgb(c.R, c.G, c.B);
                if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK) set(Tint.Hex(Color.FromRgb(dlg.Color.R, dlg.Color.G, dlg.Color.B)));
            });
            more.Margin = new Thickness(0);
            more.ToolTip = "Abre o seletor de cores do Windows.";
            DockPanel.SetDock(more, Dock.Right);
            row.Children.Add(more);
            var ok = Button("OK", null, apply);
            ok.Margin = new Thickness(6, 0, 6, 0);
            row.Children.Add(ok);
            box.Children.Add(row);

            pop.Child = new Border { Background = Card, BorderBrush = Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(9), Child = box };
            pop.IsOpen = true;
            return pop;
        }

        public static FrameworkElement ColorCell(string label, string tip, string value, Action<string> set, Action<Popup> opened)
        {
            var cell = new StackPanel { Margin = new Thickness(0, 0, 8, 8), Width = 96 };
            var l = T(label, 12, Dim, false);
            l.Margin = new Thickness(1, 0, 0, 4);
            cell.Children.Add(l);
            var sw = new Border();
            sw.Height = 32;
            sw.CornerRadius = new CornerRadius(8);
            sw.Background = Pal.B(value, 1);
            sw.BorderBrush = Line;
            sw.BorderThickness = new Thickness(1);
            sw.Cursor = Cursors.Hand;
            var hex = T(value, 12, On(value), false);
            hex.FontFamily = Mono;
            hex.HorizontalAlignment = HorizontalAlignment.Center;
            sw.Child = hex;
            if (tip != null) sw.ToolTip = tip;
            sw.MouseLeftButtonUp += delegate(object s, MouseButtonEventArgs e)
            {
                e.Handled = true;
                var p = OpenPicker(sw, value, set);
                if (opened != null) opened(p);
            };
            cell.Children.Add(sw);
            return cell;
        }

        // Um aviso discreto que some sozinho (o "toast" de dentro da janela).
        public static Border Toast(string text, Brush color)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            var ic = Icon(IcCheck, 12, color);
            ic.Margin = new Thickness(0, 1, 8, 0);
            row.Children.Add(ic);
            row.Children.Add(T(text, 12.5, Text, false));
            return new Border { Background = CardHi, BorderBrush = Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(14, 9, 16, 10), Child = row, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 20), IsHitTestVisible = false };
        }
    }
}
