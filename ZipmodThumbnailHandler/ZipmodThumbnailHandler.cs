using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using ZipmodThumbnailHandler.Tools;
using SharpShell.Attributes;
using SharpShell.SharpThumbnailHandler;
using SharpShell.Helpers;
using System.Diagnostics;

namespace ZipmodThumbnailHandler;

[ComVisible(true)]
[COMServerAssociation(AssociationType.ClassOfExtension, ".Zipmod")]
[DisplayName("Zipmod Thumbnail Handler")]
[Guid("0d56e05f-7489-3812-8e0d-cd79410139c8")] // Don't change Guid
public class ZipmodThumbnailHandler : SharpThumbnailHandler
{
    private readonly GenerateThumbnailImage _generateThumbnailImage;

    public ZipmodThumbnailHandler()
    {
        _generateThumbnailImage = new GenerateThumbnailImage { ThumbHandler = this };
    }

    protected override Bitmap GetThumbnailImage(uint width)
    {
        try
        {
            var localPath = Process.GetCurrentProcess().MainModule.FileName;
#if DEBUG
                Log($"Process path: {localPath}");
#endif
            return _generateThumbnailImage.ComaItem(SelectedItemStream, width);
        }
#if DEBUG
        catch(Exception ex)
        {
            TrunLog($"exception occured opening the Zipmod file. \r\n{SelectedItemStream}", true, ex);
            return null;
        }
#endif
        finally
        {
            Clear();
        }
    }

    internal void TrunLog(
        string message, bool isError = false, Exception ex = null)
    {
        if (isError)
        {
            LogError(message, ex);
            return;
        }
        Log(message);
    }

    public void Clear()
    {
        _generateThumbnailImage.Dispose();
        SelectedItemStream.Dispose();
    }
}
