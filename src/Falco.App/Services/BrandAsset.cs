using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Falco.App.Services;

/// <summary>品牌图标加载：从 exe 同目录 falco.ico 取最大帧（标题栏徽章与「立即加速」按钮共用）。</summary>
public static class BrandAsset
{
    public static ImageSource LoadFromIco()
    {
        try
        {
            var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "falco.ico");
            if (!File.Exists(path)) return null;
            using var fs = File.OpenRead(path);
            var dec = BitmapDecoder.Create(fs, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            var best = dec.Frames.OrderByDescending(f => f.PixelWidth).First();
            best.Freeze();
            return best;
        }
        catch { return null; }
    }
}
