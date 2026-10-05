using System.Xml.Linq;

namespace SocialMediaAutomation.ArchitectureTests;

// Checks declared project/package references rather than compiled assembly references:
// the compiler drops unused references, so an empty layer would pass an assembly check vacuously.
public class DependencyDirectionTests
{
    private static readonly string SrcDirectory = FindSrcDirectory();

    private static readonly string[] ForbiddenApplicationPackagePrefixes =
    [
        "Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore", "Microsoft.Data.SqlClient",
        "Microsoft.Extensions.Http", "Azure.", "AWSSDK", "OpenAI", "Anthropic",
    ];

    public static TheoryData<string, string[]> AllowedProjectReferences => new()
    {
        { "Domain", [] },
        { "Application", ["Domain"] },
        { "Infrastructure", ["Application", "Domain"] },
        { "Api", ["Application", "Infrastructure"] },
        { "Worker", ["Application", "Infrastructure"] },
    };

    [Theory]
    [MemberData(nameof(AllowedProjectReferences))]
    public void Project_references_follow_dependency_direction(string layer, string[] allowed)
    {
        var referenced = LoadProject(layer).Descendants("ProjectReference")
            .Select(r => Path.GetFileNameWithoutExtension(r.Attribute("Include")!.Value).Replace("SocialMediaAutomation.", ""));

        Assert.Empty(referenced.Except(allowed));
    }

    [Fact]
    public void Domain_has_no_package_or_framework_dependencies()
    {
        var project = LoadProject("Domain");

        Assert.Equal("Microsoft.NET.Sdk", project.Root!.Attribute("Sdk")?.Value);
        Assert.Empty(project.Descendants("PackageReference"));
        Assert.Empty(project.Descendants("FrameworkReference"));
    }

    [Fact]
    public void Application_has_no_persistence_web_or_provider_dependencies()
    {
        var project = LoadProject("Application");
        var packages = project.Descendants("PackageReference").Select(p => p.Attribute("Include")!.Value);

        Assert.Equal("Microsoft.NET.Sdk", project.Root!.Attribute("Sdk")?.Value);
        Assert.Empty(project.Descendants("FrameworkReference"));
        Assert.DoesNotContain(packages, p => ForbiddenApplicationPackagePrefixes.Any(f => p.StartsWith(f, StringComparison.OrdinalIgnoreCase)));
    }

    private static XDocument LoadProject(string layer) =>
        XDocument.Load(Path.Combine(SrcDirectory, $"SocialMediaAutomation.{layer}", $"SocialMediaAutomation.{layer}.csproj"));

    private static string FindSrcDirectory()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "SocialMediaAutomation.sln")))
                return Path.Combine(dir.FullName, "src");
        }
        throw new InvalidOperationException("Could not locate backend/SocialMediaAutomation.sln from the test output directory.");
    }
}
