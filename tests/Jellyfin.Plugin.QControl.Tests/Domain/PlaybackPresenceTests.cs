using System.Collections.Generic;
using Jellyfin.Plugin.QControl.Domain.Playback;
using Xunit;

namespace Jellyfin.Plugin.QControl.Tests.Domain;

public sealed class PlaybackPresenceTests
{
    public static TheoryData<IReadOnlyList<PlaybackSessionSnapshot>, bool> PresenceCases => new()
    {
        { [], false },
        { [new("connected", HasCurrentMedia: false, IsPaused: false)], false },
        { [new("playing", HasCurrentMedia: true, IsPaused: false)], true },
        { [new("paused", HasCurrentMedia: true, IsPaused: true)], true },
        {
            [
                new("connected", HasCurrentMedia: false, IsPaused: false),
                new("paused", HasCurrentMedia: true, IsPaused: true),
            ],
            true
        },
    };

    [Theory]
    [MemberData(nameof(PresenceCases))]
    public void CurrentMediaDeterminesPlaybackPresence(
        IReadOnlyList<PlaybackSessionSnapshot> sessions,
        bool expected)
    {
        var result = PlaybackPresence.Evaluate(sessions);

        Assert.Equal(expected, result.IsPresent);
    }

    [Fact]
    public void ParticipatingSessionIdsAreUniqueAndOrdinallySorted()
    {
        PlaybackSessionSnapshot[] sessions =
        [
            new("zeta", HasCurrentMedia: true, IsPaused: false),
            new("ignored", HasCurrentMedia: false, IsPaused: false),
            new("alpha", HasCurrentMedia: true, IsPaused: true),
            new("zeta", HasCurrentMedia: true, IsPaused: true),
        ];

        var result = PlaybackPresence.Evaluate(sessions);

        Assert.Equal(["alpha", "zeta"], result.SessionIds);
    }
}
