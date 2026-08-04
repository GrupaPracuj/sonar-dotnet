/*
 * GP C#
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

  // Derived from the rule ID rather than kept as a hand-maintained list: a rule missing from such a list produces no
  // build failure and no error at scan time - the rule simply never reports anything, which is very hard to notice.
  private static final String CUSTOM_RULE_ID_PREFIX = "GP";

  public GpRoslynRules(PluginMetadata metadata) {
    super(metadata);
  }

  @Override
  public List<Rule> rules() {
    return super.rules().stream().filter(rule -> rule.getId().startsWith(CUSTOM_RULE_ID_PREFIX)).toList();
  }
}
