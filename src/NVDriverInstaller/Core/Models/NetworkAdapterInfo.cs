using System.ComponentModel;

namespace NVDriverInstaller.Core.Models;

public class NetworkAdapterInfo : INotifyPropertyChanged
{
    private string _name = string.Empty;
    private string _interfaceDescription = string.Empty;
    private string _macAddress = string.Empty;
    private string _adminStatus = "Down";
    private string _status = "Unknown";
    private bool _isPhysical = true;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Name
    {
        get => _name;
        set
        {
            if (_name != value)
            {
                _name = value;
                OnPropertyChanged(nameof(Name));
            }
        }
    }

    public string InterfaceDescription
    {
        get => _interfaceDescription;
        set
        {
            if (_interfaceDescription != value)
            {
                _interfaceDescription = value;
                OnPropertyChanged(nameof(InterfaceDescription));
            }
        }
    }

    public string MacAddress
    {
        get => _macAddress;
        set
        {
            if (_macAddress != value)
            {
                _macAddress = value;
                OnPropertyChanged(nameof(MacAddress));
            }
        }
    }

    public string AdminStatus
    {
        get => _adminStatus;
        set
        {
            if (_adminStatus != value)
            {
                _adminStatus = value;
                OnPropertyChanged(nameof(AdminStatus));
                OnPropertyChanged(nameof(IsUp));
            }
        }
    }

    public string Status
    {
        get => _status;
        set
        {
            if (_status != value)
            {
                _status = value;
                OnPropertyChanged(nameof(Status));
            }
        }
    }

    public bool IsPhysical
    {
        get => _isPhysical;
        set
        {
            if (_isPhysical != value)
            {
                _isPhysical = value;
                OnPropertyChanged(nameof(IsPhysical));
            }
        }
    }

    public bool IsUp => AdminStatus.Equals("Up", StringComparison.OrdinalIgnoreCase);

    protected virtual void OnPropertyChanged(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    public override string ToString() => $"{Name} ({InterfaceDescription}) - [{AdminStatus}]";
}
