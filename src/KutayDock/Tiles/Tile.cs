using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using KutayDock.Core;

namespace KutayDock.Tiles
{
    /// <summary>Dock'taki bir kart. Her saniye Refresh() ile güncellenir.</summary>
    internal abstract class Tile : Border
    {
        protected readonly Services S;
        protected readonly Theme T;
        protected readonly double K; // ölçek
        public abstract string Id { get; }
        public virtual bool Wanted => true; // ör. pil yoksa pil kartı gizlenir

        protected Tile(Services s, Theme t, bool card = true)
        {
            S = s; T = t; K = s.Settings.Scale;
            Height = 48 * K;
            // Dikey dock (Sol/Sağ): kartlar alt alta, aralık dikeyde
            Margin = DockLayout.Vertical(s.Settings.Edge) ? new Thickness(0, 2 * K, 0, 2 * K) : new Thickness(2 * K, 0, 2 * K, 0);
            VerticalAlignment = VerticalAlignment.Center; HorizontalAlignment = HorizontalAlignment.Center;
            if (card)
            {
                CornerRadius = new CornerRadius(16 * K); Background = t.TileBg; BorderBrush = t.TileBorder;
                BorderThickness = new Thickness(1); Padding = new Thickness(10 * K, 0, 10 * K, 0);
                MouseEnter += (_, _) => Background = t.TileHover;
                MouseLeave += (_, _) => Background = t.TileBg;
            }
        }

        public abstract void Refresh();

        protected TextBlock Txt(string s, double size, bool dim = false, FontWeight? w = null, double maxW = double.PositiveInfinity) =>
            Ui.Text(s, size * K, dim ? T.FgDim : T.Fg, w, maxW * K);

        protected static StackPanel Row(params UIElement[] items)
        {
            var p = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            foreach (var i in items) p.Children.Add(i);
            return p;
        }

        protected static StackPanel Col(params UIElement[] items)
        {
            var p = new StackPanel { Orientation = Orientation.Vertical, VerticalAlignment = VerticalAlignment.Center };
            foreach (var i in items) p.Children.Add(i);
            return p;
        }

        protected void OnClick(Action a)
        {
            Cursor = Cursors.Hand;
            MouseLeftButtonUp += (_, e) => { a(); e.Handled = true; };
        }

        protected static void Run(string file, string args = null)
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(file, args ?? "") { UseShellExecute = true }); }
            catch (Exception) { }
        }
    }
}
