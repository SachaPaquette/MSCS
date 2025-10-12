namespace MSCS.ViewModels
{
    public class MainMenuTab
    {
        public MainMenuTab(string key, string title, string iconGlyph, BaseViewModel viewModel)
        {
            Key = key;
            Title = title;
            IconGlyph = iconGlyph;
            ViewModel = viewModel;
        }

        public string Key { get; }
        public string Title { get; }
        public string IconGlyph { get; }
        public BaseViewModel ViewModel { get; }
    }
}