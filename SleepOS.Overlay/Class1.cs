namespace SleepOS.Overlay;

public interface IOverlayService
{
    void Show(string message);
    void Hide();
    bool IsVisible { get; }
}

public sealed class OverlayService : IOverlayService
{
    public bool IsVisible { get; private set; }

    public void Show(string message)
    {
        IsVisible = true;
        LastMessage = message;
    }

    public void Hide()
    {
        IsVisible = false;
        LastMessage = string.Empty;
    }

    public string LastMessage { get; private set; } = string.Empty;
}