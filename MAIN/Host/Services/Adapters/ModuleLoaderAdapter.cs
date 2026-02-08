using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;
using Microsoft.Extensions.Options;
using VAL.Host;
using VAL.Host.Options;
using VAL.Host.Services;

namespace VAL.Host.Services.Adapters
{
    public sealed class ModuleLoaderAdapter : IModuleLoader
    {
        private readonly ModuleOptions _moduleOptions;
        private readonly IAppPaths _appPaths;
        private readonly IBuildInfo _buildInfo;

        public ModuleLoaderAdapter(IOptions<ModuleOptions> moduleOptions, IAppPaths appPaths, IBuildInfo buildInfo)
        {
            _moduleOptions = moduleOptions.Value;
            _appPaths = appPaths;
            _buildInfo = buildInfo;
        }

        public Task InitializeAsync(CoreWebView2 core)
        {
            return ModuleLoader.Initialize(core, _appPaths.ModulesRoot, _appPaths.ContentRoot, _moduleOptions, _buildInfo.Version);
        }
    }
}
