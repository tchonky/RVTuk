using System;
using System.IO;
using System.Text;
using OpenMcdf;

namespace ReviTchucky.Core.Extraction
{
    public static class ThumbnailExtractor
    {
        private const string SummaryInfoStreamName = "\x05SummaryInformation";
        private const string BasicFileInfoStreamName = "BasicFileInfo";

        /// <summary>
        /// Opens the .rfa file once and extracts both the thumbnail PNG and the Revit
        /// build year (e.g. 2024). Year = 0 means unreadable / unknown.
        /// </summary>
        public static (byte[]? Thumbnail, int RevitYear) ExtractFromRfa(string rfaPath)
        {
            try
            {
                using var rootStorage = RootStorage.OpenRead(rfaPath);
                return (TryExtractThumbnail(rootStorage), TryReadRevitYear(rootStorage));
            }
            catch
            {
                return (null, 0);
            }
        }

        // Keep the old single-purpose method so nothing else breaks
        public static byte[]? ExtractThumbnailFromRfa(string rfaPath)
        {
            try
            {
                using var rootStorage = RootStorage.OpenRead(rfaPath);
                return TryExtractThumbnail(rootStorage);
            }
            catch
            {
                return null;
            }
        }

        // ── thumbnail ────────────────────────────────────────────────────────────

        private static byte[]? TryExtractThumbnail(RootStorage rootStorage)
        {
            try
            {
                using var stream = rootStorage.OpenStream(SummaryInfoStreamName);
                var data = ReadAllBytes(stream);
                var dib = ParseThumbnailDib(data);
                return dib == null ? null : ConvertDibToPng(dib);
            }
            catch
            {
                return null;
            }
        }

        private static byte[] ReadAllBytes(CfbStream stream)
        {
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            return ms.ToArray();
        }

        private static byte[]? ParseThumbnailDib(byte[] data)
        {
            if (data.Length < 48) return null;
            if (BitConverter.ToUInt16(data, 0) != 0xFFFE) return null;

            uint sectionOffset = BitConverter.ToUInt32(data, 44);
            if (sectionOffset + 8 > (uint)data.Length) return null;

            uint propertyCount = BitConverter.ToUInt32(data, (int)sectionOffset + 4);

            for (uint i = 0; i < propertyCount; i++)
            {
                uint pairOffset = sectionOffset + 8 + i * 8;
                if (pairOffset + 8 > (uint)data.Length) break;

                uint propId = BitConverter.ToUInt32(data, (int)pairOffset);
                uint valueRelOffset = BitConverter.ToUInt32(data, (int)pairOffset + 4);

                if (propId != 0x0F) continue; // PIDSI_THUMBNAIL

                uint absOffset = sectionOffset + valueRelOffset;
                if (absOffset + 12 > (uint)data.Length) return null;

                ushort varType = BitConverter.ToUInt16(data, (int)absOffset);
                if (varType != 0x0047) return null; // VT_CF

                uint cbSize = BitConverter.ToUInt32(data, (int)absOffset + 4);
                uint clipFormat = BitConverter.ToUInt32(data, (int)absOffset + 8);
                if (clipFormat != 8 && clipFormat != 2) return null; // CF_DIB / CF_BITMAP

                int dibStart = (int)absOffset + 12;
                int dibLength = (int)cbSize - 4;
                if (dibLength <= 0 || dibStart + dibLength > data.Length) return null;

                var dib = new byte[dibLength];
                Array.Copy(data, dibStart, dib, 0, dibLength);
                return dib;
            }
            return null;
        }

        private static byte[]? ConvertDibToPng(byte[] dib)
        {
            try
            {
                if (dib.Length < 40) return null;

                int biBitCount = BitConverter.ToInt16(dib, 14);
                int biClrUsed = BitConverter.ToInt32(dib, 32);
                if (biClrUsed == 0 && biBitCount < 16)
                    biClrUsed = 1 << biBitCount;

                int bfOffBits = 14 + 40 + biClrUsed * 4;
                var bmpBytes = new byte[14 + dib.Length];
                bmpBytes[0] = (byte)'B';
                bmpBytes[1] = (byte)'M';
                BitConverter.GetBytes(bmpBytes.Length).CopyTo(bmpBytes, 2);
                BitConverter.GetBytes(bfOffBits).CopyTo(bmpBytes, 10);
                dib.CopyTo(bmpBytes, 14);

                using var bmpStream = new MemoryStream(bmpBytes);
                using var bitmap = new System.Drawing.Bitmap(bmpStream);
                using var pngStream = new MemoryStream();
                bitmap.Save(pngStream, System.Drawing.Imaging.ImageFormat.Png);
                return pngStream.ToArray();
            }
            catch
            {
                return null;
            }
        }

        // ── Revit version year ───────────────────────────────────────────────────

        private static int TryReadRevitYear(RootStorage rootStorage)
        {
            try
            {
                using var stream = rootStorage.OpenStream(BasicFileInfoStreamName);
                using var ms = new MemoryStream();
                stream.CopyTo(ms);
                var bytes = ms.ToArray();

                // BasicFileInfo contains UTF-16LE text with a line like:
                //   "Revit Build: Autodesk Revit 2024 (Build: 20240606_1515(x64))"
                var marker = Encoding.Unicode.GetBytes("Revit Build:");
                int pos = IndexOf(bytes, marker);
                if (pos < 0) return 0;

                int start = pos + marker.Length;
                int len = Math.Min(200, bytes.Length - start);
                if (len % 2 != 0) len--;
                if (len <= 0) return 0;

                var text = Encoding.Unicode.GetString(bytes, start, len);

                // Find first 4-digit token starting with "20"
                for (int i = 0; i + 3 < text.Length; i++)
                {
                    if (text[i] == '2' && text[i + 1] == '0'
                        && char.IsDigit(text[i + 2]) && char.IsDigit(text[i + 3])
                        && (i == 0 || !char.IsDigit(text[i - 1]))
                        && (i + 4 >= text.Length || !char.IsDigit(text[i + 4])))
                    {
                        if (int.TryParse(text.Substring(i, 4), out int year))
                            return year;
                    }
                }
                return 0;
            }
            catch
            {
                return 0;
            }
        }

        private static int IndexOf(byte[] haystack, byte[] needle)
        {
            for (int i = 0; i <= haystack.Length - needle.Length; i++)
            {
                bool match = true;
                for (int j = 0; j < needle.Length; j++)
                    if (haystack[i + j] != needle[j]) { match = false; break; }
                if (match) return i;
            }
            return -1;
        }
    }
}
