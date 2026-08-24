[CmdletBinding()]
Param(
    [String]$RspecRoot = "analyzers\rspec"
)

Set-StrictMode -version 2.0
$ErrorActionPreference = "Stop"

# The allowed values below mirror the enums that org.sonarsource.analyzer.commons.RuleMetadataLoader
# feeds every rule JSON into. An unknown value makes RulesRegistrant throw on the server, which stops
# SonarQube during startup instead of just skipping the rule - so it has to be caught before packaging.
#
# Re-derive them after a sonar.plugin.api.version bump (version is in pom.xml):
#   javap -cp <~/.m2>/org/sonarsource/api/plugin/sonar-plugin-api/<ver>/sonar-plugin-api-<ver>.jar `
#         org.sonar.api.issue.impact.SoftwareQuality
# Same for org.sonar.api.issue.impact.Severity, org.sonar.api.rules.CleanCodeAttribute,
# org.sonar.api.rules.RuleType and org.sonar.api.rule.RuleStatus.
$ValidSoftwareQualities = @("MAINTAINABILITY", "RELIABILITY", "SECURITY")
$ValidImpactSeverities = @("INFO", "LOW", "MEDIUM", "HIGH", "BLOCKER")
$ValidCleanCodeAttributes = @("CONVENTIONAL", "FORMATTED", "IDENTIFIABLE", "CLEAR", "COMPLETE", "EFFICIENT",
                              "LOGICAL", "DISTINCT", "FOCUSED", "MODULAR", "TESTED", "LAWFUL", "RESPECTFUL",
                              "TRUSTWORTHY")
$ValidRuleTypes = @("CODE_SMELL", "BUG", "VULNERABILITY", "SECURITY_HOTSPOT")
$ValidRuleStatuses = @("BETA", "DEPRECATED", "READY", "REMOVED")
$ValidScopes = @("MAIN", "TEST", "TESTS", "ALL")            # RuleMetadataLoader maps TESTS onto RuleScope.TEST
$ValidGpScopes = @("MAIN", "TESTS")                         # AGENTS.md: GP rules are Main or Tests, never All
$ValidDefaultSeverities = @("INFO", "MINOR", "MAJOR", "CRITICAL", "BLOCKER")
$TagPattern = "^[a-z0-9+#\-.]+$"

# RuleMetadataLoader upper-cases these before hitting valueOf, so "ready" and "Main" are legal spellings.
# The clean code section is not upper-cased: impact qualities and severities must already be upper case.
$UpperCasedByLoader = $True

$Errors = New-Object System.Collections.Generic.List[String]
$FileCount = 0

function HasBom($Path) {
    $Stream = [System.IO.File]::OpenRead($Path)
    try {
        $Head = New-Object byte[] 3
        $Read = $Stream.Read($Head, 0, 3)
        return ($Read -eq 3) -and ($Head[0] -eq 0xEF) -and ($Head[1] -eq 0xBB) -and ($Head[2] -eq 0xBF)
    }
    finally {
        $Stream.Dispose()
    }
}

function Prop($Object, $Name) {
    $Property = $Object.PSObject.Properties[$Name]
    If ($null -eq $Property) {
        return $null
    }
    return $Property.Value
}

function AddError($RuleKey, $Message) {
    $Errors.Add("${RuleKey}: $Message")
}

function CheckEnum($RuleKey, $Field, $Value, $Allowed, $CaseInsensitive) {
    If ($null -eq $Value) {
        return
    }
    $Candidate = If ($CaseInsensitive) { ([String]$Value).ToUpperInvariant() } else { [String]$Value }
    If ($Allowed -notcontains $Candidate) {
        AddError $RuleKey "$Field '$Value' is not one of $($Allowed -join ', ')"
    }
}

function CheckRemediation($RuleKey, $Remediation) {
    If ($null -eq $Remediation) {
        AddError $RuleKey "missing 'remediation'"
        return
    }
    $Func = Prop $Remediation "func"
    If ($null -eq $Func) {
        AddError $RuleKey "remediation is missing 'func'"
        return
    }
    If ($Func.StartsWith("Constant")) {
        If ($null -eq (Prop $Remediation "constantCost")) {
            AddError $RuleKey "remediation func '$Func' requires 'constantCost'"
        }
    }
    ElseIf ($Func.StartsWith("Linear")) {
        If ($null -eq (Prop $Remediation "linearFactor")) {
            AddError $RuleKey "remediation func '$Func' requires 'linearFactor'"
        }
        If ($Func -eq "Linear with offset" -and $null -eq (Prop $Remediation "linearOffset")) {
            AddError $RuleKey "remediation func '$Func' requires 'linearOffset'"
        }
    }
    Else {
        AddError $RuleKey "unknown remediation func '$Func' (expected 'Constant/Issue', 'Linear' or 'Linear with offset')"
    }
}

