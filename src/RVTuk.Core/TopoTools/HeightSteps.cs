namespace RVTuk.Core.TopoTools
{
    /// <summary>
    /// A Pick in Steps run: each clicked line takes the height of the line clicked before it plus
    /// one step (the Height box's move, "+0.1" or "-0.1"). The first line clicked starts the run and
    /// keeps its own height.
    /// </summary>
    public sealed class HeightSteps
    {
        private readonly HeightBoxInput _step;

        private HeightSteps(HeightBoxInput step) { _step = step; }

        /// <summary>The height the run is at, as a label reads; null until it starts.</summary>
        public string? Last { get; private set; }

        /// <summary>What the next clicked line's label becomes; null before the start, or when the
        /// step would take it past the heights a label may hold.</summary>
        public string? NextLabel => Last == null ? null : _step.NewLabel(Last);

        /// <summary>A run from what was typed: only a move ("+0.1", "-0.1") makes steps.</summary>
        public static bool TryCreate(string? typed, out HeightSteps steps)
        {
            steps = null!;
            if (!HeightBoxInput.TryParse(typed, out var input) || input.IsAbsolute) return false;
            steps = new HeightSteps(input);
            return true;
        }

        /// <summary>Starts the run at the first line's height; false when its label does not read.</summary>
        public bool Start(string? label)
        {
            if (!SurveyHeightText.TryParse(label, out double feet)) return false;
            Last = SurveyHeightText.Format(feet);
            return true;
        }

        /// <summary>The next line now carries <paramref name="label"/>: the run goes on from it.
        /// Called only once Revit has kept the change, so a rolled-back click does not advance.</summary>
        public void Accept(string label) => Last = label;
    }
}
