using System;
using System.Globalization;

namespace Retroboat.Text
{
    public static class Format
    {
        public static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("pt-BR");

        static readonly string[] Units = { "B", "KB", "MB", "GB", "TB" };

        public static string Bytes(long bytes)
        {
            double value = bytes;
            int unit = 0;
            while (Math.Abs(value) >= 1024 && unit < Units.Length - 1)
            {
                value /= 1024;
                unit++;
            }

            if (unit == 0)
                return bytes.ToString(Culture) + " B";

            return value.ToString(value >= 100 ? "0" : "0.0", Culture) + " " + Units[unit];
        }

        public static string Speed(double bytesPerSecond) => Bytes((long)bytesPerSecond) + "/s";

        public static string Duration(TimeSpan duration)
        {
            if (duration.TotalHours >= 1)
                return ((int)duration.TotalHours).ToString(Culture) + duration.ToString(@"\:mm\:ss", Culture);

            return duration.ToString(@"m\:ss", Culture);
        }

        public static string Percent(long done, long total)
        {
            if (total <= 0)
                return "100%";

            return (100.0 * done / total).ToString("0.0", Culture) + "%";
        }
    }
}
