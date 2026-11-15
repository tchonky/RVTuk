using System;
using System.IO;
using System.Text;
using System.Windows.Markup;
using RVTuk.UI.Shared.Controls;
using Xunit;

namespace RVTuk.UI.Tests.FamilyBrowser
{
    /// <summary>
    /// A tool that runs as a test: with RVTUK_FIXTURE_DIR set, writes raw-{runtime}.txt — the
    /// sample document as this runtime's XamlWriter saves it, which is exactly what builds before
    /// the fix stored (hover style serialised) — and saved-{runtime}.txt, the sample document as
    /// RichTextBoxHelper.SerializeDocument stores it now. Without the variable it does nothing.
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
                Write($"raw-{InstructionFixtures.Runtime}.txt", XamlWriter.Save(InstructionFixtures.SampleDocument()));
                string? saved = RichTextBoxHelper.SerializeDocument(InstructionFixtures.SampleDocument());
                Assert.NotNull(saved);
                Write($"saved-{InstructionFixtures.Runtime}.txt", saved);
            });

            void Write(string name, string xaml) =>
                File.WriteAllText(Path.Combine(directory, name), xaml, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
    }
}
