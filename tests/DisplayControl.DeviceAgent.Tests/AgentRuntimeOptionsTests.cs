namespace DisplayControl.DeviceAgent.Tests;

public sealed class AgentRuntimeOptionsTests
{
    [Theory]
    [InlineData("http://device.example.test", 30, false)]
    [InlineData("https://device.example.test", 9, false)]
    [InlineData("https://device.example.test", 301, false)]
    [InlineData("https://device.example.test", 30, true)]
    public void IsValidEnforcesHttpsAndBoundedHeartbeat(string endpoint, int interval, bool expected)
    {
        var options = new AgentRuntimeOptions
        {
            ServerBaseAddress = new Uri(endpoint),
            HeartbeatIntervalSeconds = interval,
            StateDirectory = Path.GetFullPath("agent-state-test")
        };

        Assert.Equal(expected, AgentRuntimeOptions.IsValid(options));
    }
}
