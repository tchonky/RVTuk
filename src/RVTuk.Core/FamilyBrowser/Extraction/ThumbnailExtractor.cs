using System;
using System.IO;
using System.Text;
using OpenMcdf;

namespace RVTuk.Core.FamilyBrowser.Extraction
{
    public static class ThumbnailExtractor
    {
        private const string SummaryInfoStreamName = "\x05SummaryInformation";
        private const string BasicFileInfoStreamName = "BasicFileInfo";
        // Modern Revit (2024/2025) stores the browse/Explorer preview as an embedded PNG
        // inside a dedicated top-level stream named "RevitPreview4.0" — NOT in the legacy
        // \x05SummaryInformation PIDSI_THUMBNAIL property. Current .rfa families leave that
        // legacy property empty, which is why the SummaryInformation-only path found nothing.
        private const string RevitPreviewStreamName = "RevitPreview4.0";
        private const string RevitPreviewStreamPrefix = "RevitPreview";

        // 8-byte PNG file signature (89 50 4E 47 0D 0A 1A 0A).
        private static readonly byte[] PngSignature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

        /// <summary>
        /// Opens the .rfa file once and extracts both the thumbnail PNG and the Revit
        /// build year (e.g. 2024). Year = 0 means unreadable / unknown.
        /// </summary>
        public static (byte[]? Thumbnail, int RevitYear) ExtractFromRfa(string rfaPath)
        {
            try
            {
                // Our own stream, not RootStorage.OpenRead: that one leaks its FileStream until GC
                // when the header parse throws (0-byte/truncated file), blocking Revit from saving it.
                using var fs = new FileStream(rfaPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var rootStorage = RootStorage.Open(fs, StorageModeFlags.LeaveOpen);
                return (TryExtractThumbnail(rootStorage), TryReadRevitYear(rootStorage));
            }
            catch
            {
                return (null, 0);
            }
        }

        // ── thumbnail ────────────────────────────────────────────────────────────

        // 1) Preferred path: the modern "RevitPreview4.0" stream holds an embedded PNG.
        //    This is what current Revit 2024/2025 families actually populate.
        // 2) Fallback path (never worse than before): legacy \x05SummaryInformation
        //    PIDSI_THUMBNAIL DIB. Kept for old families / files that still carry it.
        private static byte[]? TryExtractThumbnail(RootStorage rootStorage)
            => TryExtractRevitPreviewPng(rootStorage) ?? TryExtractSummaryInfoThumbnail(rootStorage);

        // ── modern Revit preview stream ─────────────────────────────────────────
        // "RevitPreview4.0" is a PNG sandwiched between a small binary prefix (image
        // type/size metadata) and a postfix, so we scan the stream for the PNG
        // signature rather than assuming the image starts at offset 0.
        private static byte[]? TryExtractRevitPreviewPng(RootStorage rootStorage)
        {
            string? streamName = FindRevitPreviewStreamName(rootStorage);
            if (streamName == null) return null;

            try
            {
                using var stream = rootStorage.OpenStream(streamName);
                return ExtractEmbeddedPng(ReadAllBytes(stream));
            }
            catch { return null; }
        }

        // Locate the preview stream by name, tolerating future "RevitPreviewX.Y"
        // variants. Returns null when no such stream exists.
        private static string? FindRevitPreviewStreamName(RootStorage rootStorage)
        {
            try
            {
                string? fallback = null;
                foreach (var entry in rootStorage.EnumerateEntries())
                {
                    if (entry.Type != EntryType.Stream) continue;
                    if (string.Equals(entry.Name, RevitPreviewStreamName, StringComparison.OrdinalIgnoreCase))
                        return entry.Name;
                    if (fallback == null &&
                        entry.Name.StartsWith(RevitPreviewStreamPrefix, StringComparison.OrdinalIgnoreCase))
                        fallback = entry.Name;
                }
                return fallback;
            }
            catch
            {
                // If enumeration is unavailable, fall back to the canonical name only if present.
                try { return rootStorage.ContainsEntry(RevitPreviewStreamName) ? RevitPreviewStreamName : null; }
                catch { return null; }
            }
        }

        /// <summary>
        /// Returns the embedded PNG (signature .. end of IEND chunk) found anywhere inside
        /// <paramref name="data"/>, or null if no PNG signature is present. Public so it can
        /// be unit-tested against crafted byte buffers without a real .rfa.
        /// </summary>
        public static byte[]? ExtractEmbeddedPng(byte[]? data)
        {
            if (data == null) return null;
            int start = IndexOf(data, PngSignature, 0);
            if (start < 0) return null;

            // Find the terminating IEND chunk: 4-byte length (0) + "IEND" + 4-byte CRC.
            // The image ends 8 bytes after the "IEND" type marker.
            int iend = IndexOf(data, IendChunkType, start + PngSignature.Length);
            int end = iend >= 0 ? iend + IendChunkType.Length + 4 : data.Length;
            if (end > data.Length) end = data.Length;

            int length = end - start;
            if (length < PngSignature.Length) return null;

            var png = new byte[length];
            Array.Copy(data, start, png, 0, length);
            return png;
        }

        private static readonly byte[] IendChunkType = { 0x49, 0x45, 0x4E, 0x44 }; // "IEND"

        // ── legacy SummaryInformation thumbnail ─────────────────────────────────
        private static byte[]? TryExtractSummaryInfoThumbnail(RootStorage rootStorage)
        {
            byte[] data;
            try
            {
                using var stream = rootStorage.OpenStream(SummaryInfoStreamName);
                data = ReadAllBytes(stream);
            }
            catch { return null; }

            var dib = ParseThumbnailDib(data);
            return dib == null ? null : ConvertDibToPng(dib);
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
            if (dib.Length < 40) return null;
            try
            {
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

                // BasicFileInfo contains UTF-16LE text lines (at an odd byte offset, hence the
                // byte search). Modern Revit writes "Format: 2024"; older files wrote
                //   "Revit Build: Autodesk Revit 2017 (Build: 20160225_1515(x64))"
                var marker = Encoding.Unicode.GetBytes("Format: ");
                int pos = IndexOf(bytes, marker);
                if (pos < 0)
                {
                    marker = Encoding.Unicode.GetBytes("Revit Build:");
                    pos = IndexOf(bytes, marker);
                }
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

        private static int IndexOf(byte[] haystack, byte[] needle) => IndexOf(haystack, needle, 0);

        private static int IndexOf(byte[] haystack, byte[] needle, int startIndex)
        {
            if (needle.Length == 0 || haystack.Length < needle.Length) return -1;
            for (int i = Math.Max(0, startIndex); i <= haystack.Length - needle.Length; i++)
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
