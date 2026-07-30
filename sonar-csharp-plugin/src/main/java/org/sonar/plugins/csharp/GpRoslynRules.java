/*
 * SonarC#
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
package org.sonar.plugins.csharp;

import java.util.List;
import org.sonarsource.dotnet.shared.plugins.PluginMetadata;
import org.sonarsource.dotnet.shared.plugins.RoslynRules;

public class GpRoslynRules extends RoslynRules {
  private static final String CUSTOM_RULE_ID = "GP0001";

  public GpRoslynRules(PluginMetadata metadata) {
    super(metadata);
  }

  @Override
  public List<Rule> rules() {
    return super.rules().stream().filter(rule -> CUSTOM_RULE_ID.equals(rule.getId())).toList();
  }
}
