using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;

namespace VAL.Host.Services
{
    public interface IModuleLoader
    {
        Task InitializeAsync(CoreWebView2 core);
    }
}
