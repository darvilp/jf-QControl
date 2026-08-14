namespace Jellyfin.Plugin.QControl.Playback;

/// <summary>
/// Coalesces a playback or timer wake-up without performing reconciliation inline.
/// </summary>
public interface IProtectionWakeSignal
{
    /// <summary>
    /// Signals that authoritative state should be reread.
    /// </summary>
    void Wake();
}
