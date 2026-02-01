namespace VAL.Host.Services
{
    public interface IDataWipeService
    {
        DataWipeResult WipeData();
    }

    public sealed record DataWipeResult(
        bool Success,
        bool Partial,
        int DeletedTargets,
        int FailedTargets);
}
