using Microsoft.Extensions.DependencyInjection;

using VAL.Host.Services;

using Xunit;

namespace VAL.Tests.Hosting
{
    public sealed class ServiceProviderWindowFactoryTests
    {
        [Fact]
        public void CreateWithArgumentResolvesRuntimeArgumentAndRegisteredDependencies()
        {
            var services = new ServiceCollection();
            services.AddSingleton(new TestDependency("dep"));
            services.AddTransient(typeof(IWindowFactory<,>), typeof(ServiceProviderWindowFactory<,>));

            using var provider = services.BuildServiceProvider(validateScopes: true);
            var factory = provider.GetRequiredService<IWindowFactory<TestWindow, TestWindowRequest>>();
            var request = new TestWindowRequest("details");

            var window = factory.Create(request);

            Assert.Same(request, window.Request);
            Assert.Equal("dep", window.Dependency.Value);
        }

        private sealed record TestWindowRequest(string CrashDetails);

        private sealed class TestDependency
        {
            public TestDependency(string value)
            {
                Value = value;
            }

            public string Value { get; }
        }

        private sealed class TestWindow
        {
            public TestWindow(TestWindowRequest request, TestDependency dependency)
            {
                Request = request;
                Dependency = dependency;
            }

            public TestWindowRequest Request { get; }
            public TestDependency Dependency { get; }
        }
    }
}
