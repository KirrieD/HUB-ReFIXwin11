using System;
using System.Windows;

namespace QuietMixer
{
    internal static class Policy
    {
        internal static float Volume(double value)
        {
            return double.IsNaN(value) ? 0 : (float)(Math.Max(0, Math.Min(100, value)) / 100);
        }
        internal static bool IsVolumeIcon(string id, string name, double width, double height)
        {
            // Never intercept the whole network / volume / battery group.
            if (width < 8 || height < 8 || width > 64 || height > 64) return false;
            id = (id ?? "").ToLowerInvariant(); name = (name ?? "").ToLowerInvariant();
            return id == "systemtray.volume" || id == "volume" || id == "volumeicon" ||
                name.StartsWith("speakers") || name.StartsWith("headphones") ||
                name.StartsWith("volume") || name.StartsWith("динамики") ||
                name.StartsWith("наушники") || name.StartsWith("громкость") ||
                name.StartsWith("том динамики") || name.StartsWith("том наушники");
        }
        internal static bool Hit(Rect rect, double x, double y)
        {
            return !rect.IsEmpty && rect.Width > 0 && rect.Height > 0 &&
                x >= rect.Left && x < rect.Right && y >= rect.Top && y < rect.Bottom;
        }
        internal static Point Place(Rect work, double width, double height, Point anchor)
        {
            return new Point(Math.Max(work.Left + 12, Math.Min(anchor.X - width / 2, work.Right - width - 12)),
                anchor.Y < work.Top + work.Height / 2 ? work.Top + 12 : Math.Max(work.Top + 12, work.Bottom - height - 12));
        }
    }
}
