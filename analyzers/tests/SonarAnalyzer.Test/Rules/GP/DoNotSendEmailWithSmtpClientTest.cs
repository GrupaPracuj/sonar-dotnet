/*
 * GP analyzers for SonarAnalyzer .NET
 * Copyright (C) Grupa Pracuj
 *
 * Part of a fork of SonarAnalyzer for .NET; see LICENSE.txt at the root of this
 * repository for the terms that apply.
 */

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
                public System.Threading.Tasks.Task SendMailAsync(string message) => null;
            }
        }

        namespace System.Web.Mail
        {
            public static class SmtpMail
            {
                public static void Send(string message) { }
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
    public void DoNotSendEmailWithSmtpClient_NoncompliantOnceForLocalConstructionAndSend() =>
        builder.AddSnippet(
            Stubs + """

            public class Notifier
            {
                public void Notify(string body)
                {
                    var client = new System.Net.Mail.SmtpClient("smtp.internal"); // Noncompliant {{Send email through Juno's email sender instead of 'SmtpClient'.}}
                    client.Send(body);
                }
            }
            """)
            .Verify();

    [TestMethod]
    public void DoNotSendEmailWithSmtpClient_NoncompliantOnceForParenthesizedConstructionAndSend() =>
        builder.AddSnippet(
            Stubs + """

            public class Notifier
            {
                public void Notify(string body) =>
                    (new System.Net.Mail.SmtpClient("smtp.internal")).Send(body); // Noncompliant {{Send email through Juno's email sender instead of 'SmtpClient'.}}
            }
            """)
            .Verify();

    [TestMethod]
    public void DoNotSendEmailWithSmtpClient_NoncompliantForInjectedSmtpClient() =>
        builder.AddSnippet(
            Stubs + """

            public class Notifier
            {
                private readonly System.Net.Mail.SmtpClient _client;

                public System.Threading.Tasks.Task Notify(string body) =>
                    _client.SendMailAsync(body); // Noncompliant {{Send email through Juno's email sender instead of 'SmtpClient'.}}
            }
            """)
            .Verify();

    [TestMethod]
    public void DoNotSendEmailWithSmtpClient_NoncompliantForLegacySmtpMail() =>
        builder.AddSnippet(
            Stubs + """

            public class Notifier
            {
                public void Notify(string body) =>
                    System.Web.Mail.SmtpMail.Send(body); // Noncompliant {{Send email through Juno's email sender instead of 'SmtpMail'.}}
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
