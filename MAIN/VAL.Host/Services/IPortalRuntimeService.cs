using System;

namespace VAL.Host.Services
{
    public interface IPortalRuntimeService
    {
        void Initialize(Action focusControl);
        void AttachWindow(IntPtr hwnd);
    }
}
