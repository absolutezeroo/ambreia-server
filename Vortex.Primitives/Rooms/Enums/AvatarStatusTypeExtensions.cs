namespace Vortex.Primitives.Rooms.Enums;

public static class AvatarStatusTypeExtensions
{
    public static string ToLegacyString(this AvatarStatusType statusType) =>
        statusType switch
        {
            AvatarStatusType.Move => "mv",
            AvatarStatusType.Slide => "sld",
            AvatarStatusType.Sit => "sit",
            AvatarStatusType.Lay => "lay",
            AvatarStatusType.FlatControl => "flatctrl",
            AvatarStatusType.Sign => "sign",
            AvatarStatusType.Gesture => "gst",
            AvatarStatusType.Wave => "wav",
            AvatarStatusType.Trading => "trd",
            AvatarStatusType.Dip => "dip",
            AvatarStatusType.Eat => "eat",
            AvatarStatusType.Beg => "beg",
            AvatarStatusType.Dead => "ded",
            AvatarStatusType.Jump => "jmp",
            AvatarStatusType.Play => "pla",
            AvatarStatusType.Speak => "spk",
            AvatarStatusType.Croak => "crk",
            AvatarStatusType.Relax => "rlx",
            AvatarStatusType.Wings => "wng",
            AvatarStatusType.Flame => "flm",
            AvatarStatusType.Rip => "rip",
            AvatarStatusType.Grow => "grw",
            AvatarStatusType.Grow1 => "grw1",
            AvatarStatusType.Grow2 => "grw2",
            AvatarStatusType.Grow3 => "grw3",
            AvatarStatusType.Grow4 => "grw4",
            AvatarStatusType.Grow5 => "grw5",
            AvatarStatusType.Grow6 => "grw6",
            AvatarStatusType.Grow7 => "grw7",
            AvatarStatusType.LayIn => "lay-in",
            AvatarStatusType.LayOut => "lay-out",
            AvatarStatusType.Kick => "kck",
            AvatarStatusType.WagTail => "wag",
            AvatarStatusType.JumpIn => "jmp-in",
            AvatarStatusType.JumpOut => "jmp-out",
            _ => throw new System.NotImplementedException(),
        };
}
