using System.ComponentModel;

namespace MyNewsFeeder.Models
{
    public class Feed : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        private string _name;
        private string _url;
        private bool _isEnabled = true;

        public string Name
        {
            get => _name;
            set
            {
                _name = value;
                OnPropertyChanged(nameof(Name));
            }
        }

        public string Url
        {
            get => _url;
            set
            {
                _url = value;
                OnPropertyChanged(nameof(Url));
            }
        }

        public bool IsEnabled
        {
            get => _isEnabled;
            set
            {
                _isEnabled = value;
                OnPropertyChanged(nameof(IsEnabled));
                System.Diagnostics.Debug.WriteLine($"Feed '{Name}' IsEnabled changed to: {value}");
            }
        }

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
