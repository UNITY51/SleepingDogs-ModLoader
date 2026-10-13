using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace SDModLoader.App;

/// <summary>Достаёт иконку из exe (используем иконку установленной игры, не распространяя её).</summary>
public static class IconHelper
{
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern uint ExtractIconEx(string file, int index, IntPtr[]? large, IntPtr[]? small, uint count);

    [DllImport("user32.dll")]
    static extern bool DestroyIcon(IntPtr handle);

    public static ImageSource? FromExe(string path)
    {
        try
        {
            var large = new IntPtr[1];
            if (ExtractIconEx(path, 0, large, null, 1) < 1 || large[0] == IntPtr.Zero) return null;
            try
            {
                var src = Imaging.CreateBitmapSourceFromHIcon(large[0], Int32Rect.Empty, System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());
                src.Freeze();
                return src;
            }
            finally { DestroyIcon(large[0]); }
        }
        catch { return null; }
    }
}
