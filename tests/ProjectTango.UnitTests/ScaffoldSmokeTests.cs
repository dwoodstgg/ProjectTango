using ProjectTango.Domain;

namespace ProjectTango.UnitTests;

public class ScaffoldSmokeTests
{
    [Fact]
    public void Domain_assembly_is_referenced()
    {
        Assert.NotNull(typeof(AssemblyMarker).Assembly);
    }
}
