using System;
using System.Globalization;

namespace ReviTchucky.Core.Database
{
    internal static class DbConvert
    {
        /// <summary>
        /// Parses a date/time string read from the index database back into a UTC
        /// <see cref="DateTime"/>. All dates are written by this app with
        /// <c>DateTime.ToString("o")</c> in UTC, but a bare <c>DateTime.Parse(string)</c>
        /// silently converts a "...Z" value to <see cref="DateTimeKind.Local"/>, shifting the
        /// instant by the machine's UTC offset. That corrupts every comparison against
        /// <c>FileInfo.LastWriteTimeUtc</c> (version checks, incremental-scan change detection).
        /// This preserves the true UTC instant and also tolerates SQLite's
        /// <c>CURRENT_TIMESTAMP</c> format ("yyyy-MM-dd HH:mm:ss", which is UTC).
        /// </summary>
        public static DateTime ParseUtc(string value)
        {
            var dt = DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
            return dt.Kind switch
            {
                DateTimeKind.Utc => dt,
                DateTimeKind.Local => dt.ToUniversalTime(),
                _ => DateTime.SpecifyKind(dt, DateTimeKind.Utc), // Unspecified (CURRENT_TIMESTAMP) is UTC
            };
        }
    }
}
