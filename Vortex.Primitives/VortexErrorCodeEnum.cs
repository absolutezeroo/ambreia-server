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
    PetFoodNotFound,
    NoPermissionToManipulatePet,
    RoomLimitReached,
    NavigatorCategoryNotFound,
}

public static class ErrorCodeExtensions
{
    public static string ToDefaultMessage(this VortexErrorCodeEnum code) =>
        code switch
        {
            VortexErrorCodeEnum.AvatarNotFound => "The specified avatar could not be found.",
            VortexErrorCodeEnum.PlayerNotFound => "The specified player could not be found.",
            VortexErrorCodeEnum.RoomNotFound => "The specified room could not be found.",
            VortexErrorCodeEnum.ModelNotFound => "The room model could not be found.",
            VortexErrorCodeEnum.MapNotLoaded => "The room map is not loaded.",
            VortexErrorCodeEnum.TileOutOfBounds => "The tile index is out of bounds.",
            VortexErrorCodeEnum.FloorItemNotFound => "The specified floor item could not be found.",
            VortexErrorCodeEnum.WallItemNotFound => "The specified wall item could not be found.",
            VortexErrorCodeEnum.FurnitureDefinitionNotFound =>
                "The specified furniture definition could not be found.",
            VortexErrorCodeEnum.InvalidLogic => "The logic is not valid.",
            VortexErrorCodeEnum.InvalidFurnitureProductType =>
                "The furniture product type is invalid.",
            VortexErrorCodeEnum.InvalidSession => "The session is invalid.",
            VortexErrorCodeEnum.InvalidMoveTarget => "The move target is invalid.",
            VortexErrorCodeEnum.NoPermissionToPlaceFurni =>
                "You do not have permission to place furniture.",
            VortexErrorCodeEnum.NoPermissionToManipulateFurni =>
                "You do not have permission to manipulate furniture.",
            VortexErrorCodeEnum.PetNotFound => "The specified pet could not be found.",
            VortexErrorCodeEnum.PetFoodNotFound => "The specified pet food could not be found.",
            VortexErrorCodeEnum.NoPermissionToManipulatePet =>
                "You do not have permission to manipulate this pet.",
            VortexErrorCodeEnum.RoomLimitReached => "You already own as many rooms as you may.",
            VortexErrorCodeEnum.NavigatorCategoryNotFound =>
                "The navigator category could not be found.",
            _ => "An unknown error occurred.",
        };
}
