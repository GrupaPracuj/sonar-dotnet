/*
 * SonarAnalyzer for .NET
 * Copyright (C) SonarSource Sàrl
 * mailto:info AT sonarsource DOT com
 *
 * You can redistribute and/or modify this program under the terms of
 * the Sonar Source-Available License Version 1, as published by SonarSource Sàrl.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
 * See the Sonar Source-Available License for more details.
 *
 * You should have received a copy of the Sonar Source-Available License
 * along with this program; if not, see https://sonarsource.com/license/ssal/
 */

using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace SonarAnalyzer.CSharp.Rules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class AbrakadabraWord : DiagnosticAnalyzer
{
    internal const string DiagnosticId = "GP0001";
    private const string Keyword = "abrakadabra";
    private const string MessageFormat = "Remove the word 'abrakadabra' from the code.";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        "'abrakadabra' should not appear in source code",
        MessageFormat,
        "GP C#",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxTreeAction(Analyze);
    }

    private static void Analyze(SyntaxTreeAnalysisContext context)
    {
        var text = context.Tree.GetText(context.CancellationToken);
        foreach (var line in text.Lines)
        {
            var lineText = line.ToString();
            var index = lineText.IndexOf(Keyword, StringComparison.OrdinalIgnoreCase);
            if (index >= 0)
            {
                var location = Location.Create(context.Tree, new TextSpan(line.Start + index, Keyword.Length));
                context.ReportDiagnostic(Diagnostic.Create(Rule, location));
            }
        }
    }
}
