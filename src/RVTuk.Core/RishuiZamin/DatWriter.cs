using System;
using System.Text;

namespace RVTuk.Core.RishuiZamin
{
    /// <summary>
    /// Generates the exact-byte `.dat` file format for DWFX scale output.
    /// </summary>
    public static class DatWriter
    {
        /// <summary>
        /// Builds the byte representation of a DWFX_SCALE data file.
        /// </summary>
        /// <remarks>
        /// The value is always 10, whatever the sheet scale. It is not the sheet scale: every
        /// real 1:100 sample writes 10, and writing 100 for a 1:100 sheet made the robot reject
        /// the frame as oversized (error #41, verified 2026-10-12). Only 1:100 has been
        /// confirmed — see docs/tools/rishui-zamin/rules.md §8.
        /// </remarks>
        /// <returns>The ASCII-encoded bytes: "DWFX_SCALE\t10\n".</returns>
        public static byte[] Build()
        {
            return Encoding.ASCII.GetBytes("DWFX_SCALE\t10\n");
        }
    }
}
