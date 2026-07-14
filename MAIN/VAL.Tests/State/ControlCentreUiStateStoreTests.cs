using System;
using System.IO;
using System.Text.Json;

using VAL.App.State;
using VAL.Host.Services;

using Xunit;

namespace VAL.Tests.State
{
    public sealed class ControlCentreUiStateStoreTests
    {
        [Fact]
        public void LoadWhenCurrentStateMissingFallsBackToLegacyDockState()
        {
            using var sandbox = new StateSandbox();
            File.WriteAllText(
                sandbox.LegacyDockStatePath,
                JsonSerializer.Serialize(new
                {
                    version = 1,
                    isOpen = true,
                    x = 144,
                    y = 88,
                    w = 720,
                    h = 520
                }));

            var store = new ControlCentreUiStateStore(sandbox.AppPaths);

            var state = store.Load();

            Assert.True(state.Dock.IsOpen);
            Assert.Equal(144, state.Dock.X);
            Assert.Equal(88, state.Dock.Y);
            Assert.Equal(720, state.Dock.W);
            Assert.Equal(520, state.Dock.H);
        }

        [Fact]
        public void SaveRemovesLegacyDockStateFile()
        {
            using var sandbox = new StateSandbox();
            File.WriteAllText(
                sandbox.LegacyDockStatePath,
                JsonSerializer.Serialize(new
                {
                    version = 1,
                    isOpen = false,
                    x = 72,
                    y = 56,
                    w = 560,
                    h = 460
                }));

            var store = new ControlCentreUiStateStore(sandbox.AppPaths);
            var state = new ControlCentreUiState
            {
                Dock = new DockGeometryState
                {
                    IsOpen = true,
                    X = 210,
                    Y = 120,
                    W = 680,
                    H = 480
                }
            };

            store.Save(state);

            Assert.False(File.Exists(sandbox.LegacyDockStatePath));
            Assert.True(File.Exists(sandbox.CurrentStatePath));
        }

        private sealed class StateSandbox : IDisposable
        {
            private readonly string _root;

            public StateSandbox()
            {
                _root = Path.Combine(Path.GetTempPath(), "val-controlcentre-state-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(_root);
                AppPaths = new TestAppPaths(_root);
            }

            public TestAppPaths AppPaths { get; }

            public string CurrentStatePath => Path.Combine(AppPaths.StateRoot, "controlcentre.ui.json");

            public string LegacyDockStatePath => Path.Combine(AppPaths.StateRoot, "dock.ui.json");

            public void Dispose()
            {
                try
                {
                    if (Directory.Exists(_root))
                        Directory.Delete(_root, recursive: true);
                }
                catch
                {
                    // Best-effort test cleanup only.
                }
            }
        }

        private sealed class TestAppPaths : IAppPaths
        {
            public TestAppPaths(string root)
            {
                var stateRoot = Path.Combine(root, "State");
                Directory.CreateDirectory(stateRoot);
                StateRoot = stateRoot;
            }

            public string ContentRoot => StateRoot;
            public string ProductRoot => StateRoot;
            public string StateRoot { get; }
            public string DataRoot => StateRoot;
            public string LogsRoot => StateRoot;
            public string ModulesRoot => StateRoot;
            public string MemoryChatsRoot => StateRoot;
            public string ProfileRoot => StateRoot;
        }
    }
}
