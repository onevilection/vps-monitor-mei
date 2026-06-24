using VpsWatcher.App.Configuration;

namespace VpsWatcher.App.Tests;

/// <summary>--debug_mode startup flag parsing (instruction §5).</summary>
public class LaunchOptionsTests
{
    [Fact]
    public void DebugMode_is_on_when_the_flag_is_present_alongside_other_args()
    {
        Assert.True(LaunchOptions.IsDebugMode(new[] { "--host", "x", "--debug_mode" }));
    }

    [Fact]
    public void DebugMode_is_off_when_absent()
    {
        Assert.False(LaunchOptions.IsDebugMode(new[] { "--host", "x", "--port", "22" }));
    }

    [Fact]
    public void DebugMode_flag_is_case_insensitive()
    {
        Assert.True(LaunchOptions.IsDebugMode(new[] { "--DEBUG_MODE" }));
    }

    [Fact]
    public void DebugMode_is_off_for_no_args()
    {
        Assert.False(LaunchOptions.IsDebugMode(Array.Empty<string>()));
    }
}
