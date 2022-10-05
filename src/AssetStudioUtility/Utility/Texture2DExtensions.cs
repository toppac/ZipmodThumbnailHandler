using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System.IO;

namespace AssetStudio
{
    public static class Texture2DExtensions
    {
        public static Image<Bgra32> ConvertToImage(this Texture2D m_Texture2D, bool flip)
        {
            var converter = new Texture2DConverter(m_Texture2D);
#if ORG
            var buff = BigArrayPool<byte>.Shared.Rent(m_Texture2D.m_Width * m_Texture2D.m_Height * 4);
#else
            var buff = new byte[m_Texture2D.m_Width * m_Texture2D.m_Height * 4];
#endif
            try
            {
                if (converter.DecodeTexture2D(buff))
                {
                    var image = Image.LoadPixelData<Bgra32>(buff, m_Texture2D.m_Width, m_Texture2D.m_Height);
                    if (flip)
                    {
                        image.Mutate(x => x.Flip(FlipMode.Vertical));
                    }
                    return image;
                }
                return null;
            }
#if ORG
            finally
            {
                BigArrayPool<byte>.Shared.Return(buff);
            }
#else
            catch { return null; }
#endif
        }

        public static MemoryStream ConvertToStream(this Texture2D m_Texture2D, ImageFormat imageFormat, bool flip)
        {
            var image = ConvertToImage(m_Texture2D, flip);
            if (image != null)
            {
                using (image)
                {
                    return image.ConvertToStream(imageFormat);
                }
            }
            return null;
        }
    }
}
