using System.Text.RegularExpressions;

namespace VAL.Host.Logging
{
    public static class LogSanitizer
    {
        private static readonly Regex WindowsPath = new(@"[A-Za-z]:\\[^\s""']+", RegexOptions.Compiled);
        private static readonly Regex UnixPath = new(@"/[^\s""']+", RegexOptions.Compiled);

        public static string Sanitize(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return string.Empty;

            var sanitized = WindowsPath.Replace(message, "<path>");
            sanitized = UnixPath.Replace(sanitized, "<path>");
            return sanitized;
        }
    }
}
