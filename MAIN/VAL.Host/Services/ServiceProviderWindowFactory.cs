using System;
using Microsoft.Extensions.DependencyInjection;

namespace VAL.Host.Services
{
    public sealed class ServiceProviderWindowFactory<TWindow> : IWindowFactory<TWindow>
        where TWindow : class
    {
        private readonly IServiceProvider _serviceProvider;

        public ServiceProviderWindowFactory(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        }

        public TWindow Create()
        {
            return _serviceProvider.GetRequiredService<TWindow>();
        }
    }
}
