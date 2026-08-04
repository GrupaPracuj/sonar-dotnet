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

import org.junit.jupiter.api.BeforeAll;
import org.junit.jupiter.api.Test;
import org.sonar.api.rule.RuleKey;
import org.sonar.api.server.profile.BuiltInQualityProfilesDefinition.BuiltInQualityProfile;
import org.sonar.api.server.profile.BuiltInQualityProfilesDefinition.Context;

import static org.assertj.core.api.Assertions.assertThat;

class GpCSharpProfileTest {

  private static final String REPOSITORY_KEY = CSharpPlugin.METADATA.repositoryKey();
  private static final GpRoslynRules ROSLYN_RULES = new GpRoslynRules(CSharpPlugin.METADATA);

  private static BuiltInQualityProfile profile;

  @BeforeAll
  static void setup() {
    Context context = new Context();
    new GpCSharpProfile(CSharpPlugin.METADATA, ROSLYN_RULES).define(context);
    profile = context.profile("cs", GpCSharpProfile.PROFILE_NAME);
  }

  @Test
  void profile_isDefined() {
    assertThat(profile).isNotNull();
    assertThat(profile.language()).isEqualTo("cs");
  }

  @Test
  void everyGpRule_isActive() {
    assertThat(profile.rules()).hasSize(ROSLYN_RULES.rules().size());
    for (var rule : ROSLYN_RULES.rules()) {
      assertThat(profile.rule(RuleKey.of(REPOSITORY_KEY, rule.getId()))).as("rule " + rule.getId()).isNotNull();
    }
  }

  @Test
  void builtInRules_areNotActive() {
    assertThat(profile.rule(RuleKey.of(REPOSITORY_KEY, "S100"))).isNull();
    assertThat(profile.rule(RuleKey.of(REPOSITORY_KEY, "S2259"))).isNull();
  }
}
