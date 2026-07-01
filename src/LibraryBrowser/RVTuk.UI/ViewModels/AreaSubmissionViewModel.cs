using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Forms;
using System.Windows.Input;
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
        private readonly Func<AreaSubmissionConfig, (bool ok, string msg)> _export;

        private SubmissionPane _currentPane = SubmissionPane.Config;
        private AreaRowViewModel? _selectedRow;

        /// <param name="extract">Reads the areas on the open sheet (id + record).</param>
        /// <param name="selectInModel">Selects an Area in the model by element id.</param>
        /// <param name="export">Validates + writes the DXF/DAT; returns (ok, message).</param>
        public AreaSubmissionViewModel(
            Func<IReadOnlyList<(long Id, AreaRecord Rec)>> extract,
            Action<long> selectInModel,
            Func<AreaSubmissionConfig, (bool ok, string msg)> export)
        {
            _extract = extract;
            _selectInModel = selectInModel;
            _export = export;

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

        private void Refresh()
        {
            Levels.Clear();
            IReadOnlyList<(long Id, AreaRecord Rec)> extracted;
            try { extracted = _extract(); }
            catch (Exception ex) { ExportCompleted?.Invoke(false, "Could not read the open sheet: " + ex.Message); return; }

            foreach (var grp in extracted
                        .GroupBy(e => e.Rec.Level ?? string.Empty)
                        .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
            {
                var groupVm = new AreaLevelGroupViewModel(grp.Key);
                foreach (var (id, rec) in grp.OrderBy(e => e.Rec.Number, StringComparer.OrdinalIgnoreCase))
                    groupVm.Rows.Add(new AreaRowViewModel(id, rec));
                Levels.Add(groupVm);
            }

            CurrentPane = SubmissionPane.Areas;
        }

        private void Export()
        {
            var (ok, msg) = _export(Config);
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
