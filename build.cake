//////////////////////////////////////////////////////////////////////
// TOOLS
//////////////////////////////////////////////////////////////////////
#tool "nuget:?package=Newtonsoft.Json"
#tool "nuget:?package=OctopusTools&version=4.38.1"
#addin "Cake.Http"

using Path = System.IO.Path;
using IO = System.IO;
using System.Text.RegularExpressions;
using Cake.Common.Tools;
using Newtonsoft.Json.Linq;
using System.Net;

//////////////////////////////////////////////////////////////////////
// ARGUMENTS
//////////////////////////////////////////////////////////////////////
var target = Argument("target", "Default");
var configuration = Argument("configuration", "Release");
var package = Argument("package", string.Empty);

///////////////////////////////////////////////////////////////////////////////
// GLOBAL VARIABLES
///////////////////////////////////////////////////////////////////////////////
var localPackagesDir = "../LocalPackages";
var buildDir = @".\build";
var unpackFolder = Path.Combine(buildDir, "temp");
var unpackFolderFullPath = Path.GetFullPath(unpackFolder);
var artifactsDir = @".\artifacts";
var nugetVersion = string.Empty;
var nugetPackageFile = string.Empty;
var file = string.Empty;

///////////////////////////////////////////////////////////////////////////////
// SETUP / TEARDOWN
///////////////////////////////////////////////////////////////////////////////
Setup(context =>
{

});

Teardown(context =>
{
    Information("Finished running tasks.");
});

//////////////////////////////////////////////////////////////////////
// TASKS
//////////////////////////////////////////////////////////////////////

Task("Clean")
    .Does(() =>
{
    CleanDirectory(unpackFolder);
    CleanDirectory(buildDir);
    CleanDirectory(artifactsDir);
    if (FileExists("./Octopus.Dependencies.AzureCLI.temp.nuspec"))
    {
        DeleteFile("./Octopus.Dependencies.AzureCLI.temp.nuspec");
    }

});

Task("Restore-Source-Package")
    .IsDependentOn("Clean")
    .Does(() =>
{
    var request = (HttpWebRequest)WebRequest.Create("https://aka.ms/installazurecliwindows");
    request.AllowAutoRedirect = false;
    string url;
    using (var response = request.GetResponse())
    {
        url = response.Headers[HttpResponseHeader.Location];
    }

    var uri = new Uri(url);
    file = Path.GetFileName(uri.LocalPath);

    Information($"Downloading {url}");
    var outputPath = File($"{buildDir}/{file}");

    DownloadFile(url, outputPath);
});

Task("Unpack-Source-Package")
    .IsDependentOn("Restore-Source-Package")
    .IsDependentOn("Clean")
    .Does(() =>
{
    var sourcePackage = file;

    Information($"Unpacking {sourcePackage}");

    var processArgumentBuilder = new ProcessArgumentBuilder();
    processArgumentBuilder.Append($"/a {sourcePackage}");
    processArgumentBuilder.Append("/qn");
    processArgumentBuilder.Append($"TARGETDIR={unpackFolderFullPath}");
    var processSettings = new ProcessSettings { Arguments = processArgumentBuilder, WorkingDirectory = buildDir };
    StartProcess("msiexec.exe", processSettings);
    Information($"Unpacked {sourcePackage} to {unpackFolderFullPath}");
});

Task("GetVersion")
    .IsDependentOn("Unpack-Source-Package")
    .Does(() =>
{
    Information("Determining version number");

    var regexMatch = Regex.Match(file, @"azure-cli-(?<Version>[\d\.]*).msi");
    nugetVersion = regexMatch.Groups["Version"].Value;
    Information($"Calculated version number: {nugetVersion}");

    if(BuildSystem.IsRunningOnTeamCity) {
        BuildSystem.TeamCity.SetBuildNumber(nugetVersion);
    }
});


Task("Pack")
    .IsDependentOn("GetVersion")
    .Does(() =>
{
    Information($"Building Octopus.Dependencies.AzureCLI v{nugetVersion}");
    
    var processArgumentBuilder = new ProcessArgumentBuilder();
    processArgumentBuilder.Append($"pack");
    processArgumentBuilder.Append("--id Octopus.Dependencies.AzureCLI");
    processArgumentBuilder.Append($"--version {nugetVersion}");
    processArgumentBuilder.Append("--format nupkg");
    processArgumentBuilder.Append($"--outfolder {artifactsDir}");
    processArgumentBuilder.Append($"--basePath {unpackFolderFullPath}");
    processArgumentBuilder.Append($"--author Octopus Deploy");
    processArgumentBuilder.Append($"--title Octopus.Dependencies.AzureCLI");
    processArgumentBuilder.Append($"--description Nuget package of Azure Powershell CLI");
    var processSettings = new ProcessSettings { Arguments = processArgumentBuilder };
    StartProcess(@".\tools\OctopusTools.4.38.1\tools\Octo.exe", processSettings);

});

Task("Publish")
    .WithCriteria(BuildSystem.IsRunningOnTeamCity)
    .IsDependentOn("Pack")
    .Does(() =>
{
    NuGetPush($"{artifactsDir}/Octopus.Dependencies.AzureCLI.{nugetVersion}.nupkg", new NuGetPushSettings {
        Source = "https://octopus.myget.org/F/octopus-dependencies/api/v3/index.json",
        ApiKey = EnvironmentVariable("MyGetApiKey")
    });
});

Task("CopyToLocalPackages")
    .WithCriteria(BuildSystem.IsLocalBuild)
    .IsDependentOn("Pack")
    .Does(() =>
{
    CreateDirectory(localPackagesDir);
    CopyFileToDirectory(Path.Combine(artifactsDir, $"Octopus.Dependencies.AzureCLI.{nugetVersion}.nupkg"), localPackagesDir);
});

//////////////////////////////////////////////////////////////////////
// TASK TARGETS
//////////////////////////////////////////////////////////////////////

Task("FullChain")
    .IsDependentOn("Clean")
    .IsDependentOn("Restore-Source-Package")
    .IsDependentOn("Unpack-Source-Package")
    .IsDependentOn("GetVersion")
    .IsDependentOn("Pack")
    .IsDependentOn("Publish")
    .IsDependentOn("CopyToLocalPackages");

Task("Default").IsDependentOn("FullChain");

//////////////////////////////////////////////////////////////////////
// EXECUTION
//////////////////////////////////////////////////////////////////////

RunTarget(target);