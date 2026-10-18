using MechSurvival.Mech;
using Xunit;

namespace MechSurvival.Tests;

public class FieldPartsTests
{
    private readonly PartDefinition _leg = new("leg", [MechSlot.LeftLeg]);
    [Fact] public void CatalogResolvesCanonicalDefinitionAndRejectsDuplicates()
    {
        Assert.Same(_leg, new PartCatalog([_leg]).Resolve("leg"));
        Assert.Throws<ArgumentException>(() => new PartCatalog([_leg, _leg]));
        Assert.Throws<ArgumentException>(() => new PartCatalog([_leg]).Resolve("missing"));
    }
    [Theory]
    [InlineData(float.NaN)] [InlineData(float.PositiveInfinity)] [InlineData(-0.1f)] [InlineData(1.1f)]
    public void InvalidConditionRejected(float condition) => Assert.Throws<ArgumentOutOfRangeException>(() =>
        new LoosePartProjection(new(Guid.NewGuid()), "leg", condition, new([_leg])));
    [Fact] public void EmptyIdentityAndUnknownModelRejected()
    {
        Assert.Throws<ArgumentException>(() => new LoosePartProjection(new(Guid.Empty), "leg", 1, new([_leg])));
        Assert.Throws<ArgumentException>(() => new LoosePartProjection(new(Guid.NewGuid()), "missing", 1, new([_leg])));
    }
    [Fact] public void ProjectionRequiresExactAuthoritativeIdentityDefinitionConditionAndDetachedState()
    {
        var catalog = new PartCatalog([_leg]);
        var part = new PartInstance(_leg, 0.8f);
        var view = new LoosePartProjection(part.Id, "leg", part.Condition, catalog);
        view.ValidateInstance(part, catalog);
        Assert.Throws<ArgumentException>(() => view.ValidateInstance(new(_leg, 0.8f), catalog));
        Assert.Throws<ArgumentException>(() => new LoosePartProjection(part.Id, "leg", 1, catalog).ValidateInstance(part, catalog));
        Assert.Throws<ArgumentException>(() => view.ValidateInstance(part, new([new("leg", [MechSlot.LeftLeg])])));
        var body = new MechBody();
        Assert.True(body.AttachPart(MechSlot.LeftLeg, part));
        Assert.Throws<ArgumentException>(() => view.ValidateInstance(part, catalog));
        Assert.Same(part, body.DetachPart(MechSlot.LeftLeg));
        view.ValidateInstance(part, catalog);
        Assert.Equal(view.Id, part.Id);
    }
    [Theory] [InlineData(0)] [InlineData(1)]
    public void BoundaryConditionsAccepted(float condition) => Assert.Equal(condition,
        new LoosePartProjection(new(Guid.NewGuid()), "leg", condition, new([_leg])).Condition);
}
