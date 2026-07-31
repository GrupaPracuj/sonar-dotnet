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

import com.sonarsource.scanner.engine.sensor.test.fixtures.TestSonarRuntime;
import org.junit.jupiter.api.BeforeAll;
import org.junit.jupiter.api.Test;
import org.sonar.api.SonarEdition;
import org.sonar.api.SonarQubeSide;
import org.sonar.api.SonarRuntime;
import org.sonar.api.server.rule.RulesDefinition;
import org.sonar.api.utils.Version;

import static org.assertj.core.api.Assertions.assertThat;

class CSharpRulesDefinitionTest {
  private static final RulesDefinition.Context CONTEXT = new RulesDefinition.Context();
  private static final SonarRuntime SONAR_RUNTIME = TestSonarRuntime.forSonarQube(Version.create(10, 10), SonarQubeSide.SCANNER,
    SonarEdition.COMMUNITY);
  private static final GpRoslynRules ROSLYN_RULES = new GpRoslynRules(CSharpPlugin.METADATA);

  private static RulesDefinition.Repository ruleRepo;

  @BeforeAll
  static void setupContext() {
    GpCSharpRulesDefinition definition = new GpCSharpRulesDefinition(CSharpPlugin.METADATA, SONAR_RUNTIME, ROSLYN_RULES);
    definition.define(CONTEXT);
    ruleRepo = CONTEXT.repository("gp-csharpsquid");
  }

  @Test
  void rules_areDefined() {
    assertThat(CONTEXT.repositories()).hasSize(1);
    assertThat(ruleRepo.name()).isEqualTo("GP C#");
    RulesDefinition.Rule gp0001 = ruleRepo.rule("GP0001");
    RulesDefinition.Rule gp0002 = ruleRepo.rule("GP0002");
    RulesDefinition.Rule gp0003 = ruleRepo.rule("GP0003");
    RulesDefinition.Rule gp0004 = ruleRepo.rule("GP0004");
    RulesDefinition.Rule gp0005 = ruleRepo.rule("GP0005");
    RulesDefinition.Rule gp0006 = ruleRepo.rule("GP0006");
    assertThat(gp0001).isNotNull();
    assertThat(gp0002).isNotNull();
    assertThat(gp0003).isNotNull();
    assertThat(gp0004).isNotNull();
    assertThat(gp0005).isNotNull();
    assertThat(gp0006).isNotNull();
    assertThat(gp0001.name()).isEqualTo("The word 'abrakadabra' should not appear in C# source files");
  }

  @Test
  void builtInRules_areNotDefined() {
    assertThat(CONTEXT.repositories()).hasSize(1);
    assertThat(ruleRepo.rule("S100")).isNull();
    assertThat(ruleRepo.rule("S2259")).isNull();
    assertThat(ruleRepo.rules()).hasSize(6);
  }

  @Test
  void allRules_haveMetadata() {
    for (RulesDefinition.Rule rule : ruleRepo.rules()) {
      assertThat(rule.name()).isNotEmpty();
      assertThat(rule.type()).isNotNull();
      assertThat(rule.status()).isNotNull();
      assertThat(rule.severity()).isNotEmpty();
    }
  }

  @Test
  void allRules_haveHtmlDescription() {
    for (RulesDefinition.Rule rule : ruleRepo.rules()) {
      assertThat(rule.htmlDescription()).isNotEmpty().hasSizeGreaterThan(100);
    }
  }
}
