namespace VAL.App.Host.Services
{
    public interface IDataWipeService
    {
        void ScheduleWipe();
    }

    public sealed record DataWipeResult(
        bool Success,
        bool Partial,
        int DeletedTargets,
        int FailedTargets);
}
