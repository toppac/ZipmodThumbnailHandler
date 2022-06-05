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
    // read count
    private static readonly int _readItem = 1;

    private readonly AssetsManager _assetsManager;
    public ZipmodThumbnailHandler ThumbHandler { get; internal set; }

    public HashSet<string> Unity3dList { get; private set; }
    public HashSet<string> PngFolder { get; private set; }
    public HashSet<string> PngList { get; private set; }
    private readonly List<string> _studioThumbList;
    public Dictionary<int, List<KeyValuePair<string, string>>> CharaItems { get; private set; }

    public int CharaItemCount => _charaItemCount;
    public static string AssemblyDirectory => ToolUnit.AssemblyDirectory;
    public string StreamFileName => ThumbHandler.SelectedItemStream.Name;

    //private static readonly MethodInfo locateZipEntryMethodInfo = typeof(ZipFile).GetMethod("LocateEntry", ToolUnit.all);
    private CompressionMethod _thumbCompMethod = CompressionMethod.Stored;
    private string _thumbAbSize;

    public GenerateThumbnailImage()
    {
        _studioThumbList = new List<string>();
        CharaItems = new Dictionary<int, List<KeyValuePair<string, string>>>();
        _assetsManager = new AssetsManager();
    }

    // 4 ComaItem => 1 preview thumb (WIP)
    public Bitmap ComaItem(Stream stream, uint width)
    {
        if (StreamFileName.ContainsCase("QuickAccessBox")) return null;
        using (var archive = new ZipFile(stream))
        {
            LoadCsvList(archive);
            LoadStuThumb(archive);

            // Get studio item thumb
            if (!CharaItems.Any())
            {
                if (!_studioThumbList.Any()) return null;
                var entry = archive.GetEntry(_studioThumbList[0]);
                using (var inputST = archive.GetInputStream(entry))
                {
                    using (var image = Image.FromStream(inputST))
                    {
                        return RendImageAndResize(image, width);
                    }
                }
            }
            // Get chara item thumb.
            var items = GetFirstItem();
            if (items.Count == 0) return null;
            return ImageResolver(archive, items[0].Key, items[0].Value, width);
        }

    }

    public Bitmap ImageResolver(ZipFile archive, string tmbAB, string tmbName, uint width)
    {
        // Folder Png
        var index = tmbAB.LastIndexOf('.');

        var entry = archive.GetEntry($"abdata/{tmbAB}");
        if (entry != null)
            return GetBundleImage(archive, entry, tmbName, width);

        entry = archive.GetEntry(
            index > 0
            ? $"abdata/{tmbAB.Remove(index)}/{tmbName}.png"
            : $"abdata/{tmbAB}/{tmbName}.png");
        if (entry != null)
            return GetFolderImage(archive, entry, tmbName, width);

        return null;
    }

    public Bitmap GetFolderImage(ZipFile archive, ZipEntry entry, string tmbName, uint width)
    {
        if (entry.Size > BUFFER_SIZE) return null;
        using (var inputST = archive.GetInputStream(entry))
        {
            var buffer = new byte[(int)entry.Size];
            var count = inputST.Read(buffer, 0, buffer.Length);
            if (count != buffer.Length) return null;
            _thumbCompMethod = entry.CompressionMethod;
            using (var ms = new MemoryStream(buffer))
            {
                using (var image = Image.FromStream(ms))
                {
#if DEBUG
                    ThumbHandler.TrunLog($"Image Size: {image.Size}");
#endif
                    SaveThumb(image, tmbName);
                    return RendImageAndResize(image, width);
                }
            }
        }
    }

    public Bitmap GetBundleImage(ZipFile archive, ZipEntry entry, string tmbName, uint width)
    {
        var dummyPath = Path.Combine(StreamFileName, entry.Name);
        if (entry.Size > BUFFER_SIZE) return null;
        int entrySize = (int)entry.Size;

        _thumbCompMethod = entry.CompressionMethod;
        _thumbAbSize = ToolUnit.FormatFileSize(entry.Size);
        if (entry.CompressionMethod == CompressionMethod.Stored)
        {
            using (var st = archive.GetInputStream(entry))
            {
                _assetsManager.LoadFile(dummyPath, st);
                return Texture2DToBitmap(tmbName, width);
            }
        }

        var buffer = new byte[entrySize];
        using (var inputST = archive.GetInputStream(entry))
        {
            var count = inputST.Read(buffer, 0, entrySize);
            if (count != entrySize) return null;
            using (var ms = new MemoryStream(buffer))
            {
                _assetsManager.LoadFile(dummyPath, ms);
                return Texture2DToBitmap(tmbName, width);
            }
        }
    }

    public Bitmap Texture2DToBitmap(string tmbName, uint width)
    {
        var tex2D = FindFirtTexture2D(tmbName);
        if (tex2D == null) return null;

        using var bmp = tex2D.ConvertToStream(ImageFormat.Bmp, true);
        using Image image = Image.FromStream(bmp);
        SaveThumb(image, tmbName);
        return RendImageAndResize(image, width);
    }

    public Bitmap RendImageAndResize(Image image, uint width)
    {
        float scale = Math.Min(
            (float)width / image.Width, (float)width / image.Height);

        var scaleWidth = (int)(image.Width * scale);
        var scaleHeight = (int)(image.Height * scale);
        if (scaleWidth <= 0 || scaleHeight <= 0 || image == null) return null;
        var destImage = new Bitmap(scaleWidth, scaleHeight, PixelFormat.Format32bppArgb);

        destImage.SetResolution(image.HorizontalResolution, image.VerticalResolution);
        using (var graphics = System.Drawing.Graphics.FromImage(destImage))
        {
            graphics.CompositingMode = CompositingMode.SourceCopy;
            graphics.CompositingQuality = CompositingQuality.HighQuality;
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.SmoothingMode = SmoothingMode.HighQuality;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
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
                if (index < 0) continue;

                // REG: ^\d{1,8}-\d{1,8}
                var sp = entry.Name.Remove(0, index).Split('-');
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
                using var stream = zipFile.GetInputStream(entry);
                GetCsvList(stream);
            }
        }
    }

    private bool GetCsvList(Stream st)
    {
        using (StreamReader sr = new(st, Encoding.UTF8))
        {
            // id:1 id:2 ...
            var categoryNo = sr.ReadLine().Split(',')[0].Trim();
            sr.ReadLine().Split(',')[0].Trim(); // var distributionNo
            sr.ReadLine().Split(',')[0].Trim(); // var filePath
            string[] lstKey = sr.ReadLine().Trim().Split(',').ToArray();

            int itemCount = 0;
            // int readItem = 4;
            while (!sr.EndOfStream)
            {
                string line = sr.ReadLine();
                if (!line.Contains(',')) return false;
                itemCount++;
                string[] lsp = line.Split(',');
                int tmbKey = lstKey.Check("ThumbAB");
                if (tmbKey >= 0 && itemCount <= _readItem)
                {
                    int tmbResKey = lstKey.Check("ThumbTex");
                    string abPath = lsp[tmbKey];
                    string tmbRes = lsp[tmbResKey];
                    KeyValuePair<string, string> itemInfo = new KeyValuePair<string, string>(abPath, tmbRes);
                    int catNo = int.Parse(categoryNo);
                    if (CharaItems.ContainsKey(catNo))
                    {
                        CharaItems[catNo].Add(itemInfo);
                        continue;
                    }
                    CharaItems[catNo] = new List<KeyValuePair<string, string>> { itemInfo };
                }
            }
            _charaItemCount += itemCount;
        }
        return true;
    }

    public void SaveThumb(Image image, string tmbName)
    {
        // Eg:
        // ShellService\Thumbs\
        // ShellService\Lib\Zipmod\ZipmodThumbnailHandler.dll
        string dir = $"{AssemblyDirectory}\\..\\..\\Thumbs";
        if (!Directory.Exists(dir)) return;

        var abSize = _thumbAbSize != null ? $" {_thumbAbSize}" : string.Empty;
        var arcName = Path.GetFileNameWithoutExtension(StreamFileName);
        var tmbPath = Path.Combine(
            dir, $"{arcName} [{tmbName}] {_thumbCompMethod}{abSize}.png");

        if (!File.Exists(tmbPath))
        {
            try { image.Save(tmbPath, System.Drawing.Imaging.ImageFormat.Png); }
            catch { return; }
        }
    }

    public Texture2D FindFirtTexture2D(string texName)
    {
        var objectList = _assetsManager.assetsFileList.SelectMany(o => o.Objects);
        foreach (Object obj in objectList)
        {
            if (obj.type != ClassIDType.Texture2D) continue;
            var tex = obj as Texture2D;
            if (tex.m_Name.Equals(texName)) return tex;
        }
        return null;
    }

    public List<KeyValuePair<string, string>> GetCharaItem(int categroyId)
    {
        CharaItems.TryGetValue(categroyId, out var item);
        return item;
    }

    public List<KeyValuePair<string, string>> GetFirstItem() => CharaItems.ElementAt(0).Value;

    public IEnumerable<ZipEntry> FindEntries(ZipFile archive, IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            yield return archive.GetEntry(path);
        }
    }

    public void GetPngPath(ZipFile archive)
    {
        if (PngList == null) PngList = new HashSet<string>();
        if (PngFolder == null) PngFolder = new HashSet<string>();

        foreach (ZipEntry entry in archive)
        {
            if (entry.Name.StartsWithCase("abdata/")
                && entry.Name.EndsWithCase(".png"))
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

    public void Dispose()
    {
        _studioThumbList.Clear();
        _assetsManager.Clear();
        CharaItems.Clear();
        Unity3dList?.Clear();
        PngFolder?.Clear();
        PngList?.Clear();
        _charaItemCount = 0;
        _thumbAbSize = null;
        _thumbCompMethod = CompressionMethod.Stored;
    }
}
