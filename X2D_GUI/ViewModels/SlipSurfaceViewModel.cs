using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace GeotechSlopeApp.ViewModels;

public class SlipSurfaceViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
