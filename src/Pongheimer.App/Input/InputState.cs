using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace Pongheimer.Input;

/// <summary>
/// Знімок клавіатури й миші на кадр, із відстеженням «щойно натиснули».
/// Коли вікно неактивне, усе вважається відпущеним: гра не реагує на клавіші, набрані в іншій програмі.
/// </summary>
public sealed class InputState
{
    private readonly List<char> _typed = [];
    private KeyboardState _keys;
    private KeyboardState _previousKeys;
    private MouseState _mouse;
    private MouseState _previousMouse;

    /// <summary>Символи, набрані за цей кадр, з урахуванням розкладки (для поля вводу).</summary>
    public IReadOnlyList<char> Typed => _typed;

    /// <summary>Позиція курсора в пікселях вікна.</summary>
    public Vector2 MousePosition => new(_mouse.X, _mouse.Y);

    public bool IsLeftButtonDown => _mouse.LeftButton == ButtonState.Pressed;

    public bool WasLeftButtonPressed =>
        _mouse.LeftButton == ButtonState.Pressed && _previousMouse.LeftButton == ButtonState.Released;

    public bool WasLeftButtonReleased =>
        _mouse.LeftButton == ButtonState.Released && _previousMouse.LeftButton == ButtonState.Pressed;

    public bool IsControlDown => IsDown(Keys.LeftControl) || IsDown(Keys.RightControl);

    public void Update(bool windowIsActive, Queue<char> typedSinceLastFrame)
    {
        _previousKeys = _keys;
        _previousMouse = _mouse;
        _keys = windowIsActive ? Keyboard.GetState() : default;

        var mouse = Mouse.GetState();
        _mouse = windowIsActive
            ? mouse
            : new MouseState(mouse.X, mouse.Y, mouse.ScrollWheelValue, default, default, default, default, default);

        _typed.Clear();
        while (typedSinceLastFrame.TryDequeue(out var character))
        {
            if (windowIsActive)
            {
                _typed.Add(character);
            }
        }
    }

    public bool IsDown(Keys key) => _keys.IsKeyDown(key);

    /// <summary>Клавішу натиснули саме цього кадру (утримання не повторюється).</summary>
    public bool WasPressed(Keys key) => _keys.IsKeyDown(key) && _previousKeys.IsKeyUp(key);
}
