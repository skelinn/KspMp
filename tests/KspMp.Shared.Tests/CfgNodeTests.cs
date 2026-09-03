using KspMp.Shared.Config;
using Xunit;

namespace KspMp.Shared.Tests;

public class CfgNodeTests
{
    private const string Sample = """
        // header comment
        VESSEL
        {
        	pid = 3f1c2b6e2a1c4b4c9d1e0f2a3b4c5d6e
        	name = Mun Lander = Mk2
        	ref = 12345
        	PART
        	{
        		name = mk1pod.v2
        		crew = Jebediah Kerman
        		crew = Bill Kerman
        		flag = Squad/Flags/default // trailing comment
        	}
        	PART {
        		name = fuelTank
        	}
        }
        TIME { ut = 1234.5 }
        """;

    [Fact]
    public void ParsesValuesNodesAndRepeatedKeys()
    {
        var root = CfgNode.Parse(Sample);
        var vessel = root.GetNode("VESSEL");
        Assert.NotNull(vessel);
        Assert.Equal("Mun Lander = Mk2", vessel!.GetValue("name"));
        Assert.Equal(12345, vessel.GetInt("ref"));
        var parts = vessel.GetNodes("PART").ToList();
        Assert.Equal(2, parts.Count);
        Assert.Equal(new[] { "Jebediah Kerman", "Bill Kerman" }, parts[0].GetValues("crew").ToArray());
        Assert.Equal("Squad/Flags/default", parts[0].GetValue("flag"));
        Assert.Equal("fuelTank", parts[1].GetValue("name"));
        Assert.Equal(1234.5, root.GetNode("TIME")!.GetDouble("ut"));
        Assert.Null(vessel.GetValue("missing"));
        Assert.Equal(7, vessel.GetInt("missing", 7));
    }

    [Fact]
    public void RoundTripsThroughText()
    {
        var root = CfgNode.Parse(Sample);
        var again = CfgNode.Parse(root.ToText());
        Assert.Equal(root.ToText(), again.ToText());
        Assert.Contains("\tPART\n\t{\n\t\tname = mk1pod.v2", root.ToText());
    }

    [Fact]
    public void TypedValuesUseInvariantCulture()
    {
        var node = new CfgNode("X");
        node.AddValue("d", 1.5);
        node.AddValue("f", 2.25f);
        node.AddValue("b", true);
        node.AddValue("g", Guid.Empty);
        node.SetValue("d", "3.75");
        Assert.Equal(3.75, node.GetDouble("d"));
        Assert.Equal(2.25f, node.GetFloat("f"));
        Assert.True(node.GetBool("b"));
        Assert.Equal(Guid.Empty, node.GetGuid("g", Guid.NewGuid()));
        Assert.Single(node.GetValues("d"));
    }

    [Fact]
    public void SavesAndLoadsFiles()
    {
        var path = Path.Combine(Path.GetTempPath(), "kspmp-" + Guid.NewGuid().ToString("N") + ".cfg");
        try
        {
            var root = new CfgNode();
            root.AddNode("SERVER").AddValue("name", "Test");
            root.Save(path);
            Assert.Equal("Test", CfgNode.Load(path).GetNode("SERVER")!.GetValue("name"));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
