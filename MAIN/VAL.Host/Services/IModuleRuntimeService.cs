using System.Threading.Tasks;

namespace VAL.Host.Services
{
    public interface IModuleRuntimeService
    {
        void Start();
        Task EnsureModulesInitializedAsync();
    }
}
