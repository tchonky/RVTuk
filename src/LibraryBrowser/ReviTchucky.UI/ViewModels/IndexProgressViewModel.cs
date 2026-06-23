using System;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace ReviTchucky.UI.ViewModels
{
    public class IndexProgressViewModel : ViewModelBase
    {
        private int _progressValue;
        private string _currentFileName = string.Empty;
        private int _currentCount;
        private int _totalCount;
        private string _elapsedTime = "00:00:00";
        private string _remainingTime = "--:--:--";
        private bool _isRunning;

        private readonly DispatcherTimer _timer;
        private DateTime _startTime;
        private CancellationTokenSource? _cts;

        public int ProgressValue { get => _progressValue; set => SetProperty(ref _progressValue, value); }
        public string CurrentFileName { get => _currentFileName; set => SetProperty(ref _currentFileName, value); }
        public int CurrentCount { get => _currentCount; set => SetProperty(ref _currentCount, value); }
        public int TotalCount { get => _totalCount; set => SetProperty(ref _totalCount, value); }
        public string ElapsedTime { get => _elapsedTime; set => SetProperty(ref _elapsedTime, value); }
        public string RemainingTime { get => _remainingTime; set => SetProperty(ref _remainingTime, value); }
        public bool IsRunning { get => _isRunning; set => SetProperty(ref _isRunning, value); }

        public ICommand CancelCommand { get; }

        public event Action? RebuildRequested;

        public ICommand RebuildIndexCommand { get; }

        public IndexProgressViewModel()
        {
            CancelCommand = new RelayCommand(Cancel, () => IsRunning);
            RebuildIndexCommand = new RelayCommand(RequestRebuild, () => !IsRunning);

            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick += (_, _) =>
            {
                ElapsedTime = (DateTime.UtcNow - _startTime).ToString(@"hh\:mm\:ss");
                UpdateEta();
            };
        }

        private void UpdateEta()
        {
            if (_currentCount <= 0 || _totalCount <= 0 || _currentCount >= _totalCount)
            {
                RemainingTime = _currentCount >= _totalCount && _totalCount > 0 ? "00:00:00" : "--:--:--";
                return;
            }
            var elapsed = DateTime.UtcNow - _startTime;
            var remaining = TimeSpan.FromTicks(elapsed.Ticks / _currentCount * (_totalCount - _currentCount));
            RemainingTime = remaining.ToString(@"hh\:mm\:ss");
        }

        public CancellationToken Start()
        {
            _cts = new CancellationTokenSource();
            _startTime = DateTime.UtcNow;
            RemainingTime = "--:--:--";
            IsRunning = true;
            _timer.Start();
            return _cts.Token;
        }

        public void UpdateProgress(string fileName, int current, int total)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                CurrentFileName = fileName;
                CurrentCount = current;
                TotalCount = total;
                ProgressValue = total > 0 ? (int)(100.0 * current / total) : 0;
                UpdateEta();
            });
        }

        public void Finish()
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                _timer.Stop();
                IsRunning = false;
            });
        }

        private void Cancel()
        {
            _cts?.Cancel();
        }

        private void RequestRebuild()
        {
            RebuildRequested?.Invoke();
        }
    }
}
