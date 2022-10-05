using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using AssetStudio;
using ICSharpCode.SharpZipLib.Zip;
using ImageFormat = AssetStudio.ImageFormat;
using Object = AssetStudio.Object;
//using System.IO.Compression;

namespace ZipmodThumbnailHandler.Tools;

internal class GenerateThumbnailImage : IDisposable
{
    public const int BUFFER_SIZE = 134217728;

    private int _charaItemCount = 0;

    // Default: read count = 1;
    private static readonly int _readItem = 1;

    private AssetsManager _assetsManager;

    private readonly ZipmodThumbnailHandler _thumbHandler;

    private readonly List<string> _studioThumbList = new();

    private readonly Dictionary<int, List<KeyValuePair<string, string>>> _charaItems = new();

    public int CharaItemCount => _charaItemCount;

#if DEBUG

    public HashSet<string> Unity3dList { get; private set; }
    public HashSet<string> PngFolder { get; private set; }
    public HashSet<string> PngList { get; private set; }
    public string StreamFileName => _thumbHandler.SelectedItemStream.Name;

#else
    internal string StreamFileName => _thumbHandler.FullName;
#endif

#if OTHER_BRANCH
    private static readonly MethodInfo locateZipEntryMethodInfo = typeof(ZipFile).GetMethod("LocateEntry", ToolUnit.all);
#endif

    private CompressionMethod _thumbCompMethod = CompressionMethod.Stored;
    private string _thumbAbSize;

    public GenerateThumbnailImage(ZipmodThumbnailHandler handler)
    {
        _thumbHandler = handler;
    }

    // 4 ComaItem => 1 preview thumb (WIP)
    public Bitmap ComaItem(Stream stream, uint width)
    {
        if (StreamFileName.ContainsCase("QuickAccessBox")) return null;
        using var archive = new ZipFile(stream);
        LoadCsvList(archive);
        LoadStuThumb(archive);

        // Get studio item thumb
        if (_charaItems.Count < 1)
        {
            if (_studioThumbList.Count < 1) return null;
            return GetStudioImage(archive, width);
        }

        // Get chara item thumb.
        var items = GetFirstItem();
        if (items.Count == 0) return null;
        return ImageResolver(archive, items[0].Key, items[0].Value, width);
    }

    private Bitmap GetStudioImage(ZipFile archive, uint width)
    {
        Bitmap image = null;
        byte[] buffer;
        var entry = archive.GetEntry(_studioThumbList[0]);

        if (entry.Size > BUFFER_SIZE) return null;
        using var inputST = archive.GetInputStream(entry);

        buffer = new byte[(int)entry.Size];
        if (inputST.Read(buffer, 0, buffer.Length) != buffer.Length) return null;

        SaveThumb(buffer, Path.GetFileNameWithoutExtension(entry.Name));

        using var ms = new MemoryStream(buffer);
        using var org = new Bitmap(ms);
        if (org != null) { image = RendImageAndResize(org, width); }

#if DEBUG
        _thumbHandler.TrunLog($"Image Size: {image.Size}");
#endif
        return image;
    }

    public Bitmap ImageResolver(ZipFile archive, string tmbAB, string tmbName, uint width)
    {
        var entry = archive.GetEntry($"abdata/{tmbAB}");
        if (entry != null)
        {
            return GetBundleImage(archive, entry, tmbName, width);
        }

        // Folder Png
        var index = tmbAB.LastIndexOf('.');
        entry = archive.GetEntry(
            index > 0
            ? $"abdata/{tmbAB.Substring(0, index)}/{tmbName}.png"
            : $"abdata/{tmbAB}/{tmbName}.png");

        if (entry != null)
        {
            return GetFolderImage(archive, entry, tmbName, width);
        }

        return null;
    }

    public Bitmap GetFolderImage(ZipFile archive, ZipEntry entry, string tmbName, uint width)
    {
        if (entry.Size > BUFFER_SIZE) return null;

        byte[] buffer;
        Bitmap image = null;
        using var inputST = archive.GetInputStream(entry);

        _thumbCompMethod = entry.CompressionMethod;
        buffer = new byte[(int)entry.Size];
        if (inputST.Read(buffer, 0, buffer.Length) != buffer.Length) return null;
        
        SaveThumb(buffer, tmbName);

        using var ms = new MemoryStream(buffer);
        using var org = new Bitmap(ms);
        if (org != null) { image = RendImageAndResize(org, width); }

#if DEBUG
        _thumbHandler.TrunLog($"Image Size: {image.Size}");
#endif
        return image;
    }

