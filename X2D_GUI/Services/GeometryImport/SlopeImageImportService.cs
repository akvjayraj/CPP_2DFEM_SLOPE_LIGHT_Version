using System;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace GeotechSlopeApp.Services.GeometryImport;

/// <summary>File pick and bitmap load helpers for slope-image underlays.</summary>
public static class SlopeImageImportService
{
    public const string FileFilter =
        "Image files (*.png;*.jpg;*.jpeg;*.bmp;*.tif;*.tiff)|*.png;*.jpg;*.jpeg;*.bmp;*.tif;*.tiff|" +
        "PNG (*.png)|*.png|" +
        "JPEG (*.jpg;*.jpeg)|*.jpg;*.jpeg|" +
        "Bitmap (*.bmp)|*.bmp|" +
        "TIFF (*.tif;*.tiff)|*.tif;*.tiff|" +
        "All files (*.*)|*.*";

    public static bool TryPickImageFile(out string? filePath)
    {
        var dialog = new OpenFileDialog
        {
            Filter = FileFilter,
            Title = "Import Slope Image"
        };
        if (dialog.ShowDialog() != true)
        {
            filePath = null;
            return false;
        }

        filePath = dialog.FileName;
        return true;
    }

    public static BitmapImage LoadBitmap(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            throw new FileNotFoundException("Slope image file not found.", filePath);

        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.UriSource = new Uri(Path.GetFullPath(filePath), UriKind.Absolute);
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    /// <summary>Writes a PNG sidecar next to the .xfem (or copies an existing file).</summary>
    public static string WriteSidecarBesideProject(string xfemPath, string? sourceImagePath, BitmapSource? bitmap)
    {
        var dir = Path.GetDirectoryName(xfemPath) ?? ".";
        var baseName = Path.GetFileNameWithoutExtension(xfemPath);
        var sidecar = Path.Combine(dir, baseName + ".underlay.png");

        if (!string.IsNullOrWhiteSpace(sourceImagePath)
            && File.Exists(sourceImagePath)
            && Path.GetExtension(sourceImagePath).Equals(".png", StringComparison.OrdinalIgnoreCase)
            && !PathsEqual(sourceImagePath, sidecar))
        {
            File.Copy(sourceImagePath, sidecar, overwrite: true);
            return sidecar;
        }

        if (bitmap != null)
        {
            EncodePng(bitmap, sidecar);
            return sidecar;
        }

        if (!string.IsNullOrWhiteSpace(sourceImagePath) && File.Exists(sourceImagePath))
        {
            // Re-encode non-PNG sources to the sidecar PNG.
            var loaded = LoadBitmap(sourceImagePath);
            EncodePng(loaded, sidecar);
            return sidecar;
        }

        throw new InvalidOperationException("No slope image bytes available to write the underlay sidecar.");
    }

    public static string MakeRelativeToProject(string xfemPath, string absoluteImagePath)
    {
        var projectDir = Path.GetDirectoryName(Path.GetFullPath(xfemPath)) ?? "";
        var abs = Path.GetFullPath(absoluteImagePath);
        try
        {
            return Path.GetRelativePath(projectDir, abs).Replace('\\', '/');
        }
        catch
        {
            return abs;
        }
    }

    public static string ResolveBesideProject(string xfemPath, string storedPath)
    {
        if (string.IsNullOrWhiteSpace(storedPath)) return "";
        if (Path.IsPathRooted(storedPath) && File.Exists(storedPath)) return Path.GetFullPath(storedPath);
        var projectDir = Path.GetDirectoryName(Path.GetFullPath(xfemPath)) ?? ".";
        var combined = Path.GetFullPath(Path.Combine(projectDir, storedPath.Replace('/', Path.DirectorySeparatorChar)));
        return combined;
    }

    private static void EncodePng(BitmapSource bitmap, string path)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var fs = File.Create(path);
        encoder.Save(fs);
    }

    private static bool PathsEqual(string a, string b) =>
        string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
}
