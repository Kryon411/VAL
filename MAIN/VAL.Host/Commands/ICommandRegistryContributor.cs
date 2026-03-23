namespace VAL.Host.Commands
{
    public interface ICommandRegistryContributor
    {
        void Register(CommandRegistry registry);
    }
}
