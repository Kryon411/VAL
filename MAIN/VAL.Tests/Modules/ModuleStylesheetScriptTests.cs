using System.Text.Json;

using VAL.Host;

using Xunit;

namespace VAL.Tests.Modules;

public sealed class ModuleStylesheetScriptTests
{
    [Fact]
    public void MultipleStylesInOneModuleHaveDistinctStableIdentities()
    {
        var first = ModuleStylesheetScript.GetStyleId("Dock", "Dock.styles.css");
        var second = ModuleStylesheetScript.GetStyleId("Dock", "VAL.Controls.css");
        Assert.NotEqual(first, second);
        Assert.Equal(first, ModuleStylesheetScript.GetStyleId("Dock", "Dock.styles.css"));
    }

    [Fact]
    public void CaseAndPathSeparatorVariantsShareIdentity()
    {
        Assert.Equal(ModuleStylesheetScript.GetStyleId("Dock", "Theme/Controls.css"),
            ModuleStylesheetScript.GetStyleId("DOCK", "theme\\controls.CSS"));
    }

    [Fact]
    public void SanitizedNamesCannotCollide()
    {
        Assert.NotEqual(ModuleStylesheetScript.GetStyleId("A.B", "Controls.css"),
            ModuleStylesheetScript.GetStyleId("A-B", "Controls.css"));
        Assert.NotEqual(ModuleStylesheetScript.GetStyleId("Dock", "A.B.css"),
            ModuleStylesheetScript.GetStyleId("Dock", "A-B.css"));
    }

    [Fact]
    public void InjectionKeepsEachStylesheetAndSecurityBoundary()
    {
        const string css = "button { content: '\"\\\\'; }\n/* </script> */";
        var script = ModuleStylesheetScript.Create("Dock", "VAL.Controls.css", css);
        Assert.Contains(JsonSerializer.Serialize(ModuleStylesheetScript.GetStyleId("Dock", "VAL.Controls.css")), script);
        Assert.Contains(JsonSerializer.Serialize(css), script);
        Assert.Contains("window !== window.top", script);
        Assert.Contains("window.location.pathname", script);
        Assert.Contains("document.getElementById(id)", script);
        Assert.Contains("DOMContentLoaded", script);
        Assert.Contains("s.textContent = css", script);
    }
}
