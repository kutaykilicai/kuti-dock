using System.Windows.Media;
using KutayDock.Core;

namespace KutayDock
{
    /// <summary>Dock stilleri. "Cam" bulanık arka plan kullanır, diğerleri düz renk.</summary>
    internal sealed class Theme
    {
        public Brush DockBg, DockBorder, TileBg, TileHover, TileBorder, Fg, FgDim, Track;
        public bool Blur;
        public static readonly Brush Accent = B("#0A84FF"), Red = B("#FF453A"), Green = B("#30D158"), Orange = B("#FF9F0A"), White = B("#FFFFFF");
        public static readonly FontFamily Font = new("Segoe UI Variable Display, Segoe UI Variable Text, Segoe UI");

        public static Theme For(DockStyle s) => s switch
        {
            DockStyle.Siyah => Dark("#F00C0C0E", "#22FFFFFF", false),
            DockStyle.Gece => Dark("#EE0B1222", "#262E4A7A", false, tile: "#33182544", hover: "#4D22345E"),
            DockStyle.Beyaz => new Theme
            {
                DockBg = B("#F2F4F4F6"), DockBorder = B("#14000000"), TileBg = B("#0D000000"), TileHover = B("#1A000000"),
                TileBorder = B("#0F000000"), Fg = B("#FF1C1C1E"), FgDim = B("#991C1C1E"), Track = B("#1F000000"), Blur = false,
            },
            _ => Dark("#73141416", "#1FFFFFFF", true),
        };

        static Theme Dark(string bg, string border, bool blur, string tile = "#2E000000", string hover = "#1AFFFFFF") => new()
        {
            DockBg = B(bg), DockBorder = B(border), TileBg = B(tile), TileHover = B(hover), TileBorder = B("#1FFFFFFF"),
            Fg = B("#FFF5F5F7"), FgDim = B("#99F5F5F7"), Track = B("#33FFFFFF"), Blur = blur,
        };

        public static SolidColorBrush B(string hex)
        {
            var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            b.Freeze();
            return b;
        }
    }
}
