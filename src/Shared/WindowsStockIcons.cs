using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace ReFix.Shared
{
    public sealed class WindowsStockIcon
    {
        public int Id { get; private set; }
        public string Name { get; private set; }
        internal WindowsStockIcon(int id, string name) { Id = id; Name = name; }
    }

    public static class WindowsStockIcons
    {
        public static readonly IList<WindowsStockIcon> Options = Array.AsReadOnly(new[]
        {
            new WindowsStockIcon(0, "Документ"), new WindowsStockIcon(1, "Файл приложения"),
            new WindowsStockIcon(2, "Приложение"), new WindowsStockIcon(3, "Папка"),
            new WindowsStockIcon(4, "Открытая папка"), new WindowsStockIcon(7, "Съёмный диск"),
            new WindowsStockIcon(8, "Жёсткий диск"), new WindowsStockIcon(9, "Сетевой диск"),
            new WindowsStockIcon(11, "Дисковод"), new WindowsStockIcon(12, "Диск в памяти"),
            new WindowsStockIcon(13, "Глобальная сеть"), new WindowsStockIcon(15, "Сервер"),
            new WindowsStockIcon(16, "Принтер"), new WindowsStockIcon(17, "Сеть"),
            new WindowsStockIcon(22, "Поиск"), new WindowsStockIcon(23, "Справка"),
            new WindowsStockIcon(29, "Ярлык"), new WindowsStockIcon(31, "Корзина"),
            new WindowsStockIcon(32, "Полная корзина"), new WindowsStockIcon(40, "Аудиодиск"),
            new WindowsStockIcon(47, "Замок"), new WindowsStockIcon(50, "Сетевой принтер"),
            new WindowsStockIcon(57, "Папка с файлами"), new WindowsStockIcon(71, "Музыка"),
            new WindowsStockIcon(72, "Изображения"), new WindowsStockIcon(73, "Видео"),
            new WindowsStockIcon(74, "Медиафайлы"), new WindowsStockIcon(78, "Предупреждение"),
            new WindowsStockIcon(79, "Информация"), new WindowsStockIcon(81, "Ключ"),
            new WindowsStockIcon(82, "Программы"), new WindowsStockIcon(94, "Компьютер"),
            new WindowsStockIcon(95, "Ноутбук"), new WindowsStockIcon(96, "Пользователи"),
            new WindowsStockIcon(99, "Телефон"), new WindowsStockIcon(100, "Фотоаппарат"),
            new WindowsStockIcon(101, "Видеокамера"), new WindowsStockIcon(102, "Аудиоплеер"),
            new WindowsStockIcon(103, "Подключение"), new WindowsStockIcon(104, "Интернет"),
            new WindowsStockIcon(105, "ZIP-архив"), new WindowsStockIcon(106, "Параметры")
        });
        static readonly HashSet<int> ids = new HashSet<int>(System.Linq.Enumerable.Select(Options, item => item.Id));

        public static bool IsKnown(int id) { return ids.Contains(id); }

        public static Bitmap CreateBitmap(int id, int size)
        {
            if (!IsKnown(id)) return null;
            size = Math.Max(16, Math.Min(256, size));
            var request = new StockIconInfo { Size = (uint)Marshal.SizeOf(typeof(StockIconInfo)) };
            const uint icon = 0x100, small = 0x1;
            try
            {
                if (SHGetStockIconInfo(id, icon | (size <= 16 ? small : 0), ref request) != 0 || request.Handle == IntPtr.Zero)
                    return null;
                using (var borrowed = Icon.FromHandle(request.Handle))
                using (var original = borrowed.ToBitmap())
                {
                    var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
                    try
                    {
                        using (var graphics = Graphics.FromImage(bitmap))
                        {
                            graphics.CompositingMode = CompositingMode.SourceCopy;
                            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                            graphics.DrawImage(original, new Rectangle(0, 0, size, size));
                        }
                        return bitmap;
                    }
                    catch { bitmap.Dispose(); throw; }
                }
            }
            finally { if (request.Handle != IntPtr.Zero) DestroyIcon(request.Handle); }
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct StockIconInfo
        {
            internal uint Size;
            internal IntPtr Handle;
            internal int ImageIndex, ResourceIndex;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] internal string ResourcePath;
        }

        [DllImport("shell32.dll")] static extern int SHGetStockIconInfo(int id, uint flags, ref StockIconInfo info);
        [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr icon);
    }
}
