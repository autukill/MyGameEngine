namespace TheGodTheyMade.Game;

using GameEngine.Core.Domain.Input;

internal static class GameInputs
{
    public static readonly InputAxis2DRef CameraMove = new("camera.move");
    public static readonly InputActionRef PraiseFamiliar = new("familiar.praise");
    public static readonly InputActionRef StopFamiliar = new("familiar.stop");
}
