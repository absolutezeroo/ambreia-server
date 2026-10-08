namespace Vortex.Primitives;

public enum VortexErrorCodeEnum
{
    Unknown = 0,
    AvatarNotFound,
    PlayerNotFound,
    RoomNotFound,
    ModelNotFound,
    MapNotLoaded,
    TileOutOfBounds,
    FloorItemNotFound,
    WallItemNotFound,
    FurnitureDefinitionNotFound,
    InvalidLogic,
    InvalidFurnitureProductType,
    InvalidSession,
    InvalidMoveTarget,
    NoPermissionToPlaceFurni,
    NoPermissionToManipulateFurni,
    PetNotFound,
    NoPermissionToManipulatePet,
    RoomLimitReached,
    NavigatorCategoryNotFound,
}
