namespace VAL.Host.Services
{
    public interface IBuildInfo
    {
        string Version { get; }
        string InformationalVersion { get; }
        string Environment { get; }
        string? BuildDate { get; }
        string? GitSha { get; }
    }
}
