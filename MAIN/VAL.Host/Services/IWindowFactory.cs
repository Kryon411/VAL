namespace VAL.Host.Services
{
    public interface IWindowFactory<TWindow>
        where TWindow : class
    {
        TWindow Create();
    }
}
