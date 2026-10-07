using System.ComponentModel;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Vortex.Database.Entities.Pets;

[Table("pet_skins")]
[Index(nameof(PetType))]
[Index(nameof(PetType), nameof(SkinIndex), nameof(Rare), IsUnique = true)]
public class PetSkinEntity : VortexEntity
{
    [Column("pet_type")]
    public required int PetType { get; set; }

    [Column("skin_index")]
    public required int SkinIndex { get; set; }

    [Column("color")]
    public required int Color { get; set; }

    [Column("sellable")]
    public required bool Sellable { get; set; }

    [Column("rare")]
    [DefaultValue(false)]
    public required bool Rare { get; set; }
}
