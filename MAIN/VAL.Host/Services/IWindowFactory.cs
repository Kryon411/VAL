namespace VAL.Host.Services
{
    public interface IWindowFactory<TWindow>
        where TWindow : class
    {
        TWindow Create();
    }

    public interface IWindowFactory<TWindow, in TArgument>
        where TWindow : class
    {
        TWindow Create(TArgument argument);
    }
}
