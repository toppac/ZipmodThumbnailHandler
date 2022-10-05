using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using ZipmodThumbnailHandler.Tools;
using System.Buffers;
using System.Text;
#if DEBUG
using SharpShell.Attributes;
using SharpShell.SharpThumbnailHandler;
using SharpShell.Helpers;
#else
using Microsoft.WindowsAPICodePack.ShellExtensions;

using Microsoft.WindowsAPICodePack.Shell;
#endif


namespace ZipmodThumbnailHandler;

#if DEBUG

// Parckage = SharpShell
[ComVisible(true)]
[COMServerAssociation(AssociationType.ClassOfExtension, ".zipmod")]
[DisplayName("Zipmod Thumbnail Handler")]
[Guid("0d56e05f-7489-3812-8e0d-cd79410139c8")] // Don't change Guid
public class ZipmodThumbnailHandler : SharpThumbnailHandler
{
    private GenerateThumbnailImage _thumbHelper;

    protected override Bitmap GetThumbnailImage(uint width)
    {
        try
        {
            _thumbHelper = new GenerateThumbnailImage(this);
            return _thumbHelper.ComaItem(SelectedItemStream, width);
        }
        catch(Exception ex)
        {
            // Top exception occured opening the zipmod file.
            TrunLog($"{SelectedItemStream}{Environment.NewLine}", ex);
            TrunLog($"Process path: {SelectedItemStream.Name}");
            return null;
        }
        finally
        {
            Clear();
        }
    }

    internal void TrunLog(string message, Exception ex = null)
    {
        if (ex != null)
        {
            LogError(message, ex);
            return;
        }
        Log(message);
    }

    public void Clear()
    {
        _thumbHelper.Dispose();
        SelectedItemStream.Dispose();
    }
}

#else

// Parckage = WindowsAPICodePack
[ComVisible(true)]
[ClassInterface(ClassInterfaceType.None)]
[ProgId("ThumbnailProvider.ZipmodThumbnailHandler")]
[Guid("0d56e05f-7489-3812-8e0d-cd79410139c8")]
[ThumbnailProvider("ZipmodThumbnailHandler", ".zipmod", ThumbnailAdornment = ThumbnailAdornment.None, DisableProcessIsolation = true)]
public class ZipmodThumbnailHandler : ThumbnailProvider, IThumbnailFromShellObject, IThumbnailFromFile
{
    internal string FullName { get; private set; }

    private GenerateThumbnailImage _thumbHelper;

    public Bitmap ConstructBitmap(ShellObject shellObject, int sideSize)
    {
        FullName = shellObject.ParsingName;
        try
        {
            return GetImage(sideSize);
        }
        catch
        {
            return null;
        }
        finally
        {
            Clear();
        }
    }

    public Bitmap ConstructBitmap(FileInfo info, int sideSize)
    {
        FullName = info.FullName;
        try
        {

            return GetImage(sideSize);
        }
        catch
        {
            return null;
        }
        finally
        {
            Clear();
        }
    }

    private Bitmap GetImage(int size)
    {
        Bitmap result;
        using var fs = new FileStream(FullName, FileMode.Open, FileAccess.Read);
        _thumbHelper = new GenerateThumbnailImage(this);
        result = _thumbHelper.ComaItem(fs, (uint)size);
        return result;
    }

    public void Clear()
    {
        FullName = null;
        _thumbHelper?.Dispose();
        _thumbHelper = null;
    }
}

#endif
