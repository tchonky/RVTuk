using System;
using System.IO;
using OpenMcdf;

namespace RVTuk.Core.Extraction
{
    public static class ThumbnailWriter
    {
        private const string StreamName = "\x05SummaryInformation";

        /// <summary>
        /// Writes pngData as the OLE thumbnail into the .rfa file.
        /// Returns true on success. Makes a .bak backup and restores it on failure.
        /// </summary>
        public static bool WriteThumbnailToRfa(string rfaPath, byte[] pngData)
        {
            try
            {
                if (!File.Exists(rfaPath)) return false;

                var dib = ConvertPngToDib(pngData);
                if (dib == null) return false;

                var streamBytes = BuildSummaryInfoStream(dib);

                var backup = rfaPath + ".thumb_bak";
                File.Copy(rfaPath, backup, overwrite: true);
                try
                {
                    WriteStreamToCompoundFile(rfaPath, StreamName, streamBytes);
                    return true;
                }
                catch
                {
                    File.Copy(backup, rfaPath, overwrite: true);
                    return false;
                }
                finally
                {
                    if (File.Exists(backup)) File.Delete(backup);
                }
            }
            catch { return false; }
        }

        private static void WriteStreamToCompoundFile(string path, string streamName, byte[] data)
        {
            // OpenMCDF 3.1.2 v3 API — open for read/write, non-transacted.
            // RootStorage.Open(path, FileMode, FileAccess, StorageModeFlags) is the v3
            // equivalent of CompoundFile(path, CFSUpdateMode.Update, ...) from v2.
            using var storage = RootStorage.Open(path, FileMode.Open, FileAccess.ReadWrite, StorageModeFlags.None);
            try { storage.Delete(streamName); } catch { /* stream may not exist */ }
            using var stream = storage.CreateStream(streamName);
            stream.Write(data, 0, data.Length);
            storage.Flush(false);
        }

        // Build a minimal \x05SummaryInformation property set stream
        // containing only PIDSI_THUMBNAIL (0x0F) with the supplied DIB bytes.
        private static byte[] BuildSummaryInfoStream(byte[] dib)
        {
            // Property value layout at section offset 16:
            //   [2] VT_CF = 0x47 0x00
            //   [2] padding
            //   [4] cbSize = 4 + dib.Length  (includes the 4-byte CF format field)
            //   [4] CF_DIB = 0x08 0x00 0x00 0x00
            //   [N] dib bytes
            int propValueSize = 12 + dib.Length;
            // Pad to 4-byte boundary
            int propValuePadded = (propValueSize + 3) & ~3;

            // Section layout:
            //   [4] section size = 16 (header) + propValuePadded
            //   [4] property count = 1
            //   [4] property ID = 0x0F
            //   [4] property offset from section start = 16
            //   [propValuePadded] property value
            int sectionSize = 16 + propValuePadded;

            // File header layout (48 bytes):
            //   [2] byte order 0xFE 0xFF
            //   [2] format version 0x00 0x00
            //   [2] OS major 0x06 0x00
            //   [2] OS minor 0x02 0x00
            //   [16] CLSID (zeros)
            //   [4] property set count = 1
            //   [16] FMTID_SummaryInformation
            //   [4] section offset = 48

            using var ms = new MemoryStream();
            using var w = new BinaryWriter(ms);

            // File header
            w.Write((ushort)0xFFFE);   // byte order
            w.Write((ushort)0x0000);   // format version
            w.Write((ushort)0x0006);   // OS major
            w.Write((ushort)0x0002);   // OS minor
            w.Write(new byte[16]);     // CLSID zeros
            w.Write((uint)1);          // one property set

            // FMTID_SummaryInformation {F29F85E0-4FF9-1068-AB91-08002B27B3D9}
            w.Write(new byte[] {
                0xE0, 0x85, 0x9F, 0xF2, 0xF9, 0x4F, 0x68, 0x10,
                0xAB, 0x91, 0x08, 0x00, 0x2B, 0x27, 0xB3, 0xD9
            });
            w.Write((uint)48);         // section starts at offset 48

            // Section header
            w.Write((uint)sectionSize);
            w.Write((uint)1);          // one property
            w.Write((uint)0x0F);       // PIDSI_THUMBNAIL
            w.Write((uint)16);         // property value at section offset 16

            // Property value
            w.Write((ushort)0x0047);   // VT_CF
            w.Write((ushort)0x0000);   // padding
            w.Write((uint)(4 + dib.Length)); // cbSize
            w.Write((uint)8);          // CF_DIB
            w.Write(dib);

            // Pad to 4-byte boundary
            int written = 12 + dib.Length;
            int pad = propValuePadded - written;
            if (pad > 0) w.Write(new byte[pad]);

            return ms.ToArray();
        }

        private static byte[]? ConvertPngToDib(byte[] pngData)
        {
            try
            {
                using var ms = new MemoryStream(pngData);
                using var bmp = new System.Drawing.Bitmap(ms);
                using var bmpStream = new MemoryStream();
                bmp.Save(bmpStream, System.Drawing.Imaging.ImageFormat.Bmp);
                var bmpBytes = bmpStream.ToArray();
                // DIB = BMP without 14-byte BITMAPFILEHEADER
                var dib = new byte[bmpBytes.Length - 14];
                Array.Copy(bmpBytes, 14, dib, 0, dib.Length);
                return dib;
            }
            catch { return null; }
        }
    }
}
