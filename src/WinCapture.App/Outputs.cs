using Windows.Win32.Foundation;
using WinCapture.Core;

namespace WinCapture.App;

/// <summary>What became of an image that was to be copied or saved.</summary>
/// <param name="Summary">What was done, as a short headline.</param>
/// <param name="SavedPath">The file written. Null when the image was not saved.</param>
/// <param name="Problem">Why saving failed. Null when it did not.</param>
/// <param name="Png">The image as a PNG file.</param>
internal sealed record Output(string Summary, string? SavedPath, string? Problem, byte[] Png);

internal static class Outputs
{
    // The shape of the picture in a notification, and twice the width a
    // notification has at 100 % scaling, so the preview is still sharp at 200 %.
    private const int PreviewAspectWidth = 16;
    private const int PreviewAspectHeight = 9;
    private const int PreviewWidth = 728;
    private const int PreviewHeight = PreviewWidth * PreviewAspectHeight / PreviewAspectWidth;

    private const string PreviewPrefix = "preview-";

    /// <summary>
    /// Where a capture that was not saved is kept for the editor, which opens
    /// screenshots from files.
    /// </summary>
    private static string UnsavedCapturePath => Path.Combine(AppPaths.DataFolder, "last-capture.png");

    /// <summary>Copies an image to the clipboard, saves it as PNG, or both.</summary>
    public static Output Deliver(HWND clipboardOwner, ComposedImage image, AfterCapture action, string folder)
    {
        byte[] png = PngEncoder.Encode(image.Image, withAlpha: image.HasGaps);

        bool copied = false;
        if (action is AfterCapture.Copy or AfterCapture.CopyAndSave)
        {
            copied = ClipboardImage.Set(clipboardOwner, image.Image, png);
            if (!copied)
            {
                Log.Write("The clipboard could not be opened.");
            }
        }

        string? savedPath = null;
        string? problem = null;
        if (action is AfterCapture.Save or AfterCapture.CopyAndSave)
        {
            try
            {
                Directory.CreateDirectory(folder);
                string path = ScreenshotFile.Resolve(folder, DateTime.Now, File.Exists);
                File.WriteAllBytes(path, png);
                savedPath = path;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Log.Write($"Saving failed: {e.Message}");
                problem = e.Message;
            }
        }

        string summary = (copied, savedPath is not null) switch
        {
            (true, true) => "Screenshot copied and saved",
            (true, false) => "Screenshot copied",
            (false, true) => "Screenshot saved",
            (false, false) => "Screenshot taken",
        };
        return new Output(summary, savedPath, problem, png);
    }

    /// <summary>Keeps a capture that was not saved, so that it can still be opened for drawing.</summary>
    /// <returns>The path of the file. Null when it could not be written.</returns>
    public static string? KeepUnsaved(byte[] png)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.DataFolder);
            File.WriteAllBytes(UnsavedCapturePath, png);
            return UnsavedCapturePath;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Write($"The capture could not be kept for drawing: {e.Message}");
            return null;
        }
    }

    /// <summary>
    /// Writes the picture for the notification, which only takes images from files:
    /// the middle of the screenshot in the notification's own proportions, so that
    /// it fills the picture area completely. Each preview gets a new name, because
    /// Windows keeps showing the old picture when a file it has shown before
    /// changes. Earlier previews are removed.
    /// </summary>
    /// <returns>The path of the preview. Null when it could not be written.</returns>
    public static string? WritePreview(Bitmap32 image)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.DataFolder);
            foreach (string old in Directory.EnumerateFiles(AppPaths.DataFolder, PreviewPrefix + "*.png"))
            {
                File.Delete(old);
            }

            Bitmap32 preview = image
                .CropToAspect(PreviewAspectWidth, PreviewAspectHeight)
                .ShrinkToFit(PreviewWidth, PreviewHeight);
            string path = Path.Combine(AppPaths.DataFolder, $"{PreviewPrefix}{DateTime.Now:HHmmssfff}.png");
            File.WriteAllBytes(path, PngEncoder.Encode(preview, withAlpha: false));
            return path;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Write($"The preview could not be written: {e.Message}");
            return null;
        }
    }
}
