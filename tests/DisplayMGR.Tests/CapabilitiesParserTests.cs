using DisplayMGR.Monitors;
using Xunit;

namespace DisplayMGR.Tests;

public class CapabilitiesParserTests
{
    private const string Dell =
        "(prot(monitor)type(lcd)model(U2720Q)cmds(01 02 03 07 0C E3 F3)" +
        "vcp(02 04 05 08 10 12 14(01 04 05 06 08 09 0B 0C) 16 18 1A 52 60(1B 0F 11) AA(01 02 04) AC AE B2 B6 C6 C8 C9 D6(01 04 05) DC(00 03 05) DF E0 E1 E2(00 1D 02 20 21 22 0E 12 14) F0(0C) F1 F2 FD)" +
        "mswhql(1)asset_eep(40)mccs_ver(2.1))";

    [Fact]
    public void Parses_model_and_version()
    {
        var caps = CapabilitiesParser.Parse(Dell);
        Assert.Equal("U2720Q", caps.Model);
        Assert.Equal("2.1", caps.MccsVersion);
    }

    [Fact]
    public void Parses_continuous_codes_without_values()
    {
        var caps = CapabilitiesParser.Parse(Dell);
        Assert.True(caps.Supports(VcpCatalog.Brightness));
        Assert.True(caps.Supports(VcpCatalog.Contrast));
        Assert.Empty(caps.ValuesFor(VcpCatalog.Brightness));
        Assert.False(caps.Supports(VcpCatalog.Volume));
    }

    [Fact]
    public void Parses_value_lists()
    {
        var caps = CapabilitiesParser.Parse(Dell);
        Assert.Equal(new byte[] { 0x1B, 0x0F, 0x11 }, caps.ValuesFor(VcpCatalog.InputSource));
        Assert.Equal(new byte[] { 0x01, 0x04, 0x05 }, caps.ValuesFor(VcpCatalog.PowerMode));
        Assert.Equal(new byte[] { 0x00, 0x03, 0x05 }, caps.ValuesFor(VcpCatalog.DisplayMode));
    }

    [Fact]
    public void Codes_inside_value_lists_are_not_reported_as_supported()
    {
        // 0x0E, 0x20 and 0x21 only appear as values of E2.
        var caps = CapabilitiesParser.Parse(Dell);
        Assert.False(caps.Supports(0x0E));
        Assert.False(caps.Supports(0x20));
        Assert.False(caps.Supports(0x21));
    }

    [Fact]
    public void Does_not_confuse_vcpname_with_vcp()
    {
        var caps = CapabilitiesParser.Parse("(vcpname(F0(Custom))vcp(10 60 (0F 11))mccs_ver(2.2))");
        Assert.Equal(2, caps.Vcp.Count);
        Assert.Equal(new byte[] { 0x0F, 0x11 }, caps.ValuesFor(VcpCatalog.InputSource));
    }

    [Fact]
    public void Tolerates_missing_spaces_and_truncation()
    {
        var caps = CapabilitiesParser.Parse("(prot(monitor)vcp(101214(0508) 60(11 12");
        Assert.True(caps.Supports(0x10));
        Assert.True(caps.Supports(0x12));
        Assert.Equal(new byte[] { 0x05, 0x08 }, caps.ValuesFor(VcpCatalog.ColorPreset));
        Assert.Equal(new byte[] { 0x11, 0x12 }, caps.ValuesFor(VcpCatalog.InputSource));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("garbage")]
    [InlineData("vcp(")]
    [InlineData("((((")]
    public void Never_throws_on_bad_input(string? text)
    {
        var caps = CapabilitiesParser.Parse(text);
        Assert.Empty(caps.Vcp);
    }

    [Fact]
    public void Names_known_and_unknown_inputs()
    {
        Assert.Equal("HDMI 1", VcpCatalog.InputName(0x11));
        Assert.Equal("DisplayPort 1", VcpCatalog.InputName(0x0F));
        Assert.Equal("Input 0xD0", VcpCatalog.InputName(0xD0));
    }
}