function CheckCleanCode($RuleKey, $Code) {
    If ($null -eq $Code) {
        return    # A handful of upstream rules predate the clean code taxonomy and carry no 'code' section.
    }
    CheckEnum $RuleKey "code.attribute" (Prop $Code "attribute") $ValidCleanCodeAttributes $False

    $Impacts = Prop $Code "impacts"
    If ($null -eq $Impacts) {
        AddError $RuleKey "code section is missing 'impacts'"
        return
    }
    $Qualities = @($Impacts.PSObject.Properties)
    If ($Qualities.Count -eq 0) {
        AddError $RuleKey "code.impacts is empty"
    }
    ForEach ($Quality in $Qualities) {
        If ($ValidSoftwareQualities -notcontains $Quality.Name) {
            AddError $RuleKey "code.impacts has unknown software quality '$($Quality.Name)' (expected $($ValidSoftwareQualities -join ', '))"
        }
        If ($ValidImpactSeverities -notcontains [String]$Quality.Value) {
            AddError $RuleKey "code.impacts.$($Quality.Name) severity '$($Quality.Value)' is not one of $($ValidImpactSeverities -join ', ')"
        }
    }
}

function CheckRule($JsonPath) {
    $RuleKey = [System.IO.Path]::GetFileNameWithoutExtension($JsonPath)
    $Relative = Resolve-Path -Relative $JsonPath

    If (HasBom $JsonPath) {
        AddError $RuleKey "$Relative starts with a UTF-8 BOM"
    }

    Try {
        $Metadata = [System.IO.File]::ReadAllText($JsonPath) | ConvertFrom-Json
    }
    Catch {
        AddError $RuleKey "$Relative is not valid JSON: $($_.Exception.Message)"
        return
    }

    $SqKey = Prop $Metadata "sqKey"
    If ($SqKey -ne $RuleKey) {
        AddError $RuleKey "sqKey '$SqKey' does not match the file name"
    }

    $Title = Prop $Metadata "title"
    If ([String]::IsNullOrWhiteSpace($Title)) {
        AddError $RuleKey "missing or empty 'title'"
    }

    CheckEnum $RuleKey "type" (Prop $Metadata "type") $ValidRuleTypes $UpperCasedByLoader
    CheckEnum $RuleKey "status" (Prop $Metadata "status") $ValidRuleStatuses $UpperCasedByLoader
    CheckEnum $RuleKey "scope" (Prop $Metadata "scope") $ValidScopes $UpperCasedByLoader
    If ($RuleKey.StartsWith("GP")) {
        CheckEnum $RuleKey "scope (GP rule)" (Prop $Metadata "scope") $ValidGpScopes $UpperCasedByLoader
    }
    CheckEnum $RuleKey "defaultSeverity" (Prop $Metadata "defaultSeverity") $ValidDefaultSeverities $UpperCasedByLoader

    ForEach ($Field in @("type", "status", "scope", "defaultSeverity")) {
        If ($null -eq (Prop $Metadata $Field)) {
            AddError $RuleKey "missing '$Field'"
        }
    }

    ForEach ($Tag in @(Prop $Metadata "tags")) {
        If ($Tag -notmatch $TagPattern) {
            AddError $RuleKey "tag '$Tag' does not match $TagPattern"
        }
    }

    CheckRemediation $RuleKey (Prop $Metadata "remediation")
    CheckCleanCode $RuleKey (Prop $Metadata "code")

    # The description is a sibling .html; a missing or empty one makes the loader fail the same way.
    $HtmlPath = [System.IO.Path]::ChangeExtension($JsonPath, ".html")
    If (-Not (Test-Path $HtmlPath)) {
        AddError $RuleKey "no description file next to $Relative"
    }
    Else {
        If (HasBom $HtmlPath) {
            AddError $RuleKey "$([System.IO.Path]::GetFileName($HtmlPath)) starts with a UTF-8 BOM"
        }
        If ([String]::IsNullOrWhiteSpace([System.IO.File]::ReadAllText($HtmlPath))) {
            AddError $RuleKey "$([System.IO.Path]::GetFileName($HtmlPath)) is empty"
        }
    }
}

Push-Location "${PSScriptRoot}\.."  # Run everything from the root of the repository
try {
    If (-Not (Test-Path $RspecRoot)) {
        throw "Rule metadata directory '$RspecRoot' not found."
    }

    ForEach ($Json in Get-ChildItem $RspecRoot -Recurse -Filter *.json -File) {
        If ($Json.Name -eq "Sonar_way_profile.json") {
            continue
        }
        $FileCount++
        CheckRule $Json.FullName
    }

    If ($Errors.Count -gt 0) {
        Write-Host "Rule metadata validation failed - $($Errors.Count) problem(s) in $FileCount rule(s):"
        ForEach ($Problem in $Errors) {
            Write-Host "  $Problem"
        }
        exit 1
    }

    Write-Host "Rule metadata OK ($FileCount rules)"
    exit 0
}
catch {
    Write-Host $_
    Write-Host $_.Exception
    Write-Host $_.ScriptStackTrace
    exit 1
}
finally {
    Pop-Location
}
