using NUnit.Framework;
using Toolbelt.Blazor.I18nText.Test.Internals;
using static Toolbelt.Diagnostics.XProcess;

namespace Toolbelt.Blazor.I18nText.Test;

[Parallelizable(ParallelScope.All)]
public class BuildTest
{
    private static readonly IEnumerable<string> HostingModels = ["Client", "Host", "Server"];

    private static readonly IEnumerable<string> Frameworks = ["net6.0", "net8.0"];

    public static readonly IEnumerable<object[]> Projects =
        from startupProjName in HostingModels
        from framework in Frameworks
        select new object[] { startupProjName, framework };

    private static readonly IEnumerable<string> ExpectedTextResJsonFromPackages = [
        "Lib4PackRef.I18nText.Text.en.json",
        "Lib4PackRef.I18nText.Text.ja.json",
        "Lib4PackRef6.I18nText.Text.en.json",
        "Lib4PackRef6.I18nText.Text.ja.json"];

    private static readonly IEnumerable<string> ExpectedTextResJsonFromProjects = [
        "Lib4ProjRef.I18nText.Text.en.json",
        "Lib4ProjRef.I18nText.Text.ja.json",
        "SampleSite.Components.I18nText.Text.en.json",
        "SampleSite.Components.I18nText.Text.ja.json"];

    [Test, TestCaseSource(nameof(Projects))]
    public async Task BasicBuildTest(string startupProjName, string framework)
    {
        // Given
        using var workSpace = new WorkSpace(startupProjName, framework, configuration: "Debug");
        var distDir = Path.Combine(workSpace.OutputDir, Path.Combine("wwwroot/_content/i18ntext".Split('/')));

        // When
        await Start("dotnet", $"build -f:{framework}", workSpace.StartupProj).ExitCodeIs(0);

        // Then

        // 1. The only text resource json files from projects have been generated under the output folder.
        // (The text resource json files from packages won't copied to the output folder.
        // They are referenced by the ASP.NET Core static web assets provider from the package cache folder, such as %HOME%/.nuget/packages. )
        var textResJsonFileNames = Directory.GetFiles(distDir, "*.*")
            .Select(Path.GetFileName)
            .Order();

        textResJsonFileNames
            .Is(ExpectedTextResJsonFromProjects);

        // 2. The static web assets json have been generated, and it includes all text resource json file entries.
        var assetJsonPath = Path.Combine(workSpace.OutputDir, $"SampleSite.{startupProjName}.staticwebassets.runtime.json");
        File.Exists(assetJsonPath).IsTrue(message: $"The static web assets json file \"{assetJsonPath}\" was not found.");
        dynamic assets = JsonToDynamicConverter.Parse(File.ReadAllText(assetJsonPath));
        var textResJsonFileEntries = (IDictionary<string, object?>)assets.Root.Children._content.Children.i18ntext.Children;

        textResJsonFileEntries.Keys.Order()
            .Is(ExpectedTextResJsonFromPackages.Concat(ExpectedTextResJsonFromProjects).Order());
    }

    [Test, TestCaseSource(nameof(Projects))]
    public async Task PublishTest(string startupProjName, string framework)
    {
        // Given
        using var workSpace = new WorkSpace(startupProjName, framework, configuration: "Release");
        var wwwrootContentDir = Path.Combine(workSpace.PublishDir, "wwwroot", "_content");
        var i18nDistDir = Path.Combine(wwwrootContentDir, "i18ntext");
        var staticWebAssetDir = Path.Combine(wwwrootContentDir, "Toolbelt.Blazor.I18nText");

        // When
        await Start("dotnet", $"publish -c:Release -f:{framework} -p:UsingBrowserRuntimeWorkload=false --nologo", workSpace.StartupProj).ExitCodeIs(0);

        // Then

        // 1. Support client JavaScript file should be published into "_content/{PackageId}" folder.
        FileIO.ExistsAnyFilesInDir(staticWebAssetDir, "script.min.js").IsTrue();

        // 2. Text resource json files have been generated under the publish folder.
        var textResJsonFileNames = Directory.GetFiles(i18nDistDir, "*.json")
            .Select(Path.GetFileName)
            .Order();

        textResJsonFileNames
            .Is(ExpectedTextResJsonFromPackages.Concat(ExpectedTextResJsonFromProjects).Order());
    }
}
