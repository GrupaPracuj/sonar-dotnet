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

import com.sonarsource.scanner.engine.sensor.test.fixtures.TestSonarRuntime;
import org.junit.jupiter.api.BeforeAll;
import org.junit.jupiter.api.Test;
import org.sonar.api.SonarEdition;
import org.sonar.api.SonarQubeSide;
import org.sonar.api.SonarRuntime;
import org.sonar.api.rule.RuleScope;
import org.sonar.api.server.rule.RulesDefinition;
import org.sonar.api.utils.Version;
import org.sonarsource.dotnet.shared.plugins.RoslynRules;

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
    ruleRepo = CONTEXT.repository("roslyn.GPcsharp.cs");
  }

  // Asserted against GpRoslynRules rather than a second hand-maintained list, so adding a rule cannot leave the two
  // out of sync - which is exactly the failure mode that makes a rule silently report nothing.
  @Test
  void rules_areDefined() {
    assertThat(CONTEXT.repositories()).hasSize(1);
    assertThat(ruleRepo.name()).isEqualTo("GP C#");
    assertThat(ROSLYN_RULES.rules()).isNotEmpty();
    for (RoslynRules.Rule rule : ROSLYN_RULES.rules()) {
      assertThat(ruleRepo.rule(rule.getId())).as("rule " + rule.getId()).isNotNull();
    }
    assertThat(ruleRepo.rules()).hasSize(ROSLYN_RULES.rules().size());
    assertThat(ruleRepo.rule("GP0001").name()).isEqualTo("The word 'abrakadabra' should not appear in C# source files");
  }

  @Test
  void everyRule_hasGpId() {
    assertThat(ruleRepo.rules()).allSatisfy(rule -> assertThat(rule.key()).startsWith("GP"));
  }

  @Test
  void builtInRules_areNotDefined() {
    assertThat(CONTEXT.repositories()).hasSize(1);
    assertThat(ruleRepo.rule("S100")).isNull();
    assertThat(ruleRepo.rule("S2259")).isNull();
  }

  // Proves the securityStandards block in the rule metadata is actually picked up, which is what puts these rules
  // into SonarQube's CWE-based security reports.
  @Test
  void securityRules_exposeTheirCwe() {
    assertThat(ruleRepo.rule("GP0028").securityStandards()).contains("cwe:918");
    assertThat(ruleRepo.rule("GP0029").securityStandards()).contains("cwe:502");
    assertThat(ruleRepo.rule("GP0030").securityStandards()).contains("cwe:117");
    assertThat(ruleRepo.rule("GP0031").securityStandards()).contains("cwe:601");
    assertThat(ruleRepo.rule("GP0020").securityStandards()).contains("cwe:862");
  }

  // Main code unless the rule is about the tests themselves, where running on main code would find nothing.
  @Test
  void everyRule_isScopedToMainOrTestCode() {
    assertThat(ruleRepo.rules()).allSatisfy(rule -> assertThat(rule.scope()).isIn(RuleScope.MAIN, RuleScope.TEST));
    assertThat(ruleRepo.rule("GP0041").scope()).isEqualTo(RuleScope.TEST);
    assertThat(ruleRepo.rule("GP0042").scope()).isEqualTo(RuleScope.MAIN);
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
