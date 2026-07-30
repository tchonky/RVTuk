using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows.Threading;
using RVTuk.Core.Shared.Config;
using RVTuk.Core.TopoTools;
using RVTuk.UI.Shared.ViewModels;

namespace RVTuk.UI.TopoTools.ViewModels
{
    /// <summary>
    /// The Topo Tools pane. All three Revit interactions arrive as delegates that block on an
    /// ExternalEvent ping-pong, so all three are invoked from the thread pool and their results
    /// marshalled back through the dispatcher — never called on the WPF UI thread, which is Revit's
    /// main thread and would deadlock waiting for its own event.
    /// </summary>
    public class TopoToolsPaneViewModel : ViewModelBase
    {
        private const double DefaultSpacingMillimetres = 1000;

        private readonly Func<double, TopoScope> _discover;
        private readonly Func<double, string> _apply;
        private readonly Func<string> _setUpProject;
        private readonly Dispatcher _dispatcher;

        public TopoToolsPaneViewModel(
            Func<double, TopoScope> discover,
            Func<double, string> apply,
            Func<string> setUpProject)
        {
            _discover = discover;
            _apply = apply;
            _setUpProject = setUpProject;
            _dispatcher = Dispatcher.CurrentDispatcher;

            Lines = new ObservableCollection<TopoLineInfo>();
            SetUpCommand = new RelayCommand(RunSetUp, () => !IsBusy);
            RefreshCommand = new RelayCommand(Refresh, () => !IsBusy);
            ApplyCommand = new RelayCommand(RunApply, () => !IsBusy && IsProjectSetUp);

            try
            {
                _spacingMillimetres = ConfigManager.LoadConfig().TopoPointSpacingMillimetres;
            }
            catch
            {
                _spacingMillimetres = DefaultSpacingMillimetres;
            }

            // Reads back as 0 from a config file written before the property existed — net48's
            // DataContractJsonSerializer skips property initializers. See AppConfig.
            if (_spacingMillimetres <= 0) _spacingMillimetres = DefaultSpacingMillimetres;
        }

        public ObservableCollection<TopoLineInfo> Lines { get; }
        public RelayCommand SetUpCommand { get; }
        public RelayCommand RefreshCommand { get; }
        public RelayCommand ApplyCommand { get; }

        private double _spacingMillimetres;

        /// <summary>Millimetres, because the UI layer has no access to the document's units.</summary>
        public double SpacingMillimetres
        {
            get => _spacingMillimetres;
            set
            {
                if (value <= 0) return;
                if (Math.Abs(_spacingMillimetres - value) < 0.0001) return;

                SetProperty(ref _spacingMillimetres, value);
                SaveSpacing(value);
            }
        }

        private string _viewName = "";
        public string ViewName
        {
            get => _viewName;
            private set => SetProperty(ref _viewName, value);
        }

        private bool _isProjectSetUp = true;
        public bool IsProjectSetUp
        {
            get => _isProjectSetUp;
            private set
            {
                SetProperty(ref _isProjectSetUp, value);
                OnPropertyChanged(nameof(NeedsSetup));
            }
        }

        /// <summary>The inverse, so the setup banner can bind with the built-in converter.</summary>
        public bool NeedsSetup => !_isProjectSetUp;

        private string _statusMessage = "Open a plan view and press Refresh.";
        public string StatusMessage
        {
            get => _statusMessage;
            private set => SetProperty(ref _statusMessage, value);
        }

        private bool _isBusy;
        public bool IsBusy
        {
            get => _isBusy;
            private set => SetProperty(ref _isBusy, value);
        }

        /// <summary>Re-reads the active view's topo lines. Returns immediately.</summary>
        public void Refresh()
        {
            if (IsBusy) return;

            IsBusy = true;
            StatusMessage = "Reading this view's topo lines…";
            double spacing = _spacingMillimetres;

            Task.Run(() =>
            {
                TopoScope scope;
                try
                {
                    scope = _discover(spacing);
                }
                catch (Exception ex)
                {
                    _dispatcher.Invoke(() =>
                    {
                        StatusMessage = "Could not read this view: " + ex.Message;
                        IsBusy = false;
                    });
                    return;
                }

                _dispatcher.Invoke(() =>
                {
                    Populate(scope);
                    IsBusy = false;
                });
            });
        }

        private void Populate(TopoScope scope)
        {
            IsProjectSetUp = scope.IsProjectSetUp;
            ViewName = scope.ViewName;

            Lines.Clear();
            foreach (var line in scope.Lines) Lines.Add(line);

            StatusMessage = scope.Message;
        }

        private void RunSetUp()
        {
            IsBusy = true;
            StatusMessage = "Setting this project up…";

            Task.Run(() =>
            {
                string summary;
                try
                {
                    summary = _setUpProject();
                }
                catch (Exception ex)
                {
                    summary = "Setup failed: " + ex.Message;
                }

                _dispatcher.Invoke(() =>
                {
                    StatusMessage = summary;
                    IsBusy = false;
                    Refresh();
                });
            });
        }

        private void RunApply()
        {
            IsBusy = true;
            StatusMessage = "Applying points…";
            double spacing = _spacingMillimetres;

            Task.Run(() =>
            {
                string summary;
                try
                {
                    summary = _apply(spacing);
                }
                catch (Exception ex)
                {
                    summary = "Topo Tools failed: " + ex.Message;
                }

                _dispatcher.Invoke(() =>
                {
                    StatusMessage = summary;
                    IsBusy = false;
                    Refresh();
                });
            });
        }

        private static void SaveSpacing(double millimetres)
        {
            try
            {
                var config = ConfigManager.LoadConfig();
                config.TopoPointSpacingMillimetres = millimetres;
                ConfigManager.SaveConfig(config);
            }
            catch
            {
                // A spacing that fails to persist is not worth interrupting the user over.
            }
        }
    }
}
