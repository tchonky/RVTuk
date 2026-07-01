using System;
using System.Text;

namespace RVTuk.Core.AreaSubmission
{
    /// <summary>
    /// Generates the exact-byte `.dat` file format for DWFX scale output.
    /// </summary>
    public static class DatWriter
    {
        /// <summary>
        /// Builds the byte representation of a DWFX_SCALE data file.
        /// </summary>
        /// <param name="scaleValue">The numeric scale value (e.g., 10 for 1:100).</param>
        /// <returns>The UTF-8 encoded bytes: "DWFX_SCALE\t{scaleValue}\n".</returns>
        public static byte[] Build(int scaleValue)
        {
            var content = $"DWFX_SCALE\t{scaleValue}\n";
            return Encoding.ASCII.GetBytes(content);
        }
    }
}
