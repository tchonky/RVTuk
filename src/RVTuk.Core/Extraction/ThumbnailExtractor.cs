using System;
using System.IO;
using System.Text;
using System.Threading;
using OpenMcdf;

namespace RVTuk.Core.Extraction
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
            byte[]? thumb;
            int year = 0;
            string reason;
            try
            {
                using var rootStorage = RootStorage.OpenRead(rfaPath);
                thumb = TryExtractThumbnail(rootStorage, out reason);
                year = TryReadRevitYear(rootStorage);
            }
            catch (Exception ex)
            {
                thumb = null;
                reason = "open-failed:" + ex.GetType().Name + ":" + ex.Message;
            }
            ThumbDebug.Log(rfaPath, thumb, reason);
            return (thumb, year);
        }

        // Keep the old single-purpose method so nothing else breaks
        public static byte[]? ExtractThumbnailFromRfa(string rfaPath)
        {
            try
            {
                using var rootStorage = RootStorage.OpenRead(rfaPath);
                return TryExtractThumbnail(rootStorage, out _);
            }
            catch
            {
                return null;
            }
        }

        // ── thumbnail ────────────────────────────────────────────────────────────

        private static byte[]? TryExtractThumbnail(RootStorage rootStorage, out string reason)
        {
            CfbStream stream;
            try
            {
                stream = rootStorage.OpenStream(SummaryInfoStreamName);
            }
            catch (Exception ex)
            {
                reason = "no-summaryinfo-stream:" + ex.GetType().Name;
                return null;
            }

            using (stream)
            {
                byte[] data;
                try { data = ReadAllBytes(stream); }
                catch (Exception ex) { reason = "stream-read-threw:" + ex.GetType().Name; return null; }

                var dib = ParseThumbnailDib(data, out reason);
                if (dib == null) return null;

                var png = ConvertDibToPng(dib, out string convReason);
                reason = convReason;
                return png;
            }
        }

        private static byte[] ReadAllBytes(CfbStream stream)
        {
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            return ms.ToArray();
        }

        private static byte[]? ParseThumbnailDib(byte[] data, out string reason)
        {
            if (data.Length < 48) { reason = "data-too-short:" + data.Length; return null; }
            if (BitConverter.ToUInt16(data, 0) != 0xFFFE) { reason = "no-bom:0x" + BitConverter.ToUInt16(data, 0).ToString("X4"); return null; }

            uint sectionOffset = BitConverter.ToUInt32(data, 44);
            if (sectionOffset + 8 > (uint)data.Length) { reason = "section-offset-oob:" + sectionOffset; return null; }

            uint propertyCount = BitConverter.ToUInt32(data, (int)sectionOffset + 4);

            for (uint i = 0; i < propertyCount; i++)
            {
                uint pairOffset = sectionOffset + 8 + i * 8;
                if (pairOffset + 8 > (uint)data.Length) break;

                uint propId = BitConverter.ToUInt32(data, (int)pairOffset);
                uint valueRelOffset = BitConverter.ToUInt32(data, (int)pairOffset + 4);

                if (propId != 0x0F) continue; // PIDSI_THUMBNAIL

                uint absOffset = sectionOffset + valueRelOffset;
                if (absOffset + 12 > (uint)data.Length) { reason = "value-oob"; return null; }

                ushort varType = BitConverter.ToUInt16(data, (int)absOffset);
                if (varType != 0x0047) { reason = "not-vt-cf:0x" + varType.ToString("X4"); return null; } // VT_CF

                uint cbSize = BitConverter.ToUInt32(data, (int)absOffset + 4);
                uint clipFormat = BitConverter.ToUInt32(data, (int)absOffset + 8);
                if (clipFormat != 8 && clipFormat != 2) { reason = "bad-clipformat:" + clipFormat; return null; } // CF_DIB / CF_BITMAP

                int dibStart = (int)absOffset + 12;
                int dibLength = (int)cbSize - 4;
                if (dibLength <= 0 || dibStart + dibLength > data.Length) { reason = "bad-dib-length:" + dibLength; return null; }

                var dib = new byte[dibLength];
                Array.Copy(data, dibStart, dib, 0, dibLength);
                reason = "ok-dib:" + dibLength;
                return dib;
            }
            reason = "thumb-prop-not-found:props=" + propertyCount;
            return null;
        }

        private static byte[]? ConvertDibToPng(byte[] dib, out string reason)
        {
            if (dib.Length < 40) { reason = "dib-header-too-short:" + dib.Length; return null; }
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
                reason = "ok";
                return pngStream.ToArray();
            }
            catch (Exception ex)
            {
                reason = "dib-to-png-threw:" + ex.GetType().Name + ":" + ex.Message;
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

        // ── TEMP DIAGNOSTIC — remove once the OLE-thumbnail bug is fixed (see BACKLOG) ──
        // Records, per family, which stage produced/failed the thumbnail so we can localise the
        // "no thumbnails extract" bug from a real Revit run. Best-effort and capped so a large
        // scan can't fill the disk.
        private static class ThumbDebug
        {
            private static readonly string LogPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "RVTuk", "thumb-debug.log");
            private static int _count;
            private const int MaxLines = 2000;

            public static void Log(string rfaPath, byte[]? thumb, string reason)
            {
                try
                {
                    if (Interlocked.Increment(ref _count) > MaxLines) return;
                    var size = thumb?.Length ?? 0;
                    var name = Path.GetFileName(rfaPath);
                    Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
                    File.AppendAllText(LogPath,
                        $"{(thumb != null ? "OK " : "NUL")} size={size,-7} {reason,-32} {name}{Environment.NewLine}");
                }
                catch { /* diagnostics must never break a scan */ }
            }
        }
    }
}
