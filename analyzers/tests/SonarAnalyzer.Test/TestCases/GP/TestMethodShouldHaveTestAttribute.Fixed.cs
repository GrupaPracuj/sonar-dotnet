using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tests.Diagnostics
{
    [TestClass]
    public class TokenValidatorTest
    {
        [TestMethod]
        public void Accepts_A_Valid_Token() { }

        [TestMethod]
        public void Rejects_An_Expired_Token() { } // Fixed
    }
}
