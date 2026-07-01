using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Threading;
using RVTuk.Core.AreaSubmission;

namespace RVTuk.UI.ViewModels
{
    public enum SubmissionPane { Config, Areas }

    /// <summary>
    /// View model for the Area Calc (Rishui Zamin) window. Top toolbar switches the bottom pane
    /// between the Config fields and the area tree; Revit behaviour is injected as delegates so
    /// this project takes no Revit dependency.
    /// </summary>
    public class AreaSubmissionViewModel : ViewModelBase
    {
        private readonly Func<IReadOnlyList<(long Id, AreaRecord Rec)>> _extract;
        private readonly Action<long> _selectInModel;
        private readonly Func<IReadOnlyList<AreaRecord>, AreaSubmissionConfig, (bool ok, string msg)> _export;
        private readonly Dispatcher _dispatcher;

        private SubmissionPane _currentPane = SubmissionPane.Config;
        private AreaRowViewModel? _selectedRow;
        private bool _isRefreshing;

        /// <param name="extract">Reads the areas on the open sheet (id + record).</param>
        /// <param name="selectInModel">Selects an Area in the model by element id.</param>
        /// <param name="export">Validates + writes the DXF/DAT for the given records; returns (ok, message).</param>
        public AreaSubmissionViewModel(
            Func<IReadOnlyList<(long Id, AreaRecord Rec)>> extract,
            Action<long> selectInModel,
            Func<IReadOnlyList<AreaRecord>, AreaSubmissionConfig, (bool ok, string msg)> export)
        {
            _extract = extract;
            _selectInModel = selectInModel;
            _export = export;
            _dispatcher = Dispatcher.CurrentDispatcher;

            ShowConfigCommand = new RelayCommand(() => CurrentPane = SubmissionPane.Config);
            RefreshCommand    = new RelayCommand(Refresh);
            ExportCommand     = new RelayCommand(Export);
            BrowseOutputCommand = new RelayCommand(BrowseOutput);
        }

        public AreaSubmissionConfig Config { get; } = new AreaSubmissionConfig();
        public ObservableCollection<AreaLevelGroupViewModel> Levels { get; } = new();

        public ICommand ShowConfigCommand { get; }
        public ICommand RefreshCommand { get; }
        public ICommand ExportCommand { get; }
        public ICommand BrowseOutputCommand { get; }

        /// <summary>Raised after an export attempt so the window can show a result dialog.</summary>
        public event Action<bool, string>? ExportCompleted;

        public SubmissionPane CurrentPane
        {
            get => _currentPane;
            set
            {
                SetProperty(ref _currentPane, value);
                OnPropertyChanged(nameof(IsConfigPane));
                OnPropertyChanged(nameof(IsAreasPane));
            }
        }

        public bool IsConfigPane => _currentPane == SubmissionPane.Config;
        public bool IsAreasPane  => _currentPane == SubmissionPane.Areas;

        /// <summary>Notifying wrapper over <see cref="AreaSubmissionConfig.OutputFolder"/> so the
        /// Browse button can update the bound textbox.</summary>
        public string OutputFolder
        {
            get => Config.OutputFolder;
            set { if (Config.OutputFolder != value) { Config.OutputFolder = value; OnPropertyChanged(); } }
        }

        public AreaRowViewModel? SelectedRow
        {
            get => _selectedRow;
            set
            {
                SetProperty(ref _selectedRow, value);
                if (value != null)
                {
                    try { _selectInModel(value.ElementId); }
                    catch { /* selection is best-effort; never crash the window */ }
                }
            }
        }

        // Extraction goes through a Revit ExternalEvent ping-pong that blocks the calling thread
        // until Revit's main thread services it. This window lives ON the main thread, so we must
        // run the extract off-thread (else the main thread blocks waiting for itself) and marshal
        // the results back to the Dispatcher — same pattern as the Family Browser's Sync.
        private void Refresh()
        {
            if (_isRefreshing) return;   // a second click while one runs would just double-populate
            _isRefreshing = true;
            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                IReadOnlyList<(long Id, AreaRecord Rec)> extracted;
                try { extracted = _extract(); }
                catch (Exception ex)
                {
                    _dispatcher.Invoke(() =>
                    {
                        _isRefreshing = false;
                        ExportCompleted?.Invoke(false, "Could not read the open sheet: " + ex.Message);
                    });
                    return;
                }

                var groups = extracted
                    .GroupBy(e => e.Rec.Level ?? string.Empty)
                    .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(g =>
                    {
                        var vm = new AreaLevelGroupViewModel(g.Key);
                        foreach (var (id, rec) in g.OrderBy(e => e.Rec.Number, StringComparer.OrdinalIgnoreCase))
                            vm.Rows.Add(new AreaRowViewModel(id, rec));
                        return vm;
                    })
                    .ToList();

                _dispatcher.Invoke(() =>
                {
                    _isRefreshing = false;
                    Levels.Clear();
                    foreach (var g in groups) Levels.Add(g);
                    CurrentPane = SubmissionPane.Areas;
                });
            });
        }

        private void Export()
        {
            var records = Levels.SelectMany(g => g.Rows).Select(r => r.Record).ToList();
            if (records.Count == 0)
            {
                ExportCompleted?.Invoke(false, "No areas loaded — click Refresh first.");
                return;
            }
            var (ok, msg) = _export(records, Config);
            ExportCompleted?.Invoke(ok, msg);
        }

        private void BrowseOutput()
        {
            using var dialog = new FolderBrowserDialog
            {
                Description = "Select the output folder for the .dxf / .dat files",
                SelectedPath = System.IO.Directory.Exists(OutputFolder) ? OutputFolder : string.Empty
            };
            if (dialog.ShowDialog() == DialogResult.OK)
                OutputFolder = dialog.SelectedPath;
        }
    }
}
