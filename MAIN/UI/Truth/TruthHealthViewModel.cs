using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using VAL.Host.Services;
using VAL.ViewModels;

namespace VAL.UI.Truth
{
    public sealed class TruthHealthViewModel : INotifyPropertyChanged
    {
        private readonly ITruthHealthReportService _reportService;

        private string _chatId = string.Empty;
        private string _truthPath = string.Empty;
        private string _sizeText = string.Empty;
        private string _physicalLineCount = string.Empty;
        private string _parsedEntryCount = string.Empty;
        private string _lastParsedLine = string.Empty;
        private string _lastRepairUtc = string.Empty;
        private string _bytesRemoved = string.Empty;
        private string _advisoryText = string.Empty;
        private string _statusMessage = string.Empty;

        public TruthHealthViewModel(ITruthHealthReportService reportService)
        {
            _reportService = reportService;
            RefreshCommand = new RelayCommand(Refresh);
            Refresh();
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public ICommand RefreshCommand { get; }

        public string ChatId
        {
            get => _chatId;
            private set => SetProperty(ref _chatId, value);
        }

        public string TruthPath
        {
            get => _truthPath;
            private set => SetProperty(ref _truthPath, value);
        }

        public string SizeText
        {
            get => _sizeText;
            private set => SetProperty(ref _sizeText, value);
        }

        public string PhysicalLineCount
        {
            get => _physicalLineCount;
            private set => SetProperty(ref _physicalLineCount, value);
        }

        public string ParsedEntryCount
        {
            get => _parsedEntryCount;
            private set => SetProperty(ref _parsedEntryCount, value);
        }

        public string LastParsedLine
        {
            get => _lastParsedLine;
            private set => SetProperty(ref _lastParsedLine, value);
        }

        public string LastRepairUtc
        {
            get => _lastRepairUtc;
            private set => SetProperty(ref _lastRepairUtc, value);
        }

        public string BytesRemoved
        {
            get => _bytesRemoved;
            private set => SetProperty(ref _bytesRemoved, value);
        }

        public string AdvisoryText
        {
            get => _advisoryText;
            private set => SetProperty(ref _advisoryText, value);
        }

        public string StatusMessage
        {
            get => _statusMessage;
            private set => SetProperty(ref _statusMessage, value);
        }

        private void Refresh()
        {
            var result = _reportService.GetCurrentSnapshot();
            StatusMessage = result.StatusMessage;

            if (!result.HasActiveChat || result.Snapshot == null)
            {
                ChatId = result.ChatId;
                TruthPath = "-";
                SizeText = "Unavailable";
                PhysicalLineCount = "Unavailable";
                ParsedEntryCount = "Unavailable";
                LastParsedLine = "Unavailable";
                LastRepairUtc = "Unavailable";
                BytesRemoved = "Unavailable";
                AdvisoryText = string.Empty;
                return;
            }

            var report = result.Snapshot.Report;
            ChatId = report.ChatId;
            TruthPath = result.Snapshot.RelativeTruthPath;

            var sizeMb = report.Bytes / (1024d * 1024d);
            SizeText = string.Format(CultureInfo.InvariantCulture, "{0:N0} bytes ({1:0.##} MB)", report.Bytes, sizeMb);

            PhysicalLineCount = report.PhysicalLineCount.ToString(CultureInfo.InvariantCulture);
            ParsedEntryCount = report.ParsedEntryCount.ToString(CultureInfo.InvariantCulture);
            LastParsedLine = report.LastParsedPhysicalLineNumber.ToString(CultureInfo.InvariantCulture);
            LastRepairUtc = report.LastRepairUtc?.ToString("O", CultureInfo.InvariantCulture) ?? "-";
            BytesRemoved = report.LastRepairBytesRemoved?.ToString(CultureInfo.InvariantCulture) ?? "-";
            AdvisoryText = result.Snapshot.IsLargeLog ? "Large log" : string.Empty;
        }

        private void SetProperty<T>(ref T storage, T value, [CallerMemberName] string? propertyName = null)
        {
            if (Equals(storage, value))
                return;

            storage = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
