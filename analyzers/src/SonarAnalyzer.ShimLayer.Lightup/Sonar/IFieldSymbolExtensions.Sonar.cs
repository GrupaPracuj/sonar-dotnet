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

namespace StyleCop.Analyzers.Lightup;

// The name carries the "Sonar" suffix because the vendored StyleCop lightup already declares IFieldSymbolExtensions,
// the same way INamedTypeSymbolExtensionsSonar does.
public static class IFieldSymbolExtensionsSonar
{
    private static readonly Func<IFieldSymbol, bool> IsRequiredAccessor;

    static IFieldSymbolExtensionsSonar()
    {
        IsRequiredAccessor = LightupHelpers.CreateSyntaxPropertyAccessor<IFieldSymbol, bool>(typeof(IFieldSymbol), nameof(IsRequired));
    }

    public static bool IsRequired(this IFieldSymbol fieldSymbol) =>
        IsRequiredAccessor(fieldSymbol);
}
