using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;
using VAL.Host;

namespace VAL.Host.Services.Adapters
{
    public sealed class ModuleLoaderAdapter : IModuleLoader
    {
        public Task InitializeAsync(CoreWebView2 core)
        {
            return ModuleLoader.Initialize(core);
        }
    }
}
