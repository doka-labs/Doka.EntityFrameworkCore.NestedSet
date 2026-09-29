namespace Doka.EntityFrameworkCore.NestedSet.Tests.Samples;

/// <summary>Keeps pending-model verification enabled for the fixture's real migration chain.</summary>
[DbContext(typeof(SampleLifecycleContext))]
public sealed class SampleLifecycleModelSnapshot : ModelSnapshot
{
    /// <inheritdoc />
    protected override void BuildModel(
        ModelBuilder modelBuilder
    )
    {
        modelBuilder.HasAnnotation("ProductVersion", "10.0.12");
        SampleLifecycleContext.ConfigureModel(modelBuilder);
    }
}
