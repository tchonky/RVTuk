using System;
using System.IO;
using System.Linq;
using System.Text;
using OpenMcdf;
using RVTuk.Core.FamilyBrowser.Extraction;
using Xunit;

namespace RVTuk.Core.Tests.FamilyBrowser
{
    public class ThumbnailExtractorTests
    {
        private static readonly byte[] PngSig = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        private static readonly byte[] IendType = { 0x49, 0x45, 0x4E, 0x44 }; // "IEND"
        private static readonly byte[] IendCrc = { 0xAE, 0x42, 0x60, 0x82 };

        // Minimal PNG-shaped buffer: signature + payload + IEND chunk (len+type+crc).
        private static byte[] MakePng(params byte[][] payload)
        {
            var body = payload.SelectMany(p => p);
            return PngSig
                .Concat(body)
                .Concat(new byte[] { 0x00, 0x00, 0x00, 0x00 }) // IEND length = 0
                .Concat(IendType)
                .Concat(IendCrc)
                .ToArray();
        }

        [Fact]
        public void ExtractEmbeddedPng_ReturnsWholePng_WhenAtOffsetZero()
        {
            var png = MakePng(new byte[] { 1, 2, 3, 4 });

            var result = ThumbnailExtractor.ExtractEmbeddedPng(png);

            Assert.NotNull(result);
            Assert.Equal(png, result);
        }

        [Fact]
        public void ExtractEmbeddedPng_StripsPrefixAndPostfix()
        {
            var png = MakePng(new byte[] { 9, 9, 9 });
            var prefix = new byte[] { 0x52, 0x56, 0x54, 0xDE, 0xAD }; // fake Revit preview header
            var postfix = new byte[] { 0xBE, 0xEF, 0x00, 0x11, 0x22 };
            var stream = prefix.Concat(png).Concat(postfix).ToArray();

            var result = ThumbnailExtractor.ExtractEmbeddedPng(stream);

            Assert.NotNull(result);
            Assert.Equal(png, result); // exactly the PNG, no prefix/postfix bytes
        }

        [Fact]
        public void ExtractEmbeddedPng_ReadsToEnd_WhenNoIendChunk()
        {
            // Signature present but no IEND: fall back to end-of-buffer.
            var buf = PngSig.Concat(new byte[] { 1, 2, 3 }).ToArray();

            var result = ThumbnailExtractor.ExtractEmbeddedPng(buf);

            Assert.NotNull(result);
            Assert.Equal(buf, result);
        }

        [Fact]
        public void ExtractEmbeddedPng_ReturnsNull_WhenNoSignature()
        {
            var buf = new byte[] { 0x00, 0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77, 0x88 };

            Assert.Null(ThumbnailExtractor.ExtractEmbeddedPng(buf));
        }

        [Fact]
        public void ExtractEmbeddedPng_ReturnsNull_ForNullOrEmpty()
        {
            Assert.Null(ThumbnailExtractor.ExtractEmbeddedPng(null));
            Assert.Null(ThumbnailExtractor.ExtractEmbeddedPng(new byte[0]));
        }

        // A real compound file laid out like a Revit 2023-2026 family: "RevitPreview4.0" holds the
        // PNG between a binary prefix and postfix, and "BasicFileInfo" holds UTF-16LE lines starting
        // at an odd byte offset. Returns the embedded PNG.
        internal static byte[] WriteRfa(string path, bool basicFileInfo = true)
        {
            var png = MakePng(new byte[] { 1, 2, 3 });
            using var root = RootStorage.Create(path);
            using (var preview = root.CreateStream("RevitPreview4.0"))
            {
                var bytes = new byte[] { 0x62, 0x19, 0x22, 0x05 }.Concat(png).Concat(new byte[] { 0x80, 0, 0, 0 }).ToArray();
                preview.Write(bytes, 0, bytes.Length);
            }
            if (basicFileInfo)
            {
                using var info = root.CreateStream("BasicFileInfo");
                var bytes = new byte[] { 0x0E }.Concat(Encoding.Unicode.GetBytes(
                    "Central Model Path: \r\nFormat: 2024\r\nBuild: 20230121_1515(x64)\r\n")).ToArray();
                info.Write(bytes, 0, bytes.Length);
            }
            return png;
        }

        [Fact]
        public void ExtractFromRfa_ReadsPreviewPng_AndYearFromFormatLine()
        {
            var path = Path.Combine(Path.GetTempPath(), "rvtuk_rfa_" + Guid.NewGuid().ToString("N") + ".rfa");
            try
            {
                var png = WriteRfa(path);
                var (thumb, year) = ThumbnailExtractor.ExtractFromRfa(path);
                Assert.Equal(png, thumb);
                Assert.Equal(2024, year);

                WriteRfa(path, basicFileInfo: false);
                Assert.Equal(0, ThumbnailExtractor.ExtractFromRfa(path).RevitYear);
            }
            finally { File.Delete(path); }
        }

        // A file that isn't a compound file (0-byte, truncated, mid-save) must not stay open:
        // a lingering handle blocks Revit from saving the family.
        [Fact]
        public void ExtractFromRfa_NonCompoundFile_ReturnsNothing_AndReleasesTheFile()
        {
            var path = Path.Combine(Path.GetTempPath(), "rvtuk_rfa_" + Guid.NewGuid().ToString("N") + ".rfa");
            File.WriteAllText(path, "not a compound file");

            Assert.Equal((null, 0), ThumbnailExtractor.ExtractFromRfa(path));
            File.Delete(path); // throws IOException on Windows while a handle is open
        }
    }
}
