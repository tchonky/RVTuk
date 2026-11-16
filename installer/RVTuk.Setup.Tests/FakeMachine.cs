using System.Collections.Generic;
using System.IO;

namespace RVTuk.Setup.Tests
{
    internal sealed class FakeMachine : IMachine
    {
        private readonly string _root;
        public FakeMachine(string root) { _root = root; }

        public HashSet<string> Installed { get; set; } = new HashSet<string>();
        public HashSet<string> Running { get; set; } = new HashSet<string>();

        public string YearFolder(string year) => Path.Combine(_root, year);
        public bool IsInstalled(string year) => Installed.Contains(year);
        public bool IsRunning(string year) => Running.Contains(year);
    }
}
