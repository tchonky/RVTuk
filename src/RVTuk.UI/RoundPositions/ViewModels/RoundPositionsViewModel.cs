using RVTuk.UI.Shared.ViewModels;

namespace RVTuk.UI.RoundPositions.ViewModels
{
    public class RoundPositionsViewModel : ViewModelBase
    {
        private bool _allowHalfCentimetres = true;

        /// <summary>On: the grid is 0.5 cm. Off: 1 cm, and 0.5 rounds up to 1.</summary>
        public bool AllowHalfCentimetres
        {
            get => _allowHalfCentimetres;
            set => SetProperty(ref _allowHalfCentimetres, value);
        }
    }
}
