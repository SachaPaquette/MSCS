namespace MSCS.Interfaces
{
    public interface ITrackingDialogViewModel : IDisposable
    {
        string ProviderId { get; }

        string DisplayName { get; }

        event EventHandler<bool>? CloseRequested;
    }
}