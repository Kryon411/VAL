using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using VAL.Host.Services;
using VAL.ViewModels;

namespace VAL.UI.Truth
{
    public sealed class TruthHealthViewModel : INotifyPropertyChanged
    {
        private const int WarnMb = 50;
        private readonly ITruthHealthService _truthHealthService;

        private string _statusMessage = string.Empty;
        private string _chatId = "Unavailable";
        private string _relativePath = "Unavailable";
        private string _sizeText = "Unavailable";
        private string _physicalLineCount = "Unavailable";
        private string _parsedEntryCount = "Unavailable";
        private string _lastParsedLine = "Unavailable";
        private string _lastRepairUtc = "Unavailable";
        private string _bytesRemoved = "Unavailable";
        private string _advisoryText = string.Empty;

        public TruthHealthViewModel(ITruthHealthService truthHealthService)
        {
            _truthHealthService = truthHealthService;
            RefreshCommand = new RelayCommand(Refresh);
            Refresh();
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public RelayCommand RefreshCommand { get; }

        public string StatusMessage
        {
            get => _statusMessage;
            private set => SetProperty(ref _statusMessage, value);
        }

        public string ChatId
        {
            get => _chatId;
            private set => SetProperty(ref _chatId, value);
        }

        public string RelativePath
        {
            get => _relativePath;
            private set => SetProperty(ref _relativePath, value);
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

        private void Refresh()
        {
            var snapshot = _truthHealthService.GetSnapshot();

            if (!snapshot.HasChat)
            {
                StatusMessage = "No active chat session detected.";
                ChatId = "Unavailable";
                RelativePath = "Unavailable";
                SizeText = "Unavailable";
                PhysicalLineCount = "Unavailable";
                ParsedEntryCount = "Unavailable";
                LastParsedLine = "Unavailable";
                LastRepairUtc = "Unavailable";
                BytesRemoved = "Unavailable";
                AdvisoryText = string.Empty;
                return;
            }

            StatusMessage = string.Empty;
            ChatId = snapshot.ChatId;
            RelativePath = snapshot.RelativePath;

            if (snapshot.Report == null)
            {
                SizeText = "Unavailable";
                PhysicalLineCount = "Unavailable";
                ParsedEntryCount = "Unavailable";
                LastParsedLine = "Unavailable";
                LastRepairUtc = "Unavailable";
                BytesRemoved = "Unavailable";
                AdvisoryText = string.Empty;
                return;
            }

            var report = snapshot.Report;
            SizeText = FormatSize(report.Bytes);
            PhysicalLineCount = report.PhysicalLineCount.ToString(CultureInfo.InvariantCulture);
            ParsedEntryCount = report.ParsedEntryCount.ToString(CultureInfo.InvariantCulture);
            LastParsedLine = report.LastParsedPhysicalLineNumber.ToString(CultureInfo.InvariantCulture);
            LastRepairUtc = report.LastRepairUtc?.ToString("O", CultureInfo.InvariantCulture) ?? "-";
            BytesRemoved = report.LastRepairBytesRemoved?.ToString(CultureInfo.InvariantCulture) ?? "-";
            AdvisoryText = report.Bytes > WarnMb * 1024L * 1024L
                ? $"Large log (exceeds {WarnMb} MB)."
                : string.Empty;
        }

        private static string FormatSize(long bytes)
        {
            var sizeMb = bytes / (1024d * 1024d);
            return string.Format(CultureInfo.InvariantCulture, "{0:N0} bytes ({1:0.##} MB)", bytes, sizeMb);
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
