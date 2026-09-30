using DeskPlan.Services;

namespace DeskPlan.Tests;

public class StartupServiceTests
{
    [Theory]
    [InlineData(@"C:\Apps\Desk Plan\DeskPlan.exe")]
    [InlineData(@"D:\桌面计划\DeskPlan.EXE")]
    public void CommandQuotesEntirePath(string path)
        => Assert.Equal($"\"{path}\"", StartupService.BuildCommand(path));

    [Theory]
    [InlineData("")]
    [InlineData("DeskPlan.exe")]
    [InlineData(@"C:\Apps\DeskPlan.dll")]
    [InlineData("C:\\Apps\\bad\"name.exe")]
    public void InvalidCommandRejected(string path)
        => Assert.Throws<ArgumentException>(() => StartupService.BuildCommand(path));
}
