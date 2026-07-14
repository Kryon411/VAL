using System;

namespace VAL.App.Host.Services
{
    public interface IDeferredActionFactory
    {
        IDeferredAction Create(TimeSpan interval, Action callback);
    }
}
