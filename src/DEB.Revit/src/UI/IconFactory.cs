using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DEB.Revit.UI
{
    /// <summary>
    /// Gera ícones simples em código, para o plugin não depender de arquivos de imagem.
    /// Troque por PNGs próprios quando quiser (LargeImage 32x32, Image 16x16).
    /// </summary>
    internal static class IconFactory
    {
        public static BitmapSource Create(int size, string text)
        {
            var bg = new SolidColorBrush(Color.FromRgb(0x1F, 0x4E, 0x79));
            var visual = new DrawingVisual();
            using (DrawingContext dc = visual.RenderOpen())
            {
                double r = size * 0.18;
                dc.DrawRoundedRectangle(bg, null, new Rect(0, 0, size, size), r, r);
                var ft = new FormattedText(
                    text,
                    CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight,
                    new Typeface("Segoe UI Semibold"),
                    size * (text.Length > 1 ? 0.42 : 0.62),
                    Brushes.White,
                    1.0);
                dc.DrawText(ft, new Point((size - ft.Width) / 2.0, (size - ft.Height) / 2.0));
            }

            var bmp = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(visual);
            bmp.Freeze();
            return bmp;
        }
    }
}
