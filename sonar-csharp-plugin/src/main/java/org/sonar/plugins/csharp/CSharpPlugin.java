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
import org.sonar.api.Plugin;
import org.sonar.api.config.PropertyDefinition;
import org.sonar.api.utils.ManifestUtils;
import org.sonarsource.csharp.core.CSharpCorePluginMetadata;
import org.sonarsource.dotnet.shared.plugins.PluginMetadata;

public class CSharpPlugin implements Plugin {

  // Do NOT add any public fields here, and do NOT reference them directly. Add them to PluginMetadata and inject the metadata.
  static final PluginMetadata METADATA = new CSharpPluginMetadata();

  // The key SonarQube actually registered this plugin under. Plugin keys may only be alphanumeric, so the
  // packaging step silently strips the hyphen from the "GP-csharp" key configured in pom.xml - the two must
  // be kept in sync manually, since nothing fails loudly if they drift apart.
  private static final String INSTALLED_PLUGIN_KEY = "GPcsharp";

  // Prefix used for the "companion Roslyn analyzer" properties below, and for the rule repository key
  // (see CSharpPluginMetadata.repositoryKey()). This is the mechanism documented/implemented by the
  // SonarQube Roslyn SDK (https://github.com/SonarSource/sonarqube-roslyn-sdk) for a plugin that supplies an
  // *additional* Roslyn analyzer alongside the official "csharp" plugin, instead of trying to replace it:
  // SonarScanner for .NET (RoslynAnalyzerProvider.CreatePlugins) collects, for every ACTIVE rule whose
  // repository key starts with "roslyn.", the properties "<repoKeyWithoutPrefix>.pluginKey" /
  // ".pluginVersion" / ".staticResourceName", and downloads the referenced plugin resource in addition to
  // the primary language analyzer - no collision with "sonar.cs.analyzer.dotnet.pluginKey", because the
  // property names are namespaced under our own key instead of the shared "sonar.cs." prefix.
  private static final String ROSLYN_PROPERTY_PREFIX = "GPcsharp.cs";

  @Override
  public void define(Context context) {
    var version = pluginVersion();
    context.addExtensions(
      GpCSharpRulesDefinition.class,
      GpCSharpProfile.class,
      METADATA,
      GpRoslynRules.class,
      PropertyDefinition.builder(ROSLYN_PROPERTY_PREFIX + ".pluginKey").defaultValue(INSTALLED_PLUGIN_KEY).hidden().build(),
      PropertyDefinition.builder(ROSLYN_PROPERTY_PREFIX + ".pluginVersion").defaultValue(version).hidden().build(),
      PropertyDefinition.builder(ROSLYN_PROPERTY_PREFIX + ".staticResourceName").defaultValue("SonarAnalyzer-GP-csharp-" + version + ".zip").hidden().build());
  }

  private static String pluginVersion() {
    List<String> propertyValues = ManifestUtils.getPropertyValues(CSharpPlugin.class.getClassLoader(), "Plugin-Version");
    return propertyValues.isEmpty() ? "Version-N/A" : propertyValues.iterator().next();
  }

  private static class CSharpPluginMetadata extends CSharpCorePluginMetadata {

    @Override
    public String pluginKey() {
      return "GP-csharp";
    }

    @Override
    public String repositoryKey() {
      // Must start with "roslyn." and match ROSLYN_PROPERTY_PREFIX above (repositoryKey() = "roslyn." + ROSLYN_PROPERTY_PREFIX).
      return "roslyn." + ROSLYN_PROPERTY_PREFIX;
    }

    @Override
    public String analyzerProjectName() {
      return "SonarAnalyzer.CSharp";
    }

    @Override
    public String resourcesDirectory() {
      return "/org/sonar/plugins/csharp";
    }
  }
}
