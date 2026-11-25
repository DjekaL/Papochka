using System.ComponentModel;
using System.Text.Json.Serialization;
using System.Windows.Media;

namespace Papochka {
    public class AppItem : INotifyPropertyChanged {
        public string Name { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;

        private ImageSource? _icon;

        public bool IsTransferring { get; set; }

        [JsonIgnore]
        public ImageSource? Icon {
            get => _icon;
            set {
                if (_icon != value) {
                    _icon = value;
                    OnPropertyChanged(nameof(Icon));
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged(string propertyName) {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
