using System.Globalization;

namespace WinCapture.Core;

/// <summary>One monitor in a LittleBigMouse layout.</summary>
/// <param name="Pixels">The monitor on the Windows virtual desktop.</param>
/// <param name="Physical">The monitor's panel on the desk, in millimetres.</param>
public sealed record LittleBigMouseZone(PixelRect Pixels, PhysicalRect Physical);

/// <summary>
/// Reads the layout LittleBigMouse is running with. LittleBigMouse knows how large
/// the monitors are and where they stand on the desk, and moves the mouse between
/// them accordingly; a canvas built from the same layout makes a selection across
/// monitors follow the mouse.
/// </summary>
public static class LittleBigMouseLayout
{
    /// <summary>Where LittleBigMouse 5 keeps the layout its mouse hook is running with.</summary>
    public static string DefaultPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Mgth", "LittleBigMouse", "Current.xml");

    /// <summary>
    /// Reads the monitors out of LittleBigMouse's Current.xml. The file is a sequence
    /// of commands for the mouse hook; the one that loads the layout lists each
    /// monitor as a zone with its bounds in pixels and in millimetres.
    /// </summary>
    /// <returns>The zones found. Empty when the text is not such a file.</returns>
    public static IReadOnlyList<LittleBigMouseZone> Parse(string xml)
    {
        // Read tag by tag instead of with an XML parser: the few elements needed are
        // easy to pick out, and the parser would double the size of the program.
        var zones = new List<LittleBigMouseZone>();
        try
        {
            bool inMainZones = false;
            string? boundsKind = null;
            PixelRect? pixels = null;
            PhysicalRect? physical = null;

            int at = 0;
            while ((at = xml.IndexOf('<', at)) >= 0)
            {
                int end = xml.IndexOf('>', at);
                if (end < 0)
                {
                    break;
                }

                ReadOnlySpan<char> tag = xml.AsSpan(at + 1, end - at - 1);
                at = end + 1;

                bool closing = tag.StartsWith("/");
                ReadOnlySpan<char> name = closing ? tag[1..] : tag;
                int nameEnd = name.IndexOfAny(' ', '/', '\t');
                name = nameEnd < 0 ? name : name[..nameEnd];

                if (closing)
                {
                    if (name is "Zone" && inMainZones && pixels is { } p && physical is { } m)
                    {
                        zones.Add(new LittleBigMouseZone(p, m));
                        pixels = null;
                        physical = null;
                    }
                    else if (name is "MainZones")
                    {
                        inMainZones = false;
                    }

                    continue;
                }

                switch (name)
                {
                    // Besides the main zones a layout can hold copies of them for
                    // wrapping around the desktop's edges. Those are not monitors.
                    case "MainZones":
                        inMainZones = true;
                        break;
                    case "Zone" when inMainZones:
                        pixels = null;
                        physical = null;
                        break;
                    case "PixelsBounds" or "PhysicalBounds" when inMainZones:
                        boundsKind = name.ToString();
                        break;
                    case "Rect" when boundsKind is not null:
                        double left = Number(tag, "Left");
                        double top = Number(tag, "Top");
                        double width = Number(tag, "Width");
                        double height = Number(tag, "Height");
                        if (boundsKind == "PixelsBounds")
                        {
                            pixels = PixelRect.FromSize(
                                (int)Math.Round(left), (int)Math.Round(top), (int)Math.Round(width), (int)Math.Round(height));
                        }
                        else
                        {
                            physical = new PhysicalRect(left, top, width, height);
                        }

                        boundsKind = null;
                        break;
                }
            }
        }
        catch (Exception e) when (e is FormatException or OverflowException)
        {
            zones.Clear();
        }

        return zones;
    }
    /// <summary>
    /// Builds the canvas for the monitors Windows reports from a LittleBigMouse layout.
    /// </summary>
    /// <returns>
    /// Null when the layout does not describe exactly these monitors, for example
    /// because it was made for another arrangement.
    /// </returns>
    public static Canvas? BuildCanvas(ScreenLayout layout, IReadOnlyList<LittleBigMouseZone> zones)
    {
        var monitors = new List<(Monitor, PhysicalRect)>(layout.Monitors.Count);
        foreach (Monitor monitor in layout.Monitors)
        {
            LittleBigMouseZone? zone = zones.FirstOrDefault(candidate => candidate.Pixels == monitor.Bounds);
            if (zone is null || !(zone.Physical.Width > 0) || !(zone.Physical.Height > 0))
            {
                return null;
            }

            monitors.Add((monitor, zone.Physical));
        }

        return Canvas.FromPhysical(monitors);
    }

    /// <summary>The value of a numeric attribute of a tag.</summary>
    private static double Number(ReadOnlySpan<char> tag, string attribute)
    {
        // The leading space keeps "Left" from matching the end of a longer name.
        string opening = $" {attribute}=\"";
        int start = tag.IndexOf(opening);
        if (start < 0)
        {
            throw new FormatException();
        }

        ReadOnlySpan<char> value = tag[(start + opening.Length)..];
        int end = value.IndexOf('"');
        if (end < 0)
        {
            throw new FormatException();
        }

        return double.Parse(value[..end], NumberStyles.Float, CultureInfo.InvariantCulture);
    }
}