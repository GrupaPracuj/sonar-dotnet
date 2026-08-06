using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tests.Diagnostics
{
    [TestClass]
    public class TokenValidatorTest
    {
        [TestMethod]
        public void Accepts_A_Valid_Token() { }

        public void Rejects_An_Expired_Token() { } // Noncompliant {{Add a test attribute to 'Rejects_An_Expired_Token' or make it private - as it stands it never runs.}}
    }
}
