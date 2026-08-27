namespace Onyx.ViewModels;

public sealed class SheetViewModel : ObservableObject
{
    string _title = "";
    string _message = "";
    string _primaryLabel = "";
    bool _isOpen;
    bool _isBusy;
    Func<Task>? _primary;

    public string Title { get => _title; private set => Set(ref _title, value); }
    public string Message { get => _message; set => Set(ref _message, value); }
    public string PrimaryLabel { get => _primaryLabel; private set => Set(ref _primaryLabel, value); }
    public bool IsOpen { get => _isOpen; private set => Set(ref _isOpen, value); }
    public bool IsBusy { get => _isBusy; private set => Set(ref _isBusy, value); }

    public RelayCommand PrimaryCommand { get; }
    public RelayCommand CancelCommand { get; }

    public SheetViewModel()
    {
        PrimaryCommand = new RelayCommand(async () =>
        {
            if (_primary is null) return;

            IsBusy = true;
            try
            {
                await _primary();
            }
            finally
            {
                IsBusy = false;
            }
        });

        CancelCommand = new RelayCommand(() =>
        {
            Close();
            return Task.CompletedTask;
        });
    }

    public void Show(string title, string message, string primaryLabel, Func<Task> primary)
    {
        Title = title;
        Message = message;
        PrimaryLabel = primaryLabel;
        _primary = primary;
        IsOpen = true;
    }

    public void Close()
    {
        IsOpen = false;
        _primary = null;
    }
}
