using System;
using System.IO;
using Xunit;

namespace RVTuk.Setup.Tests
{
    public sealed class TrustTests : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "rvtuk-trust-tests-" + Guid.NewGuid().ToString("N"));
        private readonly FakeMachine _machine;

        public TrustTests()
        {
            Directory.CreateDirectory(_dir);
            _machine = new FakeMachine(Path.Combine(_dir, "m"));
        }

        public void Dispose() { Directory.Delete(_dir, true); }

        private void Cer(params byte[] bytes) => File.WriteAllBytes(Path.Combine(_dir, Engine.SigningCertificateFile), bytes);

        [Fact]
        public void APayloadWithTheCertificate_HasTheMachineTrustIt()
        {
            Cer(1, 2, 3);
            var r = Engine.TrustPublisher(_dir, _machine, null);
            Assert.Equal(Outcome.Installed, r!.Outcome);
            Assert.Equal(new byte[] { 1, 2, 3 }, Assert.Single(_machine.Trusted));
        }

        [Fact]
        public void ACertificateTrustedAlready_IsUnchanged()
        {
            Cer(1, 2, 3);
            Engine.TrustPublisher(_dir, _machine, null);
            Assert.Equal(Outcome.Unchanged, Engine.TrustPublisher(_dir, _machine, null)!.Outcome);
            Assert.Single(_machine.Trusted);
        }

        [Fact]
        public void APayloadWithoutACertificate_TrustsNothing()
        {
            Assert.Null(Engine.TrustPublisher(_dir, _machine, null));
            Assert.Empty(_machine.Trusted);
        }

        [Fact]
        public void AStoreThatRefuses_IsAFailureThatSaysWhy()
        {
            Cer(1, 2, 3);
            _machine.TrustError = new UnauthorizedAccessException("access denied");
            var r = Engine.TrustPublisher(_dir, _machine, null);
            Assert.Equal(Outcome.Failed, r!.Outcome);
            Assert.Contains("access denied", r.Message);
            Assert.True(Engine.Failed(new[] { r }));
        }
    }
}
