using System;
using System.IO;
using System.Text;
using System.Windows.Markup;
using Xunit;

namespace RVTuk.UI.Tests.FamilyBrowser
{
    /// <summary>
    /// A tool that runs as a test: with RVTUK_FIXTURE_DIR set, writes raw-{runtime}.txt — the
    /// sample document as this runtime's XamlWriter saves it, which is exactly what today's build
    /// stores (hover style serialised). Without the variable it does nothing.
    /// </summary>
    public class InstructionFixtureWriter
    {
        [Fact]
        public void WritesThisRuntimesFixtures()
        {
            string? directory = Environment.GetEnvironmentVariable("RVTUK_FIXTURE_DIR");
            if (string.IsNullOrEmpty(directory)) return;

            Sta.Run(() =>
            {
                string xaml = XamlWriter.Save(InstructionFixtures.SampleDocument());
                File.WriteAllText(Path.Combine(directory, $"raw-{InstructionFixtures.Runtime}.txt"),
                    xaml, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            });
        }
    }
}
