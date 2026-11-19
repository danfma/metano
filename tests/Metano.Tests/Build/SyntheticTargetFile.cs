namespace Metano.Tests.Build;

/// <summary>
/// A test-only <c>Metano.Build.Fake{Prefix}.targets</c> (FR-087.2): a second language target over
/// the shared <c>Metano.Build.Common.targets</c>, following the registration contract of
/// ADR-0029 exactly as a real per-target file does. Its "tool" is <c>echo</c>, which prints the
/// target name and the resolved <c>FilePrefix</c>; the target writes the stamp itself.
/// </summary>
public static class SyntheticTargetFile
{
    /// <summary>The language tag a synthetic target registers, matched by <c>MetanoAsset Language</c>.</summary>
    public static string LanguageOf(string prefix) => $"fake-{prefix}";

    /// <summary>The file name of the synthetic target with <paramref name="prefix"/>.</summary>
    public static string FileNameOf(string prefix) => $"Metano.Build.Fake{prefix}.targets";

    /// <summary>The content of the synthetic target whose settings use <c>MetanoFake{prefix}</c>.</summary>
    public static string ContentOf(string prefix)
    {
        var language = LanguageOf(prefix);

        return $$"""
            <Project>
              <Import
                Project="$(MSBuildThisFileDirectory)Metano.Build.Common.targets"
                Condition="'$(_MetanoBuildCommonImported)' != 'true'"
              />
              <ItemGroup>
                <_MetanoBuildCommonCopy
                  Include="$(MSBuildThisFileDirectory)Metano.Build.Common.targets"
                  ContentHash="$([MSBuild]::StableStringHash($([System.IO.File]::ReadAllText('$(MSBuildThisFileDirectory)Metano.Build.Common.targets'))))"
                />
              </ItemGroup>
              <PropertyGroup>
                <_MetanoResolveTargetsDependsOn>$(_MetanoResolveTargetsDependsOn);_MetanoResolveFake{{prefix}}</_MetanoResolveTargetsDependsOn>
                <_MetanoTranspileTargets>$(_MetanoTranspileTargets);MetanoTranspileFake{{prefix}}</_MetanoTranspileTargets>
              </PropertyGroup>
              <Target Name="_MetanoResolveFake{{prefix}}">
                <PropertyGroup>
                  <_Fake{{prefix}}OutputDir>$(MetanoOutputDir)</_Fake{{prefix}}OutputDir>
                  <_Fake{{prefix}}OutputDir Condition="'$(MetanoFake{{prefix}}OutputDir)' != ''">$(MetanoFake{{prefix}}OutputDir)</_Fake{{prefix}}OutputDir>
                  <_Fake{{prefix}}Enabled>$(MetanoEnabled)</_Fake{{prefix}}Enabled>
                  <_Fake{{prefix}}Enabled Condition="'$(MetanoFake{{prefix}}Enabled)' != ''">$(MetanoFake{{prefix}}Enabled)</_Fake{{prefix}}Enabled>
                  <_Fake{{prefix}}Enabled Condition="'$(_Fake{{prefix}}Enabled)' == ''">true</_Fake{{prefix}}Enabled>
                  <_Fake{{prefix}}FilePrefix>$(MetanoFilePrefix)</_Fake{{prefix}}FilePrefix>
                  <_Fake{{prefix}}FilePrefix Condition="'$(MetanoFake{{prefix}}FilePrefix)' != ''">$(MetanoFake{{prefix}}FilePrefix)</_Fake{{prefix}}FilePrefix>
                  <_Fake{{prefix}}PostBuildCommand>$(MetanoPostBuildCommand)</_Fake{{prefix}}PostBuildCommand>
                  <_Fake{{prefix}}PostBuildCommand Condition="'$(MetanoFake{{prefix}}PostBuildCommand)' != ''">$(MetanoFake{{prefix}}PostBuildCommand)</_Fake{{prefix}}PostBuildCommand>
                  <_Fake{{prefix}}Active Condition="'$(_Fake{{prefix}}Enabled)' == 'true' and '$(_Fake{{prefix}}OutputDir)' != ''">true</_Fake{{prefix}}Active>
                  <_Fake{{prefix}}KeyFile>$(IntermediateOutputPath)fake-{{prefix}}.cache</_Fake{{prefix}}KeyFile>
                </PropertyGroup>
                <ItemGroup Condition="'$(_Fake{{prefix}}Active)' == 'true'">
                  <_MetanoEnabledTarget
                    Include="{{language}}"
                    OutputDir="$(_Fake{{prefix}}OutputDir)"
                    OutputDirFull="$([MSBuild]::NormalizePath('$(MSBuildProjectDirectory)', '$(_Fake{{prefix}}OutputDir)').TrimEnd('/\'))"
                    ToolchainKeyFile="$(_Fake{{prefix}}KeyFile)"
                    CompilerProject=""
                    ToolStoreName="fake-{{prefix}}"
                    PostBuildCommand="$([MSBuild]::Escape('$(_Fake{{prefix}}PostBuildCommand)'))"
                  />
                </ItemGroup>
              </Target>
              <Target Name="MetanoTranspileFake{{prefix}}">
                <CallTarget Targets="_MetanoPrepare;_MetanoTranspileFake{{prefix}}IfOutOfDate" />
              </Target>
              <Target
                Name="_MetanoTranspileFake{{prefix}}IfOutOfDate"
                Condition="'$(_Fake{{prefix}}Active)' == 'true'"
                Inputs="@(_MetanoUserCompile);@(MetanoAsset);$(MSBuildProjectFullPath);$(_Fake{{prefix}}KeyFile)"
                Outputs="$(_Fake{{prefix}}OutputDir)/.metano-stamp"
              >
                <PropertyGroup>
                  <_Fake{{prefix}}AssetArgs>@(_MetanoAssetArg->WithMetadataValue('Language', '{{language}}'), ' ')</_Fake{{prefix}}AssetArgs>
                  <_Fake{{prefix}}Args>FAKE-{{prefix}} &quot;[$(_Fake{{prefix}}FilePrefix)]&quot; $(_Fake{{prefix}}AssetArgs)</_Fake{{prefix}}Args>
                </PropertyGroup>
                <MSBuild
                  Projects="$(MSBuildProjectFullPath)"
                  Targets="_MetanoTranspileWorker"
                  Properties="_MetanoWorkerOutputDir=$([MSBuild]::Escape('$(_Fake{{prefix}}OutputDir)'));_MetanoWorkerCommand=echo;_MetanoWorkerArguments=$([MSBuild]::Escape('$(_Fake{{prefix}}Args)'));_MetanoWorkerProbeTool=false"
                />
                <MakeDir Directories="$(_Fake{{prefix}}OutputDir)" />
                <Touch Files="$(_Fake{{prefix}}OutputDir)/.metano-stamp" AlwaysCreate="true" />
              </Target>
            </Project>
            """;
    }
}
