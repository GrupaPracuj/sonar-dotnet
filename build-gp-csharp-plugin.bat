@echo off
setlocal

pushd "%~dp0"
if errorlevel 1 exit /b 1

for /f %%i in ('powershell -NoProfile -Command "Get-Date -Format yyyyMMddHHmmss"') do set "BUILD_NUMBER=%%i"
set "PLUGIN_VERSION=10.31.0.%BUILD_NUMBER%"

echo Building GP CSharp plugin version %PLUGIN_VERSION%
echo.

echo [1/3] Building SonarAnalyzer.CSharp (Release)...
dotnet build "analyzers\src\SonarAnalyzer.CSharp\SonarAnalyzer.CSharp.csproj" -c Release -t:Rebuild
if errorlevel 1 goto :error

echo [2/3] Refreshing packaged analyzer binary...
copy /Y "analyzers\src\SonarAnalyzer.CSharp\bin\Release\netstandard2.0\SonarAnalyzer.CSharp.dll" "analyzers\packaging\binaries\SonarAnalyzer.CSharp\SonarAnalyzer.CSharp.dll" >nul
if errorlevel 1 goto :error

echo [3/3] Building GP CSharp plugin JAR...
call mvn -pl sonar-csharp-plugin -am -Drevision=%PLUGIN_VERSION% -DskipTests -Dlicense.skip=true clean package
if errorlevel 1 goto :error

echo.
echo Plugin ready:
echo %~dp0sonar-csharp-plugin\target\gp-sonar-csharp-plugin-%PLUGIN_VERSION%.jar
popd
exit /b 0

:error
echo.
echo Build failed.
popd
exit /b 1
