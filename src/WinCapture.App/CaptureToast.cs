using System.Security;
using Microsoft.Win32;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;
using WinCapture.Core;

namespace WinCapture.App;

/// <summary>
/// The Windows notification shown after a capture, with a preview of the
/// screenshot. Clicking it opens the screenshot in the editor.
/// </summary>
internal static class CaptureToast
{
    // The name Windows files the notifications under. A program that is not
    // installed as a package has to register it itself.
    private const string AppId = "WinCapture";

    // New notifications replace the previous one instead of piling up.
    private const string Tag = "capture";

    /// <summary>
    /// Tells Windows two things: the name and icon to show notifications with, and
    /// which program opens the link a notification carries. Without the first,
    /// Windows drops notifications from a program that has no package and no Start
    /// menu shortcut.
    /// </summary>
    /// <remarks>
    /// The click goes through a link and not back to this process because of the
    /// keyboard: a program that Windows starts for a link gets the foreground, while
    /// one started from here would open behind the notification and stay without
    /// the keyboard until clicked.
    /// </remarks>
    public static void Register()
    {
        using (RegistryKey app = Registry.CurrentUser.CreateSubKey($@"Software\Classes\AppUserModelId\{AppId}"))
        {
            app.SetValue("DisplayName", "WinCapture");

            string icon = Path.Combine(AppContext.BaseDirectory, "AppIcon.png");
            if (File.Exists(icon))
            {
                app.SetValue("IconUri", icon);
            }
        }

        string scheme = AppPaths.EditLastCaptureLink.Split(':')[0];
        using (RegistryKey link = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{scheme}"))
        {
            link.SetValue("", "URL:WinCapture");
            link.SetValue("URL Protocol", "");
            using RegistryKey command = link.CreateSubKey(@"shell\open\command");
            command.SetValue("", $"\"{Editor.ProgramPath}\" \"%1\"");
        }
    }

    /// <param name="previewPath">An image file to show in the notification. Null for none.</param>
    public static void Show(string title, string text, string? previewPath)
    {
        string image = previewPath is null
            ? ""
            : $"<image src=\"{SecurityElement.Escape(new Uri(previewPath).AbsoluteUri)}\"/>";

        var xml = new XmlDocument();
        xml.LoadXml(
            $"<toast activationType=\"protocol\" launch=\"{AppPaths.EditLastCaptureLink}\">" +
            "<visual><binding template=\"ToastGeneric\">" +
            $"<text>{SecurityElement.Escape(title)}</text><text>{SecurityElement.Escape(text)}</text>{image}" +
            "</binding></visual><audio silent=\"true\"/></toast>");

        ToastNotificationManager.CreateToastNotifier(AppId).Show(new ToastNotification(xml) { Tag = Tag });
    }
}