    public Bitmap GetBundleImage(ZipFile archive, ZipEntry entry, string tmbName, uint width)
    {
        int entrySize = (int)entry.Size;
        if (entrySize > BUFFER_SIZE) return null;

        _assetsManager = new AssetsManager();
        var dummyPath = Path.Combine(StreamFileName, entry.Name);

        _thumbAbSize = ToolUnit.FormatFileSize(entry.Size);
        _thumbCompMethod = entry.CompressionMethod;

        if (entry.CompressionMethod == CompressionMethod.Stored)
        {
            using var st = archive.GetInputStream(entry);
            _assetsManager.LoadFile(dummyPath, st);
            return Texture2DToBitmap(tmbName, width);
        }

        var buffer = new byte[entrySize];
        using var inputST = archive.GetInputStream(entry);
        if (inputST.Read(buffer, 0, entrySize) != entrySize) return null;

        using var ms = new MemoryStream(buffer);
        _assetsManager.LoadFile(dummyPath, ms);
        return Texture2DToBitmap(tmbName, width);
    }

    public Bitmap Texture2DToBitmap(string tmbName, uint width)
    {
        var tex2D = FindFirtTexture2D(tmbName);
        if (tex2D == null) return null;

        using var bmp = tex2D.ConvertToStream(ImageFormat.Bmp, true);
        using var image = (Bitmap)Image.FromStream(bmp);

        SaveThumb(image, tmbName);
        return RendImageAndResize(image, width);
    }

    //public Bitmap RendImageAndResize(Image image, uint width)
    public Bitmap RendImageAndResize(Bitmap image, uint width)
    {
        float scale = Math.Min(
            (float)width / image.Width, (float)width / image.Height);

        var scaleWidth = (int)(image.Width * scale);
        var scaleHeight = (int)(image.Height * scale);
        if (scaleWidth <= 0 || scaleHeight <= 0 || image == null) return null;
        var destImage = new Bitmap(scaleWidth, scaleHeight, PixelFormat.Format24bppRgb);

        destImage.SetResolution(image.HorizontalResolution, image.VerticalResolution);
        using (var graphics = System.Drawing.Graphics.FromImage(destImage))
        {
            graphics.CompositingMode = CompositingMode.SourceCopy;
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.CompositingQuality = CompositingQuality.HighQuality;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.SmoothingMode = SmoothingMode.HighQuality;
            graphics.Clear(System.Drawing.Color.White);
            graphics.DrawImage(image, 0, 0, scaleWidth, scaleHeight);
        }

        return destImage;
    }

    private void LoadStuThumb(ZipFile zipFile)
    {
        var flag = false;
        foreach (ZipEntry entry in zipFile)
        {
            // folder name eg: studio_thumbnails
            // some case:
            // abdata/studio/thumb
            // abdata/studio/stuthumb
            // abdata/studio/thumbnail
            // abdata/studio/studio_thumbnails
            if (entry.Name.ContainsCase("Studio"))
            {
                flag = true;
                break;
            }
        }

        if (!flag) return;

        foreach (ZipEntry entry in zipFile)
        {
            if (entry.Name.StartsWithCase("abdata")
                && entry.Name.EndsWithCase(".png"))
            {
                var index = entry.Name.LastIndexOf('/') + 1;
                if (index < 1) continue;

                // REG: ^\d{1,8}-\d{1,8}
                var sp = entry.Name.Substring(index).Split('-');
                if (sp.Length >= 2 && sp[0].Length == 8 && sp[1].Length == 8)
                {
                    _studioThumbList.Add(entry.Name);
                    if (_studioThumbList.Count == _readItem) return;
                }
            }
        }
    }

    private void LoadCsvList(ZipFile zipFile)
    {
        foreach (ZipEntry entry in zipFile)
        {
            if (entry.Name.StartsWithCase("abdata/list/characustom")
                && entry.Name.EndsWithCase(".csv"))
            {
                try
                {
                    using var stream = zipFile.GetInputStream(entry);
                    GetCsvList(stream);
                }
                catch { }
            }
        }
    }

