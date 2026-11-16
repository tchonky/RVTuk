namespace RVTuk.Core.TopoTools
{
    /// <summary>Composing Revit's or an exception's message into a sentence of our own.</summary>
    public static class Sentence
    {
        /// <summary>
        /// The message without its closing full stop (and trailing whitespace), so a line that adds
        /// its own "." after it does not end in "..".
        /// </summary>
        public static string WithoutFullStop(string message) =>
            (message ?? "").TrimEnd(' ', '\t', '\r', '\n', '.');
    }
}
