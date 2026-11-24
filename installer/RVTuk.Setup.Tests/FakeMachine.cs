using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace RVTuk.Setup.Tests
{
    internal sealed class FakeMachine : IMachine
    {
        private readonly string _root;
        public FakeMachine(string root) { _root = root; }

        public HashSet<string> Installed { get; set; } = new HashSet<string>();
        public HashSet<string> Running { get; set; } = new HashSet<string>();
        public List<byte[]> Trusted { get; } = new List<byte[]>();
        public Exception TrustError { get; set; }

        public string YearFolder(string year) => Path.Combine(_root, year);
        public bool IsInstalled(string year) => Installed.Contains(year);
        public bool IsRunning(string year) => Running.Contains(year);

        public bool TrustPublisher(byte[] certificate)
        {
            if (TrustError != null) throw TrustError;
            if (Trusted.Any(c => c.SequenceEqual(certificate))) return false;
            Trusted.Add(certificate);
            return true;
        }
    }
}
