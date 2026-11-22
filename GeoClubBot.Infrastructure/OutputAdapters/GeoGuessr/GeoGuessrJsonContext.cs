using System.Text.Json.Serialization;
using UseCases.OutputPorts.GeoGuessr;

namespace Infrastructure.OutputAdapters.GeoGuessr;

/// <summary>
/// The JSON metadata <see cref="IGeoGuessrApi"/>'s generated Refit client reads and writes with (#533):
/// Refit 17 has no reflection-based JSON without the Refit.Reflection package. The options are Refit's
/// own defaults (<c>SystemTextJsonContentSerializer.GetDefaultJsonSerializerOptions()</c>): camelCase,
/// names read case-insensitively, a duplicated property an error, numbers strict. A difference here
/// would not fail — the DTOs ignore what they don't declare, so a field would just stop binding.
/// Every request and response type of the interface is listed; the types they hold are found from them.
/// Tests and tools that read captured GeoGuessr JSON use <c>Default.Options</c>, to read it as the bot does.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    AllowDuplicateProperties = false)]
[JsonSerializable(typeof(List<ClubMemberDto>))]
[JsonSerializable(typeof(ClubDto))]
[JsonSerializable(typeof(UserDto))]
[JsonSerializable(typeof(PostChallengeRequestDto))]
[JsonSerializable(typeof(PostChallengeResponseDto))]
[JsonSerializable(typeof(ChallengeResultHighscoresDto))]
[JsonSerializable(typeof(ChallengeResultGameDto))]
[JsonSerializable(typeof(ReadClubActivitiesResponseDto))]
[JsonSerializable(typeof(ReadClubApplicationsResponseDto))]
[JsonSerializable(typeof(ClubMissionBoardSnapshotDto))]
[JsonSerializable(typeof(RankedProgressResponseDto))]
[JsonSerializable(typeof(RankedPeakRatingResponseDto))]
public sealed partial class GeoGuessrJsonContext : JsonSerializerContext;
