using System.ComponentModel;

namespace LanRemoteControl.Models
{
    // 被控端：已连接客户端
    public class ClientInfo : INotifyPropertyChanged
    {
        private string _mode = "";
        public string Ip { get; set; } = "";
        public string Mode
        {
            get => _mode;
            set { _mode = value; OnPropertyChanged(nameof(Mode)); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    // 远控端：文件传输任务
    public class FileTask : INotifyPropertyChanged
    {
        private string _status = "Sending";
        public string Name { get; set; } = "";
        public string SizeDisplay { get; set; } = "";
        public string Status
        {
            get => _status;
            set { _status = value; OnPropertyChanged(nameof(Status)); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
