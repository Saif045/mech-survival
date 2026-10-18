using Godot;
using System.Linq;

namespace MechSurvival.Mech;

[GlobalClass]
public partial class MechPartCatalogResource : Resource
{
    [Export] public Godot.Collections.Array<MechPartDefinitionResource> Definitions { get; set; } = [];
    private PartCatalog? _catalog;
    public PartCatalog Freeze() => _catalog ??= new(Definitions.Select(d => d.Freeze()));
}
