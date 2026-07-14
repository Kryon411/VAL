using System.Windows.Controls;
using System.Windows.Threading;

namespace VAL.App.Host.Services
{
    internal sealed class DesktopToastInstance
    {
        public DesktopToastInstance(Border view, string? groupKey)
        {
            View = view;
            GroupKey = groupKey;
        }

        public string? GroupKey { get; }

        public DispatcherTimer? Timer { get; set; }

        public Border View { get; }
    }
}