    private void GetCsvList(Stream st)
    {
        using StreamReader sr = new(st, Encoding.UTF8);
        // id:1 id:2 ...
        var categoryNo = sr.ReadLine().Split(',')[0].Trim();
        if (!int.TryParse(categoryNo, out var catNo)) return;

        sr.ReadLine();//.Split(',')[0].Trim(); // var distributionNo
        sr.ReadLine();//.Split(',')[0].Trim(); // var filePath
        var lstLine = sr.ReadLine();
        if (lstLine == null) return;
        
        string[] lstKey = lstLine.Trim().Split(',');
        if (lstKey.Length < 4) return;

        int tmbKey = lstKey.Check("ThumbAB");
        int tmbResKey = lstKey.Check("ThumbTex");
        if (tmbKey < 0 || 0 > tmbResKey) return;
        
        // Default: read 4 items;
        int itemCount = 0;

        while (!sr.EndOfStream)
        {
            if (itemCount >= _readItem) break;

            string line = sr.ReadLine();
            if (line == null) return;
            if (!line.Contains(',')) return;

            string[] lsp = line.Split(',');
            if (lsp.Length < 4) return;

            string abPath = lsp[tmbKey];
            string tmbRes = lsp[tmbResKey];

            var itemInfo = new KeyValuePair<string, string>(abPath, tmbRes);

            if (_charaItems.ContainsKey(catNo))
            {
                _charaItems[catNo].Add(itemInfo);
            }
            else _charaItems[catNo] = new List<KeyValuePair<string, string>> { itemInfo };

            ++itemCount;
        }
        _charaItemCount += itemCount;
    }

    public void SaveThumb(Image image, string tmbName)
    {
        var tmbPath = GetSaveName(tmbName);
        if (string.IsNullOrWhiteSpace(tmbPath) || File.Exists(tmbPath)) return;
        try { image.Save(tmbPath, System.Drawing.Imaging.ImageFormat.Png); }
        catch { return; }
    }

    private void SaveThumb(byte[] bytes, string tmbName)
    {
        var tmbPath = GetSaveName(tmbName);
        if (string.IsNullOrWhiteSpace(tmbPath) || File.Exists(tmbPath)) return;
        try
        {
            using var fs = new FileStream(tmbPath, FileMode.Create);
            using var bw = new BinaryWriter(fs);
            bw.Write(bytes);
        }
        catch { return; }
    }

    private string GetSaveName(string tmbName)
    {
        // Eg:
        // ShellService\Thumbs\
        // ShellService\Lib\Zipmod\ZipmodThumbnailHandler.dll
        string dir = $"{ToolUnit.AssemblyDirectory}\\..\\..\\Thumbs";
        if (!Directory.Exists(dir)) return null;

        var abSize = " " + _thumbAbSize ?? string.Empty;
        var arcName = Path.GetFileNameWithoutExtension(StreamFileName);
        return Path.Combine(dir, $"{arcName} [{tmbName}] {_thumbCompMethod}{abSize}.png");
    }

    public Texture2D FindFirtTexture2D(string texName)
    {
        foreach (Object obj in _assetsManager.assetsFileList.SelectMany(o => o.Objects))
        {
            if (obj.type != ClassIDType.Texture2D) continue;
            var tex = obj as Texture2D;
            if (tex.m_Name.Equals(texName)) return tex;
        }
        return null;
    }

    public List<KeyValuePair<string, string>> GetFirstItem() => _charaItems.ElementAt(0).Value;

#if DEBUG
    public List<KeyValuePair<string, string>> GetCharaItem(int categroyId)
    {
        _charaItems.TryGetValue(categroyId, out var item);
        return item;
    }

    public IEnumerable<ZipEntry> FindEntries(ZipFile archive, IEnumerable<string> paths)
    {
        foreach (var path in paths) yield return archive.GetEntry(path);
    }


    public void GetPngPath(ZipFile archive)
    {
        PngList ??= new HashSet<string>();
        PngFolder ??= new HashSet<string>();

        foreach (ZipEntry entry in archive)
        {
            if (entry.Name.StartsWithCase("abdata/") &&
                entry.Name.EndsWithCase(".png"))
            {
                var fakePath = entry.Name;
                fakePath = fakePath.Remove(0, fakePath.IndexOf('/') + 1);
                if (!PngList.Contains(entry.Name))
                {
                    PngList.Add(entry.Name);
                }
                fakePath = fakePath.Remove(fakePath.LastIndexOf('/'));
                if (!PngFolder.Contains(fakePath))
                {
                    PngFolder.Add(fakePath);
                }
            }
        }
    }

    public void GetAllUnityBundle(ZipFile archive)
    {
        var paths = Unity3dList ??= new HashSet<string>();
        foreach (ZipEntry entry in archive)
        {
            if (!entry.Name.EndsWithCase(".unity3d")) continue;
            paths.Add(entry.Name);
        }
    }
#endif

    public void Dispose()
    {
        _charaItems.Clear();
        _studioThumbList.Clear();
        _assetsManager?.Clear();
#if DEBUG
        Unity3dList?.Clear();
        PngFolder?.Clear();
        PngList?.Clear();
#endif
    }
}
