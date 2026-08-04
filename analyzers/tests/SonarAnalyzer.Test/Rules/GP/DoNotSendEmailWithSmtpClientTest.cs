using CS = SonarAnalyzer.CSharp.Rules;

namespace SonarAnalyzer.Test.Rules.GP;

[TestClass]
public class DoNotSendEmailWithSmtpClientTest
{
    private readonly VerifierBuilder builder = new VerifierBuilder<CS.DoNotSendEmailWithSmtpClient>();

    private const string Stubs =
        """
        namespace System.Net.Mail
        {
            public class SmtpClient
            {
                public SmtpClient(string host) { }
                public void Send(string message) { }
            }
        }
        """;

    [TestMethod]
    public void DoNotSendEmailWithSmtpClient_NoncompliantForSmtpClient() =>
        builder.AddSnippet(
            Stubs + """

            public class Notifier
            {
                public void Notify(string body) =>
                    new System.Net.Mail.SmtpClient("smtp.internal").Send(body); // Noncompliant {{Send email through Juno's email sender instead of 'SmtpClient'.}}
            }
            """)
            .Verify();

    [TestMethod]
    public void DoNotSendEmailWithSmtpClient_CompliantForOtherTypes() =>
        builder.AddSnippet(
            Stubs + """

            public class Notifier
            {
                public void Notify(string body) => System.Console.WriteLine(body);
            }
            """)
            .VerifyNoIssues();
}
