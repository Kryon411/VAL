namespace VAL.Host.Services
{
    public sealed class ModuleStatusInfo
    {
        public ModuleStatusInfo(string name, string status, string path)
        {
            Name = name;
            Status = status;
            Path = path;
        }

        public string Name { get; }

        public string Status { get; }

        public string Path { get; }
    }
}
