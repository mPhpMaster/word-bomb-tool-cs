// Shows the selected letter region (accent border) and the optional turn-gate
// region (green border). Port of ui.RegionOverlay in ui/overlay_windows.go.
using WordBombTool.Views;

namespace WordBombTool;

public sealed class RegionOverlayManager
{
    private readonly OverlayWindow _letter;
    private readonly OverlayWindow _turn;
    private Region? _region;
    private Region? _turnRegion;
    private bool _bundleVisible = true;

    /// <summary>Creates both overlay windows (initially hidden). Must be called
    /// on the UI thread.</summary>
    public RegionOverlayManager()
    {
        _letter = new OverlayWindow(Theme.Accent);
        _turn = new OverlayWindow(Theme.Success);
    }

    /// <summary>Displays the letter region and, if set, the turn region.</summary>
    public void ShowRegion(Region? region, Region? turnRegion)
    {
        _region = region;
        _turnRegion = turnRegion;
        Apply();
    }

    /// <summary>Hides/shows both overlays together with the log window.</summary>
    public void SetBundleVisible(bool visible)
    {
        _bundleVisible = visible;
        Apply();
    }

    private void Apply()
    {
        if (_region == null || !_bundleVisible) _letter.HideRegion();
        else _letter.ShowRegion(_region);

        if (_turnRegion == null || !_bundleVisible) _turn.HideRegion();
        else _turn.ShowRegion(_turnRegion);
    }
}
