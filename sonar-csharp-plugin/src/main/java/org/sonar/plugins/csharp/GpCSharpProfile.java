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

import org.sonar.api.server.profile.BuiltInQualityProfilesDefinition;
import org.sonarsource.dotnet.shared.plugins.PluginMetadata;
import org.sonarsource.dotnet.shared.plugins.RoslynRules;

/**
 * Built-in quality profile activating GP rules intended for organization-wide use.
 *
 * <p>Deliberately <em>not</em> named "Sonar way": that name belongs to the official C# plugin's profile, and this
 * plugin contributes an additional rule repository alongside it rather than replacing it. A separate name keeps the
 * two independent - copy or extend this profile instead of hand-picking rules, and newly added GP rules are active
 * without anyone having to remember to tick them.
 */
public class GpCSharpProfile implements BuiltInQualityProfilesDefinition {

  static final String PROFILE_NAME = "GP way";

  private final PluginMetadata metadata;
  private final RoslynRules roslynRules;

  public GpCSharpProfile(PluginMetadata metadata, GpRoslynRules roslynRules) {
    this.metadata = metadata;
    this.roslynRules = roslynRules;
  }

  @Override
  public void define(Context context) {
    var profile = context.createBuiltInQualityProfile(PROFILE_NAME, metadata.languageKey());
    for (var rule : roslynRules.rules()) {
      profile.activateRule(metadata.repositoryKey(), rule.getId());
    }
    profile.done();
  }
}
