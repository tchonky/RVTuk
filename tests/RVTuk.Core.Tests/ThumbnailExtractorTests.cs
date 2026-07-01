using System.Linq;
using RVTuk.Core.Extraction;
using Xunit;

namespace RVTuk.Core.Tests
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
    }
}
