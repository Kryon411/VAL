namespace VAL.Host.Options
{
    public sealed class WebViewOptions
    {
        public const string SectionName = "WebView";
        public const string DefaultStartUrl = "https://chatgpt.com";

        public string StartUrl { get; set; } = DefaultStartUrl;
        public bool AllowDevTools { get; set; } = true;
        public bool BlockNewWindow { get; set; } = true;
        public string? UserAgentOverride { get; set; }

        public void ApplyDefaults()
        {
            if (string.IsNullOrWhiteSpace(StartUrl))
                StartUrl = DefaultStartUrl;
        }
    }
}
