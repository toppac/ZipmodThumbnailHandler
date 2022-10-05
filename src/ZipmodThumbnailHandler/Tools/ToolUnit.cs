using System;
using System.IO;
using System.IO.Compression;
using System.Reflection;

namespace ZipmodThumbnailHandler.Tools
{
    public static class ToolUnit
    {
        public static string AssemblyDirectory
        {
            get
            {
                string codeBase = Assembly.GetExecutingAssembly().CodeBase;
                UriBuilder builder = new UriBuilder(codeBase);
                string path = Uri.UnescapeDataString(builder.Path);
                return Path.GetDirectoryName(path);
            }
        }

        public static bool StartsWithCase(this string source, string dest)
        {
            return source.StartsWith(dest, StringComparison.OrdinalIgnoreCase);
        }

        public static bool EndsWithCase(this string source, string dest)
        {
            return source.EndsWith(dest, StringComparison.OrdinalIgnoreCase);
        }

        public static bool ContainsCase(this string source, string dest)
        {
            return source.Contains(dest, StringComparison.OrdinalIgnoreCase);
        }

        public static bool Contains(this string source, string dest, StringComparison comparison)
        {
            return source.IndexOf(dest, comparison) >= 0;
        }

        public static int Check<T>(this T[] array, T value)
        {
            return Check(array.Length, comparer);

            bool comparer(int idx)
            {
                return array[idx].Equals(value);
            }
        }

        public static int Check(int len, Func<int, bool> func)
        {
            int num = -1;
            while (++num < len && !func(num)) {}
            if (num < len)
            {
                return num;
            }
            return -1;
        }

        public static readonly string[] FileSizeUnit = new string[]
        { "B", "KB", "MB", "GB", "TB" };

        public static string FormatFileSize(double length)
        {
            int order = 0;
            while (length >= 1024 && order < FileSizeUnit.Length - 1)
            {
                ++order;
                length /= 1024;
            }
            return string.Format("{0:0.00} {1}", length, FileSizeUnit[order]);
        }

#if OTHER_BRANCH
        public static readonly BindingFlags all
            = BindingFlags.Instance
            | BindingFlags.Static
            | BindingFlags.Public
            | BindingFlags.NonPublic
            | BindingFlags.GetField
            | BindingFlags.SetField
            | BindingFlags.GetProperty
            | BindingFlags.SetProperty;

        // Web Data Reader
        public static MemoryStream WebToMemoryStream(this Stream instream)
        {
            MemoryStream outstream = new();
            const int BUFFER_SIZE = 4096;
            byte[] buffer = new byte[BUFFER_SIZE];
            int count;
            while ((count = instream.Read(buffer, 0, BUFFER_SIZE)) > -1)
            {
                outstream.Write(buffer, 0, count);
            }
            outstream.Seek(0, SeekOrigin.Begin);
            return outstream;
        }
#endif
    }
}
