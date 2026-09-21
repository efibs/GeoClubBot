namespace UseCases.UseCases.MemberPrivateChannels;

/// <summary>
/// The single place that derives a member's private channel name from their nickname. Create,
/// rename-on-nickname-change and restore-from-archive all have to agree on it.
/// </summary>
internal static class MemberPrivateChannelName
{
    public static string For(string nickname) => $"{nickname.ToLowerInvariant()}-private-channel";
}
