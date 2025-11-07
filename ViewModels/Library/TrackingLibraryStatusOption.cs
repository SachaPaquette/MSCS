namespace MSCS.ViewModels
{
    public interface ITrackingLibraryStatusOption
    {
        string DisplayName { get; }

        object StatusValue { get; }
    }

    public sealed class TrackingLibraryStatusOption<TStatus> : ITrackingLibraryStatusOption
    {
        public TrackingLibraryStatusOption(TStatus value, string displayName)
        {
            Value = value;
            DisplayName = displayName;
        }

        public TStatus Value { get; }

        public string DisplayName { get; }

        public object StatusValue => Value!;
    }
}