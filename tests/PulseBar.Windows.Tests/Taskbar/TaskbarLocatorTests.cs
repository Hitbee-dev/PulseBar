using System.Drawing;
using PulseBar.Windows.Taskbar;

namespace PulseBar.Windows.Tests.Taskbar;

public class TaskbarLocatorTests
{
    private static readonly Rectangle BottomTaskbar = new(0, 1040, 1920, 40);

    [Fact]
    public void ComputeOverlayRect_WithTrayBounds_PlacesLeftOfTray()
    {
        var info = new TaskbarInfo(
            BottomTaskbar,
            TrayBounds: new Rectangle(1600, 1040, 320, 40),
            TaskbarEdge.Bottom,
            AutoHide: false);

        var rect = TaskbarLocator.ComputeOverlayRect(info, overlayWidthPx: 400, overlayHeightPx: 32, marginPx: 8);

        Assert.NotNull(rect);
        Assert.Equal(1600 - 8 - 400, rect!.Value.X);
        Assert.Equal(1040 + 4, rect.Value.Y); // vertically centered: (40-32)/2 = 4
        Assert.Equal(400, rect.Value.Width);
    }

    [Fact]
    public void ComputeOverlayRect_WithoutTrayBounds_KeepsSafeMarginFromRightEnd()
    {
        var info = new TaskbarInfo(BottomTaskbar, null, TaskbarEdge.Bottom, false);

        var rect = TaskbarLocator.ComputeOverlayRect(info, 400, 32, marginPx: 8, safeTrayFallbackPx: 220);

        Assert.NotNull(rect);
        Assert.Equal(1920 - 220 - 8 - 400, rect!.Value.X);
    }

    [Fact]
    public void ComputeOverlayRect_OverlayWiderThanSpace_ClampsToTaskbarLeft()
    {
        var info = new TaskbarInfo(
            BottomTaskbar,
            new Rectangle(300, 1040, 1620, 40),
            TaskbarEdge.Bottom,
            false);

        var rect = TaskbarLocator.ComputeOverlayRect(info, 800, 32, marginPx: 8);

        Assert.NotNull(rect);
        Assert.Equal(8, rect!.Value.X); // bounds.Left + margin
    }

    [Theory]
    [InlineData(TaskbarEdge.Left)]
    [InlineData(TaskbarEdge.Right)]
    public void ComputeOverlayRect_VerticalTaskbar_ReturnsNull(TaskbarEdge edge)
    {
        var info = new TaskbarInfo(new Rectangle(0, 0, 60, 1080), null, edge, false);

        Assert.Null(TaskbarLocator.ComputeOverlayRect(info, 400, 32));
    }

    [Fact]
    public void ComputeOverlayRect_TopTaskbar_StillDocks()
    {
        var info = new TaskbarInfo(new Rectangle(0, 0, 1920, 40), null, TaskbarEdge.Top, false);

        var rect = TaskbarLocator.ComputeOverlayRect(info, 400, 32);

        Assert.NotNull(rect);
        Assert.Equal(4, rect!.Value.Y);
    }
}
