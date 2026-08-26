using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GeotechSlopeApp.Views.Controls;

namespace GeotechSlopeApp.Services.Print;

public static class CanvasPrintService
{
    private const double HeaderFooterHeightDip = 28;
    private const double MmToDip = 96.0 / 25.4;

    public static FrameworkElement? TryResolveCanvas(DependencyObject? tabContent)
    {
        if (tabContent is null)
            return null;

        foreach (var slope in FindVisualChildren<SlopeCanvasView>(tabContent))
            return slope;

        foreach (var contour in FindVisualChildren<FEContourCanvasView>(tabContent))
            return contour;

        return null;
    }

    public static string FormatToken(string template, string projectName, string tabName)
    {
        return template
            .Replace("{ProjectName}", projectName, StringComparison.OrdinalIgnoreCase)
            .Replace("{TabName}", tabName, StringComparison.OrdinalIgnoreCase)
            .Replace("{Date}", DateTime.Now.ToString("yyyy-MM-dd HH:mm"), StringComparison.OrdinalIgnoreCase);
    }

    public static FixedDocument BuildFixedDocument(
        FrameworkElement canvas,
        PrintSettings settings,
        string projectName,
        string tabName)
    {
        var pageSize = GetPageSize(settings);
        var margins = GetMargins(settings);
        var headerText = settings.ShowHeader
            ? FormatToken(settings.HeaderFormat, projectName, tabName)
            : string.Empty;
        var footerText = settings.ShowFooter
            ? FormatToken(settings.FooterFormat, projectName, tabName)
            : string.Empty;

        var contentTop = margins.Top + (settings.ShowHeader ? HeaderFooterHeightDip : 0);
        var contentBottom = margins.Bottom + (settings.ShowFooter ? HeaderFooterHeightDip : 0);
        var contentWidth = Math.Max(1, pageSize.Width - margins.Left - margins.Right);
        var contentHeight = Math.Max(1, pageSize.Height - contentTop - contentBottom);

        EnsureLayout(canvas);
        var sourceWidth = Math.Max(1, canvas.ActualWidth);
        var sourceHeight = Math.Max(1, canvas.ActualHeight);

        var scale = settings.ScaleMode == PrintScaleMode.ActualSize
            ? 1.0
            : Math.Min(contentWidth / sourceWidth, contentHeight / sourceHeight);

        var drawWidth = sourceWidth * scale;
        var drawHeight = sourceHeight * scale;
        var offsetX = margins.Left + (contentWidth - drawWidth) / 2.0;
        var offsetY = contentTop + (contentHeight - drawHeight) / 2.0;

        var bitmap = CaptureElementBitmap(canvas, sourceWidth, sourceHeight);

        var page = new FixedPage
        {
            Width = pageSize.Width,
            Height = pageSize.Height,
            Background = Brushes.White
        };

        if (settings.ShowHeader && !string.IsNullOrWhiteSpace(headerText))
        {
            var header = CreateLabel(headerText, pageSize.Width, margins.Left, margins.Top, HorizontalAlignment.Left);
            FixedPage.SetLeft(header, margins.Left);
            FixedPage.SetTop(header, margins.Top);
            page.Children.Add(header);
        }

        if (settings.ShowFooter && !string.IsNullOrWhiteSpace(footerText))
        {
            var footer = CreateLabel(footerText, pageSize.Width, margins.Left, pageSize.Height - margins.Bottom - HeaderFooterHeightDip, HorizontalAlignment.Center);
            FixedPage.SetLeft(footer, margins.Left);
            FixedPage.SetTop(footer, pageSize.Height - margins.Bottom - HeaderFooterHeightDip);
            page.Children.Add(footer);
        }

        var image = new Image
        {
            Source = bitmap,
            Width = drawWidth,
            Height = drawHeight,
            Stretch = Stretch.Uniform
        };
        FixedPage.SetLeft(image, offsetX);
        FixedPage.SetTop(image, offsetY);
        page.Children.Add(image);

        var document = new FixedDocument();
        var pageContent = new PageContent();
        ((IAddChild)pageContent).AddChild(page);
        document.Pages.Add(pageContent);
        return document;
    }

    public static bool ShowPrintDialog(FixedDocument document, string jobName = "GeotechSlope Print")
    {
        var dialog = new PrintDialog();
        if (dialog.ShowDialog() != true)
            return false;

        dialog.PrintDocument(document.DocumentPaginator, jobName);
        return true;
    }

    private static Size GetPageSize(PrintSettings settings)
    {
        // A4 at 96 DPI: 210 x 297 mm
        const double widthMm = 210;
        const double heightMm = 297;
        var portrait = new Size(widthMm * MmToDip, heightMm * MmToDip);
        return settings.Orientation == PrintPageOrientation.Portrait
            ? portrait
            : new Size(portrait.Height, portrait.Width);
    }

    private static Thickness GetMargins(PrintSettings settings) =>
        new(
            settings.MarginLeftMm * MmToDip,
            settings.MarginTopMm * MmToDip,
            settings.MarginRightMm * MmToDip,
            settings.MarginBottomMm * MmToDip);

    private static TextBlock CreateLabel(string text, double width, double marginLeft, double top, HorizontalAlignment alignment) =>
        new()
        {
            Text = text,
            Width = width - marginLeft * 2,
            Height = HeaderFooterHeightDip,
            FontSize = 11,
            Foreground = Brushes.Black,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = alignment == HorizontalAlignment.Center ? TextAlignment.Center : TextAlignment.Left
        };

    private static void EnsureLayout(FrameworkElement element)
    {
        if (element.ActualWidth > 0 && element.ActualHeight > 0)
            return;

        element.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        element.Arrange(new Rect(element.DesiredSize));
        element.UpdateLayout();
    }

    private static BitmapSource CaptureElementBitmap(FrameworkElement element, double widthDip, double heightDip)
    {
        if (element is SlopeCanvasView slopeCanvas)
        {
            var captured = slopeCanvas.CaptureForPrint();
            if (captured is not null)
                return captured;
        }

        return RenderElement(element, (int)Math.Ceiling(widthDip), (int)Math.Ceiling(heightDip));
    }

    private static RenderTargetBitmap RenderElement(FrameworkElement element, int width, int height)
    {
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element);
        bitmap.Freeze();
        return bitmap;
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject
    {
        if (parent is T match)
            yield return match;

        var count = VisualTreeHelper.GetChildrenCount(parent);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            foreach (var descendant in FindVisualChildren<T>(child))
                yield return descendant;
        }
    }
}
