using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using UserControl = System.Windows.Controls.UserControl;

namespace MSCS.Views
{
    public partial class TrackingDialogBase : UserControl
    {
        public TrackingDialogBase()
        {
            InitializeComponent();
        }

        public static readonly DependencyProperty TitleProperty =
            DependencyProperty.Register(nameof(Title), typeof(string), typeof(TrackingDialogBase), new PropertyMetadata(string.Empty));

        public static readonly DependencyProperty SearchQueryProperty =
            DependencyProperty.Register(nameof(SearchQuery), typeof(string), typeof(TrackingDialogBase), new PropertyMetadata(string.Empty));

        public static readonly DependencyProperty SearchCommandProperty =
            DependencyProperty.Register(nameof(SearchCommand), typeof(ICommand), typeof(TrackingDialogBase), new PropertyMetadata(null));

        public static readonly DependencyProperty ResultsProperty =
            DependencyProperty.Register(nameof(Results), typeof(IEnumerable), typeof(TrackingDialogBase), new PropertyMetadata(null));

        public static readonly DependencyProperty SelectedItemProperty =
            DependencyProperty.Register(nameof(SelectedItem), typeof(object), typeof(TrackingDialogBase), new PropertyMetadata(null));

        public static readonly DependencyProperty ResultItemTemplateProperty =
            DependencyProperty.Register(nameof(ResultItemTemplate), typeof(DataTemplate), typeof(TrackingDialogBase), new PropertyMetadata(null));

        public static readonly DependencyProperty SelectedContentTemplateProperty =
            DependencyProperty.Register(nameof(SelectedContentTemplate), typeof(DataTemplate), typeof(TrackingDialogBase), new PropertyMetadata(null));

        public static readonly DependencyProperty UpdateContentTemplateProperty =
            DependencyProperty.Register(nameof(UpdateContentTemplate), typeof(DataTemplate), typeof(TrackingDialogBase), new PropertyMetadata(null));

        public static readonly DependencyProperty ExistingEntrySummaryProperty =
            DependencyProperty.Register(nameof(ExistingEntrySummary), typeof(string), typeof(TrackingDialogBase), new PropertyMetadata(null));

        public static readonly DependencyProperty UpdateHeaderProperty =
            DependencyProperty.Register(nameof(UpdateHeader), typeof(string), typeof(TrackingDialogBase), new PropertyMetadata(string.Empty));

        public static readonly DependencyProperty SelectedHeaderProperty =
            DependencyProperty.Register(nameof(SelectedHeader), typeof(string), typeof(TrackingDialogBase), new PropertyMetadata("Selected series"));

        public static readonly DependencyProperty StatusMessageProperty =
            DependencyProperty.Register(nameof(StatusMessage), typeof(string), typeof(TrackingDialogBase), new PropertyMetadata(null));

        public static readonly DependencyProperty ConfirmCommandProperty =
            DependencyProperty.Register(nameof(ConfirmCommand), typeof(ICommand), typeof(TrackingDialogBase), new PropertyMetadata(null));

        public static readonly DependencyProperty CancelCommandProperty =
            DependencyProperty.Register(nameof(CancelCommand), typeof(ICommand), typeof(TrackingDialogBase), new PropertyMetadata(null));

        public string Title
        {
            get => (string)GetValue(TitleProperty);
            set => SetValue(TitleProperty, value);
        }

        public string SearchQuery
        {
            get => (string)GetValue(SearchQueryProperty);
            set => SetValue(SearchQueryProperty, value);
        }

        public ICommand SearchCommand
        {
            get => (ICommand)GetValue(SearchCommandProperty);
            set => SetValue(SearchCommandProperty, value);
        }

        public IEnumerable? Results
        {
            get => (IEnumerable?)GetValue(ResultsProperty);
            set => SetValue(ResultsProperty, value);
        }

        public object? SelectedItem
        {
            get => GetValue(SelectedItemProperty);
            set => SetValue(SelectedItemProperty, value);
        }

        public DataTemplate? ResultItemTemplate
        {
            get => (DataTemplate?)GetValue(ResultItemTemplateProperty);
            set => SetValue(ResultItemTemplateProperty, value);
        }

        public DataTemplate? SelectedContentTemplate
        {
            get => (DataTemplate?)GetValue(SelectedContentTemplateProperty);
            set => SetValue(SelectedContentTemplateProperty, value);
        }

        public DataTemplate? UpdateContentTemplate
        {
            get => (DataTemplate?)GetValue(UpdateContentTemplateProperty);
            set => SetValue(UpdateContentTemplateProperty, value);
        }

        public string? ExistingEntrySummary
        {
            get => (string?)GetValue(ExistingEntrySummaryProperty);
            set => SetValue(ExistingEntrySummaryProperty, value);
        }

        public string UpdateHeader
        {
            get => (string)GetValue(UpdateHeaderProperty);
            set => SetValue(UpdateHeaderProperty, value);
        }

        public string SelectedHeader
        {
            get => (string)GetValue(SelectedHeaderProperty);
            set => SetValue(SelectedHeaderProperty, value);
        }

        public string? StatusMessage
        {
            get => (string?)GetValue(StatusMessageProperty);
            set => SetValue(StatusMessageProperty, value);
        }

        public ICommand? ConfirmCommand
        {
            get => (ICommand?)GetValue(ConfirmCommandProperty);
            set => SetValue(ConfirmCommandProperty, value);
        }

        public ICommand? CancelCommand
        {
            get => (ICommand?)GetValue(CancelCommandProperty);
            set => SetValue(CancelCommandProperty, value);
        }

        private void ListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (ConfirmCommand?.CanExecute(null) == true)
            {
                ConfirmCommand.Execute(null);
            }
        }
    }
}